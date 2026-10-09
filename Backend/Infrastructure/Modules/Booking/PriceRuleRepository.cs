using CommonService.Application.Features.Booking;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Booking;

/// <summary>PRICE_RULE reads of the Booking module (BE-M2-02). One row per (service_tier, area_bracket), UNIQUE (SC-2).</summary>
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
}
