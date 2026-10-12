using Backend.Application.Features.Agencies;
using Backend.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Infrastructure.Modules.Agencies;

/// <summary>
/// Entity Framework implementation of the agency subscription repository
/// </summary>
public class EfSubscriptionRepository : ISubscriptionRepository
{
    private readonly AppDbContext _dbContext;

    public EfSubscriptionRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SubscriptionPackage?> GetPackageByIdAsync(int packageId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<SubscriptionPackage>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PackageId == packageId, cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionPackage>> GetActivePackagesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<SubscriptionPackage>()
            .AsNoTracking()
            .Where(p => p.IsActive)
            .ToListAsync(cancellationToken);
    }

    public async Task<PartnerSubscription?> GetActiveSubscriptionByAgencyIdAsync(int agencyId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<PartnerSubscription>()
            .AsNoTracking()
            .Where(s => s.AgencyId == agencyId && s.SubStatus == "Active")
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(PartnerSubscription subscription, CancellationToken cancellationToken = default)
    {
        await _dbContext.Set<PartnerSubscription>().AddAsync(subscription, cancellationToken);
    }

    public async Task UpdateAsync(PartnerSubscription subscription, CancellationToken cancellationToken = default)
    {
        _dbContext.Set<PartnerSubscription>().Update(subscription);
        await Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}