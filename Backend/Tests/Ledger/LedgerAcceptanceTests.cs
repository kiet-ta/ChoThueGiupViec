using CommonService.Application.Exceptions;
using CommonService.Application.Features.Ledger;
using CommonService.Application.Features.Payouts;
using CommonService.Application.Features.Payouts.Services;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using CommonService.Tests.Payouts;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Tests.Ledger;

/// <summary>
/// The acceptance test of the whole ledger (ticket LEDGER-18, handoff appendix A): one freelancer, W7, and one customer, C12, go through
/// November 2026 with the real posting, withdrawal, approval and settlement services on one hash-chained in-memory ledger. Times are Vietnam
/// time in the comments (UTC+7); the clock is moved to each step.
/// </summary>
public class LedgerAcceptanceTests
{
    private const int Worker = 7;
    private const int Customer = 12;
    private const int Admin = 1;
    private const int Agency = 3;

    /// <summary>The ledger read port over the shared ledger; the held amount is the pending requests, like the SQL query reports it.</summary>
    private class AcceptanceQuery(MemoryLedger ledger, MemoryWithdrawals withdrawals) : ILedgerQuery
    {
        private readonly LedgerStoreQuery _inner = new(ledger);

        public Task<IReadOnlyList<LedgerEntry>> GetPartyEntriesAsync(LedgerPartyType type, int? id, DateTime? beforeUtc = null, CancellationToken cancellationToken = default) =>
            _inner.GetPartyEntriesAsync(type, id, beforeUtc, cancellationToken);

        public Task<decimal> GetHeldAsync(int workerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(withdrawals.Rows.Where(r => r.WorkerId == workerId && r.RequestStatus == WithdrawalStatus.Pending).Sum(r => r.Amount));

        public Task<IReadOnlyList<(LedgerPartyType Type, int Id)>> GetSettlementPartiesAsync(DateTime beforeUtc, CancellationToken cancellationToken = default) =>
            _inner.GetSettlementPartiesAsync(beforeUtc, cancellationToken);

        public Task<IReadOnlyList<LedgerEntry>> GetCommissionsAsync(LedgerRefType refType, IReadOnlyCollection<long> refIds, CancellationToken cancellationToken = default) =>
            _inner.GetCommissionsAsync(refType, refIds, cancellationToken);

        public Task<DateTime?> GetFirstEntryAtAsync(CancellationToken cancellationToken = default) => _inner.GetFirstEntryAtAsync(cancellationToken);
    }

