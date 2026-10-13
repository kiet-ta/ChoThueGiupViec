using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CommonService.Application.Features.Dispatch;

/// <summary>
/// When an order is cancelled (customer cancel, no worker found, ...), its open offers and not-started assignments are cancelled and their
/// slots go back to AVAILABLE, so no worker is left assigned to a cancelled, refunded order and nobody accepts it afterwards.
/// </summary>
public sealed class OrderCancelledDispatchHandler(DispatchOfferEngine offerEngine, ILogger<OrderCancelledDispatchHandler> logger)
    : INotificationHandler<OrderCancelled>
{
    public async Task Handle(OrderCancelled notification, CancellationToken cancellationToken)
    {
        try
        {
            var released = await offerEngine.ReleaseCancelledOrderAsync(notification.OrderId, cancellationToken);
            if (released > 0)
            {
                logger.LogInformation("Cancelled order {OrderId}: {Count} offer(s)/assignment(s) cancelled and their slots released.",
                    notification.OrderId, released);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Booking publishes OrderCancelled before it refunds: a Dispatch failure must not stop the refund. An offer left open on
            // the cancelled order cannot be accepted (the accept checks the order status).
            logger.LogError(ex, "Releasing the offers of cancelled order {OrderId} failed.", notification.OrderId);
        }
    }
}
