using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CommonService.Application.Features.Booking;

/// <summary>
/// Booking's answer to <see cref="AssignmentFailed"/> (BR-03 no worker within 10 km, BR-10 no substitute; contract booking.md section 5):
/// a PAID / DISPATCHING / ASSIGNED order becomes CANCELLED (<c>cancel_reason</c> = the event's reason), OrderCancelled is published,
/// and the customer gets a 100 % refund through <see cref="IRefundService"/>. Idempotent: an order already CANCELLED (or not paid yet,
/// or completed) changes nothing and is not refunded twice.
/// Known gap: if the refund fails it is logged as an error and NOT retried (no retry mechanism yet; the order stays cancelled).
/// A PREMIUM order also gives its agency capacity hold back (<see cref="PremiumHoldRelease"/>).
/// </summary>
public sealed class AssignmentFailedHandler(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    IRefundService refunds,
    IAgencyCapacityService capacity,
    IClock clock,
    IPublisher publisher,
    ILogger<AssignmentFailedHandler> logger) : INotificationHandler<AssignmentFailed>
{
    private const int MaxReasonLength = 255;

    public async Task Handle(AssignmentFailed notification, CancellationToken cancellationToken)
    {
        var reason = notification.Reason.Length <= MaxReasonLength ? notification.Reason : notification.Reason[..MaxReasonLength];
        var now = clock.UtcNow;
        OrderCancelled? cancelled = null;
        decimal refundAmount = 0m;
        var serviceTier = ServiceTier.Economy;

        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var order = await orders.GetForUpdateAsync(notification.OrderId, cancellationToken);
            if (order is null)
            {
                logger.LogWarning("AssignmentFailed for unknown order {OrderId} ignored.", notification.OrderId);
                return false;
            }

            if (order.OrderStatus is not (JobOrderStatus.Paid or JobOrderStatus.Dispatching or JobOrderStatus.Assigned))
            {
                // Already cancelled (a repeat), still unpaid, or completed: nothing to cancel and nothing to refund.
                return false;
            }

            order.TransitionTo(JobOrderStatus.Cancelled);
            order.CancelReason = reason;
            order.UpdatedAt = now;
            refundAmount = order.TotalAmount;
            serviceTier = order.ServiceTier;
            cancelled = new OrderCancelled(order.OrderId, order.CustomerId, reason, now);
            return true;
        }, cancellationToken);

        if (cancelled is null)
        {
            return;
        }

        await PremiumHoldRelease.ReleaseAsync(capacity, serviceTier, notification.OrderId, logger);
        await publisher.Publish(cancelled, cancellationToken);

        var refund = await refunds.RefundAsync(new RefundRequest(notification.OrderId, refundAmount, reason), cancellationToken);
        if (!refund.Succeeded)
        {
            logger.LogError(
                "Order {OrderId} was cancelled after AssignmentFailed but the 100 % refund failed: {Message}. It must be refunded by hand until a retry exists.",
                notification.OrderId, refund.Message);
        }
    }
}
