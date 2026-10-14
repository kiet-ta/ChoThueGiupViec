using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Agencies;

/// <summary>The package in force for an agency on a day, and whether it is a Pro subscription in its grace days.</summary>
public sealed record EffectivePackage(SubscriptionPackage Package, PartnerSubscription? Subscription, bool InGrace);

/// <summary>
/// The pure subscription rules of decisions Q08, no I/O and no clock: which package is in force, and which workers are over the quota.
/// </summary>
public static class WorkerQuotaPolicy
{
    /// <summary>provenance: decisions Q08 FREE row (worker_quota 3), used only when the FREE package row cannot be read.</summary>
    public const int FallbackFreeQuota = 3;

    public const string FreePackageCode = "FREE";

    /// <summary>provenance: decisions Q08/Q11 FREE row (commission 0.200), used only when the FREE package row cannot be read.</summary>
    public const decimal FallbackFreeCommissionRate = 0.200m;

    /// <summary>
    /// The newest ACTIVE subscription whose end date plus <paramref name="graceDays"/> has not passed is in force with its package;
    /// otherwise the FREE package. <paramref name="candidates"/> are subscriptions with their package (any status, any date).
    /// </summary>
    public static EffectivePackage Effective(
        IEnumerable<(PartnerSubscription Subscription, SubscriptionPackage Package)> candidates,
        SubscriptionPackage free,
        DateOnly today,
        int graceDays)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(free);

        var current = candidates
            .Where(c => c.Subscription.SubStatus == SubscriptionStatuses.Active
                && c.Subscription.StartDate <= today
                && c.Subscription.EndDate.AddDays(graceDays) >= today)
            .OrderByDescending(c => c.Subscription.EndDate)
            .Cast<(PartnerSubscription Subscription, SubscriptionPackage Package)?>()
            .FirstOrDefault();

        return current is { } c
            ? new EffectivePackage(c.Package, c.Subscription, InGrace: c.Subscription.EndDate < today)
            : new EffectivePackage(free, null, InGrace: false);
    }

    /// <summary>
    /// Workers above the quota, most recently created first (decisions Q08: they are locked, never deleted). The caller passes only the
    /// workers that count (not already locked).
    /// </summary>
    public static IReadOnlyList<int> WorkersOverQuota(IEnumerable<(int WorkerId, DateTime CreatedAt)> workers, int quota)
    {
        ArgumentNullException.ThrowIfNull(workers);
        var ordered = workers.OrderByDescending(w => w.CreatedAt).ThenByDescending(w => w.WorkerId).ToList();
        var excess = ordered.Count - Math.Max(0, quota);
        return excess <= 0 ? [] : ordered.Take(excess).Select(w => w.WorkerId).ToList();
    }

    /// <summary>Start of a new subscription: today, or the day after the running one ends (a renewal never loses days).</summary>
    public static DateOnly NextStart(DateOnly today, DateOnly? runningEnd) =>
        runningEnd is { } end && end >= today ? end.AddDays(1) : today;

    /// <summary>Last day (inclusive) of a subscription that starts on <paramref name="start"/>; MONTHLY = 1 month, QUARTERLY = 3 (Q08).</summary>
    public static DateOnly EndFor(DateOnly start, string billingCycle) => billingCycle switch
    {
        BillingCycles.Monthly => start.AddMonths(1).AddDays(-1),
        BillingCycles.Quarterly => start.AddMonths(3).AddDays(-1),
        _ => throw new ArgumentOutOfRangeException(nameof(billingCycle), billingCycle, "Only MONTHLY and QUARTERLY packages are purchased."),
    };
}

/// <summary><c>SUBSCRIPTION_PACKAGE.billing_cycle</c> values (decisions Q08).</summary>
public static class BillingCycles
{
    public const string Monthly = "MONTHLY";
    public const string Quarterly = "QUARTERLY";
}
