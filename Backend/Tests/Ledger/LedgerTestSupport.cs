using CommonService.Application.Features.Ledger;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;

namespace CommonService.Tests.Ledger;

/// <summary>An in-memory ledger store with the same read helpers as the SQL repository (clearing balance and refund total per order).</summary>
public class MemoryLedger : ILedgerRepository
{
    public List<LedgerEntry> Rows { get; } = [];
    public bool Locked { get; private set; }

    public async Task<T> ExecuteLockedAsync<T>(Func<ILedgerLockedReader, Task<T>> action, CancellationToken cancellationToken = default)
    {
        Locked = true;
        try { return await action(this); }
        finally { Locked = false; }
    }

    public Task<IReadOnlySet<string>> FindExistingKeysAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<string>>(Rows.Where(e => keys.Contains(e.IdempotencyKey)).Select(e => e.IdempotencyKey).ToHashSet());

    public Task<string?> GetLastHashAsync(CancellationToken cancellationToken = default) => Task.FromResult(Rows.LastOrDefault()?.EntryHash);

    public Task AppendAndSealAsync(LedgerEntry entry, Func<LedgerEntry, string> hash, CancellationToken cancellationToken = default)
    {
        Assert.True(Locked, "Entries must be appended under the ledger lock.");
        entry.EntryId = Rows.Count + 1;
        typeof(LedgerEntry).GetProperty(nameof(LedgerEntry.EntryCode))!.SetValue(entry, $"LG{entry.EntryId:00000000}");
        entry.EntryHash = hash(entry);
        Rows.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LedgerEntry>> ReadChainAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LedgerEntry>>(Rows.ToArray());

    public Task<decimal> GetOrderRefundTotalAsync(long orderId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.Where(e => e.OrderId == orderId && e.EntryType == LedgerEntryType.Refund).Sum(e => e.Amount));

    public Task<decimal> GetOrderClearingBalanceAsync(long orderId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.Where(e => e.OrderId == orderId && e.ToPartyType == LedgerPartyType.PlatformClearing).Sum(e => e.Amount)
            - Rows.Where(e => e.OrderId == orderId && e.FromPartyType == LedgerPartyType.PlatformClearing).Sum(e => e.Amount));
}

/// <summary>The posting service on an in-memory ledger and a fake source, plus balance helpers for assertions.</summary>
public class LedgerFlow
{
    public static readonly DateTime Now = new(2026, 11, 10, 4, 0, 0, DateTimeKind.Utc);

    public MemoryLedger Ledger { get; } = new();
    public FakeLedgerSourceQuery Source { get; } = new();
    public FakeLedgerQuery LedgerQuery { get; } = new();
    public FakeClock Clock { get; } = new();
    public LedgerWriter Writer { get; }
    public LedgerPostingService Posting { get; }

    public LedgerFlow()
    {
        Clock.Set(Now);
        Writer = new LedgerWriter(Ledger, Clock);
        Posting = new LedgerPostingService(Writer, Source);
    }

    /// <summary>The reconciliation service on this flow; the ledger start defaults to a day before <see cref="Now"/>.</summary>
    public LedgerReconciliationService Reconciliation(LedgerOptions? options = null) =>
        new(Source, LedgerQuery, Ledger, Writer, Posting, Clock,
            Microsoft.Extensions.Options.Options.Create(options ?? new LedgerOptions { StartUtc = Now.AddDays(-1) }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LedgerReconciliationService>.Instance);

    public IReadOnlyList<LedgerEntry> Of(LedgerEntryType type) => Ledger.Rows.Where(e => e.EntryType == type).ToArray();

    public decimal Balance(LedgerPartyType type, int? id = null) =>
        Ledger.Rows.Where(e => e.ToPartyType == type && e.ToPartyId == id).Sum(e => e.Amount)
        - Ledger.Rows.Where(e => e.FromPartyType == type && e.FromPartyId == id).Sum(e => e.Amount);

    public decimal Clearing(long orderId) => Ledger.GetOrderClearingBalanceAsync(orderId).Result;

    public void AddOrder(long orderId, int customerId, decimal total, int seats = 1) =>
        Source.Orders.Add(new LedgerOrderSource(orderId, customerId, total, seats));

    public LedgerAssignmentSource AddAssignment(
        long assignmentId, long orderId, int workerId, int? agencyId = null, int seat = 1, decimal gross = 0m, decimal rate = 0.20m, DateTime? completedAt = null)
    {
        var assignment = new LedgerAssignmentSource(assignmentId, orderId, workerId, agencyId, seat, gross, rate, completedAt, completedAt is not null);
        Source.Assignments.Add(assignment);
        return assignment;
    }
}

/// <summary>The ledger read port over a <see cref="MemoryLedger"/>: what the monthly settlement reads.</summary>
public class LedgerStoreQuery(MemoryLedger ledger) : ILedgerQuery
{
    public Dictionary<int, decimal> Held { get; } = [];

