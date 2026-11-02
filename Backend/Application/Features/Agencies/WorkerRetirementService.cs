using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Domain.StateMachines;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Application.Features.Agencies;

/// <summary>
/// Error codes for worker retirement operations.
/// </summary>
public static class RetireErrorCodes
{
    public const string AlreadyRetiringOrRetired = "ALREADY_RETIRING_OR_RETIRED";
    public const string WorkerNotRetirable = "WORKER_NOT_RETIRABLE";
    public const string SlotHeld = "SLOT_HELD";
    public const string ReplacementConflict = "REPLACEMENT_CONFLICT";
    public const string ReplacementNotOfAgency = "REPLACEMENT_NOT_OF_AGENCY";
    public const string ReplacementNotAvailable = "REPLACEMENT_NOT_AVAILABLE";
    public const string DuplicatePhoneOrId = "ALREADY_EXISTS";

    /// <summary>The <c>Reason</c> of the <see cref="AgencySwapWindowExpired"/> that hands a retired worker's job to the emergency rescue.</summary>
    public const string RescueReason = "worker retired by agency";
}

/// <summary>Hands future jobs of a retiring worker to the Dispatch emergency rescue. Used by the retire call and the sweep.</summary>
internal static class RescueHandOver
{
    /// <summary>
    /// Publishes <see cref="AgencySwapWindowExpired"/> per job. Call it only AFTER the database changes are committed: the rescue
    /// opens its own transaction. A failing publish is logged and never thrown; the sweep publishes again (the rescue is idempotent).
    /// </summary>
    public static async Task PublishAsync(
        IPublisher publisher, ILogger logger, int agencyId, IEnumerable<FutureJob> jobs, DateTime nowUtc, CancellationToken cancellationToken)
    {
        foreach (var job in jobs)
        {
            try
            {
                await publisher.Publish(
                    new AgencySwapWindowExpired(job.AssignmentId, job.OrderId, agencyId, RetireErrorCodes.RescueReason, nowUtc),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Handing assignment {AssignmentId} of order {OrderId} to the emergency rescue failed; the sweep publishes it again.",
                    job.AssignmentId, job.OrderId);
            }
        }
    }
}

