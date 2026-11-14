using CommonService.Application.Exceptions;
using CommonService.Application.Features.Ledger;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Tests.Ledger;

/// <summary>
/// Decision D31 (LEDGER-30): withdrawals are paused from 00:00 on the 1st (Asia/Ho_Chi_Minh) until the previous month is settled for the
/// worker, because the settlement pays the balance at the cut-off and a withdrawal written in between would be paid a second time.
/// </summary>
public class WithdrawalSettlementPauseTests
{
    private const int Worker = 7;
    private const int Admin = 1;
    private const string October = "2026-10";

    // Asia/Ho_Chi_Minh is UTC+7: 00:00 on 1 November local is 17:00 on 31 October UTC.
    private static readonly DateTime CutOffUtc = new(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EndOfOctoberUtc = new(2026, 10, 30, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime MidNovemberUtc = new(2026, 11, 15, 3, 0, 0, DateTimeKind.Utc);

    private class Setup
    {
        public MemoryLedger Ledger { get; } = new();
        public MemoryWithdrawals Repository { get; } = new() { Settled = [] };
        public FakeClock Clock { get; } = new();
        public LedgerStoreQuery Query { get; }
        public WithdrawalService Wallet { get; }
        public AdminWithdrawalService AdminService { get; }

        public Setup(DateTime nowUtc, decimal earning = 1_000_000m)
        {
            Clock.Set(new DateTime(2026, 10, 20, 3, 0, 0, DateTimeKind.Utc));
            Query = new LedgerStoreQuery(Ledger);
            var balances = new LedgerBalanceService(Query, new FakeOpenDisputeQuery(), Clock);
            Wallet = new WithdrawalService(Repository, balances, Clock);
            AdminService = new AdminWithdrawalService(
                Repository, balances, new LedgerWriter(Ledger, Clock), new FakeAuditLog(), new FakeNotificationService(), Clock,
                NullLogger<AdminWithdrawalService>.Instance);
            Append(LedgerRules.Clearing, Self, earning, LedgerEntryType.JobEarning, "earn", Clock.UtcNow.AddHours(-30));
            Clock.Set(nowUtc);
        }

        public static LedgerParty Self => new(LedgerPartyType.Worker, Worker);

        public void Append(LedgerParty from, LedgerParty to, decimal amount, LedgerEntryType type, string key, DateTime? availableAt = null) =>
            new LedgerWriter(Ledger, Clock).AppendAsync([new NewLedgerEntry(from, to, amount, type, LedgerRefType.Assignment, 1, null, availableAt, key)]).Wait();

        /// <summary>What the settlement of October does for the worker: its item exists and the net leaves the wallet.</summary>
        public void SettleOctober(decimal net)
        {
            Repository.Settled!.Add((Worker, October));
            Append(Self, new LedgerParty(LedgerPartyType.External, null), net, LedgerEntryType.Settlement, "SETTLEMENT:2026-10:WORKER:7");
        }

        public async Task<int> PendingRequestAsync(decimal amount)
        {
            var now = Clock.UtcNow;
            Clock.Set(EndOfOctoberUtc);
            Repository.Settled!.Add((Worker, "2026-09"));
            var created = await Wallet.CreateAsync(Worker, amount);
            Repository.Settled.Remove((Worker, "2026-09"));
            Query.Held[Worker] = amount;
            Clock.Set(now);
            return created.WithdrawalId;
        }

        public decimal Balance => Ledger.Rows.Where(e => e.ToPartyType == LedgerPartyType.Worker).Sum(e => e.Amount)
            - Ledger.Rows.Where(e => e.FromPartyType == LedgerPartyType.Worker).Sum(e => e.Amount);

        public decimal PaidOut => Ledger.Rows
            .Where(e => e.FromPartyType == LedgerPartyType.Worker && e.EntryType is LedgerEntryType.Withdrawal or LedgerEntryType.Settlement).Sum(e => e.Amount);
    }

    private static async Task<BusinessRuleViolationException> Settling(Func<Task> action)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleViolationException>(action);
        Assert.Equal(WithdrawalErrorCodes.Settling, error.Code);
        return error;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(59)]
    public async Task A_request_is_refused_from_midnight_on_the_first_while_the_previous_month_has_no_batch(int minutesAfterMidnight)
    {
        var setup = new Setup(CutOffUtc.AddMinutes(minutesAfterMidnight));

        await Settling(() => setup.Wallet.CreateAsync(Worker, 100_000m));

        Assert.Empty(setup.Repository.Rows);
        Assert.Contains((Worker, October, true), setup.Repository.SettledChecks); // checked for October, under the worker lock
        Assert.True((await setup.Wallet.GetWalletAsync(Worker)).WithdrawalsPaused);
    }

    [Fact]
    public async Task The_last_minute_of_the_month_still_checks_the_month_before_it()
    {
        var setup = new Setup(CutOffUtc.AddMinutes(-1));
        setup.Repository.Settled!.Add((Worker, "2026-09"));

        var created = await setup.Wallet.CreateAsync(Worker, 100_000m);

        Assert.Equal("PENDING", created.Status);
        Assert.Contains((Worker, "2026-09", true), setup.Repository.SettledChecks);
    }

    [Fact]
    public async Task Once_the_worker_is_settled_the_request_and_the_wallet_are_free_again()
    {
        var setup = new Setup(CutOffUtc.AddMinutes(10));
        setup.SettleOctober(1_000_000m);
        setup.Append(LedgerRules.Clearing, Setup.Self, 200_000m, LedgerEntryType.JobEarning, "earn-nov", CutOffUtc);

        var wallet = await setup.Wallet.GetWalletAsync(Worker);
        Assert.False(wallet.WithdrawalsPaused);
        Assert.Equal(200_000m, wallet.Available);
        Assert.Equal("PENDING", (await setup.Wallet.CreateAsync(Worker, 200_000m)).Status);
    }

    [Fact]
    public async Task In_the_middle_of_a_month_with_the_previous_month_settled_nothing_is_paused()
    {
        var setup = new Setup(MidNovemberUtc);
        setup.SettleOctober(400_000m);

        Assert.False((await setup.Wallet.GetWalletAsync(Worker)).WithdrawalsPaused);
        Assert.Equal("PENDING", (await setup.Wallet.CreateAsync(Worker, 600_000m)).Status);
    }

    [Fact]
    public async Task An_approval_before_the_worker_is_settled_is_refused_and_writes_nothing()
    {
        var setup = new Setup(CutOffUtc.AddMinutes(2));
        var id = await setup.PendingRequestAsync(300_000m);

        var error = await Settling(() => setup.AdminService.ApproveAsync(Admin, id));

        Assert.Contains("tất toán", error.Message);
        Assert.DoesNotContain(setup.Ledger.Rows, e => e.EntryType == LedgerEntryType.Withdrawal);
        Assert.Equal(WithdrawalStatus.Pending, setup.Repository.Rows.Single().RequestStatus);
        Assert.Contains((Worker, October, true), setup.Repository.SettledChecks);
    }

    [Fact]
    public async Task Cancel_and_reject_stay_allowed_while_withdrawals_are_paused()
    {
        var setup = new Setup(CutOffUtc.AddMinutes(2));
        var first = await setup.PendingRequestAsync(100_000m);
        Assert.Equal("CANCELLED", (await setup.Wallet.CancelAsync(Worker, first)).Status);

        var second = await setup.PendingRequestAsync(100_000m);
        Assert.Equal("REJECTED", (await setup.AdminService.RejectAsync(Admin, second, "Sai số tài khoản")).Status);
    }

    [Fact]
    public async Task The_n3_case_no_longer_pays_the_same_money_twice()
    {
        // 1,000,000 earned in October, a 300,000 request still pending at the cut-off.
        var setup = new Setup(CutOffUtc.AddMinutes(2));
        var id = await setup.PendingRequestAsync(300_000m);

        // 00:02 on 1 November: the Admin tries to approve before the settlement ran.
        await Settling(() => setup.AdminService.ApproveAsync(Admin, id));

        // 00:05: the settlement pays the balance at the cut-off minus the held request (700,000); the request stays in the wallet.
        setup.SettleOctober(700_000m);

        // Now the approval goes through, from what is left in the wallet.
        Assert.Equal("PAID", (await setup.AdminService.ApproveAsync(Admin, id)).Status);
        Assert.Equal(1_000_000m, setup.PaidOut); // never 1,300,000
        Assert.Equal(0m, setup.Balance);
    }
}
