using CommonService.Application.Features.Ledger;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Tests.Ledger;

/// <summary>Every money flow of decision D28 written to the ledger with exact amounts (ticket LEDGER-05).</summary>
public class LedgerPostingTests
{
    private const int Customer = 12;
    private const int Worker = 7;
    private const int Agency = 3;
    private static readonly DateTime Done = new(2026, 11, 10, 3, 0, 0, DateTimeKind.Utc);

    private static OrderPaid Paid(long orderId, decimal amount, int seats = 1) =>
        new(orderId, Customer, amount, "MORNING", Done.Date, seats);

    private static JobCompleted Completed(long assignmentId, long orderId, int workerId = Worker, int? agencyId = null) =>
        new(assignmentId, orderId, workerId, agencyId, 0m, Done);

    private static OrderRefunded Refunded(long orderId, decimal amount, DateTime at) => new(orderId, Customer, amount, "test", at);

    [Fact]
    public async Task Order_payment_enters_clearing_and_replay_writes_nothing()
    {
        var flow = new LedgerFlow();
        await flow.Posting.PostOrderPaidAsync(Paid(1, 260_000m));
        await flow.Posting.PostOrderPaidAsync(Paid(1, 260_000m));

        var row = Assert.Single(flow.Ledger.Rows);
        Assert.Equal((LedgerEntryType.OrderPaid, LedgerPartyType.External, Customer, LedgerPartyType.PlatformClearing, 260_000m),
            (row.EntryType, row.FromPartyType, row.FromPartyId!.Value, row.ToPartyType, row.Amount));
        Assert.Equal(("ORDER_PAID:ORDER:1", LedgerRefType.Order, 1L, 1L), (row.IdempotencyKey, row.RefType, row.RefId, row.OrderId));
    }

    [Fact]
    public async Task Extension_payment_takes_the_customer_from_the_order_and_replay_writes_nothing()
    {
        var flow = new LedgerFlow();
        flow.AddOrder(1, Customer, 260_000m);
        var paid = new ExtensionPaid(5, 1, Worker, 2m, 100_000m, Done);
        await flow.Posting.PostExtensionPaidAsync(paid);
        await flow.Posting.PostExtensionPaidAsync(paid);

        var row = Assert.Single(flow.Ledger.Rows);
        Assert.Equal((LedgerEntryType.ExtensionPaid, Customer, 100_000m, "EXTENSION_PAID:EXTENSION:5", LedgerRefType.Extension, 5L),
            (row.EntryType, row.FromPartyId!.Value, row.Amount, row.IdempotencyKey, row.RefType, row.RefId));
        Assert.Equal(100_000m, flow.Clearing(1));
    }

    [Fact]
    public async Task Freelancer_job_splits_80_20_withdrawable_after_24_hours_and_clearing_returns_to_zero()
    {
        var flow = new LedgerFlow();
        flow.AddOrder(1, Customer, 260_000m);
        flow.AddAssignment(10, 1, Worker, gross: 260_000m);
        await flow.Posting.PostOrderPaidAsync(Paid(1, 260_000m));
        await flow.Posting.PostJobCompletedAsync(Completed(10, 1));

        var earning = Assert.Single(flow.Of(LedgerEntryType.JobEarning));
        Assert.Equal((LedgerPartyType.Worker, Worker, 208_000m, Done.AddHours(24), "JOB_EARNING:ASSIGNMENT:10"),
            (earning.ToPartyType, earning.ToPartyId!.Value, earning.Amount, earning.AvailableAt!.Value, earning.IdempotencyKey));
        var commission = Assert.Single(flow.Of(LedgerEntryType.Commission));
        Assert.Equal((LedgerPartyType.PlatformRevenue, 52_000m, "COMMISSION:ASSIGNMENT:10"), (commission.ToPartyType, commission.Amount, commission.IdempotencyKey));
        Assert.Equal(0m, flow.Clearing(1));
        Assert.Equal(52_000m, flow.Balance(LedgerPartyType.PlatformRevenue));

        var before = flow.Ledger.Rows.Count;
        await flow.Posting.PostJobCompletedAsync(Completed(10, 1));
        Assert.Equal(before, flow.Ledger.Rows.Count);
        Assert.True((await new LedgerChainVerifier(flow.Ledger).VerifyAsync()).Ok);
    }

