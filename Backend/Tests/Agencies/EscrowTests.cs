using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Tests.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace CommonService.Tests.Agencies;

/// <summary>BE-M5-07: escrow top-up, SLA penalty, append-only ledger, never negative, 48 h appeal and REVERSAL (decisions Q09, Q25 R4, SC-4).</summary>
public class EscrowTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc);

    // ---- the pure rules

    [Theory]
    [InlineData(10_000_000, 1_000_000, 1_000_000, 0, 9_000_000, "ACTIVE")]
    [InlineData(500_000, 1_000_000, 500_000, 500_000, 0, "SUSPENDED")] // short: deduct what exists, record the rest, suspend
    [InlineData(1_000_000, 1_000_000, 1_000_000, 0, 0, "SUSPENDED")] // reaches exactly zero
    [InlineData(0, 300_000, 0, 300_000, 0, "SUSPENDED")]
    public void Penalty_never_takes_the_balance_below_zero_and_suspends_at_zero(
        long balance, long asked, long deducted, long shortfall, long newBalance, string newStatus)
    {
        var result = EscrowRules.Penalize(balance, asked, "ACTIVE");

        Assert.Equal(deducted, result.Deducted);
        Assert.Equal(shortfall, result.Shortfall);
        Assert.Equal(newBalance, result.NewBalance);
        Assert.Equal(newStatus, result.NewStatus);
    }

    [Fact]
    public void A_penalty_of_nothing_changes_nothing_and_a_pending_agency_stays_pending()
    {
        Assert.Equal("ACTIVE", EscrowRules.Penalize(0m, 0m, "ACTIVE").NewStatus);
        Assert.Equal("PENDING", EscrowRules.Penalize(1_000m, 5_000m, "PENDING").NewStatus);
        Assert.Equal("SUSPENDED", EscrowRules.Penalize(1_000m, 5_000m, "SUSPENDED").NewStatus);
    }

    [Fact]
    public void Points_follow_the_q09_table()
    {
        var rules = new SlaPenaltyRules();

        Assert.Equal(20m, EscrowRules.Points(SlaViolation.NoShow, rules));
        Assert.Equal(10m, EscrowRules.Points(SlaViolation.Shortage, rules));
        Assert.Equal(5m, EscrowRules.Points(SlaViolation.QualityComplaint, rules));
    }

    [Theory]
    [InlineData("PENDING", 4_999_999, "PENDING")]
    [InlineData("PENDING", 5_000_000, "ACTIVE")]
    [InlineData("SUSPENDED", 4_000_000, "SUSPENDED")]
    [InlineData("SUSPENDED", 5_000_000, "ACTIVE")]
    [InlineData("ACTIVE", 100, "ACTIVE")]
    public void A_credit_activates_pending_or_suspended_agencies_at_the_minimum(string status, long balance, string expected) =>
        Assert.Equal(expected, EscrowRules.StatusAfterCredit(status, balance, 5_000_000m));

    // ---- the service

    internal sealed class InMemoryEscrow : IEscrowRepository
    {
        public AgencyEscrowRow Agency { get; set; } = new(7, 10_000_000m, 100m, "ACTIVE");
        public List<EscrowTransaction> Ledger { get; } = [];
        public List<EscrowAppeal> Appeals { get; } = [];
        private long _nextTxn = 1;
        private int _nextAppeal = 1;

        /// <summary>When set, the first optimistic update loses a race (a concurrent writer changed the balance).</summary>
        public int RacesToLose { get; set; }

        public Task<AgencyEscrowRow?> GetAgencyAsync(int agencyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(agencyId == Agency.AgencyId ? Agency : null);

        public Task<bool> TryUpdateAgencyAsync(
            int agencyId, decimal expectedBalance, decimal newBalance, decimal newSlaScore, string newStatus, CancellationToken cancellationToken = default)
        {
            if (RacesToLose > 0)
            {
                RacesToLose--;
                Agency = Agency with { Balance = Agency.Balance + 1m }; // somebody else moved the balance
                return Task.FromResult(false);
            }

            if (Agency.Balance != expectedBalance)
            {
                return Task.FromResult(false);
            }

            Agency = Agency with { Balance = newBalance, SlaScore = newSlaScore, Status = newStatus };
            return Task.FromResult(true);
        }

        public void AddTransaction(EscrowTransaction row)
        {
            row.EscrowTxnId = _nextTxn++;
            Ledger.Add(row);
        }

        public Task<EscrowTransaction?> GetTransactionAsync(long escrowTxnId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Ledger.FirstOrDefault(t => t.EscrowTxnId == escrowTxnId));

        public Task<EscrowTransaction?> FindPenaltyAsync(int agencyId, long? orderId, int? disputeId, string reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(Ledger.FirstOrDefault(t => t.AgencyId == agencyId && t.TxnType == EscrowTransactionType.Penalty
                && t.OrderId == orderId && t.DisputeId == disputeId && t.Reason.StartsWith(reason)));

        public Task<bool> HasReversalAsync(long penaltyTxnId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Appeals.Any(a => a.EscrowTxnId == penaltyTxnId && a.Status == AppealStatuses.Accepted));

        public Task<(IReadOnlyList<EscrowTransaction> Items, int Total)> ListAsync(int agencyId, int skip, int take, CancellationToken cancellationToken = default)
        {
            var all = Ledger.Where(t => t.AgencyId == agencyId).OrderByDescending(t => t.EscrowTxnId).ToList();
            return Task.FromResult<(IReadOnlyList<EscrowTransaction>, int)>((all.Skip(skip).Take(take).ToList(), all.Count));
        }

        public Task<EscrowAppeal?> FindAppealByTransactionAsync(long escrowTxnId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Appeals.FirstOrDefault(a => a.EscrowTxnId == escrowTxnId));

        public void AddAppeal(EscrowAppeal appeal)
        {
            appeal.AppealId = _nextAppeal++;
            Appeals.Add(appeal);
        }

        public Task<EscrowAppeal?> GetAppealForUpdateAsync(int appealId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Appeals.FirstOrDefault(a => a.AppealId == appealId));

        public Task<IReadOnlyList<EscrowAppeal>> ListAppealsAsync(string? status, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EscrowAppeal>>(Appeals.Where(a => status == null || a.Status == status).Take(take).ToList());

        public Task<PaymentTransaction?> FindPendingDepositAsync(long paymentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PaymentTransaction?>(null);
    }

    internal sealed class DepositPayments : PaymentRepositoryStub
    {
        public List<PaymentTransaction> Added { get; } = [];
        private long _next = 700;

        public override void Add(PaymentTransaction transaction)
        {
            transaction.PaymentId = _next++;
            Added.Add(transaction);
        }

        public override Task<bool> TryMarkSuccessAsync(long paymentId, DateTime paidAtUtc, string ipnPayload, CancellationToken cancellationToken = default) =>
            Mark(paymentId, PaymentStatus.Pending);

        public override Task<bool> TryMarkSuccessFromExpiredAsync(long paymentId, DateTime paidAtUtc, string ipnPayload, CancellationToken cancellationToken = default) =>
            Mark(paymentId, PaymentStatus.Expired);

        private Task<bool> Mark(long paymentId, PaymentStatus from)
        {
            var t = Added.First(p => p.PaymentId == paymentId);
            if (t.TxnStatus != from)
            {
                return Task.FromResult(false);
            }

            t.TxnStatus = PaymentStatus.Success;
            return Task.FromResult(true);
        }
    }

    private sealed class Harness
    {
        public InMemoryEscrow Repo { get; } = new();
        public DepositPayments Payments { get; } = new();
        public FakePaymentGateway Gateway { get; } = new();
        public FakeAuditLog Audit { get; } = new();
        public FakeCurrentUser User { get; } = new();
        public FakeClock Clock { get; } = new();
        public EscrowService Service { get; }
        public EscrowDepositSettlement Settlement { get; }

        public Harness()
        {
            Clock.Set(Now);
            var options = MicrosoftOptions.Create(new BusinessRules());
            Service = new EscrowService(Repo, Payments, Gateway, Payments, Audit, User, Clock, options);
            Settlement = new EscrowDepositSettlement(Payments, Service, Payments, Clock, NullLogger<EscrowDepositSettlement>.Instance);
            User.SignIn(7, UserRole.Partner);
        }

        public static SlaPenaltyRequest Penalty(SlaViolation v = SlaViolation.NoShow, decimal refund = 260_000m, decimal rescue = 0m, long? orderId = 501, int? disputeId = null) =>
            new(7, v, orderId, disputeId, refund, rescue, "worker did not come");
    }

    [Fact]
    public async Task Penalty_deducts_refund_plus_rescue_cost_and_points_and_appends_one_row()
    {
        var h = new Harness();

        var result = await h.Service.ApplyAsync(Harness.Penalty(refund: 260_000m, rescue: 100_000m));

        Assert.Equal(-20m, result.SlaPointsDelta);
        Assert.Equal(80m, result.NewSlaScore);
        Assert.Equal(360_000m, result.EscrowDeducted);
        Assert.Equal(0m, result.Shortfall);
        Assert.False(result.AgencySuspended);
        var row = Assert.Single(h.Repo.Ledger);
        Assert.Equal(EscrowTransactionType.Penalty, row.TxnType);
        Assert.Equal(360_000m, row.Amount);
        Assert.Equal(10_000_000m - 360_000m, row.BalanceAfter);
        Assert.Equal(-20m, row.SlaPointsDelta);
        Assert.Equal(501, row.OrderId);
        Assert.StartsWith("NOSHOW:", row.Reason);
    }

    [Fact]
    public async Task Penalty_beyond_the_balance_records_the_shortfall_and_suspends()
    {
        var h = new Harness();
        h.Repo.Agency = h.Repo.Agency with { Balance = 100_000m };

        var result = await h.Service.ApplyAsync(Harness.Penalty(refund: 260_000m));

        Assert.Equal(100_000m, result.EscrowDeducted);
        Assert.Equal(160_000m, result.Shortfall);
        Assert.True(result.AgencySuspended);
        Assert.Equal(0m, h.Repo.Agency.Balance);
        Assert.Equal("SUSPENDED", h.Repo.Agency.Status);
        Assert.Contains("shortfall", h.Repo.Ledger.Single().Reason);
    }

    [Fact]
    public async Task The_same_violation_of_the_same_order_is_applied_once()
    {
        var h = new Harness();

        var first = await h.Service.ApplyAsync(Harness.Penalty());
        var again = await h.Service.ApplyAsync(Harness.Penalty());

        Assert.Single(h.Repo.Ledger);
        Assert.Equal(first.EscrowDeducted, again.EscrowDeducted);
        Assert.Equal(80m, h.Repo.Agency.SlaScore);
        // A different violation of the same order is a different penalty.
        await h.Service.ApplyAsync(Harness.Penalty(SlaViolation.Shortage));
        Assert.Equal(2, h.Repo.Ledger.Count);
        Assert.Equal(70m, h.Repo.Agency.SlaScore);
    }

    [Fact]
    public async Task The_score_never_goes_below_zero_and_a_lost_race_re_reads_the_balance()
    {
        var h = new Harness();
        h.Repo.Agency = h.Repo.Agency with { SlaScore = 5m };
        h.Repo.RacesToLose = 1;

        var result = await h.Service.ApplyAsync(Harness.Penalty(SlaViolation.NoShow, refund: 1_000m));

        Assert.Equal(0m, result.NewSlaScore);
        Assert.Equal(-5m, result.SlaPointsDelta);
        Assert.Equal(10_000_000m + 1m - 1_000m, h.Repo.Agency.Balance); // the concurrent +1 was kept
        Assert.Single(h.Repo.Ledger);
    }

    [Fact]
    public async Task Deposit_settlement_credits_the_balance_once_and_activates_a_pending_agency()
    {
        var h = new Harness();
        h.Repo.Agency = new AgencyEscrowRow(7, 0m, 100m, "PENDING");
        var start = await h.Service.StartDepositAsync(new DepositRequest(5_000_000m));
        var payment = h.Payments.Added.Single();

        var first = await h.Settlement.SettleSuccessAsync(payment, wasExpired: false, "{}");
        var repeat = await h.Settlement.SettleSuccessAsync(payment, wasExpired: false, "{}");

        Assert.Equal(5_000_000m, start.Amount);
        Assert.Equal(PaymentPurpose.EscrowDeposit, payment.Purpose);
        Assert.Equal(7, payment.AgencyId);
        Assert.True(first);
        Assert.False(repeat);
        Assert.Equal(5_000_000m, h.Repo.Agency.Balance);
        Assert.Equal("ACTIVE", h.Repo.Agency.Status);
        var row = Assert.Single(h.Repo.Ledger);
        Assert.Equal(EscrowTransactionType.Deposit, row.TxnType);
        Assert.Equal(payment.PaymentId, row.PaymentId);
        Assert.Equal(5_000_000m, row.BalanceAfter);
    }

    [Fact]
    public async Task A_top_up_that_stays_below_the_minimum_leaves_a_suspended_agency_suspended()
    {
        var h = new Harness();
        h.Repo.Agency = new AgencyEscrowRow(7, 0m, 100m, "SUSPENDED");
        await h.Service.StartDepositAsync(new DepositRequest(1_000_000m));

        await h.Settlement.SettleSuccessAsync(h.Payments.Added.Single(), false, "{}");

        Assert.Equal("SUSPENDED", h.Repo.Agency.Status);
        Assert.Equal(1_000_000m, h.Repo.Agency.Balance);
    }

    [Fact]
    public async Task Deposit_needs_a_positive_whole_vnd_amount()
    {
        var h = new Harness();

        foreach (var bad in new decimal?[] { null, 0m, -5m, 1000.5m })
        {
            await Assert.ThrowsAsync<ValidationException>(() => h.Service.StartDepositAsync(new DepositRequest(bad)));
        }

        Assert.Empty(h.Payments.Added);
    }

    [Fact]
    public async Task State_reports_the_escrow_warning_the_sla_warning_and_premium_exclusion()
    {
        var h = new Harness();
        h.Repo.Agency = new AgencyEscrowRow(7, 3_000_000m, 70m, "ACTIVE");

        var state = await h.Service.GetStateAsync();

        Assert.True(state.HasEscrowWarning);
        Assert.True(state.HasSlaWarning);
        Assert.False(state.ExcludedFromPremium);
        h.Repo.Agency = h.Repo.Agency with { SlaScore = 49m };
        Assert.True((await h.Service.GetStateAsync()).ExcludedFromPremium);
    }

    // ---- appeals and reversal

    private static async Task<long> PenaltyRowAsync(Harness h)
    {
        await h.Service.ApplyAsync(Harness.Penalty(refund: 260_000m));
        return h.Repo.Ledger.Single().EscrowTxnId;
    }

    [Fact]
    public async Task An_appeal_inside_48_hours_is_recorded_once_and_outside_is_refused()
    {
        var h = new Harness();
        var txn = await PenaltyRowAsync(h);

        var appeal = await h.Service.AppealAsync(txn, new AppealRequest("It was the customer"));
        var again = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Service.AppealAsync(txn, new AppealRequest("again")));

        Assert.Equal("PENDING", appeal.Status);
        Assert.Equal(EscrowErrorCodes.AppealExists, again.Code);

        var h2 = new Harness();
        var txn2 = await PenaltyRowAsync(h2);
        h2.Clock.Advance(TimeSpan.FromHours(49));
        var late = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h2.Service.AppealAsync(txn2, new AppealRequest("late")));
        Assert.Equal(EscrowErrorCodes.AppealWindowClosed, late.Code);
    }

    [Fact]
    public async Task Only_a_penalty_of_the_own_agency_can_be_appealed()
    {
        var h = new Harness();
        await h.Service.StartDepositAsync(new DepositRequest(1_000_000m));
        await h.Settlement.SettleSuccessAsync(h.Payments.Added.Single(), false, "{}");
        var deposit = h.Repo.Ledger.Single().EscrowTxnId;

        var notPenalty = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Service.AppealAsync(deposit, new AppealRequest("x")));
        Assert.Equal(EscrowErrorCodes.NotAPenalty, notPenalty.Code);
        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.AppealAsync(9999, new AppealRequest("x")));
        await Assert.ThrowsAsync<ValidationException>(() => h.Service.AppealAsync(deposit, new AppealRequest(" ")));
    }

    [Fact]
    public async Task Accepting_an_appeal_appends_a_reversal_that_restores_money_points_and_the_status()
    {
        var h = new Harness();
        h.Repo.Agency = h.Repo.Agency with { Balance = 100_000m };
        var txn = await PenaltyRowAsync(h); // deducts 100 000, suspends, -20 points
        Assert.Equal("SUSPENDED", h.Repo.Agency.Status);
        var appeal = await h.Service.AppealAsync(txn, new AppealRequest("customer was wrong"));
        h.User.SignIn(1, UserRole.Admin);

        var decided = await h.Service.DecideAppealAsync(appeal.AppealId, new AppealDecisionRequest(true, "evidence ok"));

        Assert.Equal("ACCEPTED", decided.Status);
        Assert.Equal(2, h.Repo.Ledger.Count); // append only: the penalty row is untouched
        var reversal = h.Repo.Ledger[1];
        Assert.Equal(EscrowTransactionType.Reversal, reversal.TxnType);
        Assert.Equal(100_000m, reversal.Amount);
        Assert.Equal(20m, reversal.SlaPointsDelta);
        Assert.Equal(1, reversal.CreatedByAdminId);
        Assert.Equal(100_000m, h.Repo.Agency.Balance);
        Assert.Equal(100m, h.Repo.Agency.SlaScore);
        Assert.Single(h.Audit.Entries);

        var twice = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => h.Service.DecideAppealAsync(appeal.AppealId, new AppealDecisionRequest(true, null)));
        Assert.Equal(EscrowErrorCodes.AppealDecided, twice.Code);
    }

    [Fact]
    public async Task Rejecting_an_appeal_changes_no_money()
    {
        var h = new Harness();
        var txn = await PenaltyRowAsync(h);
        var appeal = await h.Service.AppealAsync(txn, new AppealRequest("no"));
        var balance = h.Repo.Agency.Balance;
        h.User.SignIn(1, UserRole.Admin);

        var decided = await h.Service.DecideAppealAsync(appeal.AppealId, new AppealDecisionRequest(false, "not convincing"));

        Assert.Equal("REJECTED", decided.Status);
        Assert.Single(h.Repo.Ledger);
        Assert.Equal(balance, h.Repo.Agency.Balance);
        Assert.Equal("not convincing", decided.DecisionNote);
    }

    [Fact]
    public async Task Only_an_admin_decides_and_only_a_partner_reads_the_ledger()
    {
        var h = new Harness();
        var txn = await PenaltyRowAsync(h);
        var appeal = await h.Service.AppealAsync(txn, new AppealRequest("x"));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => h.Service.DecideAppealAsync(appeal.AppealId, new AppealDecisionRequest(true, null)));
        h.User.SignIn(1, UserRole.Admin);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => h.Service.GetStateAsync());
        await Assert.ThrowsAsync<ValidationException>(() => h.Service.DecideAppealAsync(appeal.AppealId, new AppealDecisionRequest(null, null)));
        Assert.Single(await h.Service.ListAppealsAsync(null));
    }

    [Fact]
    public async Task The_ledger_lists_newest_first_with_the_appeal_flag()
    {
        var h = new Harness();
        await PenaltyRowAsync(h);
        await h.Service.StartDepositAsync(new DepositRequest(1_000_000m));
        await h.Settlement.SettleSuccessAsync(h.Payments.Added.Single(), false, "{}");

        var (items, total) = await h.Service.ListTransactionsAsync(1, 20);

        Assert.Equal(2, total);
        Assert.Equal("DEPOSIT", items[0].TxnType);
        Assert.False(items[0].CanAppeal);
        Assert.Equal("PENALTY", items[1].TxnType);
        Assert.True(items[1].CanAppeal);
    }
}
