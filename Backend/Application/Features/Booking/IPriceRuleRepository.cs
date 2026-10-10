using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Booking;

/// <summary>Access to PRICE_RULE for the Booking module (decisions Q01, SC-2).</summary>
public interface IPriceRuleRepository
{
    /// <summary>The active rule of the pair (not tracked), or null when none exists.</summary>
    Task<PriceRule?> GetActiveAsync(ServiceTier serviceTier, string areaBracket, CancellationToken cancellationToken = default);

    /// <summary>Every rule (not tracked), ordered by service tier then area bracket (BE-M2-02a).</summary>
    Task<IReadOnlyList<PriceRule>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>The rule by id, TRACKED so the caller's unit of work saves the change; null when unknown (BE-M2-02a).</summary>
    Task<PriceRule?> GetForUpdateAsync(int ruleId, CancellationToken cancellationToken = default);
}
