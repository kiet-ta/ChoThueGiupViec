using CommonService.Domain.Enums;
using MediatR;

namespace CommonService.Domain.Events;

/// <summary>Published by Disputes (M6) when a dispute ticket is resolved; triggers escrow penalties (M5) and/or refunds (M2).</summary>
public sealed record DisputeResolved(
    int DisputeId,
    long AssignmentId,
    FaultParty? FaultParty,
    decimal CustomerRefundAmount,
    string ResolutionNotes,
    DateTime ResolvedAtUtc) : INotification;
