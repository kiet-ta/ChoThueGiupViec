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
    public LedgerPostingService Posting { get; }

    public LedgerFlow()
    {
        var clock = new FakeClock();
        clock.Set(Now);
        Posting = new LedgerPostingService(new LedgerWriter(Ledger, clock), Source);
    }

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
