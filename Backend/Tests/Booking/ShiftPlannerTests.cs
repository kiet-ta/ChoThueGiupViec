using CommonService.Application.Common.Options;
using CommonService.Application.Features.Booking;
using CommonService.Domain.Enums;
using Xunit;

namespace CommonService.Tests.Booking;

public class ShiftPlannerTests
{
    private static readonly BusinessRules Defaults = new();

    [Theory]
    [InlineData("30")]
    [InlineData("79.99")]
    [InlineData("80")]
    [InlineData("80.00")]
    public void Plan_AreaUpToStandardMax_OneWorker(string area)
    {
        var plan = ShiftPlanner.Plan(decimal.Parse(area, System.Globalization.CultureInfo.InvariantCulture), Defaults, ServiceTier.Premium);

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
        var plan = ShiftPlanner.Plan(decimal.Parse(area, System.Globalization.CultureInfo.InvariantCulture), Defaults, ServiceTier.Premium);

        Assert.Equal(2, plan.RequiredWorkers);
        Assert.True(plan.IsLargeArea);
    }

    [Theory]
    [InlineData("30")]
    [InlineData("250")]
    public void Plan_ShiftLastsAtMostMaxHours(string area)
    {
        var plan = ShiftPlanner.Plan(decimal.Parse(area, System.Globalization.CultureInfo.InvariantCulture), Defaults, ServiceTier.Premium);

        Assert.Equal(4, plan.MaxHoursPerShift);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-0.01")]
    public void Plan_NonPositiveArea_Throws(string area)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShiftPlanner.Plan(decimal.Parse(area, System.Globalization.CultureInfo.InvariantCulture), Defaults, ServiceTier.Economy));
    }

    [Fact]
    public void Plan_NullRules_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ShiftPlanner.Plan(50m, null!, ServiceTier.Economy));
    }

    [Fact]
    public void Plan_UsesConfiguredThresholds()
    {
        var rules = new BusinessRules
        {
            Area = new AreaRules { StandardMaxM2 = 60m },
            Shift = new ShiftRules { MaxHours = 3 },
        };

        var atLimit = ShiftPlanner.Plan(60m, rules, ServiceTier.Premium);
        var overLimit = ShiftPlanner.Plan(60.01m, rules, ServiceTier.Premium);

        Assert.Equal(1, atLimit.RequiredWorkers);
        Assert.Equal(2, overLimit.RequiredWorkers);
        Assert.Equal(3, overLimit.MaxHoursPerShift);
    }

    [Theory]
    [InlineData("80.01")]
    [InlineData("120")]
    [InlineData("250")]
    public void Plan_EconomyLargeArea_OneWorkerPricedAsTwoUnits_D35(string area)
    {
        var plan = ShiftPlanner.Plan(decimal.Parse(area, System.Globalization.CultureInfo.InvariantCulture), Defaults, ServiceTier.Economy);

        Assert.Equal(1, plan.RequiredWorkers);
        Assert.Equal(2, plan.PriceUnits);
        Assert.True(plan.IsLargeArea);
        Assert.True(plan.IsContinuousWork);
    }

    [Theory]
    [InlineData("80.01")]
    [InlineData("250")]
    public void Plan_PremiumLargeArea_StaysTwoParallelWorkers(string area)
    {
        var plan = ShiftPlanner.Plan(decimal.Parse(area, System.Globalization.CultureInfo.InvariantCulture), Defaults, ServiceTier.Premium);

        Assert.Equal(2, plan.RequiredWorkers);
        Assert.Equal(2, plan.PriceUnits);
        Assert.False(plan.IsContinuousWork);
    }

    [Theory]
    [InlineData("30", ServiceTier.Economy)]
    [InlineData("80", ServiceTier.Economy)]
    [InlineData("30", ServiceTier.Premium)]
    public void Plan_SmallArea_OneWorkerOneUnitNotContinuous(string area, ServiceTier tier)
    {
        var plan = ShiftPlanner.Plan(decimal.Parse(area, System.Globalization.CultureInfo.InvariantCulture), Defaults, tier);

        Assert.Equal(1, plan.RequiredWorkers);
        Assert.Equal(1, plan.PriceUnits);
        Assert.False(plan.IsContinuousWork);
    }
}
