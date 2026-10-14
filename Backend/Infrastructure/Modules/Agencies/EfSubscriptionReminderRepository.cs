using CommonService.Application.Features.Agencies;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Agencies;

/// <summary>EF Core implementation of <see cref="ISubscriptionReminderRepository"/>.</summary>
public sealed class EfSubscriptionReminderRepository(AppDbContext db) : ISubscriptionReminderRepository
{
    private const string Active = "ACTIVE";

    public async Task<IReadOnlyList<ExpiringSubscription>> ListEndingAsync(
        DateOnly today, DateOnly lastEndDate, CancellationToken cancellationToken = default)
    {
        var rows = await db.PartnerSubscriptions.AsNoTracking()
            .Where(s => s.SubStatus == Active
                && s.EndDate >= today
                && s.EndDate <= lastEndDate
                && !db.PartnerSubscriptions.Any(o => o.AgencyId == s.AgencyId && o.SubStatus == Active && o.EndDate > s.EndDate))
            .OrderBy(s => s.EndDate)
            .Select(s => new { s.SubscriptionId, s.AgencyId, s.EndDate, s.RenewalReminderDays })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new ExpiringSubscription(r.SubscriptionId, r.AgencyId, r.EndDate, r.RenewalReminderDays)).ToList();
    }

    public async Task<bool> MarkRemindedAsync(int subscriptionId, int days, CancellationToken cancellationToken = default)
    {
        var subscription = await db.PartnerSubscriptions.FirstOrDefaultAsync(s => s.SubscriptionId == subscriptionId, cancellationToken);
        if (subscription is null)
        {
            return false;
        }

        subscription.RenewalReminderDays = (byte)days;
        return true;
    }
}
