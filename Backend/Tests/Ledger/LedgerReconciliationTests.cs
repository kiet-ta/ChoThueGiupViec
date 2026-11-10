using CommonService.Application.Common.Models;
using CommonService.Application.Features.Ledger;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Domain.Entities;
using CommonService.WebAPI.Controllers.Ledger;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.Tests.Ledger;

/// <summary>The reconciliation sweep and its report (ticket LEDGER-06): every source that has no ledger entry is written once.</summary>
public class LedgerReconciliationTests
{
    private const int Customer = 12;
    private const int Worker = 7;
    private const int Agency = 3;
    private static readonly DateTime Now = LedgerFlow.Now;
    private static readonly DateTime Recent = Now.AddHours(-3);
    private static readonly DateTime BeforeStart = Now.AddDays(-5);

    private static LedgerFlow FlowWithEveryKindOfSource()
    {
        var flow = new LedgerFlow();
        flow.AddOrder(1, Customer, 260_000m);
        flow.AddAssignment(10, 1, Worker, gross: 260_000m, completedAt: Recent);
        flow.AddOrder(2, Customer, 260_000m);
        var absent = new LedgerAssignmentSource(20, 2, 8, null, 1, 260_000m, 0.20m, null, false);
        flow.Source.Absences.Add(new LedgerAbsenceSource(absent, 104_000m));
        flow.Source.Assignments.Add(absent);
        flow.Source.Extensions.Add(new LedgerExtensionSource(5, 1, Worker, 100_000m, true));
        flow.Source.SweepPayments.Add(new LedgerSweepPayment(1, PaymentPurpose.Order, 1, null, null, 260_000m, Recent));
        flow.Source.SweepPayments.Add(new LedgerSweepPayment(2, PaymentPurpose.Extension, null, 5, null, 100_000m, Recent));
        flow.Source.SweepPayments.Add(new LedgerSweepPayment(3, PaymentPurpose.Subscription, null, null, Agency, 500_000m, Recent));
        flow.Source.Disputes.Add(new LedgerDisputeSource(4, 1, true, FaultParty.Freelancer, "QUALITY", 50_000m, Recent));
        flow.Source.EscrowTransactions.Add(new LedgerEscrowSource(1, Agency, EscrowTransactionType.Deposit, 2_000_000m, Recent));
        flow.Source.EscrowTransactions.Add(new LedgerEscrowSource(2, Agency, EscrowTransactionType.Penalty, 300_000m, Recent));
        flow.Source.EscrowTransactions.Add(new LedgerEscrowSource(3, Agency, EscrowTransactionType.Reversal, 100_000m, Recent));
        return flow;
    }

    [Fact]
    public async Task Sweep_writes_every_missing_source_once_and_the_report_is_then_empty()
    {
        var flow = FlowWithEveryKindOfSource();
        var service = flow.Reconciliation();

        var report = await service.ReportAsync();
        Assert.Equal(
            ["ORDER_PAID", "EXTENSION_PAID", "JOB_EARNING", "EXTENSION_EARNING", "COMMISSION", "ABSENCE_FEE", "PENALTY", "SUBSCRIPTION",
                "ESCROW_DEPOSIT", "ESCROW_PENALTY", "ESCROW_REVERSAL"],
            report.Items.Select(i => i.EntryType).ToArray());
        Assert.Equal((1, 260_000m), (report.Items.Single(i => i.EntryType == "ORDER_PAID").MissingCount, report.Items.Single(i => i.EntryType == "ORDER_PAID").MissingAmount));
        var commission = report.Items.Single(i => i.EntryType == "COMMISSION");
        Assert.Equal((2, 72_000m), (commission.MissingCount, commission.MissingAmount));
        Assert.Empty(flow.Ledger.Rows); // the report writes nothing

        var result = await service.SweepAsync();
        Assert.Equal(result.Missing, result.Written);
        Assert.Equal(12, flow.Ledger.Rows.Count);

        Assert.Equal(260_000m, Assert.Single(flow.Of(LedgerEntryType.OrderPaid)).Amount);
        Assert.Equal(100_000m, Assert.Single(flow.Of(LedgerEntryType.ExtensionPaid)).Amount);
        Assert.Equal(208_000m, Assert.Single(flow.Of(LedgerEntryType.JobEarning)).Amount);
        Assert.Equal(80_000m, Assert.Single(flow.Of(LedgerEntryType.ExtensionEarning)).Amount);
        Assert.Equal(104_000m, Assert.Single(flow.Of(LedgerEntryType.AbsenceFee)).Amount);
        Assert.Equal(50_000m, Assert.Single(flow.Of(LedgerEntryType.Penalty)).Amount);
        Assert.Equal(500_000m, Assert.Single(flow.Of(LedgerEntryType.Subscription)).Amount);

        Assert.Empty((await service.ReportAsync()).Items);
        var again = await service.SweepAsync();
        Assert.Equal((0, 0), (again.Missing, again.Written));
        Assert.Equal(12, flow.Ledger.Rows.Count);
        Assert.True((await new LedgerChainVerifier(flow.Ledger).VerifyAsync()).Ok);
    }

