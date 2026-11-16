using System.Globalization;
using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Booking;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Dispatch;

/// <summary>What the cancel transaction did (decisions D35).</summary>
/// <param name="CancelCount">The worker's <c>CANCELLED_BY_WORKER</c> assignments inside the window, this one included.</param>
/// <param name="WorkerLocked">True when this cancel reached <c>Cancel.WorkerCancelLockCount</c> and the freelancer is now LOCKED.</param>
/// <param name="AutoAcceptTurnedOff">True when the worker's "Tự động nhận đơn" switch was on and this cancel turned it off (D33 item 5).</param>
public record WorkerCancelCommit(int CancelCount, bool WorkerLocked, bool AutoAcceptTurnedOff);

/// <summary>Persistence of the freelancer cancel flow (decisions Q15, D35). Every write is conditional, inside one transaction.</summary>
public interface IWorkerCancelRepository
{
    Task<JobOrder?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default);

    /// <summary>The worker's <c>CANCELLED_BY_WORKER</c> assignments cancelled at or after <paramref name="sinceUtc"/>.</summary>
    Task<int> CountWorkerCancelsSinceAsync(int workerId, DateTime sinceUtc, CancellationToken cancellationToken = default);

    Task<bool> IsAutoAcceptOnAsync(int workerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// In one transaction: <c>ASSIGNED -&gt; CANCELLED_BY_WORKER</c> (only if still ASSIGNED and the worker's), the slot <c>BOOKED -&gt; AVAILABLE</c>,
    /// the worker's auto-accept switch off, the order <c>ASSIGNED | AWAITING_FALLBACK -&gt; DISPATCHING</c> (D7), and the freelancer LOCKED
    /// when the cancellations since <paramref name="windowStartUtc"/> reach <paramref name="lockCount"/>. Null when the assignment was
    /// no longer ASSIGNED (a concurrent change): nothing is written.
    /// </summary>
    Task<WorkerCancelCommit?> TryCancelAsync(
        long assignmentId, int workerId, DateTime cancelledAtUtc, DateTime windowStartUtc, int lockCount, CancellationToken cancellationToken = default);

    /// <summary>DISPATCHING orders scheduled between the two dates that lost a seat to a worker cancel (the cutoff sweep).</summary>
    Task<IReadOnlyList<JobOrder>> GetRedispatchingAfterWorkerCancelAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);

    /// <summary>True when seat <paramref name="assignmentSeq"/> of the order was cancelled by its worker (any seat when null).</summary>
    Task<bool> HasWorkerCancelledSeatAsync(long orderId, byte? assignmentSeq, CancellationToken cancellationToken = default);
}

/// <summary>Contract dispatch.md 3.1: what the confirm sheet shows before the worker cancels.</summary>
public record WorkerCancelPreviewDto(
    long AssignmentId,
    int CancelCountInWindow,
    int LockCount,
    int WindowDays,
    bool WillLockAccount,
    bool AutoAcceptOn,
    DateTime JobStartAt,
    bool SubstituteSearchPossible,
    // Decision D38: a Super-Freelancer rescue seat is cancelled with no penalty and the order is cancelled (refund, apology).
    bool RescueFreeCancel = false);

/// <summary>Contract dispatch.md 3.1 cancel response.</summary>
public record WorkerCancelResultDto(
    long AssignmentId,
    string Status,
    int CancelCountInWindow,
    int LockCount,
    int WindowDays,
    bool AccountLocked,
    string OrderOutcome,
    DateTime? SubstituteSearchUntil);

public static class WorkerCancelOutcomes
{
    public const string Redispatching = "REDISPATCHING";
    public const string CancelledRefunded = "CANCELLED_REFUNDED";
}

public static class WorkerCancelReasons
{
    /// <summary><c>cancel_reason</c> of an order whose cancelled seat found no substitute by the cutoff.</summary>
    public const string NoSubstitute = "WORKER_CANCELLED_NO_SUBSTITUTE";
}

