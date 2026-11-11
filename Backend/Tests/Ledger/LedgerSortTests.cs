using CommonService.Application.Exceptions;
using CommonService.Application.Features.Ledger;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;

namespace CommonService.Tests.Ledger;

/// <summary>Sorting the Admin ledger by a column (ticket LEDGER-21): the order is applied before paging, ties follow the entry id.</summary>
public class LedgerSortTests
{
    // id:   1                2                3                4
    // time: 3 Nov           1 Nov            2 Nov            4 Nov
    // amount: 300,000       100,000          200,000          100,000 (ties with id 2)
    private static readonly (decimal Amount, int Day)[] Seed = [(300_000m, 3), (100_000m, 1), (200_000m, 2), (100_000m, 4)];

    private static LedgerHistoryService Service()
    {
        var ledger = new MemoryLedger();
        foreach (var (amount, day) in Seed)
        {
            var id = ledger.Rows.Count + 1;
            var row = new LedgerEntry
            {
                EntryId = id,
                FromPartyType = LedgerPartyType.PlatformClearing,
                ToPartyType = LedgerPartyType.Worker,
                ToPartyId = 7,
                Amount = amount,
                EntryType = LedgerEntryType.JobEarning,
                RefType = LedgerRefType.Assignment,
                RefId = id,
                IdempotencyKey = $"seed:{id}",
                CreatedAt = new DateTime(2026, 11, day, 3, 0, 0, DateTimeKind.Utc),
                EntryHash = new string('0', 64),
                PrevHash = new string('0', 64),
            };
            typeof(LedgerEntry).GetProperty(nameof(LedgerEntry.EntryCode))!.SetValue(row, $"LG{id:00000000}");
            ledger.Rows.Add(row);
        }

        return new LedgerHistoryService(new MemoryHistoryQuery(ledger), new MemoryWithdrawals(), new StubWorkerEarnings(), new LedgerChainVerifier(ledger), new FakeClock());
    }

    private static async Task<long[]> Ids(string? sort, string? dir, int? page = null, int? pageSize = null) =>
        (await Service().GetAdminHistoryAsync(new LedgerHistoryRequest(null, null, null, page, pageSize, Sort: sort, Dir: dir))).Items.Select(e => e.EntryId).ToArray();

    [Fact]
    public async Task Without_a_sort_the_newest_entry_comes_first()
    {
        Assert.Equal(new long[] { 4, 3, 2, 1 }, await Ids(null, null));
    }

    [Theory]
    [InlineData("entryCode", "asc", new long[] { 1, 2, 3, 4 })]
    [InlineData("entryCode", "desc", new long[] { 4, 3, 2, 1 })]
    [InlineData("createdAt", "asc", new long[] { 2, 3, 1, 4 })]
    [InlineData("createdAt", "desc", new long[] { 4, 1, 3, 2 })]
    [InlineData("amount", "asc", new long[] { 2, 4, 3, 1 })] // the two 100,000 entries: ties by id in the same direction
    [InlineData("amount", "desc", new long[] { 1, 3, 4, 2 })]
    public async Task Every_column_orders_in_both_directions(string sort, string dir, long[] expected)
    {
        Assert.Equal(expected, await Ids(sort, dir));
    }

    [Fact]
    public async Task A_direction_alone_orders_by_time_and_a_column_alone_is_descending()
    {
        Assert.Equal(new long[] { 2, 3, 1, 4 }, await Ids(null, "asc"));
        Assert.Equal(new long[] { 4, 1, 3, 2 }, await Ids(null, "desc"));
        Assert.Equal(new long[] { 1, 3, 4, 2 }, await Ids("amount", null));
    }

    [Fact]
    public async Task The_names_are_not_case_sensitive_and_spaces_are_ignored()
    {
        Assert.Equal(new long[] { 2, 3, 1, 4 }, await Ids(" CREATEDAT ", " ASC "));
    }

    [Fact]
    public async Task The_order_is_applied_before_paging_so_it_is_right_across_pages()
    {
        var first = await Service().GetAdminHistoryAsync(new LedgerHistoryRequest(null, null, null, 1, 2, Sort: "amount", Dir: "asc"));
        var second = await Service().GetAdminHistoryAsync(new LedgerHistoryRequest(null, null, null, 2, 2, Sort: "amount", Dir: "asc"));

        Assert.Equal(new long[] { 2, 4 }, first.Items.Select(e => e.EntryId).ToArray());
        Assert.Equal(new long[] { 3, 1 }, second.Items.Select(e => e.EntryId).ToArray());
        Assert.Equal((4, 4), (first.Total, second.Total));
    }

    [Fact]
    public async Task An_unknown_sort_or_direction_is_a_400_naming_the_field()
    {
        var sort = await Assert.ThrowsAsync<ValidationException>(() => Ids("hash", "asc"));
        Assert.Contains("sort", sort.Errors.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("entryCode", sort.Errors.First(e => e.Key.Equals("sort", StringComparison.OrdinalIgnoreCase)).Value[0]);

        var dir = await Assert.ThrowsAsync<ValidationException>(() => Ids("amount", "up"));
        Assert.Contains("dir", dir.Errors.Keys, StringComparer.OrdinalIgnoreCase);

        var both = await Assert.ThrowsAsync<ValidationException>(() => Ids("x", "y"));
        Assert.Equal(2, both.Errors.Count);
    }
}
