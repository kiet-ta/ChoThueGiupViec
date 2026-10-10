using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Tests.Ports;

public class FakePortTests
{
    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ServiceProvider BuildProvider(string environment = "Development", Action<IServiceCollection>? registerRealOnes = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestEnvironment(environment));
        registerRealOnes?.Invoke(services);
        services.AddFakePorts();
        return services.BuildServiceProvider();
    }

    // ---- registration -----------------------------------------------------------------------

    [Fact]
    public void Every_port_of_the_catalogue_resolves_to_a_Fake()
    {
        using var provider = BuildProvider();
        var ports = typeof(IClock).Assembly.GetExportedTypes()
            .Where(t => t.IsInterface && t.Namespace == typeof(IClock).Namespace)
            .ToList();

        Assert.Equal(18, ports.Count);
        foreach (var port in ports)
        {
            var instance = provider.GetRequiredService(port);
            Assert.StartsWith("Fake", instance.GetType().Name);
        }
    }

    [Fact]
    public void A_real_implementation_registered_before_AddFakePorts_wins()
    {
        using var provider = BuildProvider(registerRealOnes: s => s.AddSingleton<IClock, RealClock>());

        Assert.IsType<RealClock>(provider.GetRequiredService<IClock>());
        Assert.IsType<FakeGeoService>(provider.GetRequiredService<IGeoService>());
    }

    [Fact]
    public void A_module_registered_by_AddModules_wins_over_the_Fake()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestEnvironment("Development"));
        services.AddModules(new ConfigurationBuilder().Build(), typeof(RealClockModule));
        services.AddFakePorts();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<RealClock>(provider.GetRequiredService<IClock>());
    }

    private sealed class RealClock : IClock
    {
        public DateTime UtcNow => new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public DateOnly LocalToday => new(2030, 1, 1);
        public DateTime ToLocal(DateTime utc) => utc;
        public DateTime ToUtc(DateTime local) => local;
    }

    private sealed class RealClockModule : IModule
    {
        public string Name => "RealClockForTest";

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration) =>
            services.AddSingleton<IClock, RealClock>();
    }

    // ---- IOtpSender (decisions Q06) ---------------------------------------------------------

    [Fact]
    public async Task Fake_OtpSender_records_the_code_in_Development()
    {
        var sender = new FakeOtpSender(new TestEnvironment("Development"), NullLogger<FakeOtpSender>.Instance);

        await sender.SendAsync("0912345678", "123456");

        Assert.Equal([("0912345678", "123456")], sender.Sent);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Fake_OtpSender_refuses_to_be_created_outside_Development(string environment)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new FakeOtpSender(new TestEnvironment(environment), NullLogger<FakeOtpSender>.Instance));

        Assert.Contains("Development", ex.Message);
    }

    [Fact]
    public void Host_startup_fails_when_the_Fake_OtpSender_is_registered_outside_Development()
    {
        using var provider = BuildProvider("Production");

        Assert.Throws<InvalidOperationException>(() => provider.GetServices<IHostedService>().ToList());
    }

    [Fact]
    public void Host_startup_succeeds_in_Development()
    {
        using var provider = BuildProvider("Development");

        Assert.Contains(provider.GetServices<IHostedService>(), s => s is OtpSenderStartupGuard);
    }

    // ---- IClock / ICurrentUser / IGeoService ------------------------------------------------

    [Fact]
    public void Clock_converts_between_UTC_and_Vietnam_local_time()
    {
        var clock = new FakeClock();
        var utc = new DateTime(2026, 10, 5, 1, 30, 0, DateTimeKind.Utc);

        var local = clock.ToLocal(utc);

        Assert.Equal(new DateTime(2026, 10, 5, 8, 30, 0), local);
        Assert.Equal(utc, clock.ToUtc(local));
        Assert.Equal(DateTimeKind.Utc, clock.ToUtc(local).Kind);
    }

    [Fact]
    public void Clock_can_be_pinned_and_advanced()
    {
        var clock = new FakeClock();
        clock.Set(new DateTime(2026, 10, 5, 17, 0, 0, DateTimeKind.Utc));

        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(new DateTime(2026, 10, 5, 17, 0, 30, DateTimeKind.Utc), clock.UtcNow);
        Assert.Equal(new DateOnly(2026, 10, 6), clock.LocalToday);
    }

    [Fact]
    public void CurrentUser_is_anonymous_until_signed_in()
    {
        var user = new FakeCurrentUser();
        Assert.False(user.IsAuthenticated);

        user.SignIn(7, UserRole.Customer);

        Assert.True(user.IsAuthenticated);
        Assert.Equal(7, user.UserId);
        Assert.Equal(UserRole.Customer, user.Role);
    }

    [Fact]
    public void Geo_distance_matches_a_known_value_and_zero_for_the_same_point()
    {
        var geo = new FakeGeoService();
        var hcm = new GeoPoint(10.7769, 106.7009);
        var hanoi = new GeoPoint(21.0285, 105.8542);

        Assert.Equal(0d, geo.DistanceMeters(hcm, hcm), 3);
        Assert.InRange(geo.DistanceMeters(hcm, hanoi) / 1000d, 1135d, 1155d);
    }

    // ---- IFileStorage -----------------------------------------------------------------------

    [Fact]
    public async Task FileStorage_round_trips_a_file_and_delete_is_idempotent()
    {
        var storage = new FakeFileStorage();

        var stored = await storage.SaveAsync("photos", "before.jpg", new MemoryStream([1, 2, 3]), "image/jpeg");

        Assert.Equal(3, stored.SizeBytes);
        using (var read = await storage.OpenReadAsync(stored.Path))
        {
            Assert.NotNull(read);
            var buffer = new MemoryStream();
            await read!.CopyToAsync(buffer);
            Assert.Equal([1, 2, 3], buffer.ToArray());
        }

        await storage.DeleteAsync(stored.Path);
        await storage.DeleteAsync(stored.Path);
        Assert.Null(await storage.OpenReadAsync(stored.Path));
    }

    // ---- IPasswordHasher / INotificationService / IAuditLog ----------------------------------

    [Fact]
    public void PasswordHasher_verifies_the_right_password_only()
    {
        var hasher = new FakePasswordHasher();

        var hash = hasher.Hash("Admin@123456");

        Assert.True(hasher.Verify("Admin@123456", hash));
        Assert.False(hasher.Verify("wrong", hash));
        Assert.False(hasher.Verify("Admin@123456", "not-a-hash"));
    }

    [Fact]
    public async Task NotificationService_collects_messages()
    {
        var notifications = new FakeNotificationService();
        var message = new NotificationMessage(UserRole.Customer, 5, "payment.status", "Paid", "Your order is paid");

        await notifications.SendAsync(message);

        Assert.Equal([message], notifications.Sent);
    }

    [Fact]
    public async Task AuditLog_is_append_only_in_order()
    {
        var audit = new FakeAuditLog();

        await audit.WriteAsync(new AuditEntry(AuditActorType.Admin, 1, "PRICE_RULE", "1", "unit_price", "160000", "170000", "inflation"));
        await audit.WriteAsync(new AuditEntry(AuditActorType.System, null, "WORKER", "9", "is_super_freelancer", "1", "0", null));

        Assert.Equal(["PRICE_RULE", "WORKER"], audit.Entries.Select(e => e.EntityType));
    }

    // ---- IPaymentGateway / IRefundService ---------------------------------------------------

    [Fact]
    public async Task PaymentGateway_creates_a_QR_and_reports_it_paid_after_reconciliation()
    {
        var gateway = new FakePaymentGateway();
        var qr = await gateway.CreateQrAsync(new CreatePaymentRequest(PaymentPurpose.Order, "42", 260000m, "Order 42", DateTime.UtcNow.AddMinutes(15)));

        Assert.Equal(PaymentStatus.Pending, (await gateway.QueryStatusAsync(qr.GatewayTxnRef)).Status);
        gateway.MarkPaid(qr.GatewayTxnRef);

        var status = await gateway.QueryStatusAsync(qr.GatewayTxnRef);
        Assert.Equal(PaymentStatus.Success, status.Status);
        Assert.Equal(260000m, status.Amount);
    }

    [Fact]
    public async Task PaymentGateway_rejects_a_bad_IPN_signature_and_accepts_a_good_one()
    {
        var gateway = new FakePaymentGateway();

        var bad = await gateway.VerifyIpnAsync(new Dictionary<string, string> { ["signature"] = "forged", ["gatewayTxnRef"] = "X", ["status"] = "success" });
        var good = await gateway.VerifyIpnAsync(new Dictionary<string, string> { ["signature"] = "valid", ["gatewayTxnRef"] = "X", ["amount"] = "100", ["status"] = "success" });

        Assert.False(bad.IsSignatureValid);
        Assert.True(good.IsSignatureValid);
        Assert.Equal("X", good.GatewayTxnRef);
        Assert.Equal(PaymentStatus.Success, good.Status);
    }

    [Fact]
    public async Task PaymentGateway_refund_can_report_that_the_gateway_has_no_refund_api()
    {
        var gateway = new FakePaymentGateway { RefundSupported = false };

        var result = await gateway.RefundAsync("X", 100m, "no worker");

        Assert.False(result.Succeeded);
        Assert.False(result.GatewaySupported);
    }

    [Fact]
    public async Task RefundService_returns_the_requested_amount_and_keeps_the_request()
    {
        var refunds = new FakeRefundService();

        var result = await refunds.RefundAsync(new RefundRequest(42, 260000m, "no worker within 10 km"));

        Assert.True(result.Succeeded);
        Assert.Equal(260000m, result.RefundedAmount);
        Assert.Single(refunds.Requests);
    }

    // ---- IAgencyCapacityService / ISlaPenaltyService ----------------------------------------

    [Fact]
    public async Task AgencyCapacity_hold_is_atomic_and_release_gives_the_slots_back()
    {
        var capacity = new FakeAgencyCapacityService { RemainingSlots = 2 };
        var request = new CapacityRequest(new DateOnly(2026, 10, 6), "SHIFT_1", 2, OrderId: 1);

        var first = await capacity.TryReserveAsync(request);
        var second = await capacity.TryReserveAsync(request);

        Assert.NotNull(first);
        Assert.Equal(2, first!.SlotIds.Count);
        Assert.Null(second);
        Assert.False(await capacity.HasCapacityAsync(request));

        await capacity.ReleaseAsync(first.ReservationId);
        Assert.True(await capacity.HasCapacityAsync(request));
    }

    [Fact]
    public async Task SlaPenalty_applies_the_points_of_decisions_Q09_and_accumulates()
    {
        var sla = new FakeSlaPenaltyService();

        var noShow = await sla.ApplyAsync(new SlaPenaltyRequest(3, SlaViolation.NoShow, 42, null, 260000m, 150000m, "agency worker absent"));
        var shortage = await sla.ApplyAsync(new SlaPenaltyRequest(3, SlaViolation.Shortage, 43, null, 0m, 0m, "no staff"));

        Assert.Equal(-20m, noShow.SlaPointsDelta);
        Assert.Equal(80m, noShow.NewSlaScore);
        Assert.Equal(410000m, noShow.EscrowDeducted);
        Assert.Equal(70m, shortage.NewSlaScore);
    }

    // ---- worker queries ----------------------------------------------------------------------

    [Fact]
    public async Task WorkerAvailability_filters_by_radius_and_orders_by_distance()
    {
        var query = new FakeWorkerAvailabilityQuery();
        query.Workers.AddRange([new AvailableWorker(1, 10, 6.5), new AvailableWorker(2, 11, 2.0), new AvailableWorker(3, 12, 4.0)]);

        var found = await query.FindAvailableFreelancersAsync(new AvailabilityQuery(new DateOnly(2026, 10, 6), "SHIFT_1", new GeoPoint(10.77, 106.70), 5, 10));

        Assert.Equal([2, 3], found.Select(w => w.WorkerId));
    }

    [Fact]
    public async Task WorkerReputation_returns_null_for_an_unknown_worker()
    {
        var reputation = new FakeWorkerReputation();
        reputation.Set(new WorkerReputationDto(1, 4.9m, 60, 0.97m));

        Assert.Equal(4.9m, (await reputation.GetAsync(1))!.RatingAvg);
        Assert.Null(await reputation.GetAsync(2));
    }

    [Fact]
    public async Task WorkerProfileQuery_gets_one_many_and_checks_existence()
    {
        var profiles = new FakeWorkerProfileQuery();
        profiles.Add(new WorkerProfileSummary(5, "Nguyen Van A", 4.8m, 51, WorkStatus.Idle));

        Assert.True(await profiles.ExistsAsync(5));
        Assert.False(await profiles.ExistsAsync(6));
        Assert.Equal("Nguyen Van A", (await profiles.GetAsync(5))!.FullName);
        var many = await profiles.GetManyAsync([5, 6, 5]);
        Assert.Equal([5], many.Keys);
    }

    // ---- Customer address (Booking B2) ---------------------------------------------------------

    [Fact]
    public async Task CustomerAddressQuery_returns_only_an_address_owned_by_the_customer()
    {
        var addresses = new FakeCustomerAddressQuery();
        addresses.Add(7, new CustomerAddressInfo(3, 55.5m, 10.76m, 106.66m));

        Assert.Equal(55.5m, (await addresses.GetOwnedAsync(7, 3))!.TotalAreaM2);
        Assert.Null(await addresses.GetOwnedAsync(8, 3));
        Assert.Null(await addresses.GetOwnedAsync(7, 4));
    }

    [Fact]
    public async Task AddFakePorts_registers_a_customer_address_query()
    {
        var services = new ServiceCollection();
        services.AddFakePorts();
        await using var provider = services.BuildServiceProvider();

        Assert.IsType<FakeCustomerAddressQuery>(provider.GetRequiredService<ICustomerAddressQuery>());
    }

    // ---- eKYC / image quality ----------------------------------------------------------------

    [Fact]
    public async Task EkycProvider_defaults_to_92_confidence_per_decisions_Q05()
    {
        var ekyc = new FakeEkycProvider();

        var result = await ekyc.VerifyAsync(new EkycRequest("079123456789", "ids/front.jpg", "ids/selfie.jpg"));

        Assert.Equal(92.00m, result.Confidence);
        Assert.False(result.FraudFlag);
        Assert.Equal("079123456789", result.ExtractedNationalId);
    }

    [Fact]
    public async Task ImageQuality_accepts_at_or_above_the_threshold_and_rejects_below()
    {
        var quality = new FakeImageQualityService { VolScore = 100.0 };
        Assert.True((await quality.AssessAsync(Stream.Null)).IsAccepted);

        quality.VolScore = 99.9;
        Assert.False((await quality.AssessAsync(Stream.Null)).IsAccepted);
    }
}
