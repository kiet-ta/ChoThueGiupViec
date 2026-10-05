using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure;
using CommonService.Infrastructure.Modules.Identity;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CommonService.Tests.Persistence;

public class DatabaseSeederTests
{
    private const string ConnectionString =
        "Server=localhost;Database=ChoThueGiupViec;Integrated Security=True;TrustServerCertificate=True;";

    private static bool IsSqlServerAvailable()
    {
        try
        {
            using var conn = new SqlConnection(ConnectionString + "Connect Timeout=3;");
            conn.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "CommonService";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public async Task Overview_Section10_Three_Scenarios_For_AdminSeeder()
    {
        if (!IsSqlServerAvailable()) return;

        await using var context = CreateContext();
        var hasher = new Pbkdf2PasswordHasher();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:Admin:Email"] = "admin@dev.local",
                ["Seed:Admin:Password"] = "Admin@123456",
                ["Seed:Admin:FullName"] = "Dev Admin"
            })
            .Build();

        // Prepare clean state: delete test admin if exists
        var existing = await context.Admins.Where(a => a.Email == "admin@dev.local").ToListAsync();
        if (existing.Count > 0)
        {
            context.Admins.RemoveRange(existing);
            await context.SaveChangesAsync();
        }

        // -----------------------------------------------------------------------------------------
        // Scenario 1: Empty database -> runs in Development -> creates exactly 1 ADMIN row
        // -----------------------------------------------------------------------------------------
        var devEnv = new TestHostEnvironment(Environments.Development);
        var devSeeder = new AdminSeeder(config, devEnv, hasher, NullLogger<AdminSeeder>.Instance);

        await devSeeder.SeedAsync(context);

        var admins = await context.Admins.Where(a => a.Email == "admin@dev.local").ToListAsync();
        Assert.Single(admins);
        var admin = admins[0];
        Assert.Equal("admin@dev.local", admin.Email);
        Assert.Equal("Dev Admin", admin.FullName);
        Assert.Equal("SUPER_ADMIN", admin.AdminRole);
        Assert.True(admin.IsActive);
        Assert.True(hasher.Verify("Admin@123456", admin.PasswordHash), "Password hash should verify plain password");
        var initialPasswordHash = admin.PasswordHash;

        // -----------------------------------------------------------------------------------------
        // Scenario 2: Second run in Development -> still 1 ADMIN row, no duplicate, password unchanged
        // -----------------------------------------------------------------------------------------
        await devSeeder.SeedAsync(context);

        var adminsSecondRun = await context.Admins.Where(a => a.Email == "admin@dev.local").ToListAsync();
        Assert.Single(adminsSecondRun);
        Assert.Equal(initialPasswordHash, adminsSecondRun[0].PasswordHash);

        // -----------------------------------------------------------------------------------------
        // Scenario 3: Empty database -> runs in Production -> 0 ADMIN rows created
        // -----------------------------------------------------------------------------------------
        // Delete the admin row first to test Production behavior from clean state
        context.Admins.RemoveRange(adminsSecondRun);
        await context.SaveChangesAsync();

        var prodEnv = new TestHostEnvironment(Environments.Production);
        var prodSeeder = new AdminSeeder(config, prodEnv, hasher, NullLogger<AdminSeeder>.Instance);

        await prodSeeder.SeedAsync(context);

        var adminsProd = await context.Admins.Where(a => a.Email == "admin@dev.local").ToListAsync();
        Assert.Empty(adminsProd);

        // Restore the dev admin for local development
        await devSeeder.SeedAsync(context);
    }

