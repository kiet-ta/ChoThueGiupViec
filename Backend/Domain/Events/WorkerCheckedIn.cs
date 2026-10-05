using MediatR;

namespace CommonService.Domain.Events;

/// <summary>Published by Dispatch/Field (M3) when a worker successfully checks in at the job site.</summary>
public sealed record WorkerCheckedIn(
    long AssignmentId,
    long OrderId,
    int WorkerId,
    DateTime CheckedInAtUtc,
    double DistanceMeters) : INotification;
