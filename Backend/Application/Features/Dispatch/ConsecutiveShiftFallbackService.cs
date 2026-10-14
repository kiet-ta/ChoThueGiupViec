using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Dispatch;

/// <summary>
/// Shift order for the BR-02 fallback (one worker, two consecutive shifts). The codes and start times are Booking's
/// (<c>BookingShifts</c>: 08:00, 13:00, 17:30); Dispatch does not reference the Booking feature, so they are repeated here.
/// </summary>
public static class ConsecutiveShifts
{
    private const string Morning = "SHIFT_MORNING";
    private const string Afternoon = "SHIFT_AFTERNOON";
    private const string Evening = "SHIFT_EVENING";

    /// <summary>The shift right after <paramref name="shiftCode"/> on the same day, or null (evening, or an unknown code).</summary>
    public static string? Next(string? shiftCode) => shiftCode switch
    {
        Morning => Afternoon,
        Afternoon => Evening,
        _ => null,
    };

    /// <summary>Local start time of the shift, or null for an unknown code.</summary>
    public static TimeOnly? StartLocal(string? shiftCode) => shiftCode switch
    {
        Morning => new TimeOnly(8, 0),
        Afternoon => new TimeOnly(13, 0),
        Evening => new TimeOnly(17, 30),
        _ => null,
    };
}

/// <summary>What the Customer app shows while an order is in AWAITING_FALLBACK (contract dispatch.md 3.2).</summary>
/// <param name="State">
/// <c>AWAITING_CUSTOMER</c> (the customer has not answered), <c>AWAITING_WORKER</c> (the customer agreed, the worker has not confirmed)
/// or <c>NOT_WAITING</c> (the order is in another state).
/// </param>
/// <param name="DeadlineUtc">AWAITING_CUSTOMER: the shift start (silence until then is a "no"). AWAITING_WORKER: end of the worker's window.</param>
public sealed record FallbackStatusDto(
    long OrderId,
    string State,
    int? WorkerId,
    string? SecondShiftCode,
    DateOnly? SecondShiftDate,
    DateTime? DeadlineUtc);

/// <param name="Outcome"><c>WORKER_ASKED</c> (the customer agreed; the worker must confirm) or <c>CANCELLED</c> (declined or not possible: cancelled and refunded).</param>
public sealed record FallbackResponseDto(long OrderId, string Outcome, DateTime? WorkerDeadlineUtc, string? Message);

