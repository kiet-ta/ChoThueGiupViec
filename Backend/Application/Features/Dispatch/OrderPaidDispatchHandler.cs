using CommonService.Application.Common.Options;
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

        // Map date and shift
        var date = DateOnly.FromDateTime(notification.ScheduledDate);

        // Location coordinates will be resolved from Order/CustomerAddress; default center used for demo/contract
        var orderLocation = new GeoPoint(10.762622, 106.660172);

        await _offerEngine.StartDispatchForOrderAsync(
            orderId: notification.OrderId,
            customerId: notification.CustomerId,
            serviceTier: ServiceTier.Economy, // Default tier for standard orders
            date: date,
            shiftCode: notification.ShiftCode,
            location: orderLocation,
            grossAmount: notification.Amount,
            assignmentSeq: 1,
            cancellationToken: cancellationToken
        );
    }
}
