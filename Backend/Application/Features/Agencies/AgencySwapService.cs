using CommonService.Application.Exceptions;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Application.Features.Agencies;

/// <summary>The assignment, its slot and the shift start as the swap window needs them.</summary>
public sealed record SwapAssignmentView(
    long AssignmentId, long OrderId, int AgencyId, int WorkerId, int SlotId, JobAssignmentStatus Status,
    DateOnly SlotDate, string ShiftCode, TimeOnly StartTime, ServiceTier ServiceTier);

public sealed record UnavailableRequest(string? Reason);

public sealed record SwapRequest(int? NewWorkerId);

/// <summary>The 30-minute window of one assignment (contract agencies.md 7).</summary>
/// <param name="State"><c>OPEN</c> (the agency may swap until <c>DeadlineAt</c>), <c>SWAPPED</c>, or <c>RESCUE_STARTED</c> (expired or notice of 2 h or less: Dispatch took over).</param>
public sealed record SwapWindowDto(
    long AssignmentId, long OrderId, string State, DateTime OpenedAt, DateTime DeadlineAt, int RemainingSeconds, DateTime ShiftStartUtc, int? NewWorkerId);

public static class SwapErrorCodes
{
    public const string InvalidState = "ASSIGNMENT_STATE_INVALID";
    public const string NotPremium = "NOT_AN_AGENCY_JOB";
    public const string WindowClosed = "SWAP_WINDOW_CLOSED";
    public const string WindowNotOpen = "SWAP_WINDOW_NOT_OPEN";
    public const string WorkerNotOfAgency = "WORKER_NOT_OF_AGENCY";
    public const string WorkerNotAvailable = "WORKER_NOT_AVAILABLE";
    public const string SameWorker = "SAME_WORKER";
    public const string NoSlot = "WORKER_HAS_NO_FREE_SLOT";
    public const string SlotTaken = "SLOT_TAKEN";
}