/// <summary>
/// Service for retiring agency workers and handling replacements (AGENCY-UAT-02, fixed by AGENCY-FIX-01).
/// </summary>
public sealed class WorkerRetirementService(
    IAgencyRepository agencyRepository,
    IAgencySwapRepository swapRepository,
    IWorkerQuotaGuard quota,
    IUnitOfWork unitOfWork,
    IPublisher publisher,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<WorkerRetirementService>? logger = null)
{
    private readonly ILogger<WorkerRetirementService> _logger = logger ?? NullLogger<WorkerRetirementService>.Instance;

    private int AgencyId => currentUser.UserId is int id && currentUser.Role == UserRole.Partner
        ? id
        : throw new ForbiddenAccessException("Authenticated partner agency is required.");

    /// <summary>
    /// Retires a worker, optionally with a replacement. Without <c>replacement</c> and <c>confirmRescue</c> a worker with future jobs
    /// gets a preview (<c>requiresConfirmation</c>) and nothing changes. A repeated call on a retiring/retired worker answers the current status.
    /// </summary>
    public async Task<RetireWorkerResponse> RetireWorkerAsync(int workerId, RetireWorkerRequest request, CancellationToken cancellationToken = default)
    {
        var agencyId = AgencyId;
        var now = clock.UtcNow;
        var today = clock.LocalToday;

        var current = await agencyRepository.GetAgencyWorkerAsync(agencyId, workerId, cancellationToken)
            ?? throw new NotFoundException("Worker", workerId);

        if (current.WorkStatus is WorkStatus.Retiring or WorkStatus.Retired)
        {
            return new RetireWorkerResponse { WorkerId = workerId, WorkStatus = Wire(current.WorkStatus) };
        }

        if (!WorkerStateMachine.Instance.CanTransition(current.WorkStatus, WorkStatus.Retiring))
        {
            throw new BusinessRuleViolationException("The worker cannot be retired in its current status.", RetireErrorCodes.WorkerNotRetirable);
        }

        var replacement = request.Replacement;
        if (replacement is not null && replacement.ExistingWorkerId.HasValue == (replacement.NewWorker is not null))
        {
            throw new ValidationException("replacement", "Provide either existingWorkerId or newWorker, not both and not neither.");
        }

        // A HELD slot is a customer in checkout. A new replacement worker takes the slot over; otherwise the call waits for the customer.
        if (replacement?.NewWorker is null
            && await agencyRepository.CountFutureHeldSlotsAsync(workerId, today, cancellationToken) > 0)
        {
            throw new BusinessRuleViolationException(
                "A customer is booking a shift of this worker right now; try again in a few minutes or give a new worker as replacement.",
                RetireErrorCodes.SlotHeld);
        }

        var futureJobs = await agencyRepository.GetFutureAssignedJobsAsync(workerId, cancellationToken);
        var handovers = futureJobs.Select(j => new FutureJobHandover(j.AssignmentId, j.OrderId, j.SlotDate, j.ShiftCode)).ToList();

        if (futureJobs.Count > 0 && replacement is null && !request.ConfirmRescue)
        {
            return new RetireWorkerResponse
            {
                WorkerId = workerId,
                RequiresConfirmation = true,
                WorkStatus = Wire(current.WorkStatus),
                FutureJobs = handovers,
                Warning = "Continuing hands these shifts to the emergency rescue and the agency receives an SLA penalty."
            };
        }

        var (status, replacementWorkerId) = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var worker = await agencyRepository.GetAgencyWorkerForUpdateAsync(agencyId, workerId, cancellationToken)
                ?? throw new NotFoundException("Worker", workerId);
            if (worker.WorkStatus is WorkStatus.Retiring or WorkStatus.Retired)
            {
                throw new BusinessRuleViolationException("The worker is already retiring or retired.", RetireErrorCodes.AlreadyRetiringOrRetired);
            }

            int? replacedBy = null;
            if (replacement?.ExistingWorkerId is int existingId)
            {
                await MoveJobsToExistingWorkerAsync(agencyId, existingId, futureJobs, now, cancellationToken);
                replacedBy = existingId;
            }
            else if (replacement?.NewWorker is { } details)
            {
                replacedBy = await ReplaceWithNewWorkerAsync(agencyId, worker, details, today, now, cancellationToken);
            }

            await agencyRepository.RemoveFutureAvailableSlotsAsync(workerId, today, cancellationToken);

            var stillHasFutureJobs = replacement is null && futureJobs.Count > 0;
            worker.TransitionTo(WorkStatus.Retiring);
            if (!stillHasFutureJobs && !await agencyRepository.HasActiveJobAsync(workerId, cancellationToken))
            {
                worker.TransitionTo(WorkStatus.Retired);
            }

            worker.UpdatedAt = now;
            return (worker.WorkStatus, replacedBy);
        }, cancellationToken);

        // The rescue opens its own transaction and charges the penalty: only after the retirement is committed.
        if (replacement is null && request.ConfirmRescue && futureJobs.Count > 0)
        {
            await RescueHandOver.PublishAsync(publisher, _logger, agencyId, futureJobs, now, cancellationToken);
        }

        _logger.LogInformation("Agency {AgencyId} retired worker {WorkerId} to {Status}", agencyId, workerId, status);

        return new RetireWorkerResponse
        {
            WorkerId = workerId,
            WorkStatus = Wire(status),
            ReplacementWorkerId = replacementWorkerId,
            FutureJobs = handovers.Count > 0 ? handovers : null,
            Warning = (handovers.Count, replacement is null) switch
            {
                (0, _) => null,
                (_, true) => "Worker has future jobs; the emergency rescue was started for each of them.",
                _ => "Worker has future jobs that have been transferred to the replacement worker."
            }
        };
    }

    private static string Wire(WorkStatus status) => status.ToString().ToUpperInvariant();

    /// <summary>
    /// Moves every future job to a worker of the same agency that has a free slot at the same date and shift. One job that cannot
    /// move rejects the whole call with the list of conflicts (the caller's transaction rolls back the jobs already moved).
    /// </summary>
    private async Task MoveJobsToExistingWorkerAsync(
        int agencyId, int newWorkerId, IReadOnlyList<FutureJob> jobs, DateTime now, CancellationToken cancellationToken)
    {
        var newWorker = await agencyRepository.GetAgencyWorkerAsync(agencyId, newWorkerId, cancellationToken)
            ?? throw new BusinessRuleViolationException("The replacement worker is not a worker of this agency.", RetireErrorCodes.ReplacementNotOfAgency);

        if (newWorker.WorkStatus is WorkStatus.Pending or WorkStatus.Locked or WorkStatus.Retiring or WorkStatus.Retired)
        {
            throw new BusinessRuleViolationException("The replacement worker cannot take jobs now.", RetireErrorCodes.ReplacementNotAvailable);
        }

        var targets = new List<(FutureJob Job, BookingSlot Slot)>();
        var conflicts = new List<string>();
        foreach (var job in jobs)
        {
            var slot = await swapRepository.GetAvailableSlotAsync(newWorkerId, job.SlotDate, job.ShiftCode, cancellationToken);
            if (slot is null)
            {
                conflicts.Add($"{job.SlotDate:yyyy-MM-dd} {job.ShiftCode}");
            }
            else
            {
                targets.Add((job, slot));
            }
        }

        if (conflicts.Count > 0)
        {
            throw new BusinessRuleViolationException(
                $"The replacement worker has no free slot for: {string.Join(", ", conflicts)}.",
                RetireErrorCodes.ReplacementConflict);
        }

        foreach (var (job, slot) in targets)
        {
            if (!await swapRepository.TrySwapAsync(job.AssignmentId, job.SlotId, newWorkerId, slot.SlotId, now, cancellationToken))
            {
                throw new BusinessRuleViolationException(
                    $"The job on {job.SlotDate:yyyy-MM-dd} {job.ShiftCode} could not be moved to the replacement worker.",
                    RetireErrorCodes.ReplacementConflict);
            }
        }
    }

    /// <summary>Creates the new worker exactly like the import (same row validation) and takes the old worker's future slots and jobs over.</summary>
    private async Task<int> ReplaceWithNewWorkerAsync(
        int agencyId, Worker oldWorker, NewWorkerDetails details, DateOnly today, DateTime now, CancellationToken cancellationToken)
    {
        var sheet = new List<IReadOnlyList<string>>
        {
            WorkerImportService.Columns.ToList(),
            new List<string> { details.FullName ?? "", details.PhoneNumber ?? "", details.NationalId ?? "", details.BankName ?? "", details.BankAccountNo ?? "" }
        };
        var (rows, errors) = WorkerImportService.Parse(sheet);
        if (errors.Count > 0)
        {
            var byField = errors
                .GroupBy(e => ToField(e.Column))
                .ToDictionary(g => g.Key, g => g.Select(e => $"{e.Code}: {e.Message}").ToArray());
            throw new ValidationException(byField);
        }

        var row = rows.Single();
        if (await agencyRepository.WorkerPhoneOrNationalIdExistsAsync(row.PhoneNumber, row.NationalId, cancellationToken))
        {
            throw new BusinessRuleViolationException("A worker with this phone number or national ID already exists.", RetireErrorCodes.DuplicatePhoneOrId);
        }

        // The new worker is counted, the retiring one is not any more. A locked worker was not counted, so the total grows by one.
        if (oldWorker.WorkStatus == WorkStatus.Locked)
        {
            await quota.EnsureCanAddWorkersAsync(agencyId, 1, cancellationToken);
        }

        var created = await agencyRepository.CreateAgencyStaffAsync(
            agencyId,
            new NewWorkerDetails { FullName = row.FullName, PhoneNumber = row.PhoneNumber, NationalId = row.NationalId, BankName = row.BankName, BankAccountNo = row.BankAccountNo },
            now,
            cancellationToken);

        await agencyRepository.UpdateWorkerSlotsAsync(oldWorker.WorkerId, created.WorkerId, today, now, cancellationToken);
        await agencyRepository.UpdateWorkerAssignmentsAsync(oldWorker.WorkerId, created.WorkerId, cancellationToken);
        return created.WorkerId;
    }

    private static string ToField(string importColumn) => importColumn switch
    {
        "full_name" => "newWorker.fullName",
        "phone_number" => "newWorker.phoneNumber",
        "national_id" => "newWorker.nationalId",
        "bank_name" => "newWorker.bankName",
        "bank_account_no" => "newWorker.bankAccountNo",
        _ => "newWorker"
    };
}

