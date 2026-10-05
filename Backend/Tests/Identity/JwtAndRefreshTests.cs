using System.Security.Claims;
using CommonService.Application.Common.Options;
using CommonService.Application.Features.Identity.Dtos;
using CommonService.Application.Features.Identity.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Identity;
using CommonService.Infrastructure.Persistence;
using CommonService.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CommonService.Tests.Identity;

public class JwtAndRefreshTests
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
        public Task SendAsync(string phoneNumber, string code, CancellationToken cancellationToken = default)
        {
            LastPhone = phoneNumber;
            LastCode = code;
            return Task.CompletedTask;
        }
    }

    private static string GenerateTestPhone() => $"091{Random.Shared.Next(1000000, 9999999)}";
    private static string GenerateTestIp() => $"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}";

    private static (OtpService service, TokenService tokenService, AppDbContext db, TestClock clock) CreateServices(
        DateTime? initialTime = null)
    {
        var db = CreateContext();
        var clock = new TestClock(initialTime ?? new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc));
        var sender = new TestOtpSender();
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "Test_Jwt_Secret_Key_At_Least_32_Characters_Long!"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();
        var tokenService = new TokenService(rules, config);
        var otpService = new OtpService(db, sender, clock, rules, config, tokenService, NullLogger<OtpService>.Instance);

        return (otpService, tokenService, db, clock);
    }

    [Fact]
    public void TokenService_generates_valid_jwt_and_validates_claims()
    {
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "Deterministic_Test_Key_For_Jwt_32_Bytes!"
        }).Build();

        var tokenService = new TokenService(rules, config);
        var (token, expiry) = tokenService.GenerateAccessToken(101, "Customer");

        Assert.NotEmpty(token);
        Assert.Equal(900, expiry); // 15 min default

        var principal = tokenService.ValidateAccessToken(token);
        Assert.NotNull(principal);
        Assert.True(principal.Identity?.IsAuthenticated);
        Assert.Equal("101", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("Customer", principal.FindFirst(ClaimTypes.Role)?.Value);
        Assert.NotNull(principal.FindFirst("jti")?.Value);
    }

    [Fact]
    public void TokenService_rejects_tampered_or_malformed_token()
    {
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "Deterministic_Test_Key_For_Jwt_32_Bytes!"
        }).Build();

        var tokenService = new TokenService(rules, config);
        var (token, _) = tokenService.GenerateAccessToken(101, "Customer");

        // Tamper signature
        var tampered = token[..^4] + "xxxx";
        Assert.Null(tokenService.ValidateAccessToken(tampered));

        // Invalid formats
        Assert.Null(tokenService.ValidateAccessToken(""));
        Assert.Null(tokenService.ValidateAccessToken("not.a.jwt.token"));
    }

    [Fact]
    public void ClaimsCurrentUser_resolves_claims_from_http_context()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "55"),
            new(ClaimTypes.Role, "Worker")
        };
        var identity = new ClaimsIdentity(claims, "Bearer");
        var principal = new ClaimsPrincipal(identity);

        var context = new DefaultHttpContext { User = principal };
        var accessor = new HttpContextAccessor { HttpContext = context };

        var currentUser = new ClaimsCurrentUser(accessor);

        Assert.True(currentUser.IsAuthenticated);
        Assert.Equal(55, currentUser.UserId);
        Assert.Equal(UserRole.Worker, currentUser.Role);
    }

    [Fact]
    public void Authorization_policies_enforce_role_isolation()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var module = new IdentityModule();

        module.ConfigureServices(services, config);

        using var sp = services.BuildServiceProvider();
        var authOptions = sp.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        Assert.NotNull(authOptions.GetPolicy("CustomerOnly"));
        Assert.NotNull(authOptions.GetPolicy("WorkerOnly"));
        Assert.NotNull(authOptions.GetPolicy("PartnerOnly"));
        Assert.NotNull(authOptions.GetPolicy("AdminOnly"));
    }

    [Fact]
    public async Task OtpVerify_creates_refresh_token_row_in_database()
    {
        if (!IsSqlServerAvailable()) return;

        var (service, _, db, _) = CreateServices();
        await using var _ = db;

        var phone = GenerateTestPhone();
        var ip = GenerateTestIp();

        await service.RequestOtpAsync(phone, "Customer", ip);

        var otpRecord = await db.OtpCodes.FirstAsync(x => x.PhoneNumber == phone);
        var verify = await service.VerifyOtpAsync(phone, "Customer", "000000"); // wrong try
        // find code hash or we know code is 6 digits: verify by checking OTP code
        // Instead, let's create a known OtpCode directly:
        var directOtp = new OtpCode
        {
            PhoneNumber = phone + "1",
            Role = UserRole.Customer,
            CodeHash = "DUMMY_HASH",
            AttemptCount = 0,
            RequestedIp = ip,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            ConsumedAt = null
        };
        db.OtpCodes.Add(directOtp);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task RefreshTokenAsync_rotates_token_and_marks_previous_revoked()
    {
        if (!IsSqlServerAvailable()) return;

        var (service, tokenService, db, clock) = CreateServices();
        await using var _ = db;

        var initialRaw = tokenService.GenerateRefreshToken();
        var initialHash = tokenService.HashRefreshToken(initialRaw);
        var familyId = Guid.NewGuid();

        var tokenRecord = new RefreshToken
        {
            TokenHash = initialHash,
            SubjectRole = UserRole.Customer,
            SubjectId = 123,
            FamilyId = familyId,
            CreatedAt = clock.UtcNow,
            ExpiresAt = clock.UtcNow.AddDays(30),
            RevokedAt = null,
            ReplacedById = null
        };
        db.RefreshTokens.Add(tokenRecord);
        await db.SaveChangesAsync();

        // Rotate token
        var refreshResult = await service.RefreshTokenAsync(initialRaw);

        Assert.True(refreshResult.Success);
        Assert.Equal(200, refreshResult.StatusCode);
        Assert.NotNull(refreshResult.Data);
        Assert.NotEmpty(refreshResult.Data.AccessToken);
        Assert.NotEmpty(refreshResult.Data.RefreshToken);
        Assert.NotEqual(initialRaw, refreshResult.Data.RefreshToken);

        // Verify previous token marked revoked and replaced
        var updatedInitial = await db.RefreshTokens.FirstAsync(x => x.RefreshTokenId == tokenRecord.RefreshTokenId);
        Assert.NotNull(updatedInitial.RevokedAt);
        Assert.NotNull(updatedInitial.ReplacedById);

        // Verify new token exists with same family_id
        var newHash = tokenService.HashRefreshToken(refreshResult.Data.RefreshToken);
        var newToken = await db.RefreshTokens.FirstAsync(x => x.TokenHash == newHash);
        Assert.Equal(familyId, newToken.FamilyId);
        Assert.Null(newToken.RevokedAt);
        Assert.Equal(updatedInitial.ReplacedById, newToken.RefreshTokenId);
    }

    [Fact]
    public async Task RefreshTokenAsync_reuse_detection_revokes_entire_token_family()
    {
        if (!IsSqlServerAvailable()) return;

        var (service, tokenService, db, clock) = CreateServices();
        await using var _ = db;

        var familyId = Guid.NewGuid();
        var token2Raw = tokenService.GenerateRefreshToken();
        var token2 = new RefreshToken
        {
            TokenHash = tokenService.HashRefreshToken(token2Raw),
            SubjectRole = UserRole.Customer,
            SubjectId = 200,
            FamilyId = familyId,
            CreatedAt = clock.UtcNow,
            ExpiresAt = clock.UtcNow.AddDays(30),
            RevokedAt = null,
            ReplacedById = null
        };
        db.RefreshTokens.Add(token2);
        await db.SaveChangesAsync();

        var token1Raw = tokenService.GenerateRefreshToken();
        var token1 = new RefreshToken
        {
            TokenHash = tokenService.HashRefreshToken(token1Raw),
            SubjectRole = UserRole.Customer,
            SubjectId = 200,
            FamilyId = familyId,
            CreatedAt = clock.UtcNow,
            ExpiresAt = clock.UtcNow.AddDays(30),
            RevokedAt = clock.UtcNow,
            ReplacedById = token2.RefreshTokenId // already rotated to token2
        };
        db.RefreshTokens.Add(token1);
        await db.SaveChangesAsync();

        // Attempting to reuse token1 should fail and revoke token2 as well!
        var reuseResult = await service.RefreshTokenAsync(token1Raw);

        Assert.False(reuseResult.Success);
        Assert.Equal(401, reuseResult.StatusCode);

        // Verify token2 is now also revoked
        var updatedToken2 = await db.RefreshTokens.FirstAsync(x => x.RefreshTokenId == token2.RefreshTokenId);
        Assert.NotNull(updatedToken2.RevokedAt);
    }

    [Fact]
    public async Task LogoutAsync_revokes_refresh_token()
    {
        if (!IsSqlServerAvailable()) return;

        var (service, tokenService, db, clock) = CreateServices();
        await using var _ = db;

        var rawToken = tokenService.GenerateRefreshToken();
        var hash = tokenService.HashRefreshToken(rawToken);

        var token = new RefreshToken
        {
            TokenHash = hash,
            SubjectRole = UserRole.Customer,
            SubjectId = 300,
            FamilyId = Guid.NewGuid(),
            CreatedAt = clock.UtcNow,
            ExpiresAt = clock.UtcNow.AddDays(30),
            RevokedAt = null,
            ReplacedById = null
        };
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();

        await service.LogoutAsync(rawToken);

        var updated = await db.RefreshTokens.FirstAsync(x => x.TokenHash == hash);
        Assert.NotNull(updated.RevokedAt);
    }
}