    [Fact]
    public async Task A_deleted_entry_is_reported_and_written_again_with_the_same_key()
    {
        var flow = FlowWithEveryKindOfSource();
        var service = flow.Reconciliation();
        await service.SweepAsync();
        var lost = flow.Ledger.Rows.Single(e => e.IdempotencyKey == "COMMISSION:ASSIGNMENT:10");
        flow.Ledger.Rows.Remove(lost);

        var item = Assert.Single((await service.ReportAsync()).Items);
        Assert.Equal(("COMMISSION", 1, 52_000m), (item.EntryType, item.MissingCount, item.MissingAmount));

        await service.SweepAsync();
        var back = Assert.Single(flow.Ledger.Rows, e => e.IdempotencyKey == "COMMISSION:ASSIGNMENT:10");
        Assert.Equal((LedgerPartyType.PlatformRevenue, 52_000m), (back.ToPartyType, back.Amount));
        Assert.Empty((await service.ReportAsync()).Items);
    }

    [Fact]
    public async Task Sources_before_the_ledger_start_are_ignored()
    {
        var flow = new LedgerFlow();
        flow.AddOrder(1, Customer, 260_000m);
        flow.AddAssignment(10, 1, Worker, gross: 260_000m, completedAt: BeforeStart);
        flow.Source.SweepPayments.Add(new LedgerSweepPayment(1, PaymentPurpose.Order, 1, null, null, 260_000m, BeforeStart));
        flow.Source.SweepPayments.Add(new LedgerSweepPayment(3, PaymentPurpose.Subscription, null, null, Agency, 500_000m, BeforeStart));
        flow.Source.EscrowTransactions.Add(new LedgerEscrowSource(1, Agency, EscrowTransactionType.Deposit, 2_000_000m, BeforeStart));
        flow.Source.Disputes.Add(new LedgerDisputeSource(4, 1, true, FaultParty.Freelancer, "QUALITY", 50_000m, BeforeStart));
        flow.Source.RefundedOrders.Add(new LedgerRefundedOrder(1, Customer, 100_000m, BeforeStart));
        var service = flow.Reconciliation();

        Assert.Empty((await service.ReportAsync()).Items);
        Assert.Equal(0, (await service.SweepAsync()).Written);
        Assert.Empty(flow.Ledger.Rows);
    }

    [Fact]
    public async Task Without_a_configured_start_the_first_ledger_entry_is_the_start_and_an_empty_ledger_is_left_alone()
    {
        var flow = new LedgerFlow();
        flow.Source.SweepPayments.Add(new LedgerSweepPayment(3, PaymentPurpose.Subscription, null, null, Agency, 500_000m, Recent));
        var service = flow.Reconciliation(new LedgerOptions());

        Assert.Equal(0, (await service.SweepAsync()).Missing); // nothing is written yet: there is no start
        Assert.Empty(flow.Ledger.Rows);

        flow.LedgerQuery.Entries.Add(new LedgerEntry { CreatedAt = Recent.AddHours(-1) });
        Assert.Equal(1, (await service.SweepAsync()).Written);
        flow.LedgerQuery.Entries[0].CreatedAt = Now; // the start moved after the payment: it is not reconciled any more
        flow.Ledger.Rows.Clear();
        Assert.Equal(0, (await service.SweepAsync()).Written);
    }

