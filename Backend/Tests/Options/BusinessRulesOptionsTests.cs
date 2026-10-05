using CommonService.Application.Common.Options;
using CommonService.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CommonService.Tests.Options;

public class BusinessRulesOptionsTests
{
    private static void AssertDecisionsSection4Defaults(BusinessRules rules)
    {
        // 1. Shift
        Assert.Equal(4, rules.Shift.MaxHours);

        // 2. Area
        Assert.Equal(80m, rules.Area.StandardMaxM2);

        // 3. Gps
        Assert.Equal(100.0, rules.Gps.CheckInToleranceMeters);

        // 4. Dispatch
        Assert.Equal([5.0, 7.0, 10.0], rules.Dispatch.RadiusStepsKm);
        Assert.Equal(30, rules.Dispatch.OfferTimeoutSeconds);

        // 5. Commission
        Assert.Equal(0.200m, rules.Commission.Freelancer);

        // 6. Absence
        Assert.Equal(0.40m, rules.Absence.FeeRate);
        Assert.Equal(15, rules.Absence.MinWaitMinutes);
        Assert.Equal(2, rules.Absence.MinCallAttempts);
        Assert.Equal(24, rules.Absence.CustomerDisputeHours);

        // 7. Vol
        Assert.Equal(100.0, rules.Vol.Threshold);
        Assert.Equal(640, rules.Vol.ResizeWidthPx);

        // 8. Photos
        Assert.Equal(3, rules.Photos.MinPerPhase);
        Assert.Equal(5, rules.Photos.MaxPerPhase);

        // 9. Payments
        Assert.Equal(15, rules.Payments.QrExpiryMinutes);
        Assert.Equal(3, rules.Payments.ReconcileAfterMinutes);
        Assert.Equal(60, rules.Payments.ReconcileIntervalSeconds);

        // 10. Ekyc
        Assert.Equal(85.00m, rules.Ekyc.AutoApproveConfidence);
        Assert.Equal(92.00m, rules.Ekyc.Fake.Confidence);
        Assert.Equal(0.20m, rules.Ekyc.AuditRate);
        Assert.Equal(5, rules.Ekyc.FullAuditFirstJobs);

        // 11. Otp
        Assert.Equal(6, rules.Otp.Length);
        Assert.Equal(5, rules.Otp.TtlMinutes);
        Assert.Equal(5, rules.Otp.MaxAttempts);
        Assert.Equal(60, rules.Otp.ResendCooldownSeconds);
        Assert.Equal(5, rules.Otp.MaxPerPhonePerHour);
        Assert.Equal(20, rules.Otp.MaxPerIpPerHour);

        // 12. Subscription
        Assert.Equal(7, rules.Subscription.GraceDays);

        // 13. Agency
        Assert.Equal(5_000_000m, rules.Agency.MinEscrowBalance);

        // 14. Sla
        Assert.Equal(100.00m, rules.Sla.InitialScore);
        Assert.Equal(20m, rules.Sla.Penalty.NoShow);
        Assert.Equal(10m, rules.Sla.Penalty.Shortage);
        Assert.Equal(5m, rules.Sla.Penalty.QualityComplaint);
        Assert.Equal(70m, rules.Sla.WarnAtOrBelow);
        Assert.Equal(50m, rules.Sla.BlockPremiumBelow);
        Assert.Equal(48, rules.Sla.AppealHours);

        // 15. SuperFreelancer
        Assert.Equal(4.80m, rules.SuperFreelancer.MinRating);
        Assert.Equal(50, rules.SuperFreelancer.MinCompletedJobs);
        Assert.Equal(180, rules.SuperFreelancer.NoUpheldDisputeDays);
        Assert.Equal(4.70m, rules.SuperFreelancer.RevokeBelowRating);

        // 16. Premium
        Assert.Equal(4, rules.Premium.MinLeadHours);

        // 17. Rating
        Assert.Equal(48, rules.Rating.WindowHours);
        Assert.Equal(2, rules.Rating.LowStarThreshold);
        Assert.Equal(3, rules.Rating.ConsecutiveLowToFlag);
        Assert.Equal(4.00m, rules.Rating.MinAvgAfterJobs.Rating);
        Assert.Equal(10, rules.Rating.MinAvgAfterJobs.Jobs);

        // 18. Cancel
        Assert.Equal(2, rules.Cancel.FullRefundHoursBefore);
        Assert.Equal(0.40m, rules.Cancel.LateFeeRate);
        Assert.Equal(3, rules.Cancel.WorkerCancelLockCount);
        Assert.Equal(30, rules.Cancel.WorkerCancelWindowDays);

        // 19. Auth
        Assert.Equal(10, rules.Auth.MinPasswordLength);
        Assert.Equal(5, rules.Auth.LockoutFailures);
        Assert.Equal(15, rules.Auth.LockoutMinutes);
        Assert.Equal(15, rules.Auth.AccessTokenMinutes);
        Assert.Equal(30, rules.Auth.RefreshTokenDays);
        Assert.Equal(30, rules.Auth.RegistrationTokenMinutes);

        // 20. Privacy
        Assert.Equal(60, rules.Privacy.PhoneVisibleBeforeMinutes);
        Assert.Equal(60, rules.Privacy.PhoneVisibleAfterMinutes);

        // 21. Customer
        Assert.Equal(0.00m, rules.Customer.InitialTrustScore);

        // 22. Address
        Assert.Equal(30m, rules.Address.RoomMaxAreaM2);
        Assert.Equal(10, rules.Address.HouseMaxFloors);
    }