/// <summary>Persistence of the swap window (BE-M5-09). The swap itself is one atomic conditional change.</summary>
public interface IAgencySwapRepository
{
    Task<SwapAssignmentView?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default);

    Task<AgencySwapWindow?> GetWindowAsync(long assignmentId, CancellationToken cancellationToken = default);

    void AddWindow(AgencySwapWindow window);

    /// <summary>Conditional <c>OPEN -&gt; <paramref name="to"/></c>; true only for the caller that changed it (an expiry and a swap never both win).</summary>
    Task<bool> TryCloseWindowAsync(long assignmentId, string to, int? newWorkerId, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgencySwapWindow>> ListOpenAsync(int? agencyId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgencySwapWindow>> ListOpenDueAsync(DateTime nowUtc, int take, CancellationToken cancellationToken = default);

    /// <summary>The worker's status when it belongs to the agency, otherwise null.</summary>
    Task<WorkStatus?> GetAgencyWorkerStatusAsync(int agencyId, int workerId, CancellationToken cancellationToken = default);

    /// <summary>The worker's <c>AVAILABLE</c> slot for the date and shift, or null.</summary>
    Task<BookingSlot?> GetAvailableSlotAsync(int workerId, DateOnly date, string shiftCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// One transaction: the assignment (still <c>ASSIGNED</c> on <paramref name="oldSlotId"/>) moves to the new worker and slot, the new
    /// slot <c>AVAILABLE -&gt; BOOKED</c>, the old slot <c>BOOKED -&gt; AVAILABLE</c>. False (nothing changed) when any condition no longer holds.
    /// </summary>
    Task<bool> TrySwapAsync(long assignmentId, int oldSlotId, int newWorkerId, int newSlotId, DateTime nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// The 30-minute self-swap window (BE-M5-09, decisions Q09, spec 2.5 phase 2, contract agencies.md 7). An agency that cannot staff an
/// ASSIGNED Premium job reports it: with more than 2 hours of notice it has a window of 30 minutes (never past shift start minus 2 h) to
/// replace the worker with another of its own workers who has a free slot for that shift; when the window ends, or the notice is 2 hours
/// or less, the right passes to Dispatch (<see cref="AgencySwapWindowExpired"/>: emergency rescue + SLA penalty are M3's, BE-M3-07).
/// </summary>
public sealed class AgencySwapService(
    IAgencySwapRepository repository,
    IUnitOfWork unitOfWork,
    IPublisher publisher,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<AgencySwapService>? logger = null)
{
    /// <summary>provenance: spec 2.5 phase 2 and decisions Q09: a 30-minute window; matches <c>MilestoneFailoverService.SelfSwapWindowMinutes</c>.</summary>
    public const int WindowMinutes = 30;

    /// <summary>provenance: spec 2.5 phase 2: at 2 hours or less before the shift Dispatch rescues at once (<c>MilestoneFailoverService.NoticeThresholdHours</c>).</summary>
    public const int RescueThresholdHours = 2;

    private readonly ILogger<AgencySwapService> _logger = logger ?? NullLogger<AgencySwapService>.Instance;

    private int AgencyId => currentUser.UserId is int id && currentUser.Role == UserRole.Partner
        ? id
        : throw new ForbiddenAccessException("Authenticated partner agency is required.");

    public async Task<IReadOnlyList<SwapWindowDto>> ListOpenAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var windows = await repository.ListOpenAsync(AgencyId, cancellationToken);
        var result = new List<SwapWindowDto>();
        foreach (var w in windows)
        {
            var view = await repository.GetAssignmentAsync(w.AssignmentId, cancellationToken);
            if (view is not null)
            {
                result.Add(ToDto(w, view, now));
            }
        }

        return result;
    }

    public async Task<SwapWindowDto> ReportUnavailableAsync(long assignmentId, UnavailableRequest request, CancellationToken cancellationToken = default)
    {
        var agencyId = AgencyId;
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > 255)
        {
            throw new ValidationException("reason", "reason is required and must be at most 255 characters.");
        }

        var view = await OwnAssignmentAsync(assignmentId, agencyId, cancellationToken);
        var existing = await repository.GetWindowAsync(assignmentId, cancellationToken);
        var now = clock.UtcNow;
        if (existing is not null)
        {
            return ToDto(existing, view, now); // idempotent: the window of this assignment already exists
        }

        if (view.Status != JobAssignmentStatus.Assigned)
        {
            throw new BusinessRuleViolationException(
                $"The assignment is {view.Status}, not ASSIGNED.", SwapErrorCodes.InvalidState);
        }

        var shiftStart = ShiftStartUtc(view);
        var cutoff = shiftStart.AddHours(-RescueThresholdHours);
        var noticeLeft = cutoff - now;
        var window = new AgencySwapWindow
        {
            AssignmentId = assignmentId,
            AgencyId = agencyId,
            OpenedAt = now,
            Reason = reason,
            Status = noticeLeft > TimeSpan.Zero ? SwapWindowStatuses.Open : SwapWindowStatuses.Expired,
        };

        if (noticeLeft > TimeSpan.Zero)
        {
            var thirty = now.AddMinutes(WindowMinutes);
            window.DeadlineAt = thirty < cutoff ? thirty : cutoff;
        }
        else
        {
            window.DeadlineAt = now;
            window.ClosedAt = now;
        }

        await unitOfWork.ExecuteInTransactionAsync(() =>
        {
            repository.AddWindow(window);
            return Task.FromResult(true);
        }, cancellationToken);

        if (window.Status == SwapWindowStatuses.Expired)
        {
            _logger.LogWarning("Assignment {AssignmentId}: notice of {Hours:F1} h, handing over to the emergency rescue.", assignmentId, (shiftStart - now).TotalHours);
            await publisher.Publish(new AgencySwapWindowExpired(assignmentId, view.OrderId, agencyId, reason, now), cancellationToken);
        }

        return ToDto(window, view, now);
    }

    public async Task<SwapWindowDto> SwapWorkerAsync(long assignmentId, SwapRequest request, CancellationToken cancellationToken = default)
    {
        var agencyId = AgencyId;
        if (request.NewWorkerId is not > 0)
        {
            throw new ValidationException("newWorkerId", "newWorkerId is required.");
        }

        var newWorkerId = request.NewWorkerId.Value;
        var view = await OwnAssignmentAsync(assignmentId, agencyId, cancellationToken);
        var window = await repository.GetWindowAsync(assignmentId, cancellationToken)
            ?? throw new BusinessRuleViolationException("No swap window is open for this assignment.", SwapErrorCodes.WindowNotOpen);
        var now = clock.UtcNow;

        if (window.Status != SwapWindowStatuses.Open)
        {
            throw new BusinessRuleViolationException("The swap window is closed.", SwapErrorCodes.WindowClosed);
        }

        if (now > window.DeadlineAt)
        {
            await ExpireAsync(window, view, now, cancellationToken);
            throw new BusinessRuleViolationException("The 30-minute window has ended; Dispatch took over.", SwapErrorCodes.WindowClosed);
        }

        if (newWorkerId == view.WorkerId)
        {
            throw new BusinessRuleViolationException("Choose a different worker.", SwapErrorCodes.SameWorker);
        }

        var status = await repository.GetAgencyWorkerStatusAsync(agencyId, newWorkerId, cancellationToken)
            ?? throw new BusinessRuleViolationException("The worker is not a worker of this agency.", SwapErrorCodes.WorkerNotOfAgency);
        if (status is WorkStatus.Locked or WorkStatus.Pending or WorkStatus.Retiring or WorkStatus.Retired)
        {
            throw new BusinessRuleViolationException("The worker cannot take jobs now.", SwapErrorCodes.WorkerNotAvailable);
        }

        var slot = await repository.GetAvailableSlotAsync(newWorkerId, view.SlotDate, view.ShiftCode, cancellationToken)
            ?? throw new BusinessRuleViolationException(
                "The worker has no free roster slot for this shift (put them on the roster first).", SwapErrorCodes.NoSlot);

        var swapped = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await repository.TrySwapAsync(assignmentId, view.SlotId, newWorkerId, slot.SlotId, now, cancellationToken))
            {
                return false;
            }

            // The window was closed by the sweep in the meantime: throw so the swap is rolled back with it.
            return await repository.TryCloseWindowAsync(assignmentId, SwapWindowStatuses.Swapped, newWorkerId, now, cancellationToken)
                ? true
                : throw new BusinessRuleViolationException("The 30-minute window has ended; Dispatch took over.", SwapErrorCodes.WindowClosed);
        }, cancellationToken);
        if (!swapped)
        {
            throw new BusinessRuleViolationException("The slot or the assignment changed; try again.", SwapErrorCodes.SlotTaken);
        }

        _logger.LogInformation("Agency {AgencyId} swapped assignment {AssignmentId} to worker {WorkerId}.", agencyId, assignmentId, newWorkerId);
        window.Status = SwapWindowStatuses.Swapped;
        window.NewWorkerId = newWorkerId;
        window.ClosedAt = now;
        return ToDto(window, view with { WorkerId = newWorkerId }, now);
    }

    /// <summary>
    /// Sweep: every OPEN window whose deadline passed becomes EXPIRED (conditional, once) and <see cref="AgencySwapWindowExpired"/> is
    /// published for Dispatch. Returns how many windows were handed over.
    /// </summary>
    public async Task<int> ExpireDueAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var handed = 0;
        foreach (var window in await repository.ListOpenDueAsync(now, 50, cancellationToken))
        {
            try
            {
                var view = await repository.GetAssignmentAsync(window.AssignmentId, cancellationToken);
                if (view is not null && await ExpireAsync(window, view, now, cancellationToken))
                {
                    handed++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Handing over the swap window of assignment {AssignmentId} failed; the next sweep tries again.", window.AssignmentId);
            }
        }

        return handed;
    }

    private async Task<bool> ExpireAsync(AgencySwapWindow window, SwapAssignmentView view, DateTime now, CancellationToken cancellationToken)
    {
        var closed = await unitOfWork.ExecuteInTransactionAsync(
            () => repository.TryCloseWindowAsync(window.AssignmentId, SwapWindowStatuses.Expired, null, now, cancellationToken), cancellationToken);
        if (!closed)
        {
            return false; // a swap or another sweep won
        }

        await publisher.Publish(
            new AgencySwapWindowExpired(window.AssignmentId, view.OrderId, window.AgencyId, window.Reason, now), cancellationToken);
        return true;
    }

    private async Task<SwapAssignmentView> OwnAssignmentAsync(long assignmentId, int agencyId, CancellationToken cancellationToken)
    {
        var view = await repository.GetAssignmentAsync(assignmentId, cancellationToken);
        if (view is null || view.AgencyId != agencyId)
        {
            throw new NotFoundException("Assignment", assignmentId); // someone else's job looks the same as an unknown one
        }

        return view;
    }

    private DateTime ShiftStartUtc(SwapAssignmentView view) =>
        clock.ToUtc(DateTime.SpecifyKind(view.SlotDate.ToDateTime(view.StartTime), DateTimeKind.Unspecified));

    private SwapWindowDto ToDto(AgencySwapWindow w, SwapAssignmentView view, DateTime now)
    {
        var state = w.Status switch
        {
            SwapWindowStatuses.Open => now > w.DeadlineAt ? "RESCUE_STARTED" : "OPEN",
            SwapWindowStatuses.Swapped => "SWAPPED",
            _ => "RESCUE_STARTED",
        };
        var remaining = state == "OPEN" ? (int)Math.Max(0, Math.Floor((w.DeadlineAt - now).TotalSeconds)) : 0;
        return new SwapWindowDto(w.AssignmentId, view.OrderId, state, w.OpenedAt, w.DeadlineAt, remaining, ShiftStartUtc(view), w.NewWorkerId);
    }
}

/// <summary><c>AGENCY_SWAP_WINDOW.status</c> values.</summary>
public static class SwapWindowStatuses
{
    public const string Open = "OPEN";
    public const string Swapped = "SWAPPED";
    public const string Expired = "EXPIRED";
}
