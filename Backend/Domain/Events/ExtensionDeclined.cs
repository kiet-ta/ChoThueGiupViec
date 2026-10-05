using MediatR;

namespace CommonService.Domain.Events;

/// <summary>Published by Workers (M4) when worker declines order extension; informs Dispatch/Booking (M3).</summary>
public sealed record ExtensionDeclined(
    long ExtensionId,
    long OrderId,
    int WorkerId,
    string Reason,
    DateTime DeclinedAtUtc) : INotification;
