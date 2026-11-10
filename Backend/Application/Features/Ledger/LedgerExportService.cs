using System.Globalization;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Payouts;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Ledger;

/// <summary>A generated statement, ready to be sent as the response.</summary>
public record LedgerExportFile(byte[] Content, string FileName, string ContentType);

public interface ILedgerExportService
{
    /// <summary>The Admin's statement (every entry of the filter, oldest first); with one account selected it has that account's opening and closing balance.</summary>
    Task<LedgerExportFile> ExportAdminAsync(LedgerHistoryRequest request, CancellationToken cancellationToken = default);

    /// <summary>The agency's own statement: its wallet and its escrow account, each with an opening and a closing balance.</summary>
    Task<LedgerExportFile> ExportAgencyAsync(int agencyId, LedgerHistoryRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// The <c>.xlsx</c> statements of the ledger (decision H, contract ledger.md section 9), written with the same <see cref="XlsxWriter"/> as the
/// payout files. Columns: transaction code, Vietnam time (ISO-8601 with the offset), type, sender, receiver, amount, reference and the entry
/// hash, so a row can be checked against the chain. Opening and closing balances are rows of their own at the top and the bottom.
/// </summary>
public class LedgerExportService(ILedgerHistoryQuery query, IClock clock) : ILedgerExportService
{
    /// <summary>A statement of more rows than this must be narrowed to a shorter period (400).</summary>
    public const int MaxRows = 50_000;

    public const string OpeningBalance = "OPENING_BALANCE";
    public const string ClosingBalance = "CLOSING_BALANCE";

    public static readonly IReadOnlyList<string> Headers =
        ["entry_code", "created_at", "entry_type", "from", "to", "amount", "reference", "entry_hash"];

    private readonly LedgerFilterParser _parser = new(clock);

    public async Task<LedgerExportFile> ExportAdminAsync(LedgerHistoryRequest request, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var filter = _parser.Parse(Unpaged(request), errors);
        var party = LedgerFilterParser.ParseParty(request, errors);
        if (errors.Count > 0) throw new ValidationException(errors);

        var entries = await LoadAsync(new LedgerSearch(party is null ? [] : [party], filter.FromUtc, filter.ToExclusiveUtc, filter.Type), cancellationToken);

        // One balance only makes sense for one account: an exact id, or the platform's own account (it has no id).
        var accounts = party is { AnyId: false } ? new[] { new LedgerParty(party.Type, party.Id) } : [];
        var rows = await RowsAsync(entries, accounts, filter, cancellationToken);
        return Build(rows, "Ledger", $"ledger-{FileRange(request)}.xlsx");
    }

    public async Task<LedgerExportFile> ExportAgencyAsync(int agencyId, LedgerHistoryRequest request, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var filter = _parser.Parse(Unpaged(request), errors);
        if (errors.Count > 0) throw new ValidationException(errors);

        LedgerParty[] accounts = [new LedgerParty(LedgerPartyType.Agency, agencyId), new LedgerParty(LedgerPartyType.AgencyEscrow, agencyId)];
        var entries = await LoadAsync(
            new LedgerSearch(accounts.Select(a => new LedgerPartyFilter(a.Type, a.Id, false)).ToList(), filter.FromUtc, filter.ToExclusiveUtc, filter.Type), cancellationToken);
        var rows = await RowsAsync(entries, accounts, filter, cancellationToken);
        return Build(rows, "Ledger", $"ledger-agency-{agencyId}-{FileRange(request)}.xlsx");
    }

    /// <summary>The export has no paging: the values of page and pageSize are ignored, not validated.</summary>
    private static LedgerHistoryRequest Unpaged(LedgerHistoryRequest request) => request with { Page = null, PageSize = null };

    private async Task<List<LedgerEntry>> LoadAsync(LedgerSearch search, CancellationToken cancellationToken)
    {
        var (items, _) = await query.SearchAsync(search, 1, MaxRows + 1, cancellationToken);
        if (items.Count > MaxRows)
        {
            throw new ValidationException("from", $"The statement has more than {MaxRows} entries; choose a shorter period.");
        }

        return items.OrderBy(e => e.EntryId).ToList(); // a statement reads from the oldest entry down
    }

    private async Task<List<IReadOnlyList<object?>>> RowsAsync(
        List<LedgerEntry> entries, IReadOnlyList<LedgerParty> accounts, ParsedLedgerFilter filter, CancellationToken cancellationToken)
    {
        var rows = new List<IReadOnlyList<object?>>();
        var closing = new List<IReadOnlyList<object?>>();
        foreach (var account in accounts)
        {
            var opening = filter.FromUtc is null ? 0m : await query.GetBalanceBeforeAsync(account, filter.FromUtc, cancellationToken);
            var end = await query.GetBalanceBeforeAsync(account, filter.ToExclusiveUtc, cancellationToken);
            rows.Add(BalanceRow(OpeningBalance, filter.FromUtc, account, opening));
            closing.Add(BalanceRow(ClosingBalance, filter.ToExclusiveUtc, account, end));
        }

        rows.AddRange(entries.Select(EntryRow));
        rows.AddRange(closing);
        return rows;
    }

    private IReadOnlyList<object?> BalanceRow(string code, DateTime? boundaryUtc, LedgerParty account, decimal balance) =>
        [code, boundaryUtc is { } at ? LocalTime(at) : null, "BALANCE", Label(account.Type, account.Id), null, balance, null, null];

    private IReadOnlyList<object?> EntryRow(LedgerEntry e) =>
    [
        e.EntryCode, LocalTime(e.CreatedAt), DbEnum.ToDb(e.EntryType), Label(e.FromPartyType, e.FromPartyId), Label(e.ToPartyType, e.ToPartyId), e.Amount,
        string.Create(CultureInfo.InvariantCulture, $"{DbEnum.ToDb(e.RefType)}:{e.RefId}"), e.EntryHash,
    ];

    private static string Label(LedgerPartyType type, int? id) =>
        id is { } value ? string.Create(CultureInfo.InvariantCulture, $"{DbEnum.ToDb(type)}:{value}") : DbEnum.ToDb(type);

    private static string FileRange(LedgerHistoryRequest request) =>
        $"{(string.IsNullOrWhiteSpace(request.From) ? "start" : request.From.Trim())}_{(string.IsNullOrWhiteSpace(request.To) ? "now" : request.To.Trim())}";

    private static LedgerExportFile Build(List<IReadOnlyList<object?>> rows, string sheet, string fileName) =>
        new(XlsxWriter.Build(sheet, Headers, rows), fileName, XlsxWriter.ContentType);

    /// <summary>ISO-8601 in Asia/Ho_Chi_Minh with the offset, so the reader sees local time without ambiguity (decision G-3).</summary>
    private string LocalTime(DateTime utc)
    {
        var asUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var local = DateTime.SpecifyKind(clock.ToLocal(asUtc), DateTimeKind.Unspecified);
        var offset = local - DateTime.SpecifyKind(asUtc, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, offset).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    }
}