    [Fact]
    public async Task Agency_job_pays_the_agency_party_with_its_own_rate_and_no_withdrawal_delay()
    {
        var flow = new LedgerFlow();
        flow.AddOrder(1, Customer, 260_000m);
        flow.AddAssignment(10, 1, Worker, Agency, gross: 260_000m, rate: 0.15m);
        await flow.Posting.PostOrderPaidAsync(Paid(1, 260_000m));
        await flow.Posting.PostJobCompletedAsync(Completed(10, 1, agencyId: Agency));

        var earning = Assert.Single(flow.Of(LedgerEntryType.JobEarning));
        Assert.Equal((LedgerPartyType.Agency, Agency, 221_000m), (earning.ToPartyType, earning.ToPartyId!.Value, earning.Amount));
        Assert.Null(earning.AvailableAt);
        Assert.Equal(39_000m, Assert.Single(flow.Of(LedgerEntryType.Commission)).Amount);
        Assert.Equal(0m, flow.Balance(LedgerPartyType.Worker, Worker));
        Assert.Equal(0m, flow.Clearing(1));
    }

    [Fact]
    public async Task Two_seat_order_is_split_into_whole_dong_and_clearing_returns_to_zero()
    {
        var flow = new LedgerFlow();
        flow.AddOrder(1, Customer, 255_001m, seats: 2);
        // The stored gross is fractional (total / 2): the ledger must not use it.
        flow.AddAssignment(10, 1, Worker, seat: 1, gross: 127_500.5m);
        flow.AddAssignment(11, 1, 8, seat: 2, gross: 127_500.5m);
        await flow.Posting.PostOrderPaidAsync(Paid(1, 255_001m, 2));
        await flow.Posting.PostJobCompletedAsync(Completed(10, 1));
        await flow.Posting.PostJobCompletedAsync(Completed(11, 1, 8));

        Assert.Equal([102_001m, 102_000m], flow.Of(LedgerEntryType.JobEarning).Select(e => e.Amount).ToArray());
        Assert.Equal([25_500m, 25_500m], flow.Of(LedgerEntryType.Commission).Select(e => e.Amount).ToArray());
        Assert.Equal(0m, flow.Clearing(1));
    }

    [Fact]
    public async Task A_replacement_assignment_takes_the_value_of_the_seat_it_replaces()
    {
        var flow = new LedgerFlow();
        flow.AddOrder(1, Customer, 255_001m, seats: 2);
        flow.AddAssignment(10, 1, Worker, seat: 1);
        flow.AddAssignment(12, 1, 9, seat: 2); // replaced seat 2: another worker, the same seq
        await flow.Posting.PostOrderPaidAsync(Paid(1, 255_001m, 2));
        await flow.Posting.PostJobCompletedAsync(Completed(12, 1, 9));

        Assert.Equal(102_000m, Assert.Single(flow.Of(LedgerEntryType.JobEarning)).Amount);
        Assert.Equal(25_500m, Assert.Single(flow.Of(LedgerEntryType.Commission)).Amount);
    }

    [Fact]
    public async Task Paid_extension_is_split_80_20_and_credited_only_to_the_extension_worker()
    {
        var flow = new LedgerFlow();
        flow.AddOrder(1, Customer, 260_000m, seats: 2);
        flow.AddAssignment(10, 1, Worker, seat: 1);
        flow.AddAssignment(11, 1, 8, seat: 2);
        flow.Source.Extensions.Add(new LedgerExtensionSource(5, 1, Worker, 100_000m, true));
        flow.Source.Extensions.Add(new LedgerExtensionSource(6, 1, Worker, 40_000m, false)); // declined or never paid
        await flow.Posting.PostOrderPaidAsync(Paid(1, 260_000m, 2));
        await flow.Posting.PostExtensionPaidAsync(new ExtensionPaid(5, 1, Worker, 2m, 100_000m, Done));

        await flow.Posting.PostJobCompletedAsync(Completed(11, 1, 8));
        Assert.Empty(flow.Of(LedgerEntryType.ExtensionEarning));

        await flow.Posting.PostJobCompletedAsync(Completed(10, 1));
        var extension = Assert.Single(flow.Of(LedgerEntryType.ExtensionEarning));
        Assert.Equal((Worker, 80_000m, Done.AddHours(24), "EXTENSION_EARNING:EXTENSION:5", LedgerRefType.Extension, 5L),
            (extension.ToPartyId!.Value, extension.Amount, extension.AvailableAt!.Value, extension.IdempotencyKey, extension.RefType, extension.RefId));
        Assert.Equal(20_000m, flow.Of(LedgerEntryType.Commission).Single(e => e.RefType == LedgerRefType.Extension).Amount);
        Assert.Equal(0m, flow.Clearing(1));
        Assert.Equal(360_000m - 72_000m - 0m, flow.Balance(LedgerPartyType.Worker, Worker) + flow.Balance(LedgerPartyType.Worker, 8));
        Assert.Equal(72_000m, flow.Balance(LedgerPartyType.PlatformRevenue));
    }

