using MediatR;

namespace CommonService.Domain.Events;

/// <summary>Published by Dispatch (M3) when dispatch fails across all radius steps; triggers automatic refund (M2).</summary>
public sealed record AssignmentFailed(
    long OrderId,
    string Reason,
    DateTime FailedAtUtc) : INotification;