    private class PlainUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default) => action();
    }

    private class NullPublisher : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }

    /// <summary>Every service on one ledger and one clock.</summary>
    private class Rig
    {
        public LedgerFlow Flow { get; } = new();
        public MemoryWithdrawals Withdrawals { get; } = new();
        public FakeOpenDisputeQuery Disputes { get; } = new();
        public FakeAuditLog Audit { get; } = new();
        public FakeNotificationService Notifications { get; } = new();
        public PayoutBatchServiceTests.MemoryPayouts Payouts { get; } = new();
        public WithdrawalService Wallet { get; }
        public AdminWithdrawalService Approval { get; }
        public PayoutBatchService Settlement { get; }

        public Rig(int? agencyId = null)
        {
            var query = new AcceptanceQuery(Flow.Ledger, Withdrawals);
            var balances = new LedgerBalanceService(query, Disputes, Flow.Clock);
            Wallet = new WithdrawalService(Withdrawals, balances, Flow.Clock);
            Approval = new AdminWithdrawalService(Withdrawals, balances, Flow.Writer, Audit, Notifications, Flow.Clock, NullLogger<AdminWithdrawalService>.Instance);
            Settlement = new PayoutBatchService(Payouts, query, Flow.Writer, Audit, new PlainUnitOfWork(), Flow.Clock, new NullPublisher(), NullLogger<PayoutBatchService>.Instance);

            Flow.AddOrder(1, Customer, 260_000m);
            Flow.AddAssignment(10, 1, Worker, agencyId, gross: 260_000m);
            Flow.Source.Extensions.Add(new LedgerExtensionSource(5, 1, Worker, 100_000m, true));
        }

        /// <summary>Moves the clock to a Vietnam local time.</summary>
        public void At(int month, int day, int hour, int minute = 0) =>
            Flow.Clock.Set(new DateTime(2026, month, day, hour, minute, 0, DateTimeKind.Utc).AddHours(-7));

        private DateTime Now => Flow.Clock.UtcNow;

        /// <summary>Steps 1-3: the customer pays the order (03/11 08:00) and the extension (10:00), the job is completed (13:00).</summary>
        public async Task PayAndComplete(int? agencyId = null)
        {
            At(11, 3, 8);
            await Flow.Posting.PostOrderPaidAsync(new OrderPaid(1, Customer, 260_000m, "MORNING", Now.Date, 1));
            At(11, 3, 10);
            await Flow.Posting.PostExtensionPaidAsync(new ExtensionPaid(5, 1, Worker, 2m, 100_000m, Now));
            At(11, 3, 13);
            await Flow.Posting.PostJobCompletedAsync(new JobCompleted(10, 1, Worker, agencyId, 0m, Now));
        }

        /// <summary>The dispute of order 1 is decided against the freelancer; the customer is refunded 50,000 from the platform's revenue.</summary>
        public async Task Dispute(decimal compensation, bool refund = true)
        {
            Flow.Source.Disputes.Add(new LedgerDisputeSource(4, 1, true, FaultParty.Freelancer, "QUALITY", compensation));
            if (refund) await Flow.Posting.PostRefundAsync(new OrderRefunded(1, Customer, compensation, "dispute", Now));
            await Flow.Posting.PostPenaltyAsync(new DisputeResolved(4, 10, FaultParty.Freelancer, compensation, "note", Now));
        }

        public decimal Balance(LedgerPartyType type, int? id = null) => Flow.Balance(type, id);

        public decimal WorkerBalance => Balance(LedgerPartyType.Worker, Worker);

        public IReadOnlyList<LedgerEntry> Of(LedgerEntryType type) => Flow.Of(type);

        public async Task<NewPayoutItem> Settle(string period, bool allowUnfinished = false)
        {
            var result = await Settlement.SettleAsync(period, allowUnfinished);
            Assert.True(result.Success, result.ErrorMessage);
            return Payouts.Stored.Values.Last().Single(i => i.WorkerId == Worker);
        }
    }

    private static async Task<T> Thrown<T>(Func<Task> action) where T : Exception => await Assert.ThrowsAsync<T>(action);

    [Fact]
    public async Task The_month_of_the_appendix_ends_with_net_138000_revenue_72000_and_a_zero_worker_balance()
    {
        var rig = new Rig();
        await rig.PayAndComplete();

        // 3. 13:00: the job is completed and split 80/20, extension included.
        Assert.Equal(288_000m, rig.WorkerBalance);
        Assert.Equal(72_000m, rig.Balance(LedgerPartyType.PlatformRevenue));
        Assert.Equal(0m, rig.Flow.Clearing(1));
        var beforeMaturity = await rig.Wallet.GetWalletAsync(Worker);
        Assert.Equal((288_000m, 0m, 0m), (beforeMaturity.Balance, beforeMaturity.Available, beforeMaturity.Held));

        // 4. 04/11 13:00: 24 hours have passed.
        rig.At(11, 4, 13);
        Assert.Equal(288_000m, (await rig.Wallet.GetWalletAsync(Worker)).Available);

        // 5. 05/11 09:00: W7 asks for 100,000 (held), 09:05 a second request is refused.
        rig.At(11, 5, 9);
        var request = await rig.Wallet.CreateAsync(Worker, 100_000m);
        var asked = await rig.Wallet.GetWalletAsync(Worker);
        Assert.Equal((288_000m, 188_000m, 100_000m, "PENDING"), (asked.Balance, asked.Available, asked.Held, asked.PendingWithdrawal!.Status));
        rig.At(11, 5, 9, 5);
        Assert.Equal("WITHDRAWAL_PENDING", (await Thrown<BusinessRuleViolationException>(() => rig.Wallet.CreateAsync(Worker, 10_000m))).Code);
        Assert.Single(rig.Withdrawals.Rows);

        // 7. 10:00: the Admin approves; the ledger writes the WITHDRAWAL entry and the request is paid.
        rig.At(11, 5, 10);
        var paid = await rig.Approval.ApproveAsync(Admin, request.WithdrawalId);
        Assert.Equal("PAID", paid.Status);
        var withdrawal = Assert.Single(rig.Of(LedgerEntryType.Withdrawal));
        Assert.Equal((LedgerPartyType.Worker, Worker, LedgerPartyType.External, 100_000m), (withdrawal.FromPartyType, withdrawal.FromPartyId!.Value, withdrawal.ToPartyType, withdrawal.Amount));
        Assert.Equal(188_000m, rig.WorkerBalance);

        // 8. 06/11: the dispute is decided against the freelancer: 50,000 refunded to the customer and 50,000 deducted from W7.
        rig.At(11, 6, 11);
        await rig.Dispute(50_000m);
        Assert.Equal(138_000m, rig.WorkerBalance);
        Assert.Equal(50_000m, Assert.Single(rig.Of(LedgerEntryType.Penalty)).Amount);
        Assert.Equal(50_000m, Assert.Single(rig.Of(LedgerEntryType.Refund)).Amount);
        Assert.Equal(0m, rig.Flow.Clearing(1));
        Assert.Equal(72_000m, rig.Balance(LedgerPartyType.PlatformRevenue)); // 52,000 + 20,000 - 50,000 refund + 50,000 penalty

        // 9. 01/12 00:05: the automatic settlement of November.
        rig.At(12, 1, 0, 5);
        var item = await rig.Settle("2026-11");

        Assert.Equal((360_000m, 72_000m, 50_000m, 100_000m, 0m, 138_000m), (item.GrossAmount, item.CommissionAmount, item.PenaltyAmount, item.AdvanceAmount, item.CarryInAmount, item.NetAmount));
        var settlement = Assert.Single(rig.Of(LedgerEntryType.Settlement));
        Assert.Equal((LedgerPartyType.Worker, Worker, LedgerPartyType.External, 138_000m), (settlement.FromPartyType, settlement.FromPartyId!.Value, settlement.ToPartyType, settlement.Amount));
        Assert.Equal("SETTLEMENT:2026-11:WORKER:7", settlement.IdempotencyKey);

        // The ledger at the end of the month.
        Assert.Equal(0m, rig.WorkerBalance);
        Assert.Equal(72_000m, rig.Balance(LedgerPartyType.PlatformRevenue));
        Assert.Equal(0m, rig.Flow.Clearing(1));
        Assert.Equal(0m, rig.Balance(LedgerPartyType.PlatformClearing));
        var batch = Assert.Single(rig.Payouts.Batches);
        Assert.Equal(("2026-11", "CLOSED", (int?)null), (batch.PeriodMonth, batch.BatchStatus, batch.ConfirmedBy));

        // Every dong is somewhere: the balances of all accounts add up to zero (customer, platform, worker, the outside).
        var allBalances = new[]
        {
            rig.Balance(LedgerPartyType.External, Customer), rig.Balance(LedgerPartyType.External), rig.Balance(LedgerPartyType.PlatformRevenue),
            rig.Balance(LedgerPartyType.PlatformClearing), rig.WorkerBalance,
        };
        Assert.Equal(0m, allBalances.Sum());
        Assert.Equal(-310_000m, rig.Balance(LedgerPartyType.External, Customer)); // paid 360,000, refunded 50,000
        Assert.Equal(238_000m, rig.Balance(LedgerPartyType.External)); // what left the platform to W7: 100,000 withdrawn + 138,000 settled

        // The chain verifies and a tampered entry is reported at its code.
        var verified = await new LedgerChainVerifier(rig.Flow.Ledger).VerifyAsync();
        Assert.Equal((true, rig.Flow.Ledger.Rows.Count, (string?)null), (verified.Ok, verified.CheckedCount, verified.FirstBrokenEntryCode));

        var tampered = rig.Flow.Ledger.Rows.Single(e => e.EntryType == LedgerEntryType.Withdrawal);
        tampered.Amount++;
        var broken = await new LedgerChainVerifier(rig.Flow.Ledger).VerifyAsync();
        Assert.Equal((false, tampered.EntryCode), (broken.Ok, broken.FirstBrokenEntryCode));
    }

    [Fact]
    public async Task A_replayed_settlement_and_a_replayed_event_write_nothing_new()
    {
        var rig = new Rig();
        await rig.PayAndComplete();
        rig.At(12, 1, 0, 5);
        await rig.Settle("2026-11");
        var rows = rig.Flow.Ledger.Rows.Count;

        await rig.Settle("2026-11");
        await rig.Flow.Posting.PostJobCompletedAsync(new JobCompleted(10, 1, Worker, null, 0m, new DateTime(2026, 11, 3, 6, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(rows, rig.Flow.Ledger.Rows.Count);
        Assert.Single(rig.Of(LedgerEntryType.Settlement));
        Assert.Equal(0m, rig.WorkerBalance);
    }

    [Fact]
    public async Task A_dispute_decided_before_the_approval_does_not_stop_the_approval()
    {
        var rig = new Rig();
        await rig.PayAndComplete();
        rig.At(11, 5, 9);
        var request = await rig.Wallet.CreateAsync(Worker, 100_000m);
        rig.At(11, 5, 9, 30);
        await rig.Dispute(50_000m); // step 8 before step 7: 288,000 - 50,000 - 100,000 held is still enough

        rig.At(11, 5, 10);
        var paid = await rig.Approval.ApproveAsync(Admin, request.WithdrawalId);

        Assert.Equal("PAID", paid.Status);
        Assert.Equal(138_000m, rig.WorkerBalance);
        rig.At(12, 1, 0, 5);
        var item = await rig.Settle("2026-11");
        Assert.Equal((100_000m, 50_000m, 138_000m), (item.AdvanceAmount, item.PenaltyAmount, item.NetAmount));
        Assert.Equal(0m, rig.WorkerBalance);
    }

    [Fact]
    public async Task An_open_dispute_keeps_the_order_money_out_of_the_withdrawable_amount()
    {
        var rig = new Rig();
        await rig.PayAndComplete();
        rig.Disputes.OpenOrderIds.Add(1);
        rig.At(11, 5, 9);

        var wallet = await rig.Wallet.GetWalletAsync(Worker);
        Assert.Equal((288_000m, 0m), (wallet.Balance, wallet.Available));
        await Thrown<ValidationException>(() => rig.Wallet.CreateAsync(Worker, 100_000m));
        Assert.Empty(rig.Withdrawals.Rows);

        rig.Disputes.OpenOrderIds.Clear(); // the dispute was closed without a deduction
        Assert.Equal(100_000m, (await rig.Wallet.CreateAsync(Worker, 100_000m)).Amount);
        Assert.Equal(188_000m, (await rig.Wallet.GetWalletAsync(Worker)).Available);
    }

    [Fact]
    public async Task A_request_still_waiting_at_the_end_of_the_month_stays_held_and_is_an_advance_of_the_next_month()
    {
        var rig = new Rig();
        await rig.PayAndComplete();
        rig.At(11, 5, 9);
        var request = await rig.Wallet.CreateAsync(Worker, 100_000m);
        rig.At(11, 6, 11);
        await rig.Dispute(50_000m);

        rig.At(12, 1, 0, 5);
        var item = await rig.Settle("2026-11");
        Assert.Equal((0m, 50_000m, 138_000m), (item.AdvanceAmount, item.PenaltyAmount, item.NetAmount)); // 288,000 - 50,000 - 100,000 held
        Assert.Equal(100_000m, rig.WorkerBalance);
        Assert.Equal(PayoutConstants.BatchClosed, Assert.Single(rig.Payouts.Batches).BatchStatus);

        rig.At(12, 2, 10);
        var paid = await rig.Approval.ApproveAsync(Admin, request.WithdrawalId);

        Assert.Equal("PAID", paid.Status);
        Assert.Equal(0m, rig.WorkerBalance);
        var withdrawal = Assert.Single(rig.Of(LedgerEntryType.Withdrawal));
        Assert.Equal(new DateTime(2026, 12, 2, 3, 0, 0, DateTimeKind.Utc), withdrawal.CreatedAt); // a December entry
    }

    [Fact]
    public async Task A_penalty_larger_than_the_balance_leaves_a_debt_that_is_carried_into_the_next_month()
    {
        var rig = new Rig();
        await rig.PayAndComplete();
        rig.At(11, 6, 11);
        await rig.Dispute(400_000m, refund: false);
        Assert.Equal(-112_000m, rig.WorkerBalance);

        rig.At(12, 1, 0, 5);
        var november = await rig.Settle("2026-11");
        Assert.Equal((400_000m, 0m), (november.PenaltyAmount, november.NetAmount));
        Assert.Empty(rig.Of(LedgerEntryType.Settlement)); // nothing to pay out
        Assert.Equal(-112_000m, rig.WorkerBalance);

        rig.At(12, 15, 10);
        var december = await rig.Settle("2026-12", allowUnfinished: true);
        Assert.Equal((-112_000m, 0m), (december.CarryInAmount, december.NetAmount));
        Assert.Empty(rig.Of(LedgerEntryType.Settlement));
    }

    [Fact]
    public async Task An_agency_job_is_paid_to_the_agency_with_no_waiting_time_and_settled_without_a_hold()
    {
        var rig = new Rig(agencyId: Agency);
        await rig.PayAndComplete(Agency);

        var earning = Assert.Single(rig.Of(LedgerEntryType.JobEarning));
        Assert.Equal((LedgerPartyType.Agency, Agency, 208_000m), (earning.ToPartyType, earning.ToPartyId!.Value, earning.Amount));
        Assert.Null(earning.AvailableAt);
        Assert.Equal(0m, rig.WorkerBalance);
        Assert.Equal(80_000m, Assert.Single(rig.Of(LedgerEntryType.ExtensionEarning)).Amount); // the extension of the agency's worker goes to the agency too
        Assert.Equal(288_000m, rig.Balance(LedgerPartyType.Agency, Agency));

        rig.At(12, 1, 0, 5);
        var result = await rig.Settlement.SettleAsync("2026-11");
        Assert.True(result.Success, result.ErrorMessage);
        var item = rig.Payouts.Stored.Values.Single().Single(i => i.AgencyId == Agency);
        Assert.Equal((288_000m, 0m), (item.NetAmount, item.AdvanceAmount));
        var settlement = Assert.Single(rig.Of(LedgerEntryType.Settlement));
        Assert.Equal((LedgerPartyType.Agency, Agency, item.NetAmount), (settlement.FromPartyType, settlement.FromPartyId!.Value, settlement.Amount));
        Assert.Equal(0m, rig.Balance(LedgerPartyType.Agency, Agency));
        Assert.True((await new LedgerChainVerifier(rig.Flow.Ledger).VerifyAsync()).Ok);
    }
}
