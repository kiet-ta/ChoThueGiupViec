using CommonService.Application.Features.Booking;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Booking;

/// <summary>PRICE_RULE access of the Booking module (BE-M2-02, BE-M2-02a). One row per (service_tier, area_bracket), UNIQUE (SC-2).</summary>
public class PriceRuleRepository(AppDbContext context) : IPriceRuleRepository
{
    public async Task<PriceRule?> GetActiveAsync(ServiceTier serviceTier, string areaBracket, CancellationToken cancellationToken = default)
    {
        return await context.PriceRules
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.ServiceTier == serviceTier && r.AreaBracket == areaBracket && r.IsActive,
                cancellationToken);
    }

    public async Task<IReadOnlyList<PriceRule>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.PriceRules
            .AsNoTracking()
            .OrderBy(r => r.ServiceTier)
            .ThenBy(r => r.AreaBracket)
            .ToListAsync(cancellationToken);

    public async Task<PriceRule?> GetForUpdateAsync(int ruleId, CancellationToken cancellationToken = default) =>
        await context.PriceRules.FirstOrDefaultAsync(r => r.RuleId == ruleId, cancellationToken);
}
