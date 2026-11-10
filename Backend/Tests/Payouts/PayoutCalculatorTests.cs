using CommonService.Application.Features.Payouts;
using CommonService.Domain.Enums;

namespace CommonService.Tests.Payouts;

/// <summary>BE-M6-04: the pure arithmetic of one assignment's payout and the whole-VND split (decisions Q10, Q11, G-2). Grouping and deductions are tested in <c>PayoutSettlementCalculatorTests</c>.</summary>
public class PayoutCalculatorTests
{
    private static PayoutAssignmentRow Done(long id, int worker, decimal gross, decimal rate = 0.200m, int? agency = null) =>
        new(id, worker, agency, JobAssignmentStatus.Completed, gross, rate, null);

    private static PayoutAssignmentRow Absent(long id, int worker, decimal fee, int? agency = null) =>
        new(id, worker, agency, JobAssignmentStatus.Absent, 260000m, 0.200m, fee);

    [Fact]
    public void A_completed_assignment_pays_its_gross_less_the_commission_of_its_frozen_rate()
    {
        var freelancer = PayoutCalculator.AmountsOf(Done(1, 5, 260000m));
        var agency = PayoutCalculator.AmountsOf(Done(2, 8, 260000m, 0.15m, agency: 3));

        Assert.Equal((260000m, 52000m, 208000m), (freelancer.Gross, freelancer.Commission, freelancer.Payable)); // 20 %, decision Q11
        Assert.Equal((260000m, 39000m, 221000m), (agency.Gross, agency.Commission, agency.Payable)); // the agency's own 15 %
    }

    [Fact]
    public void Commission_is_rounded_per_assignment_not_on_the_sum()
    {
        // 100003 x 0.2 = 20000.6 -> 20001 each, so 40002; rounding the sum 200006 x 0.2 = 40001.2 would give 40001.
        var each = PayoutCalculator.AmountsOf(Done(1, 5, 100003m));

        Assert.Equal(20001m, each.Commission);
        Assert.Equal(80002m, each.Payable);
    }

    [Fact]
    public void The_rate_frozen_on_each_assignment_is_used()
    {
        Assert.Equal(20000m, PayoutCalculator.AmountsOf(Done(1, 5, 100000m, 0.20m, agency: 3)).Commission);
        Assert.Equal(10000m, PayoutCalculator.AmountsOf(Done(2, 6, 100000m, 0.10m, agency: 3)).Commission);
    }

    [Fact]
    public void An_approved_absence_fee_is_paid_in_full_with_no_commission()
    {
        var fee = PayoutCalculator.AmountsOf(Absent(2, 5, 104000m));

        Assert.Equal((104000m, 0m, 104000m), (fee.Gross, fee.Commission, fee.Payable));
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

        Assert.All(rows, row => Assert.Equal((0m, 0m), (PayoutCalculator.AmountsOf(row).Gross, PayoutCalculator.AmountsOf(row).Commission)));
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
