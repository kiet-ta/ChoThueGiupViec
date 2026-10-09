using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Booking.Services;

public interface IPricingService
{
    /// <summary>Price of an order from the current PRICE_RULE (decisions Q01). Throws when the rule is missing: a price is never guessed.</summary>
    Task<PriceQuote> QuoteAsync(ServiceTier serviceTier, decimal totalAreaM2, int requiredWorkers, CancellationToken cancellationToken = default);
}
