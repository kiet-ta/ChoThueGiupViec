using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Payouts;

/// <summary>A payout item to be stored; <c>AssignmentIds</c> are the assignments it pays.</summary>
/// <summary>The column of the Admin's batch list: the month, the status, the number of payees, the total paid out or the time it was closed.</summary>
public enum PayoutBatchSortKey
{
    Period,
    Status,
    Items,
    Total,
    ClosedAt,
}

public record PayoutBatchSort(PayoutBatchSortKey Key, bool Descending);

public record NewPayoutItem(
    PayeeType PayeeType,
    int? WorkerId,
    int? AgencyId,
    int JobCount,
    decimal GrossAmount,
    decimal CommissionAmount,
    decimal PenaltyAmount,
    decimal AdvanceAmount,
    decimal CarryInAmount,
    decimal NetAmount,
    string BankAccountNo,
    IReadOnlyList<long> AssignmentIds);

/// <summary>An assignment paid through an agency item, for the per-assignment export (decision Q18, question P5).</summary>
public record PayoutDetailRow(
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
public record PayoutItemView(PayoutItem Item, string PayeeName, string? BankName);

/// <summary>
/// Persistence of PAYOUT_BATCH and PAYOUT_ITEM and the "0 JOIN" read of <c>JOB_ASSIGNMENT</c> (BE-M6-04). Name and bank columns of
/// Worker and PartnerAgency are read-only.
/// </summary>
public interface IPayoutRepository
{
    /// <summary>
    /// A transaction-scoped lock on the month, so two simultaneous runs create its batch one after the other; false when it could not be
    /// taken in time. Must be called inside the caller's unit of work.
    /// </summary>
    Task<bool> TryLockPeriodAsync(string periodMonth, CancellationToken cancellationToken = default);

    /// <summary>
    /// A transaction-scoped lock on one payee's wallet (<c>ledger-party:WORKER|AGENCY:{id}</c>, the one the withdrawal flow takes), so a
    /// withdrawal approval and the settlement of that payee never interleave. Must be called inside the caller's unit of work.
    /// </summary>
    Task LockPayeeAsync(PayeeKey payee, CancellationToken cancellationToken = default);

    Task<PayoutBatch?> FindByPeriodAsync(string periodMonth, CancellationToken cancellationToken = default);

    Task<PayoutBatch?> GetBatchAsync(int batchId, CancellationToken cancellationToken = default);

    /// <summary>Adds an empty DRAFT batch and saves it (the id is needed by the items).</summary>
    Task<PayoutBatch> AddDraftAsync(string periodMonth, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>The item the batch already holds for the payee (a settled payee is never settled again), or null.</summary>
    Task<int?> FindItemIdAsync(int batchId, PayeeKey payee, CancellationToken cancellationToken = default);

    /// <summary>Bank account of each payee (<c>""</c> when it has none), keyed like the lines.</summary>
    Task<IReadOnlyDictionary<PayeeKey, string>> GetBankAccountsAsync(IReadOnlyCollection<PayeeKey> payees, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the payee's item, already TRANSFERRED (the settlement is a ledger entry, no real money moves, decision G-1), with zero figures:
    /// the settlement entry refers to the item, and <see cref="CompleteItemAsync"/> then writes the figures. Returns the item id.
    /// </summary>
    Task<int> AddItemAsync(int batchId, PayeeKey payee, string bankAccountNo, DateTime transferredAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Writes the figures of the item and attaches the assignments it pays (each only while still unattached).</summary>
    Task CompleteItemAsync(int itemId, NewPayoutItem figures, CancellationToken cancellationToken = default);

    /// <summary>The sum of the net amounts of the batch's items.</summary>
    Task<decimal> SumNetAsync(int batchId, CancellationToken cancellationToken = default);

    /// <summary>The batches, newest month first; with a <paramref name="sort"/> the order is applied before paging, ties by the batch id in the same direction.</summary>
    Task<(IReadOnlyList<PayoutBatch> Items, int Total)> ListBatchesAsync(int page, int pageSize, PayoutBatchSort? sort, CancellationToken cancellationToken = default);

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
    /// Closes the batch with ONE conditional update (only while DRAFT): the system closes it, so <c>confirmed_by</c> stays null, and the
    /// total is stored. False when it was not DRAFT any more. The caller runs this inside its unit of work.
    /// </summary>
    Task<bool> TryCloseAsync(int batchId, decimal totalAmount, DateTime nowUtc, CancellationToken cancellationToken = default);
}
