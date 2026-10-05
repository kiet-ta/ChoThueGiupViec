using CommonService.Application.Common.Options;
using CommonService.Application.Features.Identity.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Identity;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CommonService.Tests.Identity;

public class OtpServiceTests
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

    private sealed class TestClock(DateTime initialUtc) : IClock
    {
        public DateTime UtcNow { get; set; } = initialUtc;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => local.AddHours(-7);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(UtcNow));
    }

    private sealed class TestOtpSender : IOtpSender
    {
        public string? LastPhone { get; private set; }
        public string? LastCode { get; private set; }
        public int SendCount { get; private set; }

        public Task SendAsync(string phoneNumber, string code, CancellationToken cancellationToken = default)
        {
            LastPhone = phoneNumber;
            LastCode = code;
            SendCount++;
            return Task.CompletedTask;
        }
    }

    private static (OtpService service, AppDbContext db, TestClock clock, TestOtpSender sender) CreateService(
        DateTime? initialTime = null)
    {
        var db = CreateContext();
        var clock = new TestClock(initialTime ?? new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc));
        var sender = new TestOtpSender();
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["Otp:HmacSecret"] = "Test_Hmac_Secret_At_Least_32_Characters_Long!"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();
        var tokenService = new TokenService(rules, config);
        var service = new OtpService(db, sender, clock, rules, config, tokenService, NullLogger<OtpService>.Instance);

        return (service, db, clock, sender);
    }

    private static string GenerateTestPhone() => $"091{Random.Shared.Next(1000000, 9999999)}";
    private static string GenerateTestIp() => $"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}";

    [Fact]
    public async Task RequestOtp_with_invalid_phone_returns_validation_error()
    {
        if (!IsSqlServerAvailable()) return;

        var (service, db, _, _) = CreateService();
        await using var _ = db;

        var result = await service.RequestOtpAsync("0241234567", "Customer", GenerateTestIp());

        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);
        Assert.NotNull(result.ValidationErrors);
        Assert.True(result.ValidationErrors.ContainsKey("phoneNumber"));
    }

    [Fact]
    public async Task RequestOtp_with_invalid_role_returns_validation_error()
    {
        if (!IsSqlServerAvailable()) return;

        var (service, db, _, _) = CreateService();
        await using var _ = db;

        var phone = GenerateTestPhone();
        var result = await service.RequestOtpAsync(phone, "Admin", GenerateTestIp());

        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);
        Assert.NotNull(result.ValidationErrors);
        Assert.True(result.ValidationErrors.ContainsKey("role"));
    }

    [Fact]
    public async Task RequestOtp_with_valid_input_sends_code_and_persists_in_db()
    {
        if (!IsSqlServerAvailable()) return;

        var (service, db, clock, sender) = CreateService();
        await using var _ = db;

        var phone = GenerateTestPhone();
        var result = await service.RequestOtpAsync(phone, "Customer", GenerateTestIp());

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.Equal(300, result.Data.ExpiresInSeconds);
        Assert.Equal(60, result.Data.ResendAvailableInSeconds);

        Assert.Equal(phone, sender.LastPhone);
        Assert.NotNull(sender.LastCode);
        Assert.Equal(6, sender.LastCode.Length);

        var savedOtp = await db.OtpCodes
            .FirstOrDefaultAsync(x => x.PhoneNumber == phone && x.Role == UserRole.Customer && x.ConsumedAt == null);

        Assert.NotNull(savedOtp);
        Assert.Equal(0, savedOtp.AttemptCount);
        Assert.Equal(clock.UtcNow, savedOtp.CreatedAt);
        Assert.Equal(clock.UtcNow.AddMinutes(5), savedOtp.ExpiresAt);
    }

    [Fact]
    public async Task RequestOtp_within_cooldown_returns_rate_limited_with_retry_after()
    {
        if (!IsSqlServerAvailable()) return;

        var (service, db, clock, _) = CreateService();
        await using var _ = db;

        var phone = GenerateTestPhone();
        var ip = GenerateTestIp();
        var first = await service.RequestOtpAsync(phone, "Customer", ip);
        Assert.True(first.Success);

        // Advance only 30 seconds (cooldown is 60s)
        clock.UtcNow = clock.UtcNow.AddSeconds(30);

        var second = await service.RequestOtpAsync(phone, "Customer", ip);
        Assert.False(second.Success);
        Assert.Equal(429, second.StatusCode);
        Assert.NotNull(second.RetryAfterSeconds);
        Assert.True(second.RetryAfterSeconds > 0 && second.RetryAfterSeconds <= 30);
    }

    [Fact]
    public async Task RequestOtp_exceeding_max_per_phone_per_hour_returns_rate_limited()
    {
        if (!IsSqlServerAvailable()) return;

        var (service, db, clock, _) = CreateService();
        await using var _ = db;

        var phone = GenerateTestPhone();

        // 5 requests spaced by 65 seconds (satisfying cooldown)
        for (var i = 0; i < 5; i++)
        {
            var res = await service.RequestOtpAsync(phone, "Customer", $"10.0.0.{i + 1}");
            Assert.True(res.Success);
            clock.UtcNow = clock.UtcNow.AddSeconds(65);
        }

        // 6th request within the same 1 hour window
        var sixth = await service.RequestOtpAsync(phone, "Customer", "10.0.0.99");
        Assert.False(sixth.Success);
        Assert.Equal(429, sixth.StatusCode);
        Assert.NotNull(sixth.RetryAfterSeconds);
        Assert.True(sixth.RetryAfterSeconds > 0);
    }

    [Fact]
    public async Task RequestOtp_exceeding_max_per_ip_per_hour_returns_rate_limited()
    {
        if (!IsSqlServerAvailable()) return;

        var (service, db, clock, _) = CreateService();
        await using var _ = db;

        var ip = $"192.168.1.{Random.Shared.Next(10, 200)}";

        // 20 requests from the same IP using different phones
        for (var i = 0; i < 20; i++)
        {
            var phone = GenerateTestPhone();
            var res = await service.RequestOtpAsync(phone, "Customer", ip);
            Assert.True(res.Success);
            clock.UtcNow = clock.UtcNow.AddSeconds(5);
        }

        // 21st request from same IP
        var nextPhone = GenerateTestPhone();
        var rejected = await service.RequestOtpAsync(nextPhone, "Customer", ip);
        Assert.False(rejected.Success);
        Assert.Equal(429, rejected.StatusCode);
        Assert.NotNull(rejected.RetryAfterSeconds);
    }

    [Fact]
    public async Task RequestOtp_invalidates_previous_unconsumed_codes_for_same_phone_and_role()
    {
        if (!IsSqlServerAvailable()) return;

        var (service, db, clock, sender) = CreateService();
        await using var _ = db;

        var phone = GenerateTestPhone();
        var ip = GenerateTestIp();
        var first = await service.RequestOtpAsync(phone, "Customer", ip);
        Assert.True(first.Success);
        var firstCode = sender.LastCode!;

        // Advance past cooldown
        clock.UtcNow = clock.UtcNow.AddSeconds(65);

        var second = await service.RequestOtpAsync(phone, "Customer", ip);
        Assert.True(second.Success);
        var secondCode = sender.LastCode!;

        // First code should now be invalid/consumed
        var verifyFirst = await service.VerifyOtpAsync(phone, "Customer", firstCode);
        Assert.False(verifyFirst.Success);
        Assert.Equal(401, verifyFirst.StatusCode);

        // Second code should verify successfully
        var verifySecond = await service.VerifyOtpAsync(phone, "Customer", secondCode);
        Assert.True(verifySecond.Success);
        Assert.Equal(200, verifySecond.StatusCode);
    }

    [Fact]
    public async Task VerifyOtp_wrong_code_increments_attempts_and_returns_unauthorized()
    {
        if (!IsSqlServerAvailable()) return;

        var (service, db, _, _) = CreateService();
        await using var _ = db;

        var phone = GenerateTestPhone();
        await service.RequestOtpAsync(phone, "Customer", GenerateTestIp());

        var verify = await service.VerifyOtpAsync(phone, "Customer", "000000");

        Assert.False(verify.Success);
        Assert.Equal(401, verify.StatusCode);

        var otpInDb = await db.OtpCodes
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(x => x.PhoneNumber == phone);

        Assert.NotNull(otpInDb);
        Assert.Equal(1, otpInDb.AttemptCount);
    }

    [Fact]
    public async Task VerifyOtp_fifth_wrong_attempt_invalidates_code_and_returns_429()
    {
        if (!IsSqlServerAvailable()) return;

        var (service, db, _, _) = CreateService();
        await using var _ = db;

        var phone = GenerateTestPhone();
        await service.RequestOtpAsync(phone, "Customer", GenerateTestIp());

        for (var i = 0; i < 4; i++)
        {
            var res = await service.VerifyOtpAsync(phone, "Customer", "000000");
            Assert.Equal(401, res.StatusCode);
        }

        // 5th failed try
        var fifth = await service.VerifyOtpAsync(phone, "Customer", "000000");
        Assert.Equal(429, fifth.StatusCode);
        Assert.NotNull(fifth.RetryAfterSeconds);

        var otpInDb = await db.OtpCodes
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(x => x.PhoneNumber == phone);

        Assert.NotNull(otpInDb);
        Assert.Equal(5, otpInDb.AttemptCount);
        Assert.NotNull(otpInDb.ConsumedAt);
    }

    [Fact]
    public async Task VerifyOtp_customer_first_login_creates_new_customer_with_isNewUser_true()
    {
        if (!IsSqlServerAvailable()) return;

        var (service, db, clock, sender) = CreateService();
        await using var _ = db;

        var phone = GenerateTestPhone();
        await service.RequestOtpAsync(phone, "Customer", GenerateTestIp());
        var code = sender.LastCode!;

        var verify = await service.VerifyOtpAsync(phone, "Customer", code);

        Assert.True(verify.Success);
        Assert.Equal(200, verify.StatusCode);
        Assert.NotNull(verify.AuthResult);
        Assert.True(verify.AuthResult.User.IsNewUser);
        Assert.Equal("Customer", verify.AuthResult.User.Role);
        Assert.True(verify.AuthResult.User.Id > 0);
        Assert.NotEmpty(verify.AuthResult.AccessToken);
        Assert.NotEmpty(verify.AuthResult.RefreshToken);

        var createdCustomer = await db.Customers.FirstOrDefaultAsync(c => c.PhoneNumber == phone);
        Assert.NotNull(createdCustomer);
        Assert.Equal(CustomerAccountStatus.Active, createdCustomer.AccountStatus);
        Assert.Equal(clock.UtcNow, createdCustomer.OtpVerifiedAt);
        Assert.Equal(0.00m, createdCustomer.TrustScore);
    }

    [Fact]
    public async Task VerifyOtp_customer_subsequent_login_returns_isNewUser_false()
    {
        if (!IsSqlServerAvailable()) return;

        var (service, db, clock, sender) = CreateService();
        await using var _ = db;

        var phone = GenerateTestPhone();
        var ip = GenerateTestIp();
        await service.RequestOtpAsync(phone, "Customer", ip);
        await service.VerifyOtpAsync(phone, "Customer", sender.LastCode!);

        // Second login later
        clock.UtcNow = clock.UtcNow.AddHours(2);
        await service.RequestOtpAsync(phone, "Customer", ip);
        var secondCode = sender.LastCode!;

        var secondVerify = await service.VerifyOtpAsync(phone, "Customer", secondCode);

        Assert.True(secondVerify.Success);
        Assert.NotNull(secondVerify.AuthResult);
        Assert.False(secondVerify.AuthResult.User.IsNewUser);
        Assert.Equal(clock.UtcNow, (await db.Customers.FirstAsync(c => c.PhoneNumber == phone)).OtpVerifiedAt);
    }

    [Fact]
    public async Task VerifyOtp_customer_locked_returns_403_forbidden()
    {
        if (!IsSqlServerAvailable()) return;

        var (service, db, _, sender) = CreateService();
        await using var _ = db;

        var phone = GenerateTestPhone();
        var customer = new Customer
        {
            PhoneNumber = phone,
            FullName = "Locked User",
            AccountStatus = CustomerAccountStatus.Locked,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        await service.RequestOtpAsync(phone, "Customer", GenerateTestIp());
        var verify = await service.VerifyOtpAsync(phone, "Customer", sender.LastCode!);

        Assert.False(verify.Success);
        Assert.Equal(403, verify.StatusCode);
    }

    [Fact]
    public async Task VerifyOtp_worker_without_profile_returns_registration_required()
    {
        if (!IsSqlServerAvailable()) return;

        var (service, db, _, sender) = CreateService();
        await using var _ = db;

        var phone = GenerateTestPhone();
        await service.RequestOtpAsync(phone, "Worker", GenerateTestIp());
        var code = sender.LastCode!;

        var verify = await service.VerifyOtpAsync(phone, "Worker", code);

        Assert.True(verify.Success);
        Assert.Equal(200, verify.StatusCode);
        Assert.NotNull(verify.RegistrationRequired);
        Assert.True(verify.RegistrationRequired.IsNewUser);
        Assert.NotEmpty(verify.RegistrationRequired.RegistrationToken);
        Assert.Equal(1800, verify.RegistrationRequired.RegistrationTokenExpiresInSeconds);
    }

    [Fact]
    public async Task VerifyOtp_worker_with_existing_profile_returns_auth_result()
    {
        if (!IsSqlServerAvailable()) return;

        var (service, db, _, sender) = CreateService();
        await using var _ = db;

        var phone = GenerateTestPhone();
        var nationalId = Random.Shared.NextInt64(100000000000, 999999999999).ToString();
        var worker = Worker.CreateFreelancer(phone, nationalId, "Nguyen Van Tho");
        worker.CreatedAt = DateTime.UtcNow;
        worker.UpdatedAt = DateTime.UtcNow;
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        await service.RequestOtpAsync(phone, "Worker", GenerateTestIp());
        var code = sender.LastCode!;

        var verify = await service.VerifyOtpAsync(phone, "Worker", code);

        Assert.True(verify.Success);
        Assert.NotNull(verify.AuthResult);
        Assert.False(verify.AuthResult.User.IsNewUser);
        Assert.Equal("Worker", verify.AuthResult.User.Role);
        Assert.Equal(worker.WorkerId, verify.AuthResult.User.Id);
    }
}
