using CommonService.Application.Features.Agencies;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Agencies;

/// <summary>EF Core implementation of <see cref="ISubscriptionRepository"/>. Not verified against a live SQL Server on this host.</summary>
public sealed class EfSubscriptionRepository(AppDbContext db) : ISubscriptionRepository
{
    public async Task<IReadOnlyList<SubscriptionPackage>> ListPackagesAsync(bool activeOnly, CancellationToken cancellationToken = default) =>
        await db.SubscriptionPackages.AsNoTracking()
            .Where(p => !activeOnly || p.IsActive)
            .OrderBy(p => p.Price).ThenBy(p => p.PackageId)
            .ToListAsync(cancellationToken);

    public async Task<SubscriptionPackage?> GetPackageAsync(int packageId, CancellationToken cancellationToken = default) =>
        await db.SubscriptionPackages.AsNoTracking().FirstOrDefaultAsync(p => p.PackageId == packageId, cancellationToken);

    public async Task<SubscriptionPackage?> GetPackageForUpdateAsync(int packageId, CancellationToken cancellationToken = default) =>
        await db.SubscriptionPackages.FirstOrDefaultAsync(p => p.PackageId == packageId, cancellationToken);

    public async Task<SubscriptionPackage?> GetPackageByCodeAsync(string packageCode, CancellationToken cancellationToken = default) =>
        await db.SubscriptionPackages.AsNoTracking().FirstOrDefaultAsync(p => p.PackageCode == packageCode, cancellationToken);

    public async Task<string?> GetAgencyStatusAsync(int agencyId, CancellationToken cancellationToken = default) =>
        await db.PartnerAgencies.AsNoTracking()
            .Where(a => a.AgencyId == agencyId)
            .Select(a => a.AgencyStatus)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<(PartnerSubscription Subscription, SubscriptionPackage Package)>> ListSubscriptionsAsync(
        int agencyId, CancellationToken cancellationToken = default)
    {
        var rows = await (
            from s in db.PartnerSubscriptions.AsNoTracking()
            join p in db.SubscriptionPackages.AsNoTracking() on s.PackageId equals p.PackageId
            where s.AgencyId == agencyId
            orderby s.EndDate descending
            select new { s, p }
        ).ToListAsync(cancellationToken);
        return rows.Select(r => (r.s, r.p)).ToList();
    }

    public async Task<PartnerSubscription?> FindPendingAsync(int agencyId, int packageId, CancellationToken cancellationToken = default) =>
        await db.PartnerSubscriptions.AsNoTracking()
            .Where(s => s.AgencyId == agencyId && s.PackageId == packageId && s.SubStatus == SubscriptionStatuses.PendingPayment)
            .OrderByDescending(s => s.SubscriptionId)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(PartnerSubscription subscription) => db.PartnerSubscriptions.Add(subscription);

    public async Task<PartnerSubscription?> GetForUpdateAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
        await db.PartnerSubscriptions.FirstOrDefaultAsync(s => s.SubscriptionId == subscriptionId, cancellationToken);

    public async Task<IReadOnlyList<(int WorkerId, DateTime CreatedAt)>> ListCountedWorkersAsync(int agencyId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Workers.AsNoTracking()
            .Where(w => w.AgencyId == agencyId
                && w.WorkStatus != WorkStatus.Locked
                && w.WorkStatus != WorkStatus.Retiring
                && w.WorkStatus != WorkStatus.Retired) // retiring/retired workers do not count toward quota (AGENCY-UAT-02)
            .Select(w => new { w.WorkerId, w.CreatedAt })
            .ToListAsync(cancellationToken);
        return rows.Select(r => (r.WorkerId, r.CreatedAt)).ToList();
    }

    public async Task<IReadOnlyList<int>> ListAgenciesWithWorkersAsync(CancellationToken cancellationToken = default) =>
        await db.Workers.AsNoTracking()
            .Where(w => w.AgencyId != null
                && w.WorkStatus != WorkStatus.Locked
                && w.WorkStatus != WorkStatus.Retiring
                && w.WorkStatus != WorkStatus.Retired) // retiring/retired workers do not count (AGENCY-UAT-02)
            .Select(w => w.AgencyId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<int> LockWorkersAsync(IReadOnlyCollection<int> workerIds, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var workers = await db.Workers
            .Where(w => workerIds.Contains(w.WorkerId)
                && w.WorkStatus != WorkStatus.Locked
                && w.WorkStatus != WorkStatus.Busy
                && w.WorkStatus != WorkStatus.Retiring
                && w.WorkStatus != WorkStatus.Retired) // retiring/retired workers must never be locked (AGENCY-UAT-02)
            .ToListAsync(cancellationToken);
        foreach (var worker in workers)
        {
            worker.TransitionTo(WorkStatus.Locked);
            worker.UpdatedAt = nowUtc;
        }

        return workers.Count;
    }

    public async Task<PaymentTransaction?> FindPendingPaymentAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
        await db.PaymentTransactions.AsNoTracking()
            .Where(t => t.SubscriptionId == subscriptionId && t.Purpose == PaymentPurpose.Subscription && t.TxnStatus == PaymentStatus.Pending)
            .OrderByDescending(t => t.PaymentId)
            .FirstOrDefaultAsync(cancellationToken);
}
