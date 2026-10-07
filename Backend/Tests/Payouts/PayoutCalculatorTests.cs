using CommonService.Application.Features.Payouts;
using CommonService.Domain.Enums;

namespace CommonService.Tests.Payouts;

/// <summary>BE-M6-04: the pure arithmetic of the monthly payout (decisions Q10, Q11, G-2; contract payouts.md 2.1).</summary>
public class PayoutCalculatorTests
{
    private static readonly IReadOnlyDictionary<PayeeKey, decimal> None = new Dictionary<PayeeKey, decimal>();

    private static PayoutAssignmentRow Done(long id, int worker, decimal gross, decimal rate = 0.200m, int? agency = null) =>
        new(id, worker, agency, JobAssignmentStatus.Completed, gross, rate, null);

    private static PayoutAssignmentRow Absent(long id, int worker, decimal fee, int? agency = null) =>
        new(id, worker, agency, JobAssignmentStatus.Absent, 260000m, 0.200m, fee);

    private static Dictionary<PayeeKey, decimal> Owed(PayeeType type, int id, decimal amount) => new() { [new PayeeKey(type, id)] = amount };

    [Fact]
    public void Freelancers_are_one_item_each_and_agencies_one_item_for_all_their_workers_freelancers_first()
    {
        var lines = PayoutCalculator.Build(
            [
                Done(1, 8, 260000m, 0.15m, agency: 3),
                Done(2, 5, 260000m),
                Done(3, 9, 200000m, 0.15m, agency: 3), // another worker of the same agency
                Done(4, 5, 100000m),
                Done(5, 4, 50000m),
            ],
            None, None);

        Assert.Equal(
            [new PayeeKey(PayeeType.Freelancer, 4), new PayeeKey(PayeeType.Freelancer, 5), new PayeeKey(PayeeType.Agency, 3)],
            lines.Select(l => l.Payee).ToArray());

        var worker5 = lines[1];
        Assert.Equal(2, worker5.JobCount);
        Assert.Equal(360000m, worker5.Gross);
        Assert.Equal(72000m, worker5.Commission); // 20 %, decision Q11
        Assert.Equal(288000m, worker5.Net);
        Assert.Equal([2L, 4L], worker5.AssignmentIds.ToArray());

        var agency = lines[2];
        Assert.Equal(2, agency.JobCount); // the agency is paid as one payee, not per worker
        Assert.Equal(460000m, agency.Gross);
        Assert.Equal(69000m, agency.Commission); // the agency's own frozen rate, 15 %
        Assert.Equal(391000m, agency.Net);
    }

    [Fact]
    public void Commission_is_rounded_per_assignment_not_on_the_sum()
    {
        // 100003 x 0.2 = 20000.6 -> 20001 each, so 40002; rounding the sum 200006 x 0.2 = 40001.2 would give 40001.
        var line = Assert.Single(PayoutCalculator.Build([Done(1, 5, 100003m), Done(2, 5, 100003m)], None, None));

        Assert.Equal(40002m, line.Commission);
        Assert.Equal(160004m, line.Net);
    }

    [Fact]
    public void The_rate_frozen_on_each_assignment_is_used_even_when_the_same_payee_has_different_rates()
    {
        var line = Assert.Single(PayoutCalculator.Build(
            [Done(1, 5, 100000m, 0.20m, agency: 3), Done(2, 6, 100000m, 0.10m, agency: 3)], None, None));

        Assert.Equal(30000m, line.Commission);
    }

    [Fact]
    public void An_approved_absence_fee_is_paid_in_full_with_no_commission()
    {
        var line = Assert.Single(PayoutCalculator.Build([Done(1, 5, 260000m), Absent(2, 5, 104000m)], None, None));

        Assert.Equal(2, line.JobCount);
        Assert.Equal(364000m, line.Gross); // 260000 + the 104000 fee
        Assert.Equal(52000m, line.Commission); // only the completed job pays commission
        Assert.Equal(312000m, line.Net);
    }

    [Fact]
    public void An_absent_assignment_without_a_fee_and_every_other_state_pay_nothing()
    {
        var rows = new[]
        {
            Absent(1, 5, 0m),
            new PayoutAssignmentRow(2, 5, null, JobAssignmentStatus.Absent, 260000m, 0.2m, null),
            new PayoutAssignmentRow(3, 5, null, JobAssignmentStatus.InProgress, 260000m, 0.2m, null),
            new PayoutAssignmentRow(4, 5, null, JobAssignmentStatus.Cancelled, 260000m, 0.2m, null),
            new PayoutAssignmentRow(5, 5, null, JobAssignmentStatus.CancelledByWorker, 260000m, 0.2m, null),
        };

        Assert.Empty(PayoutCalculator.Build(rows, None, None));
    }