    [Fact]
    public async Task Extension_paid_after_the_job_was_completed_is_credited_at_once()
    {
        var flow = new LedgerFlow();
        flow.AddOrder(1, Customer, 260_000m);
        flow.AddAssignment(10, 1, Worker, completedAt: Done);
        await flow.Posting.PostExtensionPaidAsync(new ExtensionPaid(5, 1, Worker, 2m, 100_000m, Done.AddHours(1)));

        Assert.Equal(80_000m, Assert.Single(flow.Of(LedgerEntryType.ExtensionEarning)).Amount);
        Assert.Equal(20_000m, Assert.Single(flow.Of(LedgerEntryType.Commission)).Amount);
        Assert.Equal(0m, flow.Clearing(1));
    }

    [Fact]
    public async Task Approved_absence_pays_40_percent_at_once_and_the_refund_comes_only_from_the_refund_event()
    {
        var flow = new LedgerFlow();
        flow.AddOrder(1, Customer, 260_000m);
        flow.AddAssignment(10, 1, Worker, gross: 260_000m);
        await flow.Posting.PostOrderPaidAsync(Paid(1, 260_000m));
        var approved = new CustomerAbsentApproved(10, 1, Worker, 104_000m, 156_000m, Done);
        await flow.Posting.PostAbsenceFeeAsync(approved);
        await flow.Posting.PostAbsenceFeeAsync(approved);

        var fee = Assert.Single(flow.Of(LedgerEntryType.AbsenceFee));
        Assert.Equal((LedgerPartyType.Worker, 104_000m, "ABSENCE_FEE:ASSIGNMENT:10"), (fee.ToPartyType, fee.Amount, fee.IdempotencyKey));
        Assert.Null(fee.AvailableAt);
        Assert.Empty(flow.Of(LedgerEntryType.Refund));

        await flow.Posting.PostRefundAsync(Refunded(1, 156_000m, Done));
        Assert.Equal(156_000m, Assert.Single(flow.Of(LedgerEntryType.Refund)).Amount);
        Assert.Equal(0m, flow.Clearing(1));
    }

    [Fact]
    public async Task Approved_absence_of_an_agency_assignment_pays_the_agency()
    {
        var flow = new LedgerFlow();
        flow.AddOrder(1, Customer, 260_000m);
        flow.AddAssignment(10, 1, Worker, Agency, gross: 260_000m);
        await flow.Posting.PostAbsenceFeeAsync(new CustomerAbsentApproved(10, 1, Worker, 104_000m, 156_000m, Done));

        var fee = Assert.Single(flow.Of(LedgerEntryType.AbsenceFee));
        Assert.Equal((LedgerPartyType.Agency, Agency), (fee.ToPartyType, fee.ToPartyId!.Value));
    }

    [Fact]
    public async Task Refund_is_taken_from_clearing_written_once_per_event_and_two_refunds_write_two_entries()
    {
        var flow = new LedgerFlow();
        await flow.Posting.PostOrderPaidAsync(Paid(1, 260_000m));
        var first = Refunded(1, 100_000m, Done);
        await flow.Posting.PostRefundAsync(first);
        await flow.Posting.PostRefundAsync(first);
        Assert.Equal(160_000m, flow.Clearing(1));

        await flow.Posting.PostRefundAsync(Refunded(1, 160_000m, Done.AddMinutes(5)));
        var refunds = flow.Of(LedgerEntryType.Refund);
        Assert.Equal([100_000m, 160_000m], refunds.Select(r => r.Amount).ToArray());
        Assert.All(refunds, r => Assert.Equal((LedgerPartyType.PlatformClearing, LedgerPartyType.External, Customer), (r.FromPartyType, r.ToPartyType, r.ToPartyId!.Value)));
        Assert.Equal($"REFUND:ORDER:1:{Done.Ticks}", refunds[0].IdempotencyKey);
        Assert.Equal(0m, flow.Clearing(1));
        Assert.Equal(0m, flow.Balance(LedgerPartyType.PlatformRevenue));
    }

