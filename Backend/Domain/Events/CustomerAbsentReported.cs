using MediatR;

namespace CommonService.Domain.Events;

/// <summary>Published by Dispatch/Field (M3) when a worker reports the customer absent (BR-05); consumed by Admin/Disputes (M6).</summary>
public sealed record CustomerAbsentReported(
    long AssignmentId,
    long OrderId,
    int WorkerId,
    DateTime ReportedAtUtc) : INotification;
