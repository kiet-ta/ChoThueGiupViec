using CommonService.Application.Common.Models;
using CommonService.Application.Common.Options;
using CommonService.Application.Features.Identity.Dtos;
using CommonService.Application.Features.Identity.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Identity;
using CommonService.Infrastructure.Persistence;
using CommonService.WebAPI.Controllers.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CommonService.Tests.Identity;

/// <summary>
/// BE-M1-03: Admin / Partner email + password login (contract identity.md §2.3, decisions Q16, SC-1, SC-8).
/// DB-backed tests run against the local SQL Server of BASE-07 and delete the rows they create.
/// </summary>
public class PasswordLoginTests
{
    private const string ConnectionString =
        "Server=localhost;Database=ChoThueGiupViec;Integrated Security=True;TrustServerCertificate=True;";

    private const string GoodPassword = "Correct@Pass1";
    private const string WrongPassword = "Wrong@Pass999";

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

    /// <summary>Owns one service graph plus the rows a test created, and removes them on dispose.</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly List<string> _adminEmails = [];
        private readonly List<string> _partnerEmails = [];

        public AppDbContext Db { get; } = CreateContext();
        public TestClock Clock { get; } = new(new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc));
        public Pbkdf2PasswordHasher Hasher { get; } = new();
        public PasswordLoginService Service { get; }

        public Fixture()
        {
            var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "Test_Jwt_Secret_Key_At_Least_32_Characters_Long!"
            }).Build();
            var tokenService = new TokenService(rules, config);
            Service = new PasswordLoginService(
                Db, Hasher, Clock, rules, tokenService, NullLogger<PasswordLoginService>.Instance);
        }

        public async Task<AdminAccount> AddAdminAsync(bool isActive = true)
        {
            var admin = new AdminAccount
            {
                Email = $"pw-admin-{Guid.NewGuid():N}@test.local",
                FullName = "Test Admin",
                PasswordHash = Hasher.Hash(GoodPassword),
                AdminRole = "SUPER_ADMIN",
                IsActive = isActive,
                CreatedAt = Clock.UtcNow
            };
            Db.Admins.Add(admin);
            await Db.SaveChangesAsync();
            _adminEmails.Add(admin.Email);
            return admin;
        }

        public async Task<PartnerAgency> AddPartnerAsync(string status = "ACTIVE", bool withPassword = true)
        {
            var agency = new PartnerAgency
            {
                TaxCode = Random.Shared.NextInt64(1_000_000_000, 9_999_999_999).ToString(),
                LegalName = "Test Agency",
                LegalRepresentative = "Test Rep",
                ContactPhone = "0901234567",
                ContactEmail = $"pw-partner-{Guid.NewGuid():N}@test.local",
                SlaScore = 100m,
                WorkerQuota = 3,
                BankAccountNo = "000111222",
                BankName = "Test Bank",
                AgencyStatus = status,
                CreatedAt = Clock.UtcNow,
                PasswordHash = withPassword ? Hasher.Hash(GoodPassword) : null
            };
            Db.PartnerAgencies.Add(agency);
            await Db.SaveChangesAsync();
            _partnerEmails.Add(agency.ContactEmail);
            return agency;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var email in _adminEmails)
            {
                var ids = await Db.Admins.Where(a => a.Email == email).Select(a => a.AdminId).ToListAsync();
                await Db.RefreshTokens
                    .Where(t => t.SubjectRole == UserRole.Admin && ids.Contains(t.SubjectId))
                    .ExecuteDeleteAsync();
                await Db.Admins.Where(a => a.Email == email).ExecuteDeleteAsync();
            }

            foreach (var email in _partnerEmails)
            {
                var ids = await Db.PartnerAgencies.Where(a => a.ContactEmail == email).Select(a => a.AgencyId).ToListAsync();
                await Db.RefreshTokens
                    .Where(t => t.SubjectRole == UserRole.Partner && ids.Contains(t.SubjectId))
                    .ExecuteDeleteAsync();
                await Db.PartnerAgencies.Where(a => a.ContactEmail == email).ExecuteDeleteAsync();
            }

            await Db.DisposeAsync();
        }
    }

    // ---- validation (no database needed: returns before any query) ----

    [Fact]
    public async Task Login_rejects_invalid_email_empty_password_and_wrong_role_with_400()
    {
        await using var fx = new Fixture();

        var result = await fx.Service.LoginAsync("not-an-email", "", "Customer");

        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);
        Assert.NotNull(result.ValidationErrors);
        Assert.Contains("email", result.ValidationErrors!.Keys);
        Assert.Contains("password", result.ValidationErrors.Keys);
        Assert.Contains("role", result.ValidationErrors.Keys);
    }

    [Theory]
    [InlineData("Worker")]
    [InlineData("Customer")]
    [InlineData("")]
    [InlineData("Root")]
    public async Task Login_rejects_roles_other_than_Admin_and_Partner(string role)
    {
        await using var fx = new Fixture();

        var result = await fx.Service.LoginAsync("someone@test.local", GoodPassword, role);

        Assert.Equal(400, result.StatusCode);
        Assert.Contains("role", result.ValidationErrors!.Keys);
    }

    // ---- Admin ----

    [Fact]
    public async Task Admin_login_succeeds_and_persists_refresh_token_family()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var admin = await fx.AddAdminAsync();

        var result = await fx.Service.LoginAsync(admin.Email, GoodPassword, "Admin");

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        var data = result.Data!;
        Assert.Equal("Bearer", data.TokenType);
        Assert.False(string.IsNullOrEmpty(data.AccessToken));
        Assert.False(string.IsNullOrEmpty(data.RefreshToken));
        Assert.Equal(900, data.AccessTokenExpiresInSeconds);
        Assert.Equal(admin.AdminId, data.User.Id);
        Assert.Equal("Admin", data.User.Role);
        Assert.False(data.User.IsNewUser);

        await using var check = CreateContext();
        var stored = await check.RefreshTokens
            .Where(t => t.SubjectRole == UserRole.Admin && t.SubjectId == admin.AdminId)
            .ToListAsync();
        var row = Assert.Single(stored);
        Assert.NotEqual(Guid.Empty, row.FamilyId);
        Assert.NotEqual(data.RefreshToken, row.TokenHash); // only the hash is stored (SC-7)
        Assert.Null(row.RevokedAt);
    }

    [Fact]
    public async Task Admin_email_lookup_ignores_surrounding_whitespace()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var admin = await fx.AddAdminAsync();

        var result = await fx.Service.LoginAsync($"  {admin.Email}  ", GoodPassword, "admin");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Unknown_email_and_wrong_password_return_the_same_401()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var admin = await fx.AddAdminAsync();

        var unknown = await fx.Service.LoginAsync($"nobody-{Guid.NewGuid():N}@test.local", GoodPassword, "Admin");
        var wrong = await fx.Service.LoginAsync(admin.Email, WrongPassword, "Admin");

        Assert.Equal(401, unknown.StatusCode);
        Assert.Equal(401, wrong.StatusCode);
        Assert.Equal(unknown.ErrorMessage, wrong.ErrorMessage);
    }

    [Fact]
    public async Task Inactive_admin_with_correct_password_gets_403_and_no_token()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var admin = await fx.AddAdminAsync(isActive: false);

        var result = await fx.Service.LoginAsync(admin.Email, GoodPassword, "Admin");

        Assert.Equal(403, result.StatusCode);
        Assert.Null(result.Data);
        await using var check = CreateContext();
        Assert.False(await check.RefreshTokens.AnyAsync(t => t.SubjectRole == UserRole.Admin && t.SubjectId == admin.AdminId));
    }

    [Fact]
    public async Task Inactive_admin_with_wrong_password_gets_401_not_403()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var admin = await fx.AddAdminAsync(isActive: false);

        var result = await fx.Service.LoginAsync(admin.Email, WrongPassword, "Admin");

        Assert.Equal(401, result.StatusCode); // disabled state is not revealed without the password
    }

    [Fact]
    public async Task Admin_is_not_found_through_the_Partner_role()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var admin = await fx.AddAdminAsync();

        var result = await fx.Service.LoginAsync(admin.Email, GoodPassword, "Partner");

        Assert.Equal(401, result.StatusCode);
    }

    // ---- Partner ----

    [Fact]
    public async Task Partner_login_succeeds_with_contact_email()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var agency = await fx.AddPartnerAsync();

        var result = await fx.Service.LoginAsync(agency.ContactEmail, GoodPassword, "Partner");

        Assert.True(result.Success);
        Assert.Equal(agency.AgencyId, result.Data!.User.Id);
        Assert.Equal("Partner", result.Data.User.Role);
        Assert.False(result.Data.User.IsNewUser);
    }

    [Fact]
    public async Task Suspended_partner_can_still_log_in()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var agency = await fx.AddPartnerAsync(status: "SUSPENDED");

        var result = await fx.Service.LoginAsync(agency.ContactEmail, GoodPassword, "Partner");

        Assert.True(result.Success); // decisions Q09 / O6
    }

    [Fact]
    public async Task Partner_without_password_hash_gets_401_and_is_not_counted_toward_lockout()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var agency = await fx.AddPartnerAsync(withPassword: false);

        for (var i = 0; i < 6; i++)
        {
            var result = await fx.Service.LoginAsync(agency.ContactEmail, GoodPassword, "Partner");
            Assert.Equal(401, result.StatusCode);
        }

        await using var check = CreateContext();
        var stored = await check.PartnerAgencies.AsNoTracking().FirstAsync(a => a.AgencyId == agency.AgencyId);
        Assert.Equal(0, stored.FailedLoginCount);
        Assert.Null(stored.LockedUntil);
    }

    // ---- lockout (Q16: 5 failures -> 15 minutes) ----

    [Fact]
    public async Task Fifth_consecutive_failure_locks_admin_for_15_minutes_with_423()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var admin = await fx.AddAdminAsync();

        for (var i = 1; i <= 4; i++)
        {
            var failure = await fx.Service.LoginAsync(admin.Email, WrongPassword, "Admin");
            Assert.Equal(401, failure.StatusCode);
        }

        var fifth = await fx.Service.LoginAsync(admin.Email, WrongPassword, "Admin");

        Assert.Equal(423, fifth.StatusCode);
        Assert.Equal(15 * 60, fifth.RetryAfterSeconds);

        await using var check = CreateContext();
        var stored = await check.Admins.AsNoTracking().FirstAsync(a => a.AdminId == admin.AdminId);
        Assert.Equal(5, stored.FailedLoginCount);
        Assert.Equal(fx.Clock.UtcNow.AddMinutes(15), stored.LockedUntil);
    }

    [Fact]
    public async Task Correct_password_during_lock_still_returns_423_with_remaining_retry_after()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var admin = await fx.AddAdminAsync();
        for (var i = 0; i < 5; i++)
        {
            await fx.Service.LoginAsync(admin.Email, WrongPassword, "Admin");
        }

        fx.Clock.UtcNow = fx.Clock.UtcNow.AddMinutes(5);
        var result = await fx.Service.LoginAsync(admin.Email, GoodPassword, "Admin");

        Assert.Equal(423, result.StatusCode);
        Assert.Equal(10 * 60, result.RetryAfterSeconds);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Lock_expires_after_15_minutes_and_correct_password_logs_in_and_resets_counter()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var admin = await fx.AddAdminAsync();
        for (var i = 0; i < 5; i++)
        {
            await fx.Service.LoginAsync(admin.Email, WrongPassword, "Admin");
        }

        fx.Clock.UtcNow = fx.Clock.UtcNow.AddMinutes(15).AddSeconds(1);
        var result = await fx.Service.LoginAsync(admin.Email, GoodPassword, "Admin");

        Assert.True(result.Success);
        await using var check = CreateContext();
        var stored = await check.Admins.AsNoTracking().FirstAsync(a => a.AdminId == admin.AdminId);
        Assert.Equal(0, stored.FailedLoginCount);
        Assert.Null(stored.LockedUntil);
    }

    [Fact]
    public async Task After_lock_expiry_a_wrong_password_starts_counting_from_one_again()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var admin = await fx.AddAdminAsync();
        for (var i = 0; i < 5; i++)
        {
            await fx.Service.LoginAsync(admin.Email, WrongPassword, "Admin");
        }

        fx.Clock.UtcNow = fx.Clock.UtcNow.AddMinutes(16);
        var result = await fx.Service.LoginAsync(admin.Email, WrongPassword, "Admin");

        Assert.Equal(401, result.StatusCode);
        await using var check = CreateContext();
        var stored = await check.Admins.AsNoTracking().FirstAsync(a => a.AdminId == admin.AdminId);
        Assert.Equal(1, stored.FailedLoginCount);
        Assert.Null(stored.LockedUntil);
    }

    [Fact]
    public async Task Successful_login_resets_failure_counter_so_failures_must_be_consecutive()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var admin = await fx.AddAdminAsync();

        for (var i = 0; i < 4; i++)
        {
            await fx.Service.LoginAsync(admin.Email, WrongPassword, "Admin");
        }

        Assert.True((await fx.Service.LoginAsync(admin.Email, GoodPassword, "Admin")).Success);

        // 4 more failures after the reset must not lock (would be 8 without the reset).
        for (var i = 0; i < 4; i++)
        {
            var failure = await fx.Service.LoginAsync(admin.Email, WrongPassword, "Admin");
            Assert.Equal(401, failure.StatusCode);
        }

        await using var check = CreateContext();
        var stored = await check.Admins.AsNoTracking().FirstAsync(a => a.AdminId == admin.AdminId);
        Assert.Equal(4, stored.FailedLoginCount);
        Assert.Null(stored.LockedUntil);
    }

    [Fact]
    public async Task Partner_lockout_uses_the_same_policy()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var agency = await fx.AddPartnerAsync();

        for (var i = 1; i <= 4; i++)
        {
            Assert.Equal(401, (await fx.Service.LoginAsync(agency.ContactEmail, WrongPassword, "Partner")).StatusCode);
        }

        var fifth = await fx.Service.LoginAsync(agency.ContactEmail, WrongPassword, "Partner");
        var duringLock = await fx.Service.LoginAsync(agency.ContactEmail, GoodPassword, "Partner");

        Assert.Equal(423, fifth.StatusCode);
        Assert.Equal(423, duringLock.StatusCode);

        fx.Clock.UtcNow = fx.Clock.UtcNow.AddMinutes(15).AddSeconds(1);
        Assert.True((await fx.Service.LoginAsync(agency.ContactEmail, GoodPassword, "Partner")).Success);
    }

    [Fact]
    public async Task Failed_login_for_one_account_does_not_lock_another()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var first = await fx.AddAdminAsync();
        var second = await fx.AddAdminAsync();
        for (var i = 0; i < 5; i++)
        {
            await fx.Service.LoginAsync(first.Email, WrongPassword, "Admin");
        }

        var result = await fx.Service.LoginAsync(second.Email, GoodPassword, "Admin");

        Assert.True(result.Success);
    }

    // ---- controller mapping (no database) ----

    private sealed class StubPasswordLoginService(PasswordLoginResult result) : IPasswordLoginService
    {
        public Task<PasswordLoginResult> LoginAsync(string email, string password, string roleString, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    private sealed class NoopOtpService : IOtpService
    {
        public Task<OtpRequestResult> RequestOtpAsync(string phoneNumber, string roleString, string clientIp, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<OtpVerifyResult> VerifyOtpAsync(string phoneNumber, string roleString, string code, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<RefreshResult> RefreshTokenAsync(string refreshToken, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task LogoutAsync(string refreshToken, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class AnonymousUser : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public int? UserId => null;
        public UserRole? Role => null;
    }

    private static (AuthController controller, DefaultHttpContext http) CreateController()
    {
        var http = new DefaultHttpContext();
        var controller = new AuthController(new NoopOtpService(), new AnonymousUser())
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
        return (controller, http);
    }

    private static readonly PasswordLoginDto Request = new() { Email = "a@test.local", Password = GoodPassword, Role = "Admin" };

    [Fact]
    public async Task Controller_returns_200_with_AuthResult_on_success()
    {
        var (controller, _) = CreateController();
        var auth = new AuthResultDto
        {
            AccessToken = "jwt",
            RefreshToken = "refresh",
            AccessTokenExpiresInSeconds = 900,
            User = new AuthUserDto { Id = 7, Role = "Admin", IsNewUser = false }
        };

        var action = await controller.PasswordLogin(Request, new StubPasswordLoginService(PasswordLoginResult.Ok(auth)), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action);
        var body = Assert.IsType<ApiResponse<AuthResultDto>>(ok.Value);
        Assert.True(body.Success);
        Assert.Equal(7, body.Data!.User.Id);
    }

    [Fact]
    public async Task Controller_returns_423_with_Retry_After_header_when_locked()
    {
        var (controller, http) = CreateController();

        var action = await controller.PasswordLogin(Request, new StubPasswordLoginService(PasswordLoginResult.Locked(840)), CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status423Locked, result.StatusCode);
        Assert.Equal("840", http.Response.Headers["Retry-After"].ToString());
        Assert.False(Assert.IsType<ApiResponse<object>>(result.Value).Success);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Controller_maps_401_and_403_without_retry_after(int status)
    {
        var (controller, http) = CreateController();
        var failure = status == 401 ? PasswordLoginResult.Unauthorized() : PasswordLoginResult.Forbidden();

        var action = await controller.PasswordLogin(Request, new StubPasswordLoginService(failure), CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(action);
        Assert.Equal(status, result.StatusCode);
        Assert.False(http.Response.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task Controller_returns_400_with_errors_map_for_validation_failures()
    {
        var (controller, _) = CreateController();
        var errors = new Dictionary<string, string[]> { ["email"] = ["A valid email address is required."] };

        var action = await controller.PasswordLogin(Request, new StubPasswordLoginService(PasswordLoginResult.ValidationError(errors)), CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(action);
        var body = Assert.IsType<ApiResponse<object>>(bad.Value);
        Assert.False(body.Success);
        Assert.NotNull(body.Data);
    }

    [Fact]
    public async Task Controller_returns_400_when_body_is_missing()
    {
        var (controller, _) = CreateController();

        var action = await controller.PasswordLogin(null!, new StubPasswordLoginService(PasswordLoginResult.Unauthorized()), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(action);
    }

    [Fact]
    public void IdentityModule_registers_password_login_service()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        new IdentityModule().ConfigureServices(services, new ConfigurationBuilder().Build());

        Assert.Contains(services, d => d.ServiceType == typeof(IPasswordLoginService)
                                       && d.ImplementationType == typeof(PasswordLoginService));
    }
}
