using CommonService.Application.Exceptions;
using CommonService.Application.Features.Payouts;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Ledger;

/// <summary>An entry as the clients see it. For a worker or an agency <see cref="SignedAmount"/> is positive when the money came in to the account named by <see cref="Account"/> and negative when it went out.</summary>
public record LedgerEntryDto(
    long EntryId, string EntryCode, DateTime CreatedAt, string EntryType,
    string FromPartyType, int? FromPartyId, string ToPartyType, int? ToPartyId,
    decimal Amount, string RefType, long RefId, long? OrderId, string? Note, string EntryHash,
    string? Account = null, decimal? SignedAmount = null);

public record AdminLedgerPageDto(IReadOnlyList<LedgerEntryDto> Items, int Page, int PageSize, int Total);

/// <summary>
/// A worker's or an agency's history with the balances around the period: <c>OpeningBalance</c> is the account's balance before the first
/// requested day, <c>ClosingBalance</c> before the day after the last one (the whole history when a bound is missing). An agency also
/// has the escrow account, with its own two balances; the filters limit the rows, never the balances.
/// </summary>
public record PartyLedgerPageDto(
    IReadOnlyList<LedgerEntryDto> Items, decimal OpeningBalance, decimal ClosingBalance, int Page, int PageSize, int Total,
    decimal? EscrowOpeningBalance = null, decimal? EscrowClosingBalance = null);

/// <summary>
/// A worker's month as it is settled. <c>AdvanceAmount</c>, <c>PenaltyAmount</c> and <c>SettlementAmount</c> equal the figures of the payout item of
/// the month; <c>Items</c> are the entries of the month, oldest first, plus the settlement entry (written at the start of the next month);
/// <c>ClosingBalance</c> is the balance after that settlement; <c>LastHash</c> is the hash of the last item (a way to check the page against the ledger).
/// </summary>
public record WorkerStatementDto(
    string PeriodMonth, decimal OpeningBalance, IReadOnlyList<LedgerEntryDto> Items,
    decimal AdvanceAmount, decimal PenaltyAmount, decimal SettlementAmount, decimal ClosingBalance, string? LastHash);

public record LedgerSummaryDto(decimal Revenue, decimal Clearing, decimal WorkerDebt, decimal AgencyDebt, decimal Held);

/// <summary>Raw query values of a history call; the service validates them (400 with field errors).</summary>
public record LedgerHistoryRequest(string? From, string? To, string? Type, int? Page, int? PageSize, string? PartyType = null, int? PartyId = null);

/// <summary>An account to search: exact (<c>Id</c>) or every account of the type (<c>AnyId</c>).</summary>
public record LedgerPartyFilter(LedgerPartyType Type, int? Id, bool AnyId);

/// <summary>Entries that touch one of <c>Parties</c> (all entries when empty) created in [<c>FromUtc</c>, <c>ToExclusiveUtc</c>) of the given type.</summary>
public record LedgerSearch(IReadOnlyList<LedgerPartyFilter> Parties, DateTime? FromUtc, DateTime? ToExclusiveUtc, LedgerEntryType? Type);

/// <summary>What the platform holds and owes: net balances of the revenue and clearing accounts, of every worker and of every agency, and the pending withdrawals.</summary>
public record LedgerTotals(decimal Revenue, decimal Clearing, decimal WorkerDebt, decimal AgencyDebt, decimal Held);

/// <summary>Read-only history queries over the ledger table; newest entry first.</summary>
public interface ILedgerHistoryQuery
{
    Task<(IReadOnlyList<LedgerEntry> Items, int Total)> SearchAsync(LedgerSearch search, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Entries to the party less entries from it, created before <paramref name="beforeUtc"/> (all of them when null).</summary>
    Task<decimal> GetBalanceBeforeAsync(LedgerParty party, DateTime? beforeUtc, CancellationToken cancellationToken = default);

    Task<LedgerTotals> GetTotalsAsync(CancellationToken cancellationToken = default);

    /// <summary>The entry with the idempotency key (e.g. a month's SETTLEMENT), or null.</summary>
    Task<LedgerEntry?> GetByKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);
}

public interface ILedgerHistoryService
{
    Task<AdminLedgerPageDto> GetAdminHistoryAsync(LedgerHistoryRequest request, CancellationToken cancellationToken = default);
    Task<PartyLedgerPageDto> GetWorkerHistoryAsync(int workerId, LedgerHistoryRequest request, CancellationToken cancellationToken = default);
    Task<PartyLedgerPageDto> GetAgencyHistoryAsync(int agencyId, LedgerHistoryRequest request, CancellationToken cancellationToken = default);
    Task<LedgerSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<LedgerChainResult> VerifyAsync(CancellationToken cancellationToken = default);