    [Fact]
    public async Task Refund_after_the_order_was_split_comes_from_revenue()
    {
        var flow = new LedgerFlow();
        flow.AddOrder(1, Customer, 260_000m);
        flow.AddAssignment(10, 1, Worker, gross: 260_000m);
        await flow.Posting.PostOrderPaidAsync(Paid(1, 260_000m));
        await flow.Posting.PostJobCompletedAsync(Completed(10, 1));
        await flow.Posting.PostRefundAsync(Refunded(1, 50_000m, Done.AddHours(2)));

        var refund = Assert.Single(flow.Of(LedgerEntryType.Refund));
        Assert.Equal((LedgerPartyType.PlatformRevenue, 50_000m), (refund.FromPartyType, refund.Amount));
        Assert.Equal($"REFUND:ORDER:1:{Done.AddHours(2).Ticks}", refund.IdempotencyKey);
        Assert.Equal(2_000m, flow.Balance(LedgerPartyType.PlatformRevenue));
    }

    [Fact]
    public async Task Refund_larger_than_clearing_takes_the_rest_from_revenue_atomically_and_replay_adds_nothing()
    {
        var flow = new LedgerFlow();
        flow.AddOrder(1, Customer, 260_000m, seats: 2);
        flow.AddAssignment(10, 1, Worker, seat: 1);
        await flow.Posting.PostOrderPaidAsync(Paid(1, 260_000m, 2));
        await flow.Posting.PostJobCompletedAsync(Completed(10, 1)); // seat 1 split: clearing keeps the other 130,000
        Assert.Equal(130_000m, flow.Clearing(1));

        var refunded = Refunded(1, 150_000m, Done.AddHours(2));
        await flow.Posting.PostRefundAsync(refunded);
        var refunds = flow.Of(LedgerEntryType.Refund);
        Assert.Equal(2, refunds.Count);
        Assert.Equal((LedgerPartyType.PlatformClearing, 130_000m, $"REFUND:ORDER:1:{refunded.RefundedAtUtc.Ticks}"),
            (refunds[0].FromPartyType, refunds[0].Amount, refunds[0].IdempotencyKey));
        Assert.Equal((LedgerPartyType.PlatformRevenue, 20_000m, $"REFUND:ORDER:1:{refunded.RefundedAtUtc.Ticks}:REVENUE"),
            (refunds[1].FromPartyType, refunds[1].Amount, refunds[1].IdempotencyKey));
        Assert.Equal(0m, flow.Clearing(1));

        // Even when the clearing balance is different on replay, the event is not written a second time.
        await flow.Posting.PostExtensionPaidAsync(new ExtensionPaid(5, 1, Worker, 1m, 40_000m, Done));
        var count = flow.Ledger.Rows.Count;
        await flow.Posting.PostRefundAsync(refunded);
        Assert.Equal(count, flow.Ledger.Rows.Count);
    }

    [Fact]
    public async Task Extension_refund_is_recorded_against_the_extension_money_in_clearing()
    {
        var flow = new LedgerFlow();
        flow.AddOrder(1, Customer, 260_000m);
        await flow.Posting.PostOrderPaidAsync(Paid(1, 260_000m));
        await flow.Posting.PostExtensionPaidAsync(new ExtensionPaid(5, 1, Worker, 2m, 100_000m, Done));
        await flow.Posting.PostRefundAsync(Refunded(1, 100_000m, Done.AddHours(1)));

        Assert.Equal(100_000m, Assert.Single(flow.Of(LedgerEntryType.Refund)).Amount);
        Assert.Equal(260_000m, flow.Clearing(1));
    }

