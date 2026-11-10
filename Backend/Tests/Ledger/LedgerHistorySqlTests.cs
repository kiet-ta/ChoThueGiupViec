using CommonService.Application.Features.Ledger;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Ledger;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Tests.Ledger;

/// <summary>
/// The SQL side of LEDGER-11: the history search, the balances and the totals computed by the database. Runs only against the scratch
/// database <c>LedgerTicket725Test</c> (set <c>ConnectionStrings__DefaultConnection</c>); everything is rolled back.
/// </summary>
public class LedgerHistorySqlTests
{
    private const int Worker = 987801;
    private const int Agency = 987802;

    [Fact]
    public async Task Sql_history_search_balances_and_totals_match_the_ledger()
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrEmpty(connection) || new SqlConnectionStringBuilder(connection).InitialCatalog != "LedgerTicket725Test") return;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE WITHDRAWAL_REQUEST NOCHECK CONSTRAINT ALL");

        var clock = new FakeClock();
        var writer = new LedgerWriter(new EfLedgerRepository(db), clock);
        var worker = new LedgerParty(LedgerPartyType.Worker, Worker);
        var agency = new LedgerParty(LedgerPartyType.Agency, Agency);
        var escrow = new LedgerParty(LedgerPartyType.AgencyEscrow, Agency);
        var external = new LedgerParty(LedgerPartyType.External, null);

        async Task Write(DateTime at, LedgerParty from, LedgerParty to, decimal amount, LedgerEntryType type, LedgerRefType refType = LedgerRefType.Assignment)
        {
            clock.Set(at);
            await writer.AppendAsync([new NewLedgerEntry(from, to, amount, type, refType, 1, null, null, "sql-history:" + Guid.NewGuid().ToString("N"))]);
        }

        var october = new DateTime(2026, 10, 31, 16, 59, 59, DateTimeKind.Utc); // 31 October 23:59:59 in Ho Chi Minh
        var november = new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc); // 1 November 00:00:00
        var middle = new DateTime(2026, 11, 5, 3, 0, 0, DateTimeKind.Utc);
        await Write(october, LedgerRules.Clearing, worker, 1_000m, LedgerEntryType.JobEarning);
        await Write(november, LedgerRules.Clearing, worker, 2_000m, LedgerEntryType.JobEarning);
        await Write(middle, LedgerRules.Clearing, LedgerRules.Revenue, 500m, LedgerEntryType.Commission);
        await Write(middle, worker, external, 300m, LedgerEntryType.Withdrawal, LedgerRefType.Withdrawal);
        await Write(middle, LedgerRules.Clearing, agency, 4_000m, LedgerEntryType.JobEarning);
        await Write(middle, external, escrow, 9_000m, LedgerEntryType.EscrowDeposit, LedgerRefType.EscrowTxn);
        await Write(middle, escrow, LedgerRules.Revenue, 700m, LedgerEntryType.EscrowPenalty, LedgerRefType.EscrowTxn);
        db.WithdrawalRequests.Add(new WithdrawalRequest { WorkerId = Worker, Amount = 150m, RequestStatus = WithdrawalStatus.Pending, RequestedAt = middle });
        await db.SaveChangesAsync();

        var query = new EfLedgerHistoryQuery(db);
        var all = await query.SearchAsync(new LedgerSearch([], null, null, null), 1, 100);
        Assert.Equal(7, all.Total);
        Assert.Equal(all.Items.Select(i => i.EntryId).OrderByDescending(id => id).ToArray(), all.Items.Select(i => i.EntryId).ToArray()); // newest first

        var mine = await query.SearchAsync(new LedgerSearch([new LedgerPartyFilter(LedgerPartyType.Worker, Worker, false)], null, null, null), 1, 100);
        Assert.Equal([300m, 2_000m, 1_000m], mine.Items.Select(i => i.Amount).ToArray());

        var anyWorker = await query.SearchAsync(new LedgerSearch([new LedgerPartyFilter(LedgerPartyType.Worker, null, true)], null, null, null), 1, 100);
        Assert.Contains(anyWorker.Items, i => i.ToPartyId == Worker);

        var revenue = await query.SearchAsync(new LedgerSearch([new LedgerPartyFilter(LedgerPartyType.PlatformRevenue, null, false)], null, null, null), 1, 100);
        Assert.Equal([700m, 500m], revenue.Items.Select(i => i.Amount).ToArray());

        var agencyBoth = await query.SearchAsync(
            new LedgerSearch([new LedgerPartyFilter(LedgerPartyType.Agency, Agency, false), new LedgerPartyFilter(LedgerPartyType.AgencyEscrow, Agency, false)], null, null, null), 1, 100);
        Assert.Equal([700m, 9_000m, 4_000m], agencyBoth.Items.Select(i => i.Amount).ToArray());

        var novemberOnly = await query.SearchAsync(new LedgerSearch([new LedgerPartyFilter(LedgerPartyType.Worker, Worker, false)], november, null, LedgerEntryType.JobEarning), 1, 100);
        Assert.Equal([2_000m], novemberOnly.Items.Select(i => i.Amount).ToArray());
        var paged = await query.SearchAsync(new LedgerSearch([], null, null, null), 2, 3);
        Assert.Equal((7, 3), (paged.Total, paged.Items.Count));

        Assert.Equal(1_000m, await query.GetBalanceBeforeAsync(worker, november)); // before 1 November
        Assert.Equal(2_700m, await query.GetBalanceBeforeAsync(worker, null)); // 1,000 + 2,000 - 300
        Assert.Equal(8_300m, await query.GetBalanceBeforeAsync(escrow, null)); // 9,000 - 700
        Assert.Equal(0m, await query.GetBalanceBeforeAsync(new LedgerParty(LedgerPartyType.Worker, 1), null));

        var totals = await query.GetTotalsAsync();
        Assert.Equal((1_200m, -7_500m, 2_700m, 4_000m, 150m), (totals.Revenue, totals.Clearing, totals.WorkerDebt, totals.AgencyDebt, totals.Held)); // the clearing account only paid out here

        // The service over the real query: Vietnam days, balances and the chain.
        var service = new LedgerHistoryService(query, new MemoryWithdrawals(), new LedgerChainVerifier(new EfLedgerRepository(db)), clock);
        var page = await service.GetAdminHistoryAsync(new LedgerHistoryRequest("2026-11-01", "2026-11-30", null, null, null, "WORKER", Worker));
        Assert.Equal([300m, 2_000m], page.Items.Select(i => i.Amount).ToArray());
        Assert.True((await service.VerifyAsync()).Ok);
        await transaction.RollbackAsync();
    }
}
