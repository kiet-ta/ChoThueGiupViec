using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Booking.Services;

/// <summary>Looks up the active PRICE_RULE of the area bracket and applies <see cref="PriceCalculator"/> (BE-M2-02).</summary>
public sealed class PricingService(IPriceRuleRepository priceRules) : IPricingService
{
    public async Task<PriceQuote> QuoteAsync(ServiceTier serviceTier, decimal totalAreaM2, int requiredWorkers, CancellationToken cancellationToken = default)
    {
        var bracket = AreaBrackets.Classify(totalAreaM2);
        var rule = await priceRules.GetActiveAsync(serviceTier, bracket, cancellationToken)
            ?? throw new InvalidOperationException($"No active PRICE_RULE for {serviceTier} / {bracket}.");

        return PriceCalculator.Calculate(serviceTier, totalAreaM2, requiredWorkers, rule.UnitPrice);
    }
}