    [Fact]
    public async Task Refund_of_a_late_payment_books_the_payment_first_so_revenue_is_untouched()
    {
        var flow = new LedgerFlow();
        flow.Source.Payments.Add(new LedgerPaymentSource(99, 1, null, 260_000m)); // paid after the order stopped waiting: no OrderPaid
        await flow.Posting.PostRefundAsync(Refunded(1, 260_000m, Done));

        Assert.Equal(260_000m, Assert.Single(flow.Of(LedgerEntryType.OrderPaid)).Amount);
        var refund = Assert.Single(flow.Of(LedgerEntryType.Refund));
        Assert.Equal(LedgerPartyType.PlatformClearing, refund.FromPartyType);
        Assert.Equal(0m, flow.Clearing(1));
        Assert.Equal(0m, flow.Balance(LedgerPartyType.PlatformRevenue));

        await flow.Posting.PostRefundAsync(Refunded(1, 260_000m, Done)); // replay
        Assert.Equal(2, flow.Ledger.Rows.Count);
    }

    [Fact]
    public async Task Payment_already_booked_is_not_booked_again_by_a_refund()
    {
        var flow = new LedgerFlow();
        flow.Source.Payments.Add(new LedgerPaymentSource(99, 1, null, 260_000m));
        await flow.Posting.PostOrderPaidAsync(Paid(1, 260_000m));
        await flow.Posting.PostRefundAsync(Refunded(1, 100_000m, Done));

        Assert.Single(flow.Of(LedgerEntryType.OrderPaid));
        Assert.Equal(160_000m, flow.Clearing(1));
    }

    private static LedgerFlow TwoFreelancerDispute(decimal compensation, FaultParty? fault, string category = "QUALITY", bool resolved = true)
    {
        var flow = new LedgerFlow();
        flow.AddOrder(1, Customer, 300_000m, seats: 2);
        flow.AddAssignment(10, 1, Worker, gross: 100_000m, seat: 1);
        flow.AddAssignment(11, 1, 8, gross: 200_000m, seat: 2);
        flow.Source.Disputes.Add(new LedgerDisputeSource(4, 1, resolved, fault, category, compensation));
        return flow;
    }

    private static Task PostPenalties(LedgerFlow flow, FaultParty? fault) =>
        Task.WhenAll(new long[] { 10, 11 }.Select(id => flow.Posting.PostPenaltyAsync(new DisputeResolved(4, id, fault, 0m, "note", Done))));

    [Fact]
    public async Task Freelancer_fault_writes_one_penalty_per_assignment_split_by_gross_and_replay_adds_nothing()
    {
        var flow = TwoFreelancerDispute(50_000m, FaultParty.Freelancer);
        await PostPenalties(flow, FaultParty.Freelancer);
        await PostPenalties(flow, FaultParty.Freelancer);

        var penalties = flow.Of(LedgerEntryType.Penalty).OrderBy(p => p.RefId).ThenBy(p => p.IdempotencyKey).ToArray();
        Assert.Equal(2, penalties.Length);
        var byWorker = penalties.ToDictionary(p => p.FromPartyId!.Value, p => p);
        Assert.Equal(16_667m, byWorker[Worker].Amount);
        Assert.Equal(33_333m, byWorker[8].Amount);
        Assert.Equal(50_000m, penalties.Sum(p => p.Amount));
        Assert.Equal("PENALTY:DISPUTE:4:ASSIGNMENT:10", byWorker[Worker].IdempotencyKey);
        Assert.All(penalties, p => Assert.Equal((LedgerPartyType.Worker, LedgerPartyType.PlatformRevenue, LedgerRefType.Dispute, 4L, 1L),
            (p.FromPartyType, p.ToPartyType, p.RefType, p.RefId, p.OrderId!.Value)));
        Assert.Equal(50_000m, flow.Balance(LedgerPartyType.PlatformRevenue));
    }

    [Fact]
    public async Task A_penalty_that_rounds_to_zero_for_an_assignment_is_not_written()
    {
        var flow = TwoFreelancerDispute(1m, FaultParty.Freelancer);
        await PostPenalties(flow, FaultParty.Freelancer);

        var penalty = Assert.Single(flow.Of(LedgerEntryType.Penalty));
        Assert.Equal(1m, penalty.Amount);
    }