    [Fact]
    public async Task DefaultDataSeeder_seeds_Q01_prices_Q08_packages_and_sample_skills()
    {
        if (!IsSqlServerAvailable()) return;

        await using var context = CreateContext();
        var seeder = new DefaultDataSeeder(NullLogger<DefaultDataSeeder>.Instance);

        // Run seeder
        await seeder.SeedAsync(context);

        // Verify 6 PRICE_RULE rows (decisions Q01)
        var priceRules = await context.PriceRules.ToListAsync();
        Assert.True(priceRules.Count >= 6, "Expected at least 6 PRICE_RULE rows");

        Assert.Contains(priceRules, r => r.ServiceTier == ServiceTier.Economy && r.AreaBracket == "UP_TO_30" && r.UnitPrice == 160000m);
        Assert.Contains(priceRules, r => r.ServiceTier == ServiceTier.Economy && r.AreaBracket == "FROM_31_TO_80" && r.UnitPrice == 260000m);
        Assert.Contains(priceRules, r => r.ServiceTier == ServiceTier.Economy && r.AreaBracket == "OVER_80" && r.UnitPrice == 260000m);
        Assert.Contains(priceRules, r => r.ServiceTier == ServiceTier.Premium && r.AreaBracket == "UP_TO_30" && r.UnitPrice == 240000m);
        Assert.Contains(priceRules, r => r.ServiceTier == ServiceTier.Premium && r.AreaBracket == "FROM_31_TO_80" && r.UnitPrice == 390000m);
        Assert.Contains(priceRules, r => r.ServiceTier == ServiceTier.Premium && r.AreaBracket == "OVER_80" && r.UnitPrice == 390000m);

        // Verify 3 SUBSCRIPTION_PACKAGE rows (decisions Q08)
        var packages = await context.SubscriptionPackages.ToListAsync();
        Assert.True(packages.Count >= 3, "Expected at least 3 SUBSCRIPTION_PACKAGE rows");

        var freePkg = packages.Single(p => p.PackageCode == "FREE");
        Assert.Equal(0m, freePkg.Price);
        Assert.Equal(3, freePkg.WorkerQuota);
        Assert.Equal(0.200m, freePkg.CommissionRate);
        Assert.Equal("NONE", freePkg.BillingCycle);
        Assert.False(freePkg.HasRosterDashboard);
        Assert.False(freePkg.HasAnalytics);
        Assert.False(freePkg.PriorityDispatch);

        var proMonthly = packages.Single(p => p.PackageCode == "PRO_MONTHLY");
        Assert.Equal(2000000m, proMonthly.Price);
        Assert.Equal(50, proMonthly.WorkerQuota);
        Assert.Equal(0.000m, proMonthly.CommissionRate);
        Assert.Equal("MONTHLY", proMonthly.BillingCycle);
        Assert.True(proMonthly.HasRosterDashboard);
        Assert.True(proMonthly.HasAnalytics);
        Assert.True(proMonthly.PriorityDispatch);

        var proQuarterly = packages.Single(p => p.PackageCode == "PRO_QUARTERLY");
        Assert.Equal(5400000m, proQuarterly.Price);
        Assert.Equal(50, proQuarterly.WorkerQuota);
        Assert.Equal(0.000m, proQuarterly.CommissionRate);
        Assert.Equal("QUARTERLY", proQuarterly.BillingCycle);
        Assert.True(proQuarterly.HasRosterDashboard);
        Assert.True(proQuarterly.HasAnalytics);
        Assert.True(proQuarterly.PriorityDispatch);

        // Verify sample skills
        var skills = await context.Skills.ToListAsync();
        Assert.True(skills.Count >= 4, "Expected at least 4 sample Skill rows");
        Assert.Contains(skills, s => s.SkillCode == "CLEAN_BASIC");
        Assert.Contains(skills, s => s.SkillCode == "DEEP_CLEAN");
        Assert.Contains(skills, s => s.SkillCode == "IRONING");
        Assert.Contains(skills, s => s.SkillCode == "COOKING");

        // Idempotency: run second time and verify no counts change
        var priceRulesCountBefore = priceRules.Count;
        var packagesCountBefore = packages.Count;
        var skillsCountBefore = skills.Count;

        await seeder.SeedAsync(context);

        Assert.Equal(priceRulesCountBefore, await context.PriceRules.CountAsync());
        Assert.Equal(packagesCountBefore, await context.SubscriptionPackages.CountAsync());
        Assert.Equal(skillsCountBefore, await context.Skills.CountAsync());
    }

    [Fact]
    public void IdentityModule_registers_Pbkdf2PasswordHasher_via_IModule()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        services.AddLogging();
        services.AddInfrastructure(config);

        using var provider = services.BuildServiceProvider();
        var hasher = provider.GetRequiredService<IPasswordHasher>();

        Assert.IsType<Pbkdf2PasswordHasher>(hasher);
    }
}
