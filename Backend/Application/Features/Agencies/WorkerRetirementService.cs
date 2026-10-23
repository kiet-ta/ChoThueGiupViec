using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Application.Features.Identity;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Application.Features.Agencies;

/// <summary>
/// Error codes for worker retirement operations.
/// </summary>
public static class RetireErrorCodes
{
    public const string WorkerNotFound = "WORKER_NOT_FOUND";
    public const string NotAgencyWorker = "NOT_AGENCY_WORKER";
    public const string AlreadyRetiringOrRetired = "ALREADY_RETIRING_OR_RETIRED";
    public const string SlotHeld = "SLOT_HELD";
    public const string QuotaExceeded = "QUOTA_EXCEEDED";
    public const string FutureJobsNeedReplacement = "FUTURE_JOBS_NEED_REPLACEMENT";
    public const string ReplacementConflict = "REPLACEMENT_CONFLICT";
    public const string ReplacementNotOfAgency = "REPLACEMENT_NOT_OF_AGENCY";
    public const string ReplacementNotAvailable = "REPLACEMENT_NOT_AVAILABLE";
    public const string NewWorkerValidation = "NEW_WORKER_VALIDATION";
    public const string DuplicatePhoneOrId = "ALREADY_EXISTS";
}

/// <summary>
/// Exception thrown when future jobs need replacement confirmation.
/// </summary>
public sealed class FutureJobsNeedReplacementException(
    string message,
    string errorCode,
    IReadOnlyList<FutureJobHandover> futureJobs)
    : BusinessRuleViolationException(message, errorCode)
{
    public IReadOnlyList<FutureJobHandover> FutureJobs { get; } = futureJobs;
}