    [Fact]
    public async Task Agency_fault_is_a_penalty_only_for_an_absence_fee_dispute_and_goes_to_the_agency()
    {
        var absent = new LedgerFlow();
        absent.AddOrder(1, Customer, 260_000m);
        absent.AddAssignment(10, 1, Worker, Agency, gross: 260_000m);
        absent.Source.Disputes.Add(new LedgerDisputeSource(4, 1, true, FaultParty.Agency, "ABSENT_FEE", 104_000m));
        await absent.Posting.PostPenaltyAsync(new DisputeResolved(4, 10, FaultParty.Agency, 104_000m, "n", Done));
        var penalty = Assert.Single(absent.Of(LedgerEntryType.Penalty));
        Assert.Equal((LedgerPartyType.Agency, Agency, 104_000m), (penalty.FromPartyType, penalty.FromPartyId!.Value, penalty.Amount));

        var quality = new LedgerFlow();
        quality.AddOrder(1, Customer, 260_000m);
        quality.AddAssignment(10, 1, Worker, Agency, gross: 260_000m);
        quality.Source.Disputes.Add(new LedgerDisputeSource(4, 1, true, FaultParty.Agency, "QUALITY", 104_000m));
        await quality.Posting.PostPenaltyAsync(new DisputeResolved(4, 10, FaultParty.Agency, 104_000m, "n", Done));
        Assert.Empty(quality.Ledger.Rows);
    }

    [Theory]
    [InlineData(null, true, 50_000)]
    [InlineData(FaultParty.Customer, true, 50_000)]
    [InlineData(FaultParty.Freelancer, false, 50_000)]
    [InlineData(FaultParty.Freelancer, true, 0)]
    public async Task No_penalty_without_a_resolved_fault_and_a_positive_compensation(FaultParty? fault, bool resolved, int compensation)
    {
        var flow = TwoFreelancerDispute(compensation, fault, resolved: resolved);
        await PostPenalties(flow, fault);
        Assert.Empty(flow.Ledger.Rows);
    }

    [Fact]
    public async Task Freelancer_fault_ignores_an_agency_assignment_of_the_same_order()
    {
        var flow = new LedgerFlow();
        flow.AddOrder(1, Customer, 300_000m, seats: 2);
        flow.AddAssignment(10, 1, Worker, gross: 150_000m, seat: 1);
        flow.AddAssignment(11, 1, 8, Agency, gross: 150_000m, seat: 2);
        flow.Source.Disputes.Add(new LedgerDisputeSource(4, 1, true, FaultParty.Freelancer, "QUALITY", 30_000m));
        await PostPenalties(flow, FaultParty.Freelancer);

        var penalty = Assert.Single(flow.Of(LedgerEntryType.Penalty));
        Assert.Equal((LedgerPartyType.Worker, Worker, 30_000m), (penalty.FromPartyType, penalty.FromPartyId!.Value, penalty.Amount));
    }

    [Fact]
    public async Task Full_flow_balances_to_the_dong_and_the_hash_chain_stays_valid()
    {
        var flow = new LedgerFlow();
        flow.AddOrder(1, Customer, 260_000m);
        flow.AddAssignment(10, 1, Worker, gross: 260_000m);
        flow.Source.Extensions.Add(new LedgerExtensionSource(5, 1, Worker, 100_000m, true));
        await flow.Posting.PostOrderPaidAsync(Paid(1, 260_000m));
        await flow.Posting.PostExtensionPaidAsync(new ExtensionPaid(5, 1, Worker, 2m, 100_000m, Done));
        await flow.Posting.PostJobCompletedAsync(Completed(10, 1));
        flow.Source.Disputes.Add(new LedgerDisputeSource(4, 1, true, FaultParty.Freelancer, "QUALITY", 50_000m));
        await flow.Posting.PostPenaltyAsync(new DisputeResolved(4, 10, FaultParty.Freelancer, 50_000m, "n", Done));

        Assert.Equal(0m, flow.Clearing(1));
        Assert.Equal(288_000m - 50_000m, flow.Balance(LedgerPartyType.Worker, Worker));
        Assert.Equal(72_000m + 50_000m, flow.Balance(LedgerPartyType.PlatformRevenue));
        Assert.Equal(-360_000m, flow.Balance(LedgerPartyType.External, Customer));
        var verified = await new LedgerChainVerifier(flow.Ledger).VerifyAsync();
        Assert.True(verified.Ok);
        Assert.Equal(flow.Ledger.Rows.Count, verified.CheckedCount);
    }