    /// <summary>The worker's statement of a month (<c>yyyy-MM</c>): every entry of the month and the month's settlement, with the advance, the deduction and the balances.</summary>
    Task<WorkerStatementDto> GetWorkerStatementAsync(int workerId, string? month, CancellationToken cancellationToken = default);
}

/// <summary>
/// The history, summary and verification of the ledger for the three sides (contract ledger.md section 9). Dates are Vietnam calendar days
/// (inclusive), turned into UTC bounds with <see cref="IClock"/>; a worker or an agency can only ever ask for its own accounts, because
/// the party comes from the caller and never from the query.
/// </summary>
public class LedgerHistoryService(
    ILedgerHistoryQuery query, IWithdrawalRepository workers, IWorkerEarningsRepository earnings, ILedgerChainVerifier verifier, IClock clock) : ILedgerHistoryService
{
    private readonly LedgerFilterParser _parser = new(clock);

    public async Task<AdminLedgerPageDto> GetAdminHistoryAsync(LedgerHistoryRequest request, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var filter = _parser.Parse(request, errors);
        var party = LedgerFilterParser.ParseParty(request, errors);
        if (errors.Count > 0) throw new ValidationException(errors);

        var (items, total) = await query.SearchAsync(
            new LedgerSearch(party is null ? [] : [party], filter.FromUtc, filter.ToExclusiveUtc, filter.Type), filter.Page, filter.PageSize, cancellationToken);
        return new AdminLedgerPageDto(items.Select(e => ToDto(e)).ToList(), filter.Page, filter.PageSize, total);
    }

    public async Task<PartyLedgerPageDto> GetWorkerHistoryAsync(int workerId, LedgerHistoryRequest request, CancellationToken cancellationToken = default)
    {
        var worker = await workers.GetWorkerAsync(workerId, cancellationToken) ?? throw new NotFoundException("Worker", workerId);
        if (worker.Type != WorkerType.Freelancer) throw new ForbiddenAccessException("Thợ thuộc Agency được trả lương qua Agency, không có ví riêng.");
        return await GetPartyHistoryAsync([new LedgerParty(LedgerPartyType.Worker, workerId)], request, cancellationToken);
    }

    public Task<PartyLedgerPageDto> GetAgencyHistoryAsync(int agencyId, LedgerHistoryRequest request, CancellationToken cancellationToken = default) =>
        GetPartyHistoryAsync([new LedgerParty(LedgerPartyType.Agency, agencyId), new LedgerParty(LedgerPartyType.AgencyEscrow, agencyId)], request, cancellationToken);

    public async Task<LedgerSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var totals = await query.GetTotalsAsync(cancellationToken);
        return new LedgerSummaryDto(totals.Revenue, totals.Clearing, totals.WorkerDebt, totals.AgencyDebt, totals.Held);
    }

    public Task<LedgerChainResult> VerifyAsync(CancellationToken cancellationToken = default) => verifier.VerifyAsync(cancellationToken);

    /// <summary>The first party is the main account (worker wallet or agency wallet); an agency's second one is its escrow.</summary>
    private async Task<PartyLedgerPageDto> GetPartyHistoryAsync(IReadOnlyList<LedgerParty> accounts, LedgerHistoryRequest request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var filter = _parser.Parse(request, errors);
        if (errors.Count > 0) throw new ValidationException(errors);

        var search = new LedgerSearch(accounts.Select(a => new LedgerPartyFilter(a.Type, a.Id, false)).ToList(), filter.FromUtc, filter.ToExclusiveUtc, filter.Type);
        var (items, total) = await query.SearchAsync(search, filter.Page, filter.PageSize, cancellationToken);

        var main = accounts[0];
        var opening = filter.FromUtc is null ? 0m : await query.GetBalanceBeforeAsync(main, filter.FromUtc, cancellationToken);
        var closing = await query.GetBalanceBeforeAsync(main, filter.ToExclusiveUtc, cancellationToken);
        decimal? escrowOpening = null, escrowClosing = null;
        if (accounts.Count > 1)
        {
            escrowOpening = filter.FromUtc is null ? 0m : await query.GetBalanceBeforeAsync(accounts[1], filter.FromUtc, cancellationToken);
            escrowClosing = await query.GetBalanceBeforeAsync(accounts[1], filter.ToExclusiveUtc, cancellationToken);
        }

        return new PartyLedgerPageDto(items.Select(e => ToDto(e, accounts)).ToList(), opening, closing, filter.Page, filter.PageSize, total, escrowOpening, escrowClosing);
    }

    public async Task<WorkerStatementDto> GetWorkerStatementAsync(int workerId, string? month, CancellationToken cancellationToken = default)
    {
        var worker = await workers.GetWorkerAsync(workerId, cancellationToken) ?? throw new NotFoundException("Worker", workerId);
        if (worker.Type != WorkerType.Freelancer) throw new ForbiddenAccessException("Thợ thuộc Agency được trả lương qua Agency, không có sao kê riêng.");

        if (!PayoutConstants.TryParsePeriod(month?.Trim(), out var year, out var monthNumber))
        {
            throw new ValidationException("month", "month must be a month in the form YYYY-MM.");
        }

        var today = clock.LocalToday;
        if ((year, monthNumber).CompareTo((today.Year, today.Month)) > 0) throw new ValidationException("month", "A future month has no statement yet.");

        var period = PayoutConstants.FormatPeriod(year, monthNumber);
        var fromUtc = _parser.LocalDayStartUtc(new DateOnly(year, monthNumber, 1));
        var toUtc = _parser.LocalDayStartUtc(new DateOnly(year, monthNumber, 1).AddMonths(1));
        var account = new LedgerParty(LedgerPartyType.Worker, workerId);

        // The month's entries, oldest first (a month of one worker is small enough to list whole).
        var monthSearch = new LedgerSearch([new LedgerPartyFilter(account.Type, account.Id, false)], fromUtc, toUtc, null);
        var (entries, _) = await query.SearchAsync(monthSearch, 1, 5000, cancellationToken);
        var rows = entries.OrderBy(e => e.EntryId).ToList();

        // The settlement of the month is written when the month is over (at the start of the next one); it belongs to this statement.
        var settlement = await query.GetByKeyAsync(LedgerKeys.Settlement(period, LedgerPartyType.Worker, workerId), cancellationToken);
        if (settlement is not null && rows.All(r => r.EntryId != settlement.EntryId)) rows.Add(settlement);

        var state = await earnings.GetBatchStateAsync(workerId, period, cancellationToken);
        var settled = state is { BatchStatus: PayoutConstants.BatchClosed, Item: not null };
        var opening = await query.GetBalanceBeforeAsync(account, fromUtc, cancellationToken);
        var atCutoff = await query.GetBalanceBeforeAsync(account, toUtc, cancellationToken);
        var settlementAmount = settled ? state!.Item!.NetAmount : 0m;

        decimal Sent(LedgerEntryType type) => rows.Where(e => e.EntryType == type && e.FromPartyType == LedgerPartyType.Worker && e.FromPartyId == workerId && e.CreatedAt < toUtc).Sum(e => e.Amount);

        // For a settled month the figures are the payout item's, which the settlement computed from these same entries.
        var advance = settled ? state!.Item!.AdvanceAmount : Sent(LedgerEntryType.Withdrawal);
        var penalty = settled ? state!.Item!.PenaltyAmount : Sent(LedgerEntryType.Penalty);
        var closing = atCutoff - (settlement is null ? 0m : settlement.Amount);
        return new WorkerStatementDto(
            period, opening, rows.Select(e => ToDto(e, [account])).ToList(), advance, penalty, settlementAmount, closing, rows.Count == 0 ? null : rows[^1].EntryHash);
    }

    private static LedgerEntryDto ToDto(LedgerEntry e, IReadOnlyList<LedgerParty>? accounts = null)
    {
        string? account = null;
        decimal? signed = null;
        if (accounts is not null)
        {
            // An entry between the agency and its own escrow touches both accounts: it is shown on the account it goes to.
            var received = accounts.FirstOrDefault(a => e.ToPartyType == a.Type && e.ToPartyId == a.Id);
            var sent = accounts.FirstOrDefault(a => e.FromPartyType == a.Type && e.FromPartyId == a.Id);
            if (received is not null) (account, signed) = (DbEnum.ToDb(received.Type), e.Amount);
            else if (sent is not null) (account, signed) = (DbEnum.ToDb(sent.Type), -e.Amount);
        }

        return new LedgerEntryDto(
            e.EntryId, e.EntryCode, DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc), DbEnum.ToDb(e.EntryType),
            DbEnum.ToDb(e.FromPartyType), e.FromPartyId, DbEnum.ToDb(e.ToPartyType), e.ToPartyId,
            e.Amount, DbEnum.ToDb(e.RefType), e.RefId, e.OrderId, e.Note, e.EntryHash, account, signed);
    }
}
