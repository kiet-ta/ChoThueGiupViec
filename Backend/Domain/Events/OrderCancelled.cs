using MediatR;

namespace CommonService.Domain.Events;

/// <summary>Published by Payments/Booking (M2) when an order is cancelled.</summary>
public record OrderCancelled(
    long OrderId,
    int CustomerId,
    string Reason,
    DateTime CancelledAtUtc) : INotification;