    // --- handlers: never throw, one per event, discovered by MediatR -------------------------------------------------

    private class ThrowingPosting : ILedgerPostingService
    {
        public Task PostOrderPaidAsync(OrderPaid paid, CancellationToken cancellationToken = default) => throw new InvalidOperationException("boom");
        public Task PostExtensionPaidAsync(ExtensionPaid paid, CancellationToken cancellationToken = default) => throw new InvalidOperationException("boom");
        public Task PostJobCompletedAsync(JobCompleted completed, CancellationToken cancellationToken = default) => throw new InvalidOperationException("boom");
        public Task PostAbsenceFeeAsync(CustomerAbsentApproved approved, CancellationToken cancellationToken = default) => throw new InvalidOperationException("boom");
        public Task PostRefundAsync(OrderRefunded refunded, CancellationToken cancellationToken = default) => throw new InvalidOperationException("boom");
        public Task PostPenaltyAsync(DisputeResolved resolved, CancellationToken cancellationToken = default) => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task Handlers_log_and_swallow_a_ledger_failure()
    {
        var posting = new ThrowingPosting();
        await new OrderPaidLedgerHandler(posting, NullLogger<OrderPaidLedgerHandler>.Instance).Handle(Paid(1, 1m), default);
        await new ExtensionPaidLedgerHandler(posting, NullLogger<ExtensionPaidLedgerHandler>.Instance)
            .Handle(new ExtensionPaid(5, 1, Worker, 1m, 1m, Done), default);
        await new JobCompletedLedgerHandler(posting, NullLogger<JobCompletedLedgerHandler>.Instance).Handle(Completed(10, 1), default);
        await new OrderRefundedLedgerHandler(posting, NullLogger<OrderRefundedLedgerHandler>.Instance).Handle(Refunded(1, 1m, Done), default);
        await new CustomerAbsentApprovedLedgerHandler(posting, NullLogger<CustomerAbsentApprovedLedgerHandler>.Instance)
            .Handle(new CustomerAbsentApproved(10, 1, Worker, 1m, 1m, Done), default);
        await new DisputeResolvedLedgerHandler(posting, NullLogger<DisputeResolvedLedgerHandler>.Instance)
            .Handle(new DisputeResolved(4, 10, FaultParty.Freelancer, 1m, "n", Done), default);
    }

    [Fact]
    public async Task Handler_with_a_missing_source_fact_does_not_throw_and_writes_nothing()
    {
        var flow = new LedgerFlow();
        await new JobCompletedLedgerHandler(flow.Posting, NullLogger<JobCompletedLedgerHandler>.Instance).Handle(Completed(10, 1), default);
        await new CustomerAbsentApprovedLedgerHandler(flow.Posting, NullLogger<CustomerAbsentApprovedLedgerHandler>.Instance)
            .Handle(new CustomerAbsentApproved(10, 1, Worker, 104_000m, 156_000m, Done), default);
        await new DisputeResolvedLedgerHandler(flow.Posting, NullLogger<DisputeResolvedLedgerHandler>.Instance)
            .Handle(new DisputeResolved(4, 10, FaultParty.Freelancer, 1m, "n", Done), default);
        Assert.Empty(flow.Ledger.Rows);
    }

    [Theory]
    [InlineData(typeof(OrderPaid), typeof(OrderPaidLedgerHandler))]
    [InlineData(typeof(ExtensionPaid), typeof(ExtensionPaidLedgerHandler))]
    [InlineData(typeof(JobCompleted), typeof(JobCompletedLedgerHandler))]
    [InlineData(typeof(OrderRefunded), typeof(OrderRefundedLedgerHandler))]
    [InlineData(typeof(CustomerAbsentApproved), typeof(CustomerAbsentApprovedLedgerHandler))]
    [InlineData(typeof(DisputeResolved), typeof(DisputeResolvedLedgerHandler))]
    public void Each_money_event_has_exactly_one_ledger_handler_that_mediatr_can_find(Type notification, Type handler)
    {
        var handlerInterface = typeof(INotificationHandler<>).MakeGenericType(notification);
        var found = typeof(LedgerPostingService).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && handlerInterface.IsAssignableFrom(t) && t.Namespace == typeof(LedgerPostingService).Namespace)
            .ToArray();
        Assert.Equal([handler], found);
    }
}
