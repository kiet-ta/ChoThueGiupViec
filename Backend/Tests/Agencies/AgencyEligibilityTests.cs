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

    [Theory]
    [InlineData("ACTIVE", 5_000_000, false)] // exactly the minimum: no warning
    [InlineData("ACTIVE", 6_000_000, false)]
    [InlineData("ACTIVE", 4_999_999, true)]
    [InlineData("ACTIVE", 3_000_000, true)] // still operating, below the minimum
    [InlineData("ACTIVE", 1, true)]
    [InlineData("PENDING", 0, false)] // status already says the minimum is not met
    [InlineData("PENDING", 3_000_000, false)]
    [InlineData("SUSPENDED", 0, false)]
    [InlineData("SUSPENDED", 3_000_000, false)]
    [InlineData("active", 3_000_000, false)] // values are exact, upper case
    public void Escrow_warning_is_only_for_an_active_agency_below_the_minimum(string status, long balance, bool expected)
    {
        Assert.Equal(expected, AgencyEligibility.HasEscrowWarning(status, balance, 5_000_000m));
    }

    [Fact]
    public void Escrow_warning_follows_a_configured_minimum()
    {
        Assert.True(AgencyEligibility.HasEscrowWarning(AgencyStatuses.Active, 9_000_000m, 10_000_000m));
        Assert.False(AgencyEligibility.HasEscrowWarning(AgencyStatuses.Active, 9_000_000m, 8_000_000m));
        Assert.False(AgencyEligibility.HasEscrowWarning(AgencyStatuses.Active, 0m, 0m));
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