/// <summary>
/// The 5-minute sweep of retiring workers (AGENCY-FIX-01): hands the future jobs of a <c>RETIRING</c> worker to the emergency rescue
/// again (the rescue is idempotent) and turns a <c>RETIRING</c> worker without any job left into <c>RETIRED</c>.
/// </summary>
public sealed class WorkerRetirementSweepService(
    IAgencyRepository agencyRepository,
    IUnitOfWork unitOfWork,
    IPublisher publisher,
    IClock clock,
    ILogger<WorkerRetirementSweepService>? logger = null)
{
    private readonly ILogger<WorkerRetirementSweepService> _logger = logger ?? NullLogger<WorkerRetirementSweepService>.Instance;

    /// <summary>Runs one sweep. Returns how many workers became <c>RETIRED</c> and how many jobs were handed to the rescue again.</summary>
    public async Task<(int Retired, int Republished)> SweepAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var retired = 0;
        var republished = 0;

        foreach (var (workerId, agencyId) in await agencyRepository.ListRetiringWorkersAsync(cancellationToken))
        {
            try
            {
                var futureJobs = await agencyRepository.GetFutureAssignedJobsAsync(workerId, cancellationToken);
                if (futureJobs.Count > 0)
                {
                    await RescueHandOver.PublishAsync(publisher, _logger, agencyId, futureJobs, now, cancellationToken);
                    republished += futureJobs.Count;
                    continue;
                }

                if (await agencyRepository.HasActiveJobAsync(workerId, cancellationToken))
                {
                    continue;
                }

                var done = await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var worker = await agencyRepository.GetAgencyWorkerForUpdateAsync(agencyId, workerId, cancellationToken);
                    if (worker is null || worker.WorkStatus != WorkStatus.Retiring)
                    {
                        return false;
                    }

                    worker.TransitionTo(WorkStatus.Retired);
                    worker.UpdatedAt = now;
                    return true;
                }, cancellationToken);

                if (done)
                {
                    retired++;
                    _logger.LogInformation("Worker {WorkerId} of agency {AgencyId} is RETIRED.", workerId, agencyId);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "The retirement sweep failed for worker {WorkerId}; it runs again at the next tick.", workerId);
            }
        }

        return (retired, republished);
    }
}