    [Fact]
    public async Task Escrow_movements_mirror_the_escrow_account_and_the_penalty_goes_to_the_revenue()
    {
        var flow = FlowWithEveryKindOfSource();
        await flow.Reconciliation().SweepAsync();

        var deposit = Assert.Single(flow.Of(LedgerEntryType.EscrowDeposit));
        Assert.Equal((LedgerPartyType.External, Agency, LedgerPartyType.AgencyEscrow, Agency, 2_000_000m, "ESCROW:1", LedgerRefType.EscrowTxn),
            (deposit.FromPartyType, deposit.FromPartyId!.Value, deposit.ToPartyType, deposit.ToPartyId!.Value, deposit.Amount, deposit.IdempotencyKey, deposit.RefType));
        var penalty = Assert.Single(flow.Of(LedgerEntryType.EscrowPenalty));
        Assert.Equal((LedgerPartyType.AgencyEscrow, LedgerPartyType.PlatformRevenue, 300_000m), (penalty.FromPartyType, penalty.ToPartyType, penalty.Amount));
        var reversal = Assert.Single(flow.Of(LedgerEntryType.EscrowReversal));
        Assert.Equal((LedgerPartyType.PlatformRevenue, LedgerPartyType.AgencyEscrow, 100_000m), (reversal.FromPartyType, reversal.ToPartyType, reversal.Amount));
        Assert.Equal(2_000_000m - 300_000m + 100_000m, flow.Balance(LedgerPartyType.AgencyEscrow, Agency));
    }

    [Fact]
    public async Task Subscription_payment_is_platform_revenue_from_the_agency()
    {
        var flow = FlowWithEveryKindOfSource();
        await flow.Reconciliation().SweepAsync();

        var row = Assert.Single(flow.Of(LedgerEntryType.Subscription));
        Assert.Equal((LedgerPartyType.External, Agency, LedgerPartyType.PlatformRevenue, "SUBSCRIPTION:PAYMENT:3", LedgerRefType.Payment, 3L),
            (row.FromPartyType, row.FromPartyId!.Value, row.ToPartyType, row.IdempotencyKey, row.RefType, row.RefId));
    }

    [Fact]
    public async Task One_unreadable_source_is_skipped_and_the_others_are_still_written()
    {
        var flow = new LedgerFlow();
        flow.Source.SweepPayments.Add(new LedgerSweepPayment(1, PaymentPurpose.Order, 404, null, null, 260_000m, Recent)); // its order is gone
        flow.Source.SweepPayments.Add(new LedgerSweepPayment(3, PaymentPurpose.Subscription, null, null, Agency, 500_000m, Recent));
        var result = await flow.Reconciliation().SweepAsync();

        Assert.Equal(1, result.Written);
        Assert.Single(flow.Of(LedgerEntryType.Subscription));
    }

    // --- refunds ------------------------------------------------------------------------------------------------------

    private static LedgerFlow PaidOrder()
    {
        var flow = new LedgerFlow();
        flow.AddOrder(1, Customer, 260_000m);
        flow.Source.Payments.Add(new LedgerPaymentSource(1, 1, null, 260_000m, 0m));
        flow.Posting.PostOrderPaidAsync(new OrderPaid(1, Customer, 260_000m, "M", Now.Date, 1)).Wait();
        return flow;
    }

    private static void Refunded(LedgerFlow flow, decimal total, DateTime latest)
    {
        flow.Source.Payments.Clear();
        flow.Source.Payments.Add(new LedgerPaymentSource(1, 1, null, 260_000m, total));
        flow.Source.RefundedOrders.Clear();
        if (total > 0) flow.Source.RefundedOrders.Add(new LedgerRefundedOrder(1, Customer, total, latest));
    }

    [Fact]
    public async Task A_lost_refund_event_is_recovered_after_ten_quiet_minutes_from_the_cumulative_total()
    {
        var flow = PaidOrder();
        Refunded(flow, 100_000m, Now.AddMinutes(-11));
        var service = flow.Reconciliation();

        var item = Assert.Single((await service.ReportAsync()).Items);
        Assert.Equal(("REFUND", 1, 100_000m, 0m), (item.EntryType, item.MissingCount, item.MissingAmount, item.ExcessAmount));

        var result = await service.SweepAsync();
        Assert.Equal(1, result.RefundOrders);
        var refund = Assert.Single(flow.Of(LedgerEntryType.Refund));
        Assert.Equal((LedgerPartyType.PlatformClearing, LedgerPartyType.External, Customer, 100_000m, "REFUND:ORDER:1:TO:100000"),
            (refund.FromPartyType, refund.ToPartyType, refund.ToPartyId!.Value, refund.Amount, refund.IdempotencyKey));
        Assert.Equal(160_000m, flow.Clearing(1));

        Assert.Empty((await service.ReportAsync()).Items);
        Assert.Equal(0, (await service.SweepAsync()).RefundOrders);
        Assert.Single(flow.Of(LedgerEntryType.Refund));
    }

