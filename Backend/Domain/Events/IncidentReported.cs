using MediatR;

namespace CommonService.Domain.Events;

/// <summary>Published by Dispatch/Field (M3) when an on-site incident is reported (BR-10).</summary>
public sealed record IncidentReported(
    long IncidentId,
    long AssignmentId,
    long OrderId,
    int WorkerId,
    string IncidentType,
    string Description,
    DateTime ReportedAtUtc) : INotification;
