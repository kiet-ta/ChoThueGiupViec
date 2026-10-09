using System.Globalization;
using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Booking.Services;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Xunit;

namespace CommonService.Tests.Booking;

public sealed class PricingTests
{
    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("0.01", AreaBrackets.UpTo30)]
    [InlineData("30", AreaBrackets.UpTo30)]
    [InlineData("30.00", AreaBrackets.UpTo30)]
    [InlineData("30.01", AreaBrackets.From31To80)]
    [InlineData("80", AreaBrackets.From31To80)]
    [InlineData("80.00", AreaBrackets.From31To80)]
    [InlineData("80.01", AreaBrackets.Over80)]
    [InlineData("9999.99", AreaBrackets.Over80)]
    public void Classify_UsesQ01Brackets(string area, string expected)
    {
        Assert.Equal(expected, AreaBrackets.Classify(D(area)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void Classify_NonPositiveArea_Throws(string area)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaBrackets.Classify(D(area)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void Calculate_WorkersOtherThanOneOrTwo_Throws(int workers)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PriceCalculator.Calculate(ServiceTier.Economy, 50m, workers, 260000m));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Calculate_NonPositiveUnitPrice_Throws(string unitPrice)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PriceCalculator.Calculate(ServiceTier.Economy, 50m, 1, D(unitPrice)));
    }

    [Fact]
    public void Calculate_TotalIsUnitTimesWorkers()
    {
        var quote = PriceCalculator.Calculate(ServiceTier.Premium, 120m, 2, 390000m);

        Assert.Equal(AreaBrackets.Over80, quote.AreaBracket);
        Assert.Equal(390000m, quote.UnitPrice);
        Assert.Equal(2, quote.RequiredWorkers);
        Assert.Equal(780000m, quote.TotalAmount);
    }

    [Fact]
    public void Calculate_RoundsToWholeVnd()
    {
        var quote = PriceCalculator.Calculate(ServiceTier.Economy, 20m, 1, 160000.5m);

        Assert.Equal(160001m, quote.TotalAmount);
    }

    // decisions Q01 DEFAULT seed: all 6 tier x bracket combinations, with the BR-01/02 worker count.
    [Theory]
    [InlineData(ServiceTier.Economy, "30", 1, "160000", "160000")]
    [InlineData(ServiceTier.Economy, "30.01", 1, "260000", "260000")]
    [InlineData(ServiceTier.Economy, "80.01", 2, "260000", "520000")]
    [InlineData(ServiceTier.Premium, "30", 1, "240000", "240000")]
    [InlineData(ServiceTier.Premium, "80", 1, "390000", "390000")]
    [InlineData(ServiceTier.Premium, "80.01", 2, "390000", "780000")]
    public async Task QuoteAsync_AllSixCombinations_WithDefaultPrices(
        ServiceTier tier, string area, int workers, string expectedUnit, string expectedTotal)
    {
        var service = new PricingService(new SeededPriceRules());

        var quote = await service.QuoteAsync(tier, D(area), workers);

        Assert.Equal(tier, quote.ServiceTier);
        Assert.Equal(D(expectedUnit), quote.UnitPrice);
        Assert.Equal(D(expectedTotal), quote.TotalAmount);
    }

    [Fact]
    public async Task QuoteAsync_MissingRule_Throws()
    {
        var service = new PricingService(new SeededPriceRules(includePremium: false));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.QuoteAsync(ServiceTier.Premium, 50m, 1));
    }

    /// <summary>In-memory copy of the Q01 DEFAULT seed (Backend/Infrastructure/Persistence/DefaultDataSeeder.cs).</summary>
    private sealed class SeededPriceRules(bool includePremium = true) : IPriceRuleRepository
    {
        private readonly List<PriceRule> _rules =
        [
            new() { ServiceTier = ServiceTier.Economy, AreaBracket = AreaBrackets.UpTo30, UnitPrice = 160000m, IsActive = true },
            new() { ServiceTier = ServiceTier.Economy, AreaBracket = AreaBrackets.From31To80, UnitPrice = 260000m, IsActive = true },
            new() { ServiceTier = ServiceTier.Economy, AreaBracket = AreaBrackets.Over80, UnitPrice = 260000m, IsActive = true },
            new() { ServiceTier = ServiceTier.Premium, AreaBracket = AreaBrackets.UpTo30, UnitPrice = 240000m, IsActive = includePremium },
            new() { ServiceTier = ServiceTier.Premium, AreaBracket = AreaBrackets.From31To80, UnitPrice = 390000m, IsActive = includePremium },
            new() { ServiceTier = ServiceTier.Premium, AreaBracket = AreaBrackets.Over80, UnitPrice = 390000m, IsActive = includePremium },
        ];

        public Task<PriceRule?> GetActiveAsync(ServiceTier serviceTier, string areaBracket, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rules.FirstOrDefault(r => r.ServiceTier == serviceTier && r.AreaBracket == areaBracket && r.IsActive));
    }
}