/// <summary>
/// Service for retiring agency workers and handling replacements (AGENCY-UAT-02).
/// </summary>
public sealed class WorkerRetirementService(
    IAgencyRepository agencyRepository,
    ISubscriptionRepository subscriptionRepository,
    IAgencySwapRepository swapRepository,
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
    /// Retires a worker, optionally with a replacement.
    /// </summary>
    public async Task<RetireWorkerResponse> RetireWorkerAsync(int workerId, RetireWorkerRequest request, CancellationToken cancellationToken = default)
    {
        var agencyId = AgencyId;
        var now = clock.UtcNow;

        // Get the worker and verify ownership
        var worker = await agencyRepository.GetAgencyWorkerAsync(agencyId, workerId, cancellationToken)
            ?? throw new NotFoundException("Worker", workerId);

        // Check if already retiring/retired
        if (worker.WorkStatus is WorkStatus.Retiring or WorkStatus.Retired)
        {
            throw new BusinessRuleViolationException("The worker is already retiring or retired.", RetireErrorCodes.AlreadyRetiringOrRetired);
        }

        // Get future assigned jobs
        var futureJobs = await agencyRepository.GetFutureAssignedJobsAsync(workerId, cancellationToken);
        var hasFutureJobs = futureJobs.Count > 0;

        // If worker has future jobs and no replacement, require confirmRescue
        if (hasFutureJobs && request.Replacement is null && !request.ConfirmRescue)
        {
            var handovers = futureJobs.Select(j => new FutureJobHandover(j.AssignmentId, j.SlotDate, j.ShiftCode)).ToList();
            throw new FutureJobsNeedReplacementException(
                $"The worker has {futureJobs.Count} future assigned jobs. Continuing will start emergency rescue and incur an SLA penalty. Please confirm or provide a replacement worker.",
                RetireErrorCodes.FutureJobsNeedReplacement,
                handovers);
        }

        // Handle replacement
        int? replacementWorkerId = null;
        if (request.Replacement is not null)
        {
            replacementWorkerId = await HandleReplacementAsync(worker, request.Replacement, futureJobs, now, cancellationToken);
        }

        // Determine new status and handle future jobs
        var newStatus = hasFutureJobs ? WorkStatus.Retiring : WorkStatus.Retired;
        var warning = hasFutureJobs && request.Replacement is null
            ? "Worker has future jobs and will be retired after they complete. Emergency rescue will be triggered for each future job."
            : hasFutureJobs && request.Replacement is not null
                ? "Worker has future jobs that have been transferred to the replacement worker."
                : null;

        // Execute the retirement in a transaction
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // Update worker status
            worker.TransitionTo(newStatus);
            worker.UpdatedAt = now;

            // If no replacement and has future jobs, publish AgencySwapWindowExpired for each to trigger rescue
            if (hasFutureJobs && request.Replacement is null && request.ConfirmRescue)
            {
                foreach (var job in futureJobs)
                {
                    await publisher.Publish(
                        new AgencySwapWindowExpired(job.AssignmentId, 0, agencyId, "worker retired by agency", now),
                        cancellationToken);
                }
            }

            return true;
        }, cancellationToken);

        // Prepare response
        var response = new RetireWorkerResponse
        {
            WorkerId = workerId,
            WorkStatus = newStatus.ToString().ToUpperInvariant(),
            ReplacementWorkerId = replacementWorkerId,
            FutureJobs = hasFutureJobs
                ? futureJobs.Select(j => new FutureJobHandover(j.AssignmentId, j.SlotDate, j.ShiftCode)).ToList()
                : null,
            Warning = warning
        };

        _logger.LogInformation("Agency {AgencyId} retired worker {WorkerId} to {Status}", agencyId, workerId, newStatus);

        return response;
    }

    private async Task<int> HandleReplacementAsync(
        Worker oldWorker,
        RetireWorkerReplacement replacement,
        IReadOnlyList<FutureJobHandover> futureJobs,
        DateTime now,
        CancellationToken cancellationToken)
    {
        int newWorkerId;

        if (replacement.ExistingWorkerId.HasValue)
        {
            // Use existing worker
            newWorkerId = replacement.ExistingWorkerId.Value;

            // Verify the replacement worker belongs to the agency and is available
            var newWorker = await agencyRepository.GetAgencyWorkerAsync(AgencyId, newWorkerId, cancellationToken)
                ?? throw new BusinessRuleViolationException("The replacement worker is not a worker of this agency.", RetireErrorCodes.ReplacementNotOfAgency);

            if (newWorker.WorkStatus is WorkStatus.Pending or WorkStatus.Locked or WorkStatus.Retiring or WorkStatus.Retired)
            {
                throw new BusinessRuleViolationException("The replacement worker cannot take jobs now.", RetireErrorCodes.ReplacementNotAvailable);
            }

            // Verify the new worker has available slots for all future jobs
            foreach (var job in futureJobs)
            {
                var slot = await swapRepository.GetAvailableSlotAsync(newWorkerId, job.SlotDate, job.ShiftCode, cancellationToken)
                    ?? throw new BusinessRuleViolationException(
                        $"The replacement worker has no free slot for {job.SlotDate} {job.ShiftCode}.",
                        RetireErrorCodes.ReplacementConflict);
            }

            // Use TrySwapAsync for each job (atomic, independent of swap window)
            foreach (var job in futureJobs)
            {
                var slot = await swapRepository.GetAvailableSlotAsync(newWorkerId, job.SlotDate, job.ShiftCode, cancellationToken);
                if (slot is null) continue; // Should not happen due to check above

                var swapped = await swapRepository.TrySwapAsync(job.AssignmentId, 0, newWorkerId, slot.SlotId, now, cancellationToken);
                if (!swapped)
                {
                    throw new BusinessRuleViolationException(
                        $"Could not transfer job {job.AssignmentId} to replacement worker.",
                        RetireErrorCodes.ReplacementConflict);
                }
            }
        }
        else if (replacement.NewWorker is not null)
        {
            // Create new worker
            var details = replacement.NewWorker;

            // Validate required fields
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(details.FullName)) errors["fullName"] = ["Full name is required."];
            string normalizedPhone = string.Empty;
            if (string.IsNullOrWhiteSpace(details.PhoneNumber))
            {
                errors["phoneNumber"] = ["Phone number is required."];
            }
            else
            {
                var (phoneIsValid, normPhone) = PhoneValidator.TryNormalize(details.PhoneNumber);
                normalizedPhone = normPhone;
                if (!phoneIsValid) errors["phoneNumber"] = ["Invalid phone number format."];
            }
            if (string.IsNullOrWhiteSpace(details.NationalId) || details.NationalId.Length != 12 || !details.NationalId.All(char.IsDigit))
                errors["nationalId"] = ["National ID must be exactly 12 digits."];

            if (errors.Count > 0)
            {
                throw new ValidationException(errors);
            }

            // Check for duplicate phone/national ID
            var exists = await agencyRepository.WorkerPhoneOrNationalIdExistsAsync(normalizedPhone, details.NationalId!, cancellationToken);
            if (exists)
            {
                throw new BusinessRuleViolationException("A worker with this phone number or national ID already exists.", RetireErrorCodes.DuplicatePhoneOrId);
            }

            // Create the new worker
            var newWorker = await agencyRepository.CreateAgencyStaffAsync(AgencyId, details, now, cancellationToken);
            newWorkerId = newWorker.WorkerId;

            // Update old worker's future slots and assignments to the new worker
            await agencyRepository.UpdateWorkerSlotsAsync(oldWorker.WorkerId, newWorkerId, now, cancellationToken);
            await agencyRepository.UpdateWorkerAssignmentsAsync(oldWorker.WorkerId, newWorkerId, cancellationToken);
        }
        else
        {
            throw new ValidationException("replacement", "Either existingWorkerId or newWorker must be provided.");
        }

        return newWorkerId;
    }
}