/// <summary>
/// BR-02 fallback, customer side (contract dispatch.md 3.2, policy CustomerOnly): the order has one accepted seat and no candidate for the
/// other, so it waits in AWAITING_FALLBACK. The customer agrees (the accepted worker is offered the second, consecutive shift and must
/// confirm it within <c>Dispatch.FallbackConfirmSeconds</c>) or declines (AssignmentFailed: cancel + 100 % refund). Silence until the
/// shift starts is a decline. An order that is not the caller's is a 404; an order that is not waiting is a 409.
/// </summary>
public sealed class ConsecutiveShiftFallbackService(
    IDispatchRepository repository,
    IClock clock,
    IMediator mediator,
    ICurrentUser currentUser,
    IOptions<BusinessRules> rules,
    ILogger<ConsecutiveShiftFallbackService>? logger = null)
{
    private readonly ILogger<ConsecutiveShiftFallbackService> _logger = logger ?? NullLogger<ConsecutiveShiftFallbackService>.Instance;

    private int ConfirmSeconds => rules.Value.Dispatch.FallbackConfirmSeconds > 0 ? rules.Value.Dispatch.FallbackConfirmSeconds : 120;

    private int CustomerId => currentUser.UserId is int id && currentUser.Role == UserRole.Customer
        ? id
        : throw new ForbiddenAccessException("Authenticated customer user ID is required.");

    public async Task<FallbackStatusDto> GetAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var order = await RequireOwnOrderAsync(orderId, CustomerId, cancellationToken);
        if (order.OrderStatus != JobOrderStatus.AwaitingFallback)
        {
            return new FallbackStatusDto(orderId, "NOT_WAITING", null, null, null, null);
        }

        var seats = await ReadSeatsAsync(orderId, cancellationToken);
        var next = ConsecutiveShifts.Next(order.ShiftCode);
        if (seats.WorkerOffer is { } offer)
        {
            return new FallbackStatusDto(
                orderId, "AWAITING_WORKER", offer.WorkerId, next, order.ScheduledDate, offer.CreatedAt.AddSeconds(ConfirmSeconds));
        }

        return new FallbackStatusDto(
            orderId, "AWAITING_CUSTOMER", seats.Filled?.WorkerId, next, order.ScheduledDate, ShiftStartUtc(order));
    }

    public async Task<FallbackResponseDto> RespondAsync(long orderId, bool accept, CancellationToken cancellationToken = default)
    {
        var order = await RequireOwnOrderAsync(orderId, CustomerId, cancellationToken);
        if (order.OrderStatus != JobOrderStatus.AwaitingFallback)
        {
            throw new BusinessRuleViolationException(
                "The order is not waiting for a choice about two consecutive shifts.", DispatchErrorCodes.FallbackNotWaiting);
        }

        var seats = await ReadSeatsAsync(orderId, cancellationToken);
        if (seats.WorkerOffer != null)
        {
            throw new BusinessRuleViolationException(
                "The choice was already made; the worker has not confirmed yet.", DispatchErrorCodes.FallbackAlreadyAnswered);
        }

        if (!accept)
        {
            return await CancelAsync(orderId, "Khách từ chối 1 thợ làm 2 ca liên tiếp (BR-02).", cancellationToken);
        }

        var nowUtc = clock.UtcNow;
        if (ShiftStartUtc(order) is { } start && nowUtc >= start)
        {
            return await CancelAsync(orderId, "Khách không trả lời trước giờ bắt đầu ca (BR-02).", cancellationToken);
        }

        var filled = seats.Filled;
        var next = ConsecutiveShifts.Next(order.ShiftCode);
        var slot = filled == null || next == null
            ? null
            : await repository.GetAvailableSlotAsync(filled.WorkerId, order.ScheduledDate, next, cancellationToken);
        if (filled == null || slot == null)
        {
            return await CancelAsync(orderId, "Thợ không còn ca liền sau để làm 2 ca liên tiếp (BR-02).", cancellationToken);
        }

        var missingSeq = (byte)(filled.AssignmentSeq == 1 ? 2 : 1);
        var offer = new JobAssignment
        {
            OrderId = orderId,
            CustomerId = order.CustomerId,
            WorkerId = filled.WorkerId,
            AgencyId = filled.AgencyId,
            SlotId = slot.SlotId,
            ServiceTier = filled.ServiceTier,
            AssignmentSeq = missingSeq,
            DispatchRadiusKm = filled.DispatchRadiusKm,
            MatchingScore = filled.MatchingScore,
            GrossAmount = filled.GrossAmount,
            CommissionRate = filled.CommissionRate,
            PayoutAmount = filled.PayoutAmount,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };

        var created = await repository.TryCreateOfferAsync(offer, cancellationToken);
        if (created == null)
        {
            return await CancelAsync(orderId, "Ca liền sau của thợ vừa bị giữ bởi việc khác (BR-02).", cancellationToken);
        }

        _logger.LogInformation(
            "Order {OrderId}: customer agreed; worker {WorkerId} is offered the consecutive shift {Next} (assignment {AssignmentId}).",
            orderId, filled.WorkerId, next, created.AssignmentId);

        return new FallbackResponseDto(orderId, "WORKER_ASKED", nowUtc.AddSeconds(ConfirmSeconds), null);
    }

    /// <summary>
    /// Silence until the shift starts is a decline: every AWAITING_FALLBACK order whose customer has not answered by the shift start is
    /// cancelled and refunded (AssignmentFailed). Returns how many orders were failed. Called by the expiry sweep.
    /// </summary>
    public async Task<int> ExpireUnansweredAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = clock.UtcNow;
        var failed = 0;
        foreach (var order in await repository.GetAwaitingFallbackOrdersAsync(cancellationToken))
        {
            try
            {
                if (ShiftStartUtc(order) is not { } start || nowUtc < start)
                {
                    continue;
                }

                await mediator.Publish(
                    new AssignmentFailed(order.OrderId, "Khách không trả lời trước giờ bắt đầu ca (BR-02).", nowUtc), cancellationToken);
                failed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failing the unanswered fallback of order {OrderId} did not complete; it is tried again at the next sweep.", order.OrderId);
            }
        }

        return failed;
    }

    private async Task<FallbackResponseDto> CancelAsync(long orderId, string reason, CancellationToken cancellationToken)
    {
        await mediator.Publish(new AssignmentFailed(orderId, reason, clock.UtcNow), cancellationToken);
        return new FallbackResponseDto(orderId, "CANCELLED", null, reason);
    }

    private DateTime? ShiftStartUtc(DispatchOrderContext order) =>
        ConsecutiveShifts.StartLocal(order.ShiftCode) is { } local
            ? clock.ToUtc(DateTime.SpecifyKind(order.ScheduledDate.ToDateTime(local), DateTimeKind.Unspecified))
            : null;

    private async Task<DispatchOrderContext> RequireOwnOrderAsync(long orderId, int customerId, CancellationToken cancellationToken)
    {
        var order = await repository.GetOrderContextAsync(orderId, cancellationToken);
        if (order == null || order.CustomerId != customerId)
        {
            throw new NotFoundException("Order", orderId);
        }

        return order;
    }

    private async Task<(JobAssignment? Filled, JobAssignment? WorkerOffer)> ReadSeatsAsync(long orderId, CancellationToken cancellationToken)
    {
        var live = await repository.GetLiveAssignmentsOfOrderAsync(orderId, cancellationToken);
        var filled = live.FirstOrDefault(a => a.AssignmentStatus == JobAssignmentStatus.Assigned);
        var offer = live.FirstOrDefault(a => a.AssignmentStatus == JobAssignmentStatus.Offered
            && filled != null && a.WorkerId == filled.WorkerId);
        return (filled, offer);
    }
}
