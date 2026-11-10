using System.Globalization;
using CommonService.Application.Exceptions;
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
}

public interface ILedgerHistoryService
{
    Task<AdminLedgerPageDto> GetAdminHistoryAsync(LedgerHistoryRequest request, CancellationToken cancellationToken = default);
    Task<PartyLedgerPageDto> GetWorkerHistoryAsync(int workerId, LedgerHistoryRequest request, CancellationToken cancellationToken = default);
    Task<PartyLedgerPageDto> GetAgencyHistoryAsync(int agencyId, LedgerHistoryRequest request, CancellationToken cancellationToken = default);
    Task<LedgerSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<LedgerChainResult> VerifyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The history, summary and verification of the ledger for the three sides (contract ledger.md section 9). Dates are Vietnam calendar days
/// (inclusive), turned into UTC bounds with <see cref="IClock"/>; a worker or an agency can only ever ask for its own accounts, because
/// the party comes from the caller and never from the query.
/// </summary>
public class LedgerHistoryService(
    ILedgerHistoryQuery query, IWithdrawalRepository workers, ILedgerChainVerifier verifier, IClock clock) : ILedgerHistoryService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public async Task<AdminLedgerPageDto> GetAdminHistoryAsync(LedgerHistoryRequest request, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var filter = ParseFilter(request, errors);
        LedgerPartyFilter? party = null;
        if (!string.IsNullOrWhiteSpace(request.PartyType))
        {
            if (ParseEnum<LedgerPartyType>(request.PartyType) is { } type) party = new LedgerPartyFilter(type, request.PartyId, request.PartyId is null);
            else errors["partyType"] = [$"partyType must be one of: {string.Join(", ", Names<LedgerPartyType>())}."];
        }
        else if (request.PartyId is not null)
        {
            errors["partyId"] = ["partyId needs a partyType."];
        }

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
        var filter = ParseFilter(request, errors);
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

    private record ParsedFilter(DateTime? FromUtc, DateTime? ToExclusiveUtc, LedgerEntryType? Type, int Page, int PageSize);

    private ParsedFilter ParseFilter(LedgerHistoryRequest request, Dictionary<string, string[]> errors)
    {
        var from = ParseDay(request.From, "from", errors);
        var to = ParseDay(request.To, "to", errors);
        if (from is not null && to is not null && from > to) errors["from"] = ["from must not be after to."];

        LedgerEntryType? type = null;
        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            type = ParseEnum<LedgerEntryType>(request.Type);
            if (type is null) errors["type"] = [$"type must be one of: {string.Join(", ", Names<LedgerEntryType>())}."];
        }

        if (request.Page is < 1) errors["page"] = ["page must be at least 1."];
        if (request.PageSize is < 1 or > MaxPageSize) errors["pageSize"] = [$"pageSize must be between 1 and {MaxPageSize}."];

        return new ParsedFilter(
            from is { } f ? LocalDayStartUtc(f) : null,
            to is { } t ? LocalDayStartUtc(t.AddDays(1)) : null, // the whole last day is included
            type, request.Page ?? 1, request.PageSize ?? DefaultPageSize);
    }

    private DateTime LocalDayStartUtc(DateOnly day) =>
        DateTime.SpecifyKind(clock.ToUtc(new DateTime(day.Year, day.Month, day.Day, 0, 0, 0, DateTimeKind.Unspecified)), DateTimeKind.Utc);

    private static DateOnly? ParseDay(string? text, string field, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return day;
        errors[field] = [$"{field} must be a date in the form yyyy-MM-dd."];
        return null;
    }

    private static TEnum? ParseEnum<TEnum>(string text) where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Cast<TEnum?>().FirstOrDefault(v => string.Equals(DbEnum.ToDb(v!.Value), text.Trim(), StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> Names<TEnum>() where TEnum : struct, Enum => Enum.GetValues<TEnum>().Select(v => DbEnum.ToDb(v));

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
