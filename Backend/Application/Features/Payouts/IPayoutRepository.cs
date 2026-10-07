using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Payouts;

/// <summary>
/// The deductions decided against payees by the Disputes module (contract question P1). Owned by Payouts and implemented in the
/// Disputes folder, so the batch never reads dispute tables itself.
/// </summary>
public interface IPayoutPenaltySource
{
    /// <summary>
    /// For each payee, the sum (whole VND) of every deduction decided before <paramref name="throughUtc"/>: a resolved dispute with
    /// <c>fault_party = FREELANCER</c> against each freelancer of the order, and the reversal of an absence fee against the agency
    /// (decision D9). Payees with nothing decided are absent.
    /// </summary>
    Task<IReadOnlyDictionary<PayeeKey, decimal>> GetDecidedAsync(DateTime throughUtc, CancellationToken cancellationToken = default);
}

/// <summary>An assignment was attached to another payout item while this one was being built; the unit of work must be undone.</summary>
public sealed class PayoutClaimLostException(long assignmentId)
    : Exception($"Assignment {assignmentId} is already attached to another payout item.");

/// <summary>A payout item to be stored; <c>AssignmentIds</c> are the assignments it pays.</summary>
public sealed record NewPayoutItem(
    PayeeType PayeeType,
    int? WorkerId,
    int? AgencyId,
    int JobCount,
    decimal GrossAmount,
    decimal CommissionAmount,
    decimal PenaltyAmount,
    decimal NetAmount,
    string BankAccountNo,
    IReadOnlyList<long> AssignmentIds);

/// <summary>An assignment paid through an agency item, for the per-assignment export (decision Q18, question P5).</summary>
public sealed record PayoutDetailRow(
    string AgencyName,
    long AssignmentId,
    long OrderId,
    string WorkerName,
    JobAssignmentStatus Status,
    decimal GrossAmount,
    decimal CommissionRate,
    decimal? AbsenceFeeAmount,
    DateTime DateUtc);

/// <summary>A stored item with the payee name and bank name read from the worker or agency.</summary>
public sealed record PayoutItemView(PayoutItem Item, string PayeeName, string? BankName);

/// <summary>
/// Persistence of PAYOUT_BATCH and PAYOUT_ITEM and the "0 JOIN" read of <c>JOB_ASSIGNMENT</c> (BE-M6-04). Name and bank columns of
/// Worker and PartnerAgency are read-only.
/// </summary>
public interface IPayoutRepository
{
    /// <summary>
    /// A transaction-scoped lock on the month, so two simultaneous builds of one month run one after the other; false when it could
    /// not be taken in time. Must be called inside the caller's unit of work.
    /// </summary>
    Task<bool> TryLockPeriodAsync(string periodMonth, CancellationToken cancellationToken = default);

    Task<PayoutBatch?> FindByPeriodAsync(string periodMonth, CancellationToken cancellationToken = default);

    Task<PayoutBatch?> GetBatchAsync(int batchId, CancellationToken cancellationToken = default);

    /// <summary>Adds an empty DRAFT batch and saves it (the id is needed by the items).</summary>
    Task<PayoutBatch> AddDraftAsync(string periodMonth, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assignments that can be paid in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>): COMPLETED by <c>completed_at</c>, ABSENT with
    /// an absence fee by <c>updated_at</c>, and either not paid yet or already attached to an item of <paramref name="batchId"/>.
    /// </summary>
    Task<IReadOnlyList<PayoutAssignmentRow>> GetPayableAsync(
        DateTime fromUtc, DateTime toUtc, int batchId, CancellationToken cancellationToken = default);

    /// <summary>What CLOSED batches of earlier months already took as penalty, per payee.</summary>
    Task<IReadOnlyDictionary<PayeeKey, decimal>> GetAppliedPenaltiesBeforeAsync(string periodMonth, CancellationToken cancellationToken = default);

    /// <summary>Bank account of each payee (<c>""</c> when it has none), keyed like the lines.</summary>
    Task<IReadOnlyDictionary<PayeeKey, string>> GetBankAccountsAsync(IReadOnlyCollection<PayeeKey> payees, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the assignments of the batch's items, deletes the items, stores <paramref name="items"/>, attaches their assignments
    /// (each only if still unpaid) and sets the batch total. Throws <see cref="PayoutClaimLostException"/> when an assignment was taken by
    /// another batch meanwhile, which undoes the caller's unit of work.
    /// </summary>
    Task ReplaceItemsAsync(int batchId, IReadOnlyList<NewPayoutItem> items, decimal totalAmount, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<PayoutBatch> Items, int Total)> ListBatchesAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, int>> CountItemsAsync(IReadOnlyCollection<int> batchIds, CancellationToken cancellationToken = default);

    /// <summary>Items of a batch (optionally one payee type) with names, in the order they were built: freelancers first, then agencies, each by id.</summary>
    Task<(IReadOnlyList<PayoutItemView> Items, int Total)> GetItemsAsync(
        int batchId, PayeeType? payeeType, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Every item of one payee type, in the order they were built (no paging), for the export files.</summary>
    Task<IReadOnlyList<PayoutItemView>> GetAllItemsAsync(int batchId, PayeeType payeeType, CancellationToken cancellationToken = default);

    /// <summary>The assignments paid through the batch's AGENCY items, by agency name then assignment id.</summary>
    Task<IReadOnlyList<PayoutDetailRow>> GetAgencyDetailAsync(int batchId, CancellationToken cancellationToken = default);

    /// <summary>Records where the last export of the batch was stored (<c>export_file_url</c>).</summary>
    Task SetExportUrlAsync(int batchId, string? url, CancellationToken cancellationToken = default);

    /// <summary>Names of the payees of the batch that have no bank account number (question P4).</summary>
    Task<IReadOnlyList<string>> GetPayeesWithoutBankAsync(int batchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the batch with ONE conditional update (only while DRAFT) and marks every item TRANSFERRED; false when it was not DRAFT any
    /// more. The caller runs this inside its unit of work.
    /// </summary>
    Task<bool> TryCloseAsync(int batchId, int adminId, DateTime nowUtc, CancellationToken cancellationToken = default);
}
