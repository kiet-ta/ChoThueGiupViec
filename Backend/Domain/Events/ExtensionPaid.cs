using MediatR;

namespace CommonService.Domain.Events;

/// <summary>Published by Payments (M2) when an extension payment is confirmed; informs Workers (M4) to proceed.</summary>
public sealed record ExtensionPaid(
    long ExtensionId,
    long OrderId,
    int WorkerId,
    decimal ExtraHours,
    decimal ExtraAmount,
    DateTime PaidAtUtc) : INotification;
