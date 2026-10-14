using CommonService.Application.Features.Agencies;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Agencies;

/// <summary>EF Core implementation of <see cref="IVerifiedPartnerRepository"/>: two set-based updates, no rows loaded.</summary>
public sealed class EfVerifiedPartnerRepository(AppDbContext db) : IVerifiedPartnerRepository
{
    private const string Active = "ACTIVE";
    private const string Pro = "PRO";

    public async Task<int> SyncAsync(DateOnly today, int graceDays, int? agencyId, CancellationToken cancellationToken = default)
    {
        var lastEnd = today.AddDays(-graceDays);

        // Agencies whose package in force is PRO: ACTIVE, started, and not past end_date + grace days.
        var pro = from s in db.PartnerSubscriptions
                  join p in db.SubscriptionPackages on s.PackageId equals p.PackageId
                  where s.SubStatus == Active && p.Tier == Pro && s.StartDate <= today && s.EndDate >= lastEnd
                  select s.AgencyId;

        var agencies = db.PartnerAgencies.Where(a => agencyId == null || a.AgencyId == agencyId);
        var on = await agencies
            .Where(a => !a.IsVerifiedPartner && pro.Contains(a.AgencyId))
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.IsVerifiedPartner, true), cancellationToken);
        var off = await agencies
            .Where(a => a.IsVerifiedPartner && !pro.Contains(a.AgencyId))
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.IsVerifiedPartner, false), cancellationToken);
        return on + off;
    }
}
