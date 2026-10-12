using CommonService.Application.Features.Agencies;
using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core repository implementation for PartnerSubscription
/// </summary>
public class EfSubscriptionRepository : EfRepository<PartnerSubscription, int>, ISubscriptionRepository
{
    public EfSubscriptionRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<PartnerSubscription?> GetByAgencyIdAsync(int agencyId, CancellationToken cancellationToken = default)
    {
        return await DbSet.FirstOrDefaultAsync(x => x.AgencyId == agencyId, cancellationToken);
    }

    public async Task<PartnerSubscription?> GetActiveSubscriptionByAgencyIdAsync(int agencyId, CancellationToken cancellationToken = default)
    {
        return await DbSet.FirstOrDefaultAsync(
            x => x.AgencyId == agencyId &&
                 x.EndDate >= DateOnly.FromDateTime(DateTime.UtcNow) &&
                 (x.SubStatus == "Active" || x.SubStatus == "Trial"),
            cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await Context.SaveChangesAsync(cancellationToken);
    }
}