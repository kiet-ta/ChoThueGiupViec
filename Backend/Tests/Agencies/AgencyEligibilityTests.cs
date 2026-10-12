using CommonService.Application.Common.Options;
using CommonService.Application.Features.Agencies;
using Xunit;

namespace CommonService.Tests.Agencies;

/// <summary>BE-M5-01: decisions Q09, an agency is ACTIVE only when escrow >= Agency.MinEscrowBalance.</summary>
public class AgencyEligibilityTests
{
    [Theory]
    [InlineData(0, "PENDING")]
    [InlineData(4_999_999, "PENDING")]
    [InlineData(5_000_000, "ACTIVE")]
    [InlineData(5_000_001, "ACTIVE")]
    [InlineData(50_000_000, "ACTIVE")]
    public void StatusFor_uses_the_default_minimum_of_five_million(long balance, string expected)
    {
        var min = new BusinessRules().Agency.MinEscrowBalance;

        Assert.Equal(5_000_000m, min);
        Assert.Equal(expected, AgencyEligibility.StatusFor(balance, min));
    }

    [Fact]
    public void StatusFor_follows_a_configured_minimum_and_keeps_decimal_precision()
    {
        Assert.Equal("PENDING", AgencyEligibility.StatusFor(9_999_999.99m, 10_000_000m));
        Assert.Equal("ACTIVE", AgencyEligibility.StatusFor(10_000_000m, 10_000_000m));
        Assert.Equal("ACTIVE", AgencyEligibility.StatusFor(0m, 0m));
    }

    [Fact]
    public void StatusFor_never_returns_suspended_and_a_negative_balance_is_pending()
    {
        Assert.Equal("PENDING", AgencyEligibility.StatusFor(-1m, 5_000_000m));
        Assert.NotEqual(AgencyStatuses.Suspended, AgencyEligibility.StatusFor(0m, 5_000_000m));
    }

    [Fact]
    public void Status_values_fit_the_VARCHAR_10_column()
    {
        Assert.All(
            new[] { AgencyStatuses.Pending, AgencyStatuses.Active, AgencyStatuses.Suspended },
            value => Assert.InRange(value.Length, 1, 10));
        Assert.Equal("PENDING", AgencyStatuses.Pending);
    }
}
