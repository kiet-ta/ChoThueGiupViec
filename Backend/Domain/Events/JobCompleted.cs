using MediatR;

namespace CommonService.Domain.Events;

/// <summary>Published by Workers/Quality (M4) when work is verified and completed; triggers payout eligibility and rating window (M6, M5).</summary>
public sealed record JobCompleted(
    long AssignmentId,
    long OrderId,
    int WorkerId,
    int? AgencyId,
    decimal PayoutAmount,
    DateTime CompletedAtUtc) : INotification;
