using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Booking;

/// <summary>Read access to PRICE_RULE for the Booking module (decisions Q01, SC-2).</summary>
public interface IPriceRuleRepository
{
    /// <summary>The active rule of the pair (not tracked), or null when none exists.</summary>
    Task<PriceRule?> GetActiveAsync(ServiceTier serviceTier, string areaBracket, CancellationToken cancellationToken = default);
}
