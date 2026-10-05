using System.Security.Cryptography;
using System.Text;
using CommonService.Application.Common.Options;
using CommonService.Application.Features.Identity.Services;
using CommonService.Application.Interfaces.Ports;
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

/// <summary>
/// BE-M1-07: OTP lifecycle cases the BE-M1-01 tests do not cover: TTL boundary and expiry, single use,
/// invalidation after the attempt limit, role separation, keyed hash, and replacement by a newer code
/// (contract identity.md §2.1 / §2.2, decisions Q06, Q20). DB-backed against the local SQL Server; every row
/// a test creates is deleted afterwards.
/// </summary>
public class OtpLifecycleTests
{
    private const string ConnectionString =
        "Server=localhost;Database=ChoThueGiupViec;Integrated Security=True;TrustServerCertificate=True;";

    private const string HmacSecret = "Test_Hmac_Secret_At_Least_32_Characters_Long!";

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

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

    private sealed class TestClock(DateTime initialUtc) : IClock
    {
        public DateTime UtcNow { get; set; } = initialUtc;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => local.AddHours(-7);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(UtcNow));
    }

    private sealed class CapturingOtpSender : IOtpSender
    {
        public string? LastCode { get; private set; }

        public Task SendAsync(string phoneNumber, string code, CancellationToken cancellationToken = default)
        {
            LastCode = code;
            return Task.CompletedTask;
        }
    }

    /// <summary>One OTP service on a real context; removes the OTP, refresh-token, customer and worker rows of the phones it used.</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly List<string> _phones = [];

        public AppDbContext Db { get; } = CreateContext();
        public TestClock Clock { get; } = new(new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc));
        public CapturingOtpSender Sender { get; } = new();
        public OtpService Service { get; }

        public Fixture()
        {
            var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Otp:HmacSecret"] = HmacSecret,
                ["Jwt:Key"] = "Test_Jwt_Secret_Key_At_Least_32_Characters_Long!"
            }).Build();
            Service = new OtpService(Db, Sender, Clock, rules, config,
                new TokenService(rules, config), NullLogger<OtpService>.Instance);
        }

        public string NewPhone()
        {
            var phone = $"09{Random.Shared.Next(10000000, 99999999)}";
            _phones.Add(phone);
            return phone;
        }

        public static string NewIp() =>
            $"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}";

        /// <summary>Requests a code and returns the plain code the sender received.</summary>
        public async Task<string> RequestCodeAsync(string phone, string role)
        {
            var result = await Service.RequestOtpAsync(phone, role, NewIp());
            Assert.True(result.Success, result.ErrorMessage);
            return Sender.LastCode!;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var phone in _phones)
            {
                var customerIds = await Db.Customers.Where(c => c.PhoneNumber == phone).Select(c => c.CustomerId).ToListAsync();
                await Db.RefreshTokens
                    .Where(t => t.SubjectRole == UserRole.Customer && customerIds.Contains(t.SubjectId))
                    .ExecuteDeleteAsync();
                await Db.Customers.Where(c => c.PhoneNumber == phone).ExecuteDeleteAsync();
                await Db.OtpCodes.Where(o => o.PhoneNumber == phone).ExecuteDeleteAsync();
            }

            await Db.DisposeAsync();
        }
    }

    private static string WrongCodeFor(string correct) => correct == "000000" ? "111111" : "000000";

    // ---- TTL boundary and expiry ----

    [Fact]
    public async Task A_correct_code_is_accepted_exactly_at_the_end_of_its_ttl()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var phone = fx.NewPhone();
        var code = await fx.RequestCodeAsync(phone, "Customer");

        fx.Clock.UtcNow = fx.Clock.UtcNow.AddMinutes(5); // Otp.TtlMinutes = 5, expires_at == now

        var result = await fx.Service.VerifyOtpAsync(phone, "Customer", code);

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(result.AuthResult);
    }

    [Fact]
    public async Task A_correct_code_one_second_after_its_ttl_is_rejected_with_401()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var phone = fx.NewPhone();
        var code = await fx.RequestCodeAsync(phone, "Customer");

        fx.Clock.UtcNow = fx.Clock.UtcNow.AddMinutes(5).AddSeconds(1);

        var result = await fx.Service.VerifyOtpAsync(phone, "Customer", code);

        Assert.False(result.Success);
        Assert.Equal(401, result.StatusCode);
        Assert.Null(result.AuthResult);
    }

    [Fact]
    public async Task An_expired_code_is_consumed_and_stays_unusable_even_if_the_clock_goes_back()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var phone = fx.NewPhone();
        var code = await fx.RequestCodeAsync(phone, "Customer");
        var issuedAt = fx.Clock.UtcNow;
        fx.Clock.UtcNow = issuedAt.AddMinutes(6);
        await fx.Service.VerifyOtpAsync(phone, "Customer", code); // expired -> consumed

        fx.Clock.UtcNow = issuedAt.AddMinutes(1); // back inside the original TTL
        var again = await fx.Service.VerifyOtpAsync(phone, "Customer", code);

        Assert.Equal(401, again.StatusCode);
        await using var db = CreateContext();
        Assert.All(await db.OtpCodes.AsNoTracking().Where(o => o.PhoneNumber == phone).ToListAsync(),
            o => Assert.NotNull(o.ConsumedAt));
    }

    [Fact]
    public async Task Expiry_gives_the_same_401_as_a_wrong_code_so_the_response_gives_no_hint()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var expiredPhone = fx.NewPhone();
        var wrongPhone = fx.NewPhone();
        var expiredCode = await fx.RequestCodeAsync(expiredPhone, "Customer");
        var wrongCode = await fx.RequestCodeAsync(wrongPhone, "Customer");

        var wrong = await fx.Service.VerifyOtpAsync(wrongPhone, "Customer", WrongCodeFor(wrongCode));
        fx.Clock.UtcNow = fx.Clock.UtcNow.AddMinutes(10);
        var expired = await fx.Service.VerifyOtpAsync(expiredPhone, "Customer", expiredCode);

        Assert.Equal(401, wrong.StatusCode);
        Assert.Equal(401, expired.StatusCode);
        Assert.Equal(wrong.ErrorMessage, expired.ErrorMessage);
    }

    // ---- single use ----

    [Fact]
    public async Task A_verified_code_is_single_use()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var phone = fx.NewPhone();
        var code = await fx.RequestCodeAsync(phone, "Customer");

        var first = await fx.Service.VerifyOtpAsync(phone, "Customer", code);
        var replay = await fx.Service.VerifyOtpAsync(phone, "Customer", code);

        Assert.True(first.Success);
        Assert.Equal(401, replay.StatusCode);
        Assert.Null(replay.AuthResult);
    }

    [Fact]
    public async Task A_replayed_code_does_not_create_a_second_customer_or_refresh_token()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var phone = fx.NewPhone();
        var code = await fx.RequestCodeAsync(phone, "Customer");
        await fx.Service.VerifyOtpAsync(phone, "Customer", code);

        await fx.Service.VerifyOtpAsync(phone, "Customer", code);

        await using var db = CreateContext();
        var customer = await db.Customers.AsNoTracking().SingleAsync(c => c.PhoneNumber == phone);
        Assert.Equal(1, await db.RefreshTokens.CountAsync(t => t.SubjectRole == UserRole.Customer && t.SubjectId == customer.CustomerId));
    }

    // ---- attempt limit ----

    [Fact]
    public async Task After_the_attempt_limit_even_the_correct_code_is_rejected_until_a_new_one_is_requested()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var phone = fx.NewPhone();
        var code = await fx.RequestCodeAsync(phone, "Customer");
        var wrong = WrongCodeFor(code);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            Assert.Equal(401, (await fx.Service.VerifyOtpAsync(phone, "Customer", wrong)).StatusCode);
        }

        Assert.Equal(429, (await fx.Service.VerifyOtpAsync(phone, "Customer", wrong)).StatusCode); // Otp.MaxAttempts = 5

        var correctButLate = await fx.Service.VerifyOtpAsync(phone, "Customer", code);
        Assert.Equal(401, correctButLate.StatusCode);

        fx.Clock.UtcNow = fx.Clock.UtcNow.AddSeconds(61); // resend cooldown
        var freshCode = await fx.RequestCodeAsync(phone, "Customer");
        Assert.True((await fx.Service.VerifyOtpAsync(phone, "Customer", freshCode)).Success);
    }

    [Fact]
    public async Task The_limit_is_per_code_so_four_wrong_tries_leave_the_fifth_try_correct_code_valid()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var phone = fx.NewPhone();
        var code = await fx.RequestCodeAsync(phone, "Customer");

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await fx.Service.VerifyOtpAsync(phone, "Customer", WrongCodeFor(code));
        }

        var result = await fx.Service.VerifyOtpAsync(phone, "Customer", code);

        Assert.True(result.Success);
    }

    // ---- role separation ----

    [Fact]
    public async Task A_code_issued_for_Customer_does_not_verify_for_Worker_on_the_same_phone()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var phone = fx.NewPhone();
        var code = await fx.RequestCodeAsync(phone, "Customer");

        var asWorker = await fx.Service.VerifyOtpAsync(phone, "Worker", code);

        Assert.Equal(401, asWorker.StatusCode);
        Assert.Null(asWorker.AuthResult);
        Assert.Null(asWorker.RegistrationRequired);
        // and it did not burn the Customer code
        Assert.True((await fx.Service.VerifyOtpAsync(phone, "Customer", code)).Success);
    }

    [Fact]
    public async Task A_code_issued_for_Worker_does_not_verify_for_Customer_and_creates_no_customer()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var phone = fx.NewPhone();
        var code = await fx.RequestCodeAsync(phone, "Worker");

        var asCustomer = await fx.Service.VerifyOtpAsync(phone, "Customer", code);

        Assert.Equal(401, asCustomer.StatusCode);
        await using var db = CreateContext();
        Assert.False(await db.Customers.AnyAsync(c => c.PhoneNumber == phone));
    }

    // ---- keyed hash (decisions Q20) ----

    [Fact]
    public async Task The_stored_hash_is_a_keyed_HMAC_and_never_the_plain_code_or_an_unkeyed_digest()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var phone = fx.NewPhone();
        var code = await fx.RequestCodeAsync(phone, "Customer");

        await using var db = CreateContext();
        var stored = await db.OtpCodes.AsNoTracking().SingleAsync(o => o.PhoneNumber == phone);

        var plainSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(HmacSecret));
        var expectedHmac = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(code)));
        using var wrongKey = new HMACSHA256(Encoding.UTF8.GetBytes("another-secret-another-secret-another!"));
        var hmacWithOtherKey = Convert.ToHexString(wrongKey.ComputeHash(Encoding.UTF8.GetBytes(code)));

        Assert.NotEqual(code, stored.CodeHash);
        Assert.DoesNotContain(code, stored.CodeHash);
        Assert.False(string.Equals(plainSha256, stored.CodeHash, StringComparison.OrdinalIgnoreCase));
        Assert.False(string.Equals(hmacWithOtherKey, stored.CodeHash, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expectedHmac, stored.CodeHash, ignoreCase: true); // keyed with the configured secret
    }

    // ---- a newer code replaces the older one ----

    [Fact]
    public async Task A_new_request_invalidates_the_previous_code_and_only_the_new_one_verifies()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var phone = fx.NewPhone();
        var oldCode = await fx.RequestCodeAsync(phone, "Customer");

        fx.Clock.UtcNow = fx.Clock.UtcNow.AddSeconds(61); // resend cooldown
        var newCode = await fx.RequestCodeAsync(phone, "Customer");
        Assert.NotEqual(oldCode, newCode);

        var withOld = await fx.Service.VerifyOtpAsync(phone, "Customer", oldCode);
        var withNew = await fx.Service.VerifyOtpAsync(phone, "Customer", newCode);

        Assert.Equal(401, withOld.StatusCode);
        Assert.True(withNew.Success);
    }

    [Fact]
    public async Task Verifying_a_phone_that_never_requested_a_code_is_a_401_not_an_error()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var phone = fx.NewPhone();

        var result = await fx.Service.VerifyOtpAsync(phone, "Customer", "123456");

        Assert.Equal(401, result.StatusCode);
    }
}
