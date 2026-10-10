using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Payments;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Booking.Services;

/// <summary>
/// The customer cancels an order (BE-M2-09, decisions Q15, contract booking.md 3.7): unpaid -> the open QR expires, nothing to refund;
/// paid and not assigned yet, or assigned more than <c>Cancel.FullRefundHoursBefore</c> hours before the shift -> 100 % refund.
/// An ASSIGNED order within that window is refused: its 40 % fee needs open question B10 (no decision yet).
/// The order is CANCELLED in one transaction, OrderCancelled is published after the commit, then the refund is asked.
/// Known gaps: a failed refund is reported but not retried; the Premium capacity hold is not released (Scope exception #257).
/// </summary>
public sealed class OrderCancellationService(
    IOrderRepository orders,
    IPaymentRepository payments,
    IUnitOfWork unitOfWork,
    IRefundService refunds,
    IClock clock,
    IPublisher publisher,
    IOptions<BusinessRules> rules,
    ILogger<OrderCancellationService> logger) : IOrderCancellationService
{
    private const int MaxReasonLength = 255;
    private readonly BusinessRules _rules = rules.Value;

    public async Task<CancelOrderResult> CancelAsync(int customerId, long orderId, CancelOrderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > MaxReasonLength)
        {
            throw new ValidationException("reason", $"reason is required and must be at most {MaxReasonLength} characters.");
        }

        var now = clock.UtcNow;
        var refundAmount = 0m;
        OrderCancelled? cancelled = null;
        CreatedOrder? result = null;

        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var order = await orders.GetForUpdateAsync(orderId, cancellationToken);
            if (order is null || order.CustomerId != customerId)
            {
                throw new NotFoundException("Order", orderId);
            }

            var previous = order.OrderStatus;
            EnsureCancellable(order, now);

            if (previous == JobOrderStatus.PendingPayment)
            {
                // Nothing was paid: the open QR must not be payable any more (contract payments.md 3.3).
                var open = await payments.FindPendingOrderPaymentAsync(orderId, cancellationToken);
                if (open is not null)
                {
                    await payments.TryMarkExpiredAsync(open.PaymentId, "{\"source\":\"customer-cancel\"}", cancellationToken);
                }
            }
            else
            {
                refundAmount = order.TotalAmount;
            }

            order.TransitionTo(JobOrderStatus.Cancelled);
            order.CancelReason = reason;
            order.UpdatedAt = now;
            cancelled = new OrderCancelled(order.OrderId, order.CustomerId, reason, now);
            result = ToOrder(order);
            return true;
        }, cancellationToken);

        await publisher.Publish(cancelled!, cancellationToken);

        string? refundProblem = null;
        if (refundAmount > 0)
        {
            var refund = await refunds.RefundAsync(new RefundRequest(orderId, refundAmount, reason), cancellationToken);
            if (!refund.Succeeded)
            {
                refundProblem = refund.Message ?? "The refund could not be completed.";
                logger.LogError(
                    "Order {OrderId} was cancelled by its customer but the 100 % refund failed: {Message}. It must be refunded by hand until a retry exists.",
                    orderId, refundProblem);
            }
        }

        return new CancelOrderResult(result!, refundProblem);
    }

    private void EnsureCancellable(JobOrder order, DateTime now)
    {
        switch (order.OrderStatus)
        {
            case JobOrderStatus.PendingPayment or JobOrderStatus.Paid or JobOrderStatus.Dispatching:
                return;

            case JobOrderStatus.Assigned:
                var start = BookingWindowPolicy.GetShiftStartUtc(order.ScheduledDate, order.ShiftCode, clock);
                if (start is not null && start.Value - now > TimeSpan.FromHours(_rules.Cancel.FullRefundHoursBefore))
                {
                    return;
                }

                // Within the late-cancel window (or an unknown shift): the 40 % fee is open question B10, not decided yet.
                throw new BusinessRuleViolationException(
                    $"An assigned order cannot be cancelled within {_rules.Cancel.FullRefundHoursBefore} hours of the shift yet.",
                    CancelErrorCodes.InvalidState);

            default:
                throw new BusinessRuleViolationException("The order can no longer be cancelled.", CancelErrorCodes.InvalidState);
        }
    }

    private CreatedOrder ToOrder(JobOrder order)
    {
        var start = BookingWindowPolicy.GetShiftStartUtc(order.ScheduledDate, order.ShiftCode, clock)
            ?? clock.ToUtc(DateTime.SpecifyKind(order.ScheduledDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified));
        var end = BookingShifts.GetLocalTimes(order.ShiftCode) is { } times
            ? clock.ToUtc(DateTime.SpecifyKind(order.ScheduledDate.ToDateTime(times.End), DateTimeKind.Unspecified))
            : start;

        return new CreatedOrder(
            order.OrderId, order.OrderCode, order.ServiceTier, order.AddressId, order.ScheduledDate, order.ShiftCode, start, end,
            order.AreaSnapshotM2, order.RequiredWorkers, order.RequiredSkill, order.TotalAmount, order.OrderStatus,
            order.CustomerNote, order.CancelReason, PaymentDeadlineAt: null, order.CreatedAt, order.UpdatedAt);
    }
}