/// <summary>
/// Decisions Q15 + D35 (DISPATCH-CANCEL-01): a freelancer cancels an accepted job before it starts. The seat is released, the order goes
/// back to DISPATCHING and only that seat is re-dispatched (D7) without the cancelling worker (they already had the order). The customer is
/// told at once. A seat still open at <c>job start - Cancel.RedispatchCutoffMinutes</c> fails the order through
/// <see cref="AssignmentFailed"/>, whose Booking handler cancels it and refunds 100 % automatically. Every cancel counts 1 towards the
/// lock (<c>Cancel.WorkerCancelLockCount</c> within <c>Cancel.WorkerCancelWindowDays</c>); there is no money penalty. Agency staff
/// follow the SLA flow (Q09) and cannot use this.
/// </summary>
public class WorkerCancelService(
    IWorkerCancelRepository repository,
    IDispatchRepository dispatchRepository,
    DispatchOfferEngine engine,
    IClock clock,
    IMediator mediator,
    IOptions<BusinessRules> rules,
    ICurrentUser currentUser,
    INotificationService? notifications = null,
    ILogger<WorkerCancelService>? logger = null,
    RescueCancelService? rescue = null)
{
    /// <summary>provenance: the order's <c>cancel_reason</c> is NVARCHAR(255); the worker's reason is kept to the same length.</summary>
    public const int MaxReasonLength = 255;

    private readonly BusinessRules _rules = rules?.Value ?? new BusinessRules();
    private readonly ILogger _logger = logger ?? (ILogger)NullLogger<WorkerCancelService>.Instance;

    private int WorkerId => currentUser.UserId is int id && currentUser.Role == UserRole.Worker
        ? id
        : throw new ForbiddenAccessException("Authenticated worker user ID is required.");

    private int LockCount => Math.Max(1, _rules.Cancel.WorkerCancelLockCount);
    private int WindowDays => Math.Max(1, _rules.Cancel.WorkerCancelWindowDays);
    private TimeSpan Cutoff => TimeSpan.FromMinutes(Math.Max(0, _rules.Cancel.RedispatchCutoffMinutes));

    public async Task<WorkerCancelPreviewDto> PreviewAsync(long assignmentId, CancellationToken cancellationToken = default)
    {
        var workerId = WorkerId;
        var (assignment, start) = await RequireCancellableAsync(assignmentId, workerId, cancellationToken);
        var now = clock.UtcNow;
        var count = await repository.CountWorkerCancelsSinceAsync(workerId, now.AddDays(-WindowDays), cancellationToken);
        var autoAccept = await repository.IsAutoAcceptOnAsync(workerId, cancellationToken);
        if (rescue is not null && RescueCancelService.IsRescue(assignment))
        {
            return rescue.Preview(assignment, start, count, LockCount, WindowDays, autoAccept);
        }

        return new WorkerCancelPreviewDto(
            assignment.AssignmentId, count, LockCount, WindowDays, count + 1 >= LockCount, autoAccept, start, now < start - Cutoff);
    }

    public async Task<WorkerCancelResultDto> CancelAsync(long assignmentId, string? reason, CancellationToken cancellationToken = default)
    {
        var clean = (reason ?? string.Empty).Trim();
        if (clean.Length == 0 || clean.Length > MaxReasonLength)
        {
            throw new ArgumentException($"reason là bắt buộc, tối đa {MaxReasonLength} ký tự.");
        }

        var workerId = WorkerId;
        var (assignment, start) = await RequireCancellableAsync(assignmentId, workerId, cancellationToken);
        var now = clock.UtcNow;

        // Decision D38: a rescue seat is cancelled with no penalty and the order is cancelled (the checks of the deadline are in RequireCancellableAsync).
        if (rescue is not null && RescueCancelService.IsRescue(assignment))
        {
            var count = await repository.CountWorkerCancelsSinceAsync(workerId, now.AddDays(-WindowDays), cancellationToken);
            return await rescue.CancelAsync(assignment, workerId, count, LockCount, WindowDays, cancellationToken);
        }

        var commit = await repository.TryCancelAsync(assignmentId, workerId, now, now.AddDays(-WindowDays), LockCount, cancellationToken)
            ?? throw new BusinessRuleViolationException("Ca này không còn ở trạng thái đã nhận.", DispatchErrorCodes.InvalidAssignmentState);

        _logger.LogInformation(
            "Worker {WorkerId} cancelled assignment {AssignmentId} of order {OrderId} ({Count}/{Lock} in {Days} days, locked: {Locked}). Reason: {Reason}",
            workerId, assignmentId, assignment.OrderId, commit.CancelCount, LockCount, WindowDays, commit.WorkerLocked, clean);

        await mediator.Publish(new AssignmentCancelledByWorker(
            assignmentId, assignment.OrderId, workerId, assignment.AssignmentSeq, clean, commit.CancelCount, commit.WorkerLocked, now), cancellationToken);

        if (commit.AutoAcceptTurnedOff)
        {
            // D33 item 5 (#891): tell the worker the switch is off, so new offers wait for their answer again.
            await NotifyAsync(UserRole.Worker, workerId, assignment.OrderId, WorkerCancelNotificationTopics.AutoAcceptOff,
                "Tự động nhận đơn đã tắt",
                "Tự động nhận đơn đã tắt vì bạn vừa hủy ca. Bạn có thể bật lại khi sẵn sàng nhận đơn.",
                cancellationToken, assignmentId);
        }

        var searchUntil = start - Cutoff;
        string outcome;
        if (now >= searchUntil)
        {
            // Too close to the start to find someone: the order fails now (Booking cancels it and refunds 100 %).
            await mediator.Publish(new AssignmentFailed(assignment.OrderId, WorkerCancelReasons.NoSubstitute, now), cancellationToken);
            outcome = WorkerCancelOutcomes.CancelledRefunded;
        }
        else
        {
            await NotifyAsync(UserRole.Customer, assignment.CustomerId, assignment.OrderId, WorkerCancelNotificationTopics.WorkerCancelled,
                "Thợ đã hủy ca",
                $"Thợ đã hủy ca của bạn. Hệ thống đang tìm thợ thay đến {Local(searchUntil)}; nếu không tìm được, đơn sẽ được hủy và hoàn tiền 100 %.",
                cancellationToken);
            try
            {
                // Only the missing seat is offered (DispatchPaidOrderAsync skips taken seats); the cancelling worker had the order already.
                await engine.DispatchPaidOrderAsync(assignment.OrderId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The cancel stands; RecoverMissingSeatsAsync offers the seat again at the next sweep.
                _logger.LogError(ex, "Re-dispatching order {OrderId} after a worker cancel failed.", assignment.OrderId);
            }

            var order = await dispatchRepository.GetOrderContextAsync(assignment.OrderId, cancellationToken);
            outcome = order?.OrderStatus == JobOrderStatus.Cancelled ? WorkerCancelOutcomes.CancelledRefunded : WorkerCancelOutcomes.Redispatching;
        }

        return new WorkerCancelResultDto(
            assignmentId, DbEnum.ToDb(JobAssignmentStatus.CancelledByWorker), commit.CancelCount, LockCount, WindowDays, commit.WorkerLocked,
            outcome, outcome == WorkerCancelOutcomes.Redispatching ? searchUntil : null);
    }

    /// <summary>
    /// The cutoff sweep (run by the dispatch background worker): every order still re-dispatching a seat lost to a worker cancel at
    /// <c>job start - Cancel.RedispatchCutoffMinutes</c> fails, so Booking cancels it and refunds 100 %. Returns how many orders failed.
    /// </summary>
    public async Task<int> FailUnfilledAtCutoffAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var today = clock.LocalToday;
        var orders = await repository.GetRedispatchingAfterWorkerCancelAsync(today.AddDays(-1), today.AddDays(1), cancellationToken);
        var failed = 0;
        foreach (var order in orders)
        {
            var start = BookingWindowPolicy.GetJobStartUtc(order, clock);
            if (start is null || now < start.Value - Cutoff)
            {
                continue;
            }

            _logger.LogWarning("Order {OrderId} found no substitute worker by the cutoff; failing it (automatic 100 % refund).", order.OrderId);
            await mediator.Publish(new AssignmentFailed(order.OrderId, WorkerCancelReasons.NoSubstitute, now), cancellationToken);
            failed++;
        }

        return failed;
    }

    private async Task<(JobAssignment Assignment, DateTime Start)> RequireCancellableAsync(
        long assignmentId, int workerId, CancellationToken cancellationToken)
    {
        var assignment = await dispatchRepository.GetAssignmentByIdAsync(assignmentId, cancellationToken);
        if (assignment == null || assignment.WorkerId != workerId)
        {
            throw new NotFoundException("Không tìm thấy ca làm việc của thợ.");
        }

        if (assignment.AgencyId != null)
        {
            throw new ForbiddenAccessException("Thợ doanh nghiệp đối tác báo vắng qua doanh nghiệp (SLA), không hủy ca trên ứng dụng.");
        }

        if (assignment.AssignmentStatus != JobAssignmentStatus.Assigned)
        {
            throw new BusinessRuleViolationException("Chỉ hủy được ca đã nhận và chưa check-in.", DispatchErrorCodes.InvalidAssignmentState);
        }

        var order = await repository.GetOrderAsync(assignment.OrderId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy đơn của ca làm việc.");
        var start = BookingWindowPolicy.GetJobStartUtc(order, clock);

        // Decision D38: a rescue seat may be cancelled until its own check-in deadline, which can be after the shift start.
        if (rescue is not null && RescueCancelService.IsRescue(assignment) && start is not null)
        {
            rescue.RequireBeforeDeadline(assignment, start.Value);
            return (assignment, start.Value);
        }

        if (start is null || clock.UtcNow >= start.Value)
        {
            throw new BusinessRuleViolationException("Ca đã đến giờ bắt đầu, không thể hủy.", DispatchErrorCodes.JobAlreadyStarted);
        }

        return (assignment, start.Value);
    }

    private string Local(DateTime utc) => clock.ToLocal(utc).ToString("HH:mm dd/MM", CultureInfo.InvariantCulture);

    private async Task NotifyAsync(
        UserRole role, int recipientId, long orderId, string topic, string title, string body, CancellationToken cancellationToken,
        long? assignmentId = null)
    {
        if (notifications == null)
        {
            return;
        }

        var data = new Dictionary<string, string> { ["orderId"] = orderId.ToString(CultureInfo.InvariantCulture) };
        if (assignmentId != null)
        {
            data["assignmentId"] = assignmentId.Value.ToString(CultureInfo.InvariantCulture);
        }

        try
        {
            await notifications.SendAsync(new NotificationMessage(role, recipientId, topic, title, body, data), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed notice never undoes the cancel.
            _logger.LogWarning(ex, "Could not notify {Role} {RecipientId} about order {OrderId} ({Topic}).", role, recipientId, orderId, topic);
        }
    }
}

public static class WorkerCancelNotificationTopics
{
    public const string WorkerCancelled = "dispatch.worker_cancelled";
    public const string SubstituteFound = "dispatch.substitute_found";
    public const string SubstituteNotFound = "dispatch.substitute_not_found";

    /// <summary>To the worker: the cancel turned their "Tự động nhận đơn" switch off (D33 item 5, #891).</summary>
    public const string AutoAcceptOff = "dispatch.auto_accept_off";
}

/// <summary>
/// Decisions D35: tells the customer how the search for a substitute ended. A <see cref="JobAssigned"/> on a seat that a worker cancelled
/// means a substitute accepted; an <see cref="AssignmentFailed"/> on an order that lost a seat to a worker cancel means none was found and
/// the order is cancelled with a 100 % refund. Never throws: a failed notification must not undo dispatch.
/// </summary>
public class WorkerCancelNotificationHandler(
    IWorkerCancelRepository repository,
    IDispatchRepository dispatchRepository,
    INotificationService notifications,
    ILogger<WorkerCancelNotificationHandler> logger) : INotificationHandler<JobAssigned>, INotificationHandler<AssignmentFailed>
{
    public async Task Handle(JobAssigned notification, CancellationToken cancellationToken)
    {
        try
        {
            var assignment = await dispatchRepository.GetAssignmentByIdAsync(notification.AssignmentId, cancellationToken);
            if (assignment == null || !await repository.HasWorkerCancelledSeatAsync(assignment.OrderId, assignment.AssignmentSeq, cancellationToken))
            {
                return;
            }

            await SendAsync(assignment.CustomerId, assignment.OrderId, WorkerCancelNotificationTopics.SubstituteFound,
                "Đã có thợ thay", "Đã có thợ mới nhận ca của bạn. Mở ứng dụng để xem thông tin thợ.", cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Substitute notice for assignment {AssignmentId} failed.", notification.AssignmentId);
        }
    }

    public async Task Handle(AssignmentFailed notification, CancellationToken cancellationToken)
    {
        try
        {
            if (!await repository.HasWorkerCancelledSeatAsync(notification.OrderId, null, cancellationToken))
            {
                return;
            }

            var order = await dispatchRepository.GetOrderContextAsync(notification.OrderId, cancellationToken);
            if (order == null)
            {
                return;
            }

            await SendAsync(order.CustomerId, order.OrderId, WorkerCancelNotificationTopics.SubstituteNotFound,
                "Không tìm được thợ thay", "Không tìm được thợ thay cho ca của bạn. Đơn đã được hủy và hoàn tiền 100 %.", cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No-substitute notice for order {OrderId} failed.", notification.OrderId);
        }
    }

    private Task SendAsync(int customerId, long orderId, string topic, string title, string body, CancellationToken cancellationToken) =>
        notifications.SendAsync(new NotificationMessage(
            UserRole.Customer, customerId, topic, title, body,
            new Dictionary<string, string> { ["orderId"] = orderId.ToString(CultureInfo.InvariantCulture) }), cancellationToken);
}
