using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Application.Features.Dispatch;

/// <summary>
/// Listens to OrderPaid event (published by Payments/Booking M2) and triggers the Dispatch Offer sequence (BR-03, Q07).
/// </summary>
public sealed class OrderPaidDispatchHandler : INotificationHandler<OrderPaid>
{
    private readonly DispatchOfferEngine _offerEngine;
    private readonly ILogger<OrderPaidDispatchHandler> _logger;

    public OrderPaidDispatchHandler(
        DispatchOfferEngine offerEngine,
        ILogger<OrderPaidDispatchHandler>? logger = null)
    {
        _offerEngine = offerEngine ?? throw new ArgumentNullException(nameof(offerEngine));
        _logger = logger ?? NullLogger<OrderPaidDispatchHandler>.Instance;
    }

    public async Task Handle(OrderPaid notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Received OrderPaid for OrderId={OrderId}. Initiating dispatch offer sequence.",
            notification.OrderId);

        // Tier, address coordinates and seats are read from the order itself (Premium -> agency hold, BR-02 -> 2 seats).
        await _offerEngine.DispatchPaidOrderAsync(notification.OrderId, cancellationToken);
    }
}
