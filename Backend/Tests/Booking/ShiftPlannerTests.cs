using CommonService.Application.Common.Options;
using CommonService.Application.Features.Booking;
using Xunit;

namespace CommonService.Tests.Booking;

public sealed class ShiftPlannerTests
{
    private static readonly BusinessRules Defaults = new();

    [Theory]
    [InlineData("30")]
    [InlineData("79.99")]
    [InlineData("80")]
    [InlineData("80.00")]
    public void Plan_AreaUpToStandardMax_OneWorker(string area)
    {
        var plan = ShiftPlanner.Plan(decimal.Parse(area, System.Globalization.CultureInfo.InvariantCulture), Defaults);

        Assert.Equal(1, plan.RequiredWorkers);
        Assert.False(plan.IsLargeArea);
    }

    [Theory]
    [InlineData("80.01")]
    [InlineData("120")]
    [InlineData("250")]
    [InlineData("9999.99")]
    public void Plan_AreaOverStandardMax_ExactlyTwoWorkers(string area)
    {
        var plan = ShiftPlanner.Plan(decimal.Parse(area, System.Globalization.CultureInfo.InvariantCulture), Defaults);

        Assert.Equal(2, plan.RequiredWorkers);
        Assert.True(plan.IsLargeArea);
    }

    [Theory]
    [InlineData("30")]
    [InlineData("250")]
    public void Plan_ShiftLastsAtMostMaxHours(string area)
    {
        var plan = ShiftPlanner.Plan(decimal.Parse(area, System.Globalization.CultureInfo.InvariantCulture), Defaults);

        Assert.Equal(4, plan.MaxHoursPerShift);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-0.01")]
    public void Plan_NonPositiveArea_Throws(string area)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShiftPlanner.Plan(decimal.Parse(area, System.Globalization.CultureInfo.InvariantCulture), Defaults));
    }

    [Fact]
    public void Plan_NullRules_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ShiftPlanner.Plan(50m, null!));
    }

    [Fact]
    public void Plan_UsesConfiguredThresholds()
    {
        var rules = new BusinessRules
        {
            Area = new AreaRules { StandardMaxM2 = 60m },
            Shift = new ShiftRules { MaxHours = 3 },
        };

        var atLimit = ShiftPlanner.Plan(60m, rules);
        var overLimit = ShiftPlanner.Plan(60.01m, rules);

        Assert.Equal(1, atLimit.RequiredWorkers);
        Assert.Equal(2, overLimit.RequiredWorkers);
        Assert.Equal(3, overLimit.MaxHoursPerShift);
    }
}
