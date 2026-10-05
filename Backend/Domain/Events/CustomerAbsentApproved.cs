using MediatR;

namespace CommonService.Domain.Events;

/// <summary>Published by Admin (M6) upon approving customer absence; triggers 40% fee to worker and 60% refund to customer (M2, M3, M4).</summary>
public sealed record CustomerAbsentApproved(
    long AssignmentId,
    long OrderId,
    int WorkerId,
    decimal CompensationAmount,
    decimal RefundAmount,
    DateTime ApprovedAtUtc) : INotification;