    [Fact]
    public async Task A_refund_reserved_minutes_ago_is_not_reconciled_so_a_gateway_refusal_leaves_no_trace()
    {
        var flow = PaidOrder();
        Refunded(flow, 100_000m, Now.AddMinutes(-2)); // reserved, the gateway has not answered yet
        var service = flow.Reconciliation();

        Assert.Empty((await service.ReportAsync()).Items);
        Assert.Equal(0, (await service.SweepAsync()).RefundOrders);
        Assert.Empty(flow.Of(LedgerEntryType.Refund));

        Refunded(flow, 0m, Now); // the gateway refused: the reservation was reverted
        flow.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Empty((await service.ReportAsync()).Items);
        Assert.Equal(0, (await service.SweepAsync()).RefundOrders);
        Assert.Empty(flow.Of(LedgerEntryType.Refund));
        Assert.Equal(260_000m, flow.Clearing(1));
    }

    [Fact]
    public async Task A_delayed_refund_event_after_the_sweep_recovered_it_is_not_booked_twice()
    {
        var flow = PaidOrder();
        var at = Now.AddMinutes(-11);
        Refunded(flow, 100_000m, at);
        await flow.Reconciliation().SweepAsync();
        Assert.Single(flow.Of(LedgerEntryType.Refund));

        await flow.Posting.PostRefundAsync(new OrderRefunded(1, Customer, 100_000m, "late event", at)); // the event finally arrives
        await flow.Posting.PostRefundAsync(new OrderRefunded(1, Customer, 100_000m, "late event", at));

        Assert.Equal(100_000m, Assert.Single(flow.Of(LedgerEntryType.Refund)).Amount);
        Assert.Equal(160_000m, flow.Clearing(1));
    }

    [Fact]
    public async Task The_sweep_books_only_the_difference_after_a_second_refund_and_a_lost_event()
    {
        var flow = PaidOrder();
        Refunded(flow, 100_000m, Now.AddMinutes(-30));
        await flow.Posting.PostRefundAsync(new OrderRefunded(1, Customer, 100_000m, "x", Now.AddMinutes(-30))); // event, as it should
        Refunded(flow, 260_000m, Now.AddMinutes(-11)); // second refund of 160,000: its event was lost
        var service = flow.Reconciliation();

        await service.SweepAsync();
        Assert.Equal([100_000m, 160_000m], flow.Of(LedgerEntryType.Refund).Select(r => r.Amount).ToArray());
        Assert.Equal("REFUND:ORDER:1:TO:260000", flow.Of(LedgerEntryType.Refund)[1].IdempotencyKey);
        Assert.Equal(0m, flow.Clearing(1));
        Assert.Empty((await service.ReportAsync()).Items);
    }

    [Fact]
    public async Task A_ledger_holding_more_refund_than_the_source_is_only_reported()
    {
        var flow = PaidOrder();
        Refunded(flow, 150_000m, Now.AddMinutes(-30));
        await flow.Posting.PostRefundAsync(new OrderRefunded(1, Customer, 150_000m, "x", Now.AddMinutes(-30)));
        Refunded(flow, 100_000m, Now.AddMinutes(-11)); // the source now shows less than the ledger holds
        var service = flow.Reconciliation();

        var item = Assert.Single((await service.ReportAsync()).Items);
        Assert.Equal(("REFUND", 0, 0m, 50_000m), (item.EntryType, item.MissingCount, item.MissingAmount, item.ExcessAmount));
        var count = flow.Ledger.Rows.Count;
        await service.SweepAsync();
        Assert.Equal(count, flow.Ledger.Rows.Count);
    }

    // --- endpoint -------------------------------------------------------------------------------------------------------

    private class StubReconciliation(LedgerReconciliationReport report) : ILedgerReconciliationService
    {
        public Task<LedgerReconciliationReport> ReportAsync(CancellationToken cancellationToken = default) => Task.FromResult(report);
        public Task<LedgerSweepResult> SweepAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Reconciliation_endpoint_returns_the_report_in_the_envelope()
    {
        var report = new LedgerReconciliationReport([new LedgerReconciliationItem("COMMISSION", 2, 72_000m, 0m)]);
        var result = await new AdminLedgerController(new StubReconciliation(report), null!, null!).Reconciliation(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ApiResponse<LedgerReconciliationReport>>(ok.Value);
        Assert.Equal("COMMISSION", Assert.Single(body.Data!.Items).EntryType);
    }

    [Fact]
    public void Reconciliation_endpoint_is_admin_only()
    {
        var authorize = typeof(AdminLedgerController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Single();
        Assert.Equal("AdminOnly", authorize.Policy);
        Assert.Equal("api/v1/admin/ledger", ((RouteAttribute)typeof(AdminLedgerController).GetCustomAttributes(typeof(RouteAttribute), true).Single()).Template);
    }
}