    [Fact]
    public void A_pending_penalty_is_taken_from_the_net()
    {
        var line = Assert.Single(PayoutCalculator.Build([Done(1, 5, 260000m)], Owed(PayeeType.Freelancer, 5, 50000m), None));

        Assert.Equal(50000m, line.Penalty);
        Assert.Equal(158000m, line.Net); // 260000 - 52000 - 50000
    }

    [Fact]
    public void A_penalty_larger_than_the_payable_amount_is_capped_so_the_net_is_zero_and_the_rest_waits()
    {
        var decided = Owed(PayeeType.Freelancer, 5, 500000m);

        var first = Assert.Single(PayoutCalculator.Build([Done(1, 5, 260000m)], decided, None));
        Assert.Equal(208000m, first.Penalty); // everything the worker earns this month
        Assert.Equal(0m, first.Net);

        // next month: 208000 of the 500000 was applied, 292000 is still pending
        var next = Assert.Single(PayoutCalculator.Build([Done(2, 5, 500000m)], decided, Owed(PayeeType.Freelancer, 5, 208000m)));
        Assert.Equal(292000m, next.Penalty);
        Assert.Equal(108000m, next.Net); // 500000 - 100000 - 292000
    }

    [Fact]
    public void A_penalty_already_applied_in_full_is_not_taken_again_and_never_goes_negative()
    {
        var decided = Owed(PayeeType.Freelancer, 5, 100000m);

        var done = Assert.Single(PayoutCalculator.Build([Done(1, 5, 260000m)], decided, Owed(PayeeType.Freelancer, 5, 100000m)));
        Assert.Equal(0m, done.Penalty);

        var odd = Assert.Single(PayoutCalculator.Build([Done(1, 5, 260000m)], decided, Owed(PayeeType.Freelancer, 5, 999999m)));
        Assert.Equal(0m, odd.Penalty); // applied above decided: nothing pending, no negative penalty
        Assert.Equal(208000m, odd.Net);
    }

    [Fact]
    public void A_penalty_belongs_to_its_own_payee_and_a_freelancer_and_an_agency_with_the_same_id_are_different_payees()
    {
        var lines = PayoutCalculator.Build(
            [Done(1, 5, 260000m), Done(2, 6, 260000m, 0.15m, agency: 5)],
            Owed(PayeeType.Agency, 5, 40000m), None);

        Assert.Equal(0m, lines[0].Penalty); // freelancer 5
        Assert.Equal(40000m, lines[1].Penalty); // agency 5
    }

    [Fact]
    public void A_payee_with_a_penalty_but_no_earnings_this_month_gets_no_item()
    {
        Assert.Empty(PayoutCalculator.Build([], Owed(PayeeType.Freelancer, 5, 50000m), None));
    }

    [Theory]
    [InlineData(100001, new[] { 300000, 100000, 100000 }, new[] { 60001, 20000, 20000 })]
    [InlineData(100, new[] { 1 }, new[] { 100 })]
    [InlineData(3, new[] { 100000, 100000, 100000, 100000, 100000 }, new[] { 1, 1, 1, 0, 0 })]
    [InlineData(0, new[] { 5, 5 }, new[] { 0, 0 })]
    [InlineData(10, new[] { 0, 0 }, new[] { 0, 10 })]
    public void A_split_is_whole_VND_adds_up_exactly_and_is_never_negative(int amount, int[] weights, int[] expected)
    {
        var parts = PayoutCalculator.SplitByWeight(amount, weights.Select(w => (decimal)w).ToList());

        Assert.Equal(expected.Select(e => (decimal)e).ToArray(), parts.ToArray());
        Assert.Equal(amount, parts.Sum());
    }

    [Fact]
    public void Splitting_over_nobody_gives_nothing()
    {
        Assert.Empty(PayoutCalculator.SplitByWeight(100m, []));
    }

    [Theory]
    [InlineData("2026-10", true, 2026, 10)]
    [InlineData("2026-01", true, 2026, 1)]
    [InlineData("2026-12", true, 2026, 12)]
    [InlineData("2026-13", false, 0, 0)]
    [InlineData("2026-00", false, 0, 0)]
    [InlineData("2026-1", false, 0, 0)]
    [InlineData("26-10", false, 0, 0)]
    [InlineData("2026/10", false, 0, 0)]
    [InlineData("1999-12", false, 0, 0)]
    [InlineData(" 2026-10", false, 0, 0)]
    [InlineData("", false, 0, 0)]
    [InlineData(null, false, 0, 0)]
    public void The_period_must_be_YYYY_MM(string? text, bool ok, int year, int month)
    {
        Assert.Equal(ok, PayoutConstants.TryParsePeriod(text, out var y, out var m));
        Assert.Equal((year, month), (y, m));
    }

    [Fact]
    public void A_period_is_formatted_with_two_digit_month()
    {
        Assert.Equal("2026-03", PayoutConstants.FormatPeriod(2026, 3));
    }
}
