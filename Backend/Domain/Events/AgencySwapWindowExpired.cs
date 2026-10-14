using MediatR;

namespace CommonService.Domain.Events;

/// <summary>
/// Published by Agencies (M5) when the 30-minute self-swap window of an assigned Premium job ended without a swap, or the shift is at most
/// 2 hours away (decisions Q09, spec 2.5 phase 2). Dispatch (M3) takes over with the emergency rescue (BE-M3-07); the agency is not
/// penalised here, M3 applies the SLA penalty with the rescue cost.
/// </summary>
public sealed record AgencySwapWindowExpired(
    long AssignmentId,
    long OrderId,
    int AgencyId,
    string Reason,
    DateTime ExpiredAtUtc) : INotification;
