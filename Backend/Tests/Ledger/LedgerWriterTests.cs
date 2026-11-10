using CommonService.Application.Features.Ledger;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Ledger;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Tests.Ledger;

public class LedgerWriterTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 4, 0, 0, DateTimeKind.Utc);
    private static readonly LedgerParty External = new(LedgerPartyType.External, 12);
    private static readonly LedgerParty Clearing = new(LedgerPartyType.PlatformClearing, null);

    private class Clock : IClock
    {
        public DateTime UtcNow => Now;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => local.AddHours(-7);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(Now));
    }

    private class MemoryLedger : ILedgerRepository
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
            Assert.True(Locked);
            entry.EntryId = Rows.Count + 1;
            typeof(LedgerEntry).GetProperty(nameof(LedgerEntry.EntryCode))!.SetValue(entry, $"LG{entry.EntryId:00000000}");
            entry.EntryHash = hash(entry);
            Rows.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LedgerEntry>> ReadChainAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LedgerEntry>>(Rows.ToArray());

        public Task<decimal> GetOrderRefundTotalAsync(long orderId, CancellationToken cancellationToken = default) => Task.FromResult(0m);
        public Task<decimal> GetOrderClearingBalanceAsync(long orderId, CancellationToken cancellationToken = default) => Task.FromResult(0m);
    }

    private static NewLedgerEntry Entry(string key, decimal amount = 100m) =>
        new(External, Clearing, amount, LedgerEntryType.OrderPaid, LedgerRefType.Order, 1, 1, null, key);

    [Fact]
    public async Task Append_chains_three_entries_from_genesis_and_replay_is_idempotent()
    {
        var repository = new MemoryLedger();
        var writer = new LedgerWriter(repository, new Clock());
        var input = new[] { Entry("one"), Entry("two", 200m), Entry("three", 300m) };

        var written = await writer.AppendAsync(input);
        Assert.Equal(3, written.Count);
        Assert.Equal(LedgerHash.Genesis, written[0].PrevHash);
        Assert.Equal("e7134d168dc061f3cf2bd1856ae2ce0edfa00528249146523ea4a36784e57406", written[0].EntryHash);
        Assert.Equal(written[0].EntryHash, written[1].PrevHash);
        Assert.Equal(written[1].EntryHash, written[2].PrevHash);
        Assert.All(written, row => Assert.Equal(LedgerHash.Compute(row), row.EntryHash));
        Assert.Empty(await writer.AppendAsync(input));
        var result = await new LedgerChainVerifier(repository).VerifyAsync();
        Assert.True(result.Ok);
        Assert.Equal(3, result.CheckedCount);
    }

    [Fact]
    public async Task Tampered_entry_is_reported_at_its_code()
    {
        var repository = new MemoryLedger();
        await new LedgerWriter(repository, new Clock()).AppendAsync([Entry("one"), Entry("two"), Entry("three")]);
        repository.Rows[1].Amount++;

        var result = await new LedgerChainVerifier(repository).VerifyAsync();
        Assert.False(result.Ok);
        Assert.Equal("LG00000002", result.FirstBrokenEntryCode);
        Assert.Equal(2, result.CheckedCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.5)]
    public async Task Invalid_amount_is_rejected(decimal amount)
    {
        var repository = new MemoryLedger();
        await Assert.ThrowsAsync<ArgumentException>(() => new LedgerWriter(repository, new Clock()).AppendAsync([Entry("key", amount)]));
        Assert.Empty(repository.Rows);
    }

    [Fact]
    public async Task Same_party_and_invalid_key_are_rejected()
    {
        var writer = new LedgerWriter(new MemoryLedger(), new Clock());
        await Assert.ThrowsAsync<ArgumentException>(() => writer.AppendAsync([Entry("key") with { To = External }]));
        await Assert.ThrowsAsync<ArgumentException>(() => writer.AppendAsync([Entry("")]));
        await Assert.ThrowsAsync<ArgumentException>(() => writer.AppendAsync([Entry(new string('x', 81))]));
    }

    [Fact]
    public async Task Invalid_second_entry_does_not_write_the_first()
    {
        var repository = new MemoryLedger();
        await Assert.ThrowsAsync<ArgumentException>(() => new LedgerWriter(repository, new Clock())
            .AppendAsync([Entry("first"), Entry("invalid", 0.5m)]));
        Assert.Empty(repository.Rows);
    }

    [Fact]
    public async Task Build_callback_runs_inside_lock()
    {
        var repository = new MemoryLedger();
        var writer = new LedgerWriter(repository, new Clock());
        var result = await writer.AppendAsync(reader =>
        {
            Assert.True(repository.Locked);
            Assert.Same(repository, reader);
            return Task.FromResult<IReadOnlyList<NewLedgerEntry>>([Entry("locked")]);
        });
        Assert.Single(result);
    }

    [Fact]
    public void Repository_contract_has_no_update_or_delete_operation()
    {
        Assert.DoesNotContain(typeof(ILedgerRepository).GetMethods(), method =>
            method.Name.Contains("Update", StringComparison.OrdinalIgnoreCase) || method.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Sql_writer_joins_existing_transaction_and_rollback_removes_entries()
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrEmpty(connection) || new SqlConnectionStringBuilder(connection).InitialCatalog != "LedgerTicket725Test") return;

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options;
        await using var db = new AppDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var repository = new EfLedgerRepository(db);
        var writer = new LedgerWriter(repository, new Clock());
        var key = "writer-test:" + Guid.NewGuid().ToString("N");
        var first = await writer.AppendAsync([Entry(key)]);
        Assert.Single(first);
        Assert.Equal(LedgerHash.Compute(first[0]), first[0].EntryHash);
        Assert.Empty(await writer.AppendAsync([Entry(key)]));
        Assert.True((await new LedgerChainVerifier(repository).VerifyAsync()).Ok);
        await transaction.RollbackAsync();
        Assert.False(await db.LedgerEntries.AsNoTracking().AnyAsync(row => row.IdempotencyKey == key));
        await transaction.DisposeAsync();

        var atomicKey = "atomic-test:" + Guid.NewGuid().ToString("N");
        await Assert.ThrowsAsync<DbUpdateException>(() => writer.AppendAsync(
            [Entry(atomicKey), Entry(atomicKey + ":bad") with { Note = new string('x', 256) }]));
        Assert.False(await db.LedgerEntries.AsNoTracking().AnyAsync(row => row.IdempotencyKey == atomicKey));
    }
}
