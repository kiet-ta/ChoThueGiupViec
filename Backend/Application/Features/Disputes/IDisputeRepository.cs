using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Disputes;

/// <param name="ShiftEndLocal">Shift end as Asia/Ho_Chi_Minh local time (slot date + end time, decision G-3).</param>
public sealed record DisputeAssignmentInfo(
    long AssignmentId, int WorkerId, JobAssignmentStatus Status, DateTime? CompletedAtUtc, DateTime ShiftEndLocal);

public sealed record DisputeOrderInfo(long OrderId, string OrderCode, int CustomerId, IReadOnlyList<DisputeAssignmentInfo> Assignments);

/// <summary>The unresolved set and the due-date window of the Admin queue.</summary>
/// <param name="Statuses">Only these statuses.</param>
/// <param name="DueFromUtc">Inclusive lower bound of <c>sla_due_at</c> (null: none).</param>
/// <param name="DueBeforeUtc">Exclusive upper bound of <c>sla_due_at</c> (null: none).</param>
public sealed record DisputeQueueFilter(IReadOnlyList<string> Statuses, DateTime? DueFromUtc, DateTime? DueBeforeUtc);

public sealed record DisputeWorkerData(int WorkerId, string FullName, WorkerType WorkerType, string? AgencyName);

public sealed record DisputeSummaryData(long OrderId, string OrderCode, string CustomerName, IReadOnlyList<DisputeWorkerData> Workers);

public sealed record DisputeCheckInData(long AssignmentId, DateTime CheckedInAtUtc, bool GpsVerified, decimal DistanceM);

public sealed record DisputePhotoData(long AssignmentId, string Phase, byte AngleNo, string Url, double VolScore, bool IsAccepted, DateTime CapturedAtUtc);

public sealed record DisputeCaseData(
    IReadOnlyList<DisputeCheckInData> CheckIns,
    IReadOnlyList<DisputePhotoData> Photos,
    IReadOnlyList<(long AssignmentId, DateTime CompletedAtUtc)> Completions);

/// <summary>An assignment of the disputed order as the verdict needs it: who worked it, for which agency, and what it was worth.</summary>
public sealed record DisputeVerdictAssignment(long AssignmentId, int WorkerId, int? AgencyId, decimal GrossAmount);

/// <summary>
/// Persistence of DISPUTE_TICKET and the read model of the Admin console (BE-M6-02a). Reads <c>JOB_ORDER</c>,
/// <c>JOB_ASSIGNMENT</c>, <c>BOOKING_SLOT</c>, <c>CHECK_IN_LOG</c>, <c>JOB_PHOTO</c> and the name columns of Customer,
/// Worker and PartnerAgency read-only (contract questions A6/D6: no read port exists yet).
/// </summary>
public interface IDisputeRepository
{
    /// <summary>The order with its assignments and shift ends; null when it does not exist.</summary>
    Task<DisputeOrderInfo?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the ticket and returns true, or false (nothing saved) when the order already has one: DISPUTE_TICKET has a
    /// UNIQUE index on order_id, which also decides a race between two simultaneous filings.
    /// </summary>
    Task<bool> TryAddAsync(DisputeTicket ticket, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DisputeTicket>> ListForCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DisputeTicket>> ListForWorkerAsync(int workerId, CancellationToken cancellationToken = default);

    Task<DisputeTicket?> GetForCustomerAsync(int disputeId, int customerId, CancellationToken cancellationToken = default);

    Task<DisputeTicket?> GetForWorkerAsync(int disputeId, int workerId, CancellationToken cancellationToken = default);

    /// <summary>Oldest <c>sla_due_at</c> first. <paramref name="page"/> is 1-based.</summary>
    Task<(IReadOnlyList<DisputeTicket> Items, int Total)> SearchAsync(
        DisputeQueueFilter filter, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>A tracked ticket, to be changed and saved by <see cref="SaveAsync"/>; null when missing.</summary>
    Task<DisputeTicket?> FindTrackedAsync(int disputeId, CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>Every assignment of the order with its agency and gross amount (the verdict applies to all of them).</summary>
    Task<IReadOnlyList<DisputeVerdictAssignment>> GetVerdictAssignmentsAsync(long orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decides the ticket with ONE conditional update (only while it is OPEN or IN_REVIEW) and returns the decided ticket, or null
    /// when somebody else decided it first: that is what makes two simultaneous verdicts one 200 and one 409. The caller runs this
    /// inside its unit of work so a later failure undoes it.
    /// </summary>
    Task<DisputeTicket?> TryResolveAsync(
        int disputeId, int adminId, string status, FaultParty? faultParty, decimal compensationAmount, DateTime resolvedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Order code, customer name and the workers of each order (one query per kind, not per ticket).</summary>
    Task<IReadOnlyDictionary<long, DisputeSummaryData>> GetSummaryDataAsync(
        IReadOnlyCollection<long> orderIds, CancellationToken cancellationToken = default);

    /// <summary>Check-ins, photos and completions of the order's assignments (the case file).</summary>
    Task<DisputeCaseData> GetCaseDataAsync(long orderId, CancellationToken cancellationToken = default);
}
