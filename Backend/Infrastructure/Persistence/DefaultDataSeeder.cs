using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CommonService.Infrastructure.Persistence;

/// <summary>
/// Seeds reference data required in all environments (decisions Q01, Q08).
/// Idempotent: checks for existence before adding rows.
/// </summary>
public class DefaultDataSeeder
{
    private readonly ILogger<DefaultDataSeeder> _logger;

    public DefaultDataSeeder(ILogger<DefaultDataSeeder> logger)
    {
        _logger = logger;
    }

    public async Task SeedAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        await SeedPriceRulesAsync(context, cancellationToken);
        await SeedSubscriptionPackagesAsync(context, cancellationToken);
    }

    private async Task SeedPriceRulesAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        // decisions.md §2 Q01 Pricing table (6 rows)
        var rules = new (ServiceTier Tier, string Bracket, decimal Price)[]
        {
            (ServiceTier.Economy, "UP_TO_30", 160000m),
            (ServiceTier.Economy, "FROM_31_TO_80", 260000m),
            (ServiceTier.Economy, "OVER_80", 260000m),
            (ServiceTier.Premium, "UP_TO_30", 240000m),
            (ServiceTier.Premium, "FROM_31_TO_80", 390000m),
            (ServiceTier.Premium, "OVER_80", 390000m)
        };

        var addedCount = 0;
        foreach (var (tier, bracket, price) in rules)
        {
            var exists = await context.PriceRules.AnyAsync(
                r => r.ServiceTier == tier && r.AreaBracket == bracket,
                cancellationToken);

            if (!exists)
            {
                context.PriceRules.Add(new PriceRule
                {
                    ServiceTier = tier,
                    AreaBracket = bracket,
                    UnitPrice = price,
                    IsActive = true,
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedBy = null
                });
                addedCount++;
            }
        }

        if (addedCount > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("DefaultDataSeeder: Seeded {Count} PRICE_RULE rows.", addedCount);
        }
    }

    private async Task SeedSubscriptionPackagesAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        // decisions.md §2 Q08 Subscription packages (3 rows)
        var packages = new[]
        {
            new SubscriptionPackage
            {
                PackageCode = "FREE",
                PackageName = "Gói Miễn Phí",
                Tier = "FREE",
                BillingCycle = "NONE",
                Price = 0m,
                WorkerQuota = 3,
                CommissionRate = 0.200m,
                HasRosterDashboard = false,
                HasAnalytics = false,
                PriorityDispatch = false,
                IsActive = true
            },
            new SubscriptionPackage
            {
                PackageCode = "PRO_MONTHLY",
                PackageName = "Gói Pro Hàng Tháng",
                Tier = "PRO",
                BillingCycle = "MONTHLY",
                Price = 2000000m,
                WorkerQuota = 50,
                CommissionRate = 0.000m,
                HasRosterDashboard = true,
                HasAnalytics = true,
                PriorityDispatch = true,
                IsActive = true
            },
            new SubscriptionPackage
            {
                PackageCode = "PRO_QUARTERLY",
                PackageName = "Gói Pro Hàng Quý",
                Tier = "PRO",
                BillingCycle = "QUARTERLY",
                Price = 5400000m,
                WorkerQuota = 50,
                CommissionRate = 0.000m,
                HasRosterDashboard = true,
                HasAnalytics = true,
                PriorityDispatch = true,
                IsActive = true
            }
        };

        var addedCount = 0;
        foreach (var pkg in packages)
        {
            var exists = await context.SubscriptionPackages.AnyAsync(
                p => p.PackageCode == pkg.PackageCode,
                cancellationToken);

            if (!exists)
            {
                context.SubscriptionPackages.Add(pkg);
                addedCount++;
            }
        }

        if (addedCount > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("DefaultDataSeeder: Seeded {Count} SUBSCRIPTION_PACKAGE rows.", addedCount);
        }
    }
}
