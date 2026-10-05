using MediatR;

namespace CommonService.Domain.Events;

/// <summary>Published by Payments (M2) when an order is refunded.</summary>
public sealed record OrderRefunded(
    long OrderId,
    int CustomerId,
    decimal Amount,
    string Reason,
    DateTime RefundedAtUtc) : INotification;
