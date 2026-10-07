using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Payouts;

/// <summary>One assignment of the worker that earned money in the month: a completed job or an approved absence fee.</summary>
/// <param name="DateUtc"><c>completed_at</c> of a job, <c>updated_at</c> of an approved absence fee (the date the fee was approved).</param>
public sealed record WorkerEarningRow(
    long AssignmentId, long OrderId, JobAssignmentStatus Status, decimal GrossAmount, decimal CommissionRate, decimal? AbsenceFeeAmount, DateTime DateUtc);

/// <summary>The state of the month's payout batch for one worker.</summary>
/// <param name="BatchStatus">DRAFT or CLOSED.</param>
/// <param name="Item">The worker's item in that batch; null when the batch has none for the worker.</param>
public sealed record WorkerBatchState(int BatchId, string BatchStatus, WorkerBatchItem? Item);

public sealed record WorkerBatchItem(int JobCount, decimal GrossAmount, decimal CommissionAmount, decimal PenaltyAmount, decimal NetAmount);

/// <summary>A closed batch item of the worker (the "payout history" screen).</summary>
public sealed record WorkerPayoutHistoryRow(int BatchId, string PeriodMonth, decimal NetAmount, string ItemStatus, DateTime? TransferredAt);

/// <summary>
/// Reads for the freelancer's own income screens (BE-M6-07). Every method is scoped to one worker id, so another worker's rows are never read.
/// </summary>
public interface IWorkerEarningsRepository
{
    /// <summary>The worker's type; null when there is no such worker.</summary>
    Task<WorkerType?> GetWorkerTypeAsync(int workerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The worker's own freelancer assignments (<c>agency_id</c> null) that earned money in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>),
    /// paid already or not: COMPLETED by <c>completed_at</c>, ABSENT with an absence fee by <c>updated_at</c>. Oldest first.
    /// </summary>
    Task<IReadOnlyList<WorkerEarningRow>> GetEarningsAsync(int workerId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>The batch of the month and the worker's item in it; null when no batch was built for the month.</summary>
    Task<WorkerBatchState?> GetBatchStateAsync(int workerId, string periodMonth, CancellationToken cancellationToken = default);

    /// <summary>What CLOSED batches of earlier months already took from this worker as penalty.</summary>
    Task<decimal> GetAppliedPenaltyBeforeAsync(int workerId, string periodMonth, CancellationToken cancellationToken = default);

    /// <summary>The worker's own items of CLOSED batches, newest month first. <paramref name="page"/> is 1-based.</summary>
    Task<(IReadOnlyList<WorkerPayoutHistoryRow> Items, int Total)> GetClosedItemsAsync(
        int workerId, int page, int pageSize, CancellationToken cancellationToken = default);
}
