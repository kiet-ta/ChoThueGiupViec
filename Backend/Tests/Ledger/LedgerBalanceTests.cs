using CommonService.Application.Features.Ledger;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Ledger;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Tests.Ledger;

public class LedgerBalanceTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 4, 0, 0, DateTimeKind.Utc);
    private static readonly LedgerParty Worker = new(LedgerPartyType.Worker, 7);

    private static (LedgerBalanceService Service, FakeLedgerQuery Ledger, FakeOpenDisputeQuery Disputes, FakeClock Clock) Create()
    {
        var ledger = new FakeLedgerQuery();
        var disputes = new FakeOpenDisputeQuery();
        var clock = new FakeClock();
        clock.Set(Now);
        return (new LedgerBalanceService(ledger, disputes, clock), ledger, disputes, clock);
    }

    private static LedgerEntry Credit(long orderId, decimal amount, DateTime? available) => new()
    {
        FromPartyType = LedgerPartyType.PlatformClearing,
        ToPartyType = LedgerPartyType.Worker,
        ToPartyId = 7,
        OrderId = orderId,
        Amount = amount,
        AvailableAt = available,
        CreatedAt = Now.AddDays(-1),
    };

    private static LedgerEntry Debit(decimal amount) => new()
    {
        FromPartyType = LedgerPartyType.Worker,
        FromPartyId = 7,
        ToPartyType = LedgerPartyType.External,
        Amount = amount,
        CreatedAt = Now,
    };

    [Fact]
    public async Task Earnings_become_available_at_exactly_twenty_four_hours()
    {
        var (service, ledger, _, clock) = Create();
        var eligibleAt = Now.AddSeconds(1);
        ledger.Entries.Add(Credit(1, 208000m, eligibleAt));
        var before = await service.GetAsync(Worker);
        Assert.Equal(208000m, before.Balance);
        Assert.Equal(0m, before.Available);
        Assert.Equal(eligibleAt, before.NextAvailableAt);
        clock.Advance(TimeSpan.FromSeconds(1));
        var atBoundary = await service.GetAsync(Worker);
        Assert.Equal(208000m, atBoundary.Available);
        Assert.Null(atBoundary.NextAvailableAt);
    }

    [Fact]
    public async Task Open_dispute_excludes_its_order_and_held_and_debits_are_subtracted()
    {
        var (service, ledger, disputes, _) = Create();
        ledger.Entries.AddRange([Credit(1, 200m, Now), Credit(2, 300m, Now), Debit(50m)]);
        ledger.Held[7] = 100m;
        disputes.OpenOrderIds.Add(1);
        var result = await service.GetAsync(Worker);
        Assert.Equal(450m, result.Balance);
        Assert.Equal(100m, result.Held);
        Assert.Equal(150m, result.Available);
    }

    [Fact]
    public async Task Negative_balance_floors_available_to_zero()
    {
        var (service, ledger, _, _) = Create();
        ledger.Entries.AddRange([Credit(1, 100m, null), Debit(300m)]);
        var result = await service.GetAsync(Worker);
        Assert.Equal(-200m, result.Balance);
        Assert.Equal(0m, result.Available);
    }

    [Fact]
    public async Task Agency_has_no_withdrawable_balance_and_cutoff_is_exclusive()
    {
        var (service, ledger, _, _) = Create();
        ledger.Entries.Add(new LedgerEntry { ToPartyType = LedgerPartyType.Agency, ToPartyId = 3, FromPartyType = LedgerPartyType.PlatformClearing, Amount = 200m, CreatedAt = Now });
        var agency = new LedgerParty(LedgerPartyType.Agency, 3);
        Assert.Equal(0m, await service.BalanceBeforeAsync(agency, Now));
        Assert.Equal(200m, await service.BalanceBeforeAsync(agency, Now.AddTicks(1)));
        var result = await service.GetAsync(agency);
        Assert.Equal(200m, result.Balance);
        Assert.Null(result.Available);
        Assert.Equal(0m, result.Held);
    }

    [Fact]
    public async Task Open_dispute_does_not_offer_a_next_available_time()
    {
        var (service, ledger, disputes, _) = Create();
        ledger.Entries.Add(Credit(1, 100m, Now.AddHours(1)));
        disputes.OpenOrderIds.Add(1);
        var result = await service.GetAsync(Worker);
        Assert.Equal(0m, result.Available);
        Assert.Null(result.NextAvailableAt);
    }

    [Fact]
    public async Task Sql_query_reads_worker_credits_and_pending_hold_within_transaction()
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrEmpty(connection) || new SqlConnectionStringBuilder(connection).InitialCatalog != "LedgerTicket725Test") return;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var clock = new FakeClock();
        clock.Set(Now);
        var worker = new LedgerParty(LedgerPartyType.Worker, 1234567);
        db.WithdrawalRequests.Add(new WithdrawalRequest { WorkerId = worker.Id!.Value, Amount = 50m, RequestStatus = WithdrawalStatus.Pending, RequestedAt = Now });
        await db.SaveChangesAsync();
        var writer = new LedgerWriter(new EfLedgerRepository(db), clock);
        await writer.AppendAsync([new NewLedgerEntry(new LedgerParty(LedgerPartyType.PlatformClearing, null), worker,
            200m, LedgerEntryType.JobEarning, LedgerRefType.Assignment, 1, 1, Now, "balance-test:" + Guid.NewGuid().ToString("N"))]);
        var service = new LedgerBalanceService(new EfLedgerQuery(db), new FakeOpenDisputeQuery(), clock);
        var result = await service.GetAsync(worker);
        Assert.Equal(200m, result.Balance);
        Assert.Equal(50m, result.Held);
        Assert.Equal(150m, result.Available);
        await transaction.RollbackAsync();
    }
}