    [Fact]
    public void Class_defaults_match_decisions_section_4_for_every_key()
    {
        var rules = new BusinessRules();
        AssertDecisionsSection4Defaults(rules);
    }

    [Fact]
    public void Bound_from_empty_config_preserves_every_decisions_section_4_default()
    {
        var configuration = new ConfigurationBuilder().Build();
        var rules = new BusinessRules();
        configuration.GetSection(BusinessRules.SectionName).Bind(rules);
        AssertDecisionsSection4Defaults(rules);
    }

    [Fact]
    public void Bound_from_appsettings_json_equals_decisions_section_4_defaults()
    {
        var appsettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(appsettingsPath))
        {
            // Traverse up to find Backend directory
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "appsettings.json")))
            {
                dir = dir.Parent;
            }
            if (dir != null)
            {
                appsettingsPath = Path.Combine(dir.FullName, "appsettings.json");
            }
        }

        Assert.True(File.Exists(appsettingsPath), $"appsettings.json not found at {appsettingsPath}");

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(appsettingsPath, optional: false)
            .Build();

        var rules = new BusinessRules();
        configuration.GetSection(BusinessRules.SectionName).Bind(rules);

        AssertDecisionsSection4Defaults(rules);
    }

    [Fact]
    public void DI_registration_via_AddInfrastructure_resolves_BusinessRules_and_IOptions()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<BusinessRules>>().Value;
        var direct = provider.GetRequiredService<BusinessRules>();

        Assert.NotNull(options);
        Assert.NotNull(direct);
        Assert.Same(options, direct);
        AssertDecisionsSection4Defaults(options);
    }

    [Fact]
    public void Appsettings_contains_required_non_secret_placeholders()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "appsettings.json")))
        {
            dir = dir.Parent;
        }
        var appsettingsPath = Path.Combine(dir!.FullName, "appsettings.json");

        var config = new ConfigurationBuilder()
            .AddJsonFile(appsettingsPath, optional: false)
            .Build();

        // MoMo sandbox placeholders
        Assert.False(string.IsNullOrEmpty(config["MoMo:PartnerCode"]));
        Assert.False(string.IsNullOrEmpty(config["MoMo:AccessKey"]));
        Assert.False(string.IsNullOrEmpty(config["MoMo:SecretKey"]));
        Assert.False(string.IsNullOrEmpty(config["MoMo:Endpoint"]));

        // Otp placeholder
        Assert.Equal("Fake", config["Otp:Provider"]);
        Assert.False(string.IsNullOrEmpty(config["Otp:ApiKey"]));

        // Ekyc placeholder
        Assert.Equal("Fake", config["Ekyc:Provider"]);
        Assert.False(string.IsNullOrEmpty(config["Ekyc:ApiKey"]));

        // FileStorage placeholder
        Assert.Equal("Local", config["FileStorage:Provider"]);
        Assert.False(string.IsNullOrEmpty(config["FileStorage:BasePath"]));
    }
}
