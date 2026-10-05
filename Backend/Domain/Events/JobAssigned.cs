using MediatR;

namespace CommonService.Domain.Events;

/// <summary>Published by Dispatch (M3) when a worker is assigned to a job seat.</summary>
public sealed record JobAssigned(
    long AssignmentId,
    long OrderId,
    int WorkerId,
    int? AgencyId,
    int SlotId,
    DateTime AssignedAtUtc) : INotification;