    public Task<IReadOnlyList<LedgerEntry>> GetPartyEntriesAsync(LedgerPartyType type, int? id, DateTime? beforeUtc = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LedgerEntry>>(ledger.Rows.Where(e =>
            ((e.ToPartyType == type && e.ToPartyId == id) || (e.FromPartyType == type && e.FromPartyId == id)) &&
            (!beforeUtc.HasValue || e.CreatedAt < beforeUtc.Value)).OrderBy(e => e.EntryId).ToArray());

    public Task<decimal> GetHeldAsync(int workerId, CancellationToken cancellationToken = default) => Task.FromResult(Held.GetValueOrDefault(workerId));

    public Task<IReadOnlyList<(LedgerPartyType Type, int Id)>> GetSettlementPartiesAsync(DateTime beforeUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<(LedgerPartyType, int)>>(ledger.Rows
            .Where(e => e.CreatedAt < beforeUtc)
            .SelectMany(e => new[] { (e.ToPartyType, e.ToPartyId), (e.FromPartyType, e.FromPartyId) })
            .Where(p => p.Item1 is LedgerPartyType.Worker or LedgerPartyType.Agency && p.Item2 is not null)
            .Select(p => (p.Item1, p.Item2!.Value))
            .Distinct().OrderBy(p => p.Item1).ThenBy(p => p.Item2).ToArray());

    public Task<IReadOnlyList<LedgerEntry>> GetCommissionsAsync(LedgerRefType refType, IReadOnlyCollection<long> refIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LedgerEntry>>(ledger.Rows.Where(e => e.EntryType == LedgerEntryType.Commission && e.RefType == refType && refIds.Contains(e.RefId)).ToArray());

    public Task<DateTime?> GetFirstEntryAtAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<DateTime?>(ledger.Rows.Count == 0 ? null : ledger.Rows.Min(e => e.CreatedAt));
}

/// <summary>The history queries over a <see cref="MemoryLedger"/> with the semantics of the SQL implementation.</summary>
public class MemoryHistoryQuery(MemoryLedger ledger) : ILedgerHistoryQuery
{
    public decimal Held { get; set; }

    private static bool Touches(LedgerEntry e, LedgerPartyFilter p) =>
        (e.ToPartyType == p.Type && (p.AnyId || e.ToPartyId == p.Id)) || (e.FromPartyType == p.Type && (p.AnyId || e.FromPartyId == p.Id));

    public Task<(IReadOnlyList<LedgerEntry> Items, int Total)> SearchAsync(LedgerSearch search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var rows = ledger.Rows.Where(e =>
            (search.FromUtc is null || e.CreatedAt >= search.FromUtc) && (search.ToExclusiveUtc is null || e.CreatedAt < search.ToExclusiveUtc)
            && (search.Type is null || e.EntryType == search.Type)
            && (search.Parties.Count == 0 || search.Parties.Any(p => Touches(e, p)))).ToList();
        rows = (search.Sort switch
        {
            null => rows.OrderByDescending(e => e.EntryId),
            { Key: LedgerSortKey.Amount, Descending: true } => rows.OrderByDescending(e => e.Amount).ThenByDescending(e => e.EntryId),
            { Key: LedgerSortKey.Amount } => rows.OrderBy(e => e.Amount).ThenBy(e => e.EntryId),
            { Key: LedgerSortKey.Time, Descending: true } => rows.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.EntryId),
            { Key: LedgerSortKey.Time } => rows.OrderBy(e => e.CreatedAt).ThenBy(e => e.EntryId),
            { Descending: true } => rows.OrderByDescending(e => e.EntryId),
            _ => rows.OrderBy(e => e.EntryId),
        }).ToList();
        return Task.FromResult<(IReadOnlyList<LedgerEntry>, int)>((rows.Skip((page - 1) * pageSize).Take(pageSize).ToList(), rows.Count));
    }

    public Task<decimal> GetBalanceBeforeAsync(LedgerParty party, DateTime? beforeUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult(ledger.Rows.Where(e => beforeUtc is null || e.CreatedAt < beforeUtc)
            .Sum(e => (e.ToPartyType == party.Type && e.ToPartyId == party.Id ? e.Amount : 0m) - (e.FromPartyType == party.Type && e.FromPartyId == party.Id ? e.Amount : 0m)));

    public Task<LedgerEntry?> GetByKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(ledger.Rows.FirstOrDefault(e => e.IdempotencyKey == idempotencyKey));

    public Task<LedgerTotals> GetTotalsAsync(CancellationToken cancellationToken = default)
    {
        decimal Net(LedgerPartyType type, bool platform) =>
            ledger.Rows.Where(e => e.ToPartyType == type && (!platform || e.ToPartyId == null)).Sum(e => e.Amount)
            - ledger.Rows.Where(e => e.FromPartyType == type && (!platform || e.FromPartyId == null)).Sum(e => e.Amount);
        return Task.FromResult(new LedgerTotals(
            Net(LedgerPartyType.PlatformRevenue, true), Net(LedgerPartyType.PlatformClearing, true), Net(LedgerPartyType.Worker, false), Net(LedgerPartyType.Agency, false), Held));
    }
}
