using System.Reflection;
using System.Security.Claims;
using CommonService.Application.Common.Models;
using CommonService.Application.Features.Customers.Dtos;
using CommonService.Application.Features.Customers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Customers;
using CommonService.Infrastructure.Modules.Identity;
using CommonService.Infrastructure.Persistence;
using CommonService.WebAPI.Controllers.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CommonService.Tests.Customers;

/// <summary>
/// BE-M1-04: GET/PUT /api/customers/me (contract customers.md §2.1, decisions Q21 C1/C2).
/// DB-backed tests run against the local SQL Server of BASE-07 and delete the rows they create.
/// </summary>
public class CustomerProfileTests
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

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

    private sealed class TestClock(DateTime initialUtc) : IClock
    {
        public DateTime UtcNow { get; set; } = initialUtc;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => local.AddHours(-7);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(UtcNow));
    }

    /// <summary>One service graph on a real context, plus the customers a test created (removed on dispose).</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly List<string> _phones = [];

        public AppDbContext Db { get; } = CreateContext();
        public TestClock Clock { get; } = new(new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc));
        public CustomerProfileService Service { get; }

        public Fixture()
        {
            Service = new CustomerProfileService(new CustomerRepository(Db), new UnitOfWork(Db), Clock);
        }

        public async Task<Customer> AddCustomerAsync(string fullName = "", string? email = null)
        {
            var phone = $"09{Random.Shared.Next(10000000, 99999999)}";
            var created = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
            var customer = new Customer
            {
                PhoneNumber = phone,
                FullName = fullName,
                Email = email,
                OtpVerifiedAt = created,
                TrustScore = 0.00m,
                AccountStatus = CustomerAccountStatus.Active,
                CreatedAt = created,
                UpdatedAt = created
            };
            Db.Customers.Add(customer);
            await Db.SaveChangesAsync();
            _phones.Add(phone);
            return customer;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var phone in _phones)
            {
                await Db.Customers.Where(c => c.PhoneNumber == phone).ExecuteDeleteAsync();
            }

            await Db.DisposeAsync();
        }
    }

    private static async Task<Customer> ReloadAsync(int customerId)
    {
        await using var db = CreateContext();
        return await db.Customers.AsNoTracking().FirstAsync(c => c.CustomerId == customerId);
    }

    // ---- GET ----

    [Fact]
    public async Task Get_returns_the_profile_of_the_requested_customer_only()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var alice = await fx.AddCustomerAsync("Alice", "alice@test.local");
        var bob = await fx.AddCustomerAsync("Bob");

        var aliceResult = await fx.Service.GetAsync(alice.CustomerId);
        var bobResult = await fx.Service.GetAsync(bob.CustomerId);

        Assert.True(aliceResult.Success);
        Assert.Equal(200, aliceResult.StatusCode);
        Assert.Equal(alice.CustomerId, aliceResult.Data!.CustomerId);
        Assert.Equal(alice.PhoneNumber, aliceResult.Data.PhoneNumber);
        Assert.Equal("Alice", aliceResult.Data.FullName);
        Assert.Equal("alice@test.local", aliceResult.Data.Email);
        Assert.Equal(0.00m, aliceResult.Data.TrustScore);
        Assert.Equal("ACTIVE", aliceResult.Data.AccountStatus);
        Assert.Equal(alice.CreatedAt, aliceResult.Data.CreatedAt);

        Assert.Equal(bob.CustomerId, bobResult.Data!.CustomerId);
        Assert.Equal("", bobResult.Data.Email ?? "");
        Assert.NotEqual(aliceResult.Data.PhoneNumber, bobResult.Data.PhoneNumber);
    }

    [Fact]
    public async Task Get_for_a_removed_customer_returns_404()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();

        var result = await fx.Service.GetAsync(int.MaxValue);

        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task New_customer_with_empty_full_name_is_returned_with_an_empty_string()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customer = await fx.AddCustomerAsync(fullName: "");

        var result = await fx.Service.GetAsync(customer.CustomerId);

        Assert.Equal("", result.Data!.FullName); // decisions Q21 C1
    }

    [Fact]
    public void CustomerProfileDto_exposes_exactly_the_contract_fields()
    {
        var names = typeof(CustomerProfileDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Order()
            .ToArray();

        Assert.Equal(
            new[] { "AccountStatus", "CreatedAt", "CustomerId", "Email", "FullName", "PhoneNumber", "TrustScore" },
            names); // no OtpVerifiedAt, no UpdatedAt (contract §1)
    }

    [Fact]
    public void UpdateCustomerProfileDto_accepts_only_fullName_and_email()
    {
        var names = typeof(UpdateCustomerProfileDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Order()
            .ToArray();

        Assert.Equal(new[] { "Email", "FullName" }, names); // phone, trustScore, accountStatus are not editable
    }

    // ---- PUT ----

    [Fact]
    public async Task Put_replaces_name_and_email_and_returns_the_updated_profile()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customer = await fx.AddCustomerAsync();

        var result = await fx.Service.UpdateAsync(customer.CustomerId,
            new UpdateCustomerProfileDto { FullName = "  Nguyen Van A  ", Email = " a@test.local " });

        Assert.True(result.Success);
        Assert.Equal("Nguyen Van A", result.Data!.FullName);
        Assert.Equal("a@test.local", result.Data.Email);

        var stored = await ReloadAsync(customer.CustomerId);
        Assert.Equal("Nguyen Van A", stored.FullName);
        Assert.Equal("a@test.local", stored.Email);
        Assert.Equal(fx.Clock.UtcNow, stored.UpdatedAt); // set by the server through IClock
    }

    [Fact]
    public async Task Put_does_not_change_phone_trust_score_status_or_created_at()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customer = await fx.AddCustomerAsync("Old Name");

        var result = await fx.Service.UpdateAsync(customer.CustomerId,
            new UpdateCustomerProfileDto { FullName = "New Name", Email = null });

        Assert.True(result.Success);
        var stored = await ReloadAsync(customer.CustomerId);
        Assert.Equal(customer.PhoneNumber, stored.PhoneNumber);
        Assert.Equal(0.00m, stored.TrustScore);
        Assert.Equal(CustomerAccountStatus.Active, stored.AccountStatus);
        Assert.Equal(customer.CreatedAt, stored.CreatedAt);
        Assert.Equal(customer.OtpVerifiedAt, stored.OtpVerifiedAt);
    }

    [Fact]
    public async Task Put_with_null_email_clears_an_existing_email()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customer = await fx.AddCustomerAsync("Name", "old@test.local");

        var result = await fx.Service.UpdateAsync(customer.CustomerId,
            new UpdateCustomerProfileDto { FullName = "Name", Email = null });

        Assert.True(result.Success);
        Assert.Null(result.Data!.Email);
        Assert.Null((await ReloadAsync(customer.CustomerId)).Email);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Put_with_blank_email_is_treated_as_no_email(string blank)
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customer = await fx.AddCustomerAsync("Name", "old@test.local");

        var result = await fx.Service.UpdateAsync(customer.CustomerId,
            new UpdateCustomerProfileDto { FullName = "Name", Email = blank });

        Assert.True(result.Success);
        Assert.Null((await ReloadAsync(customer.CustomerId)).Email);
    }

    [Fact]
    public async Task Put_accepts_a_full_name_of_exactly_100_characters()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customer = await fx.AddCustomerAsync();

        var result = await fx.Service.UpdateAsync(customer.CustomerId,
            new UpdateCustomerProfileDto { FullName = new string('a', 100) });

        Assert.True(result.Success);
        Assert.Equal(100, (await ReloadAsync(customer.CustomerId)).FullName.Length);
    }

    [Fact]
    public async Task Put_for_a_removed_customer_returns_404()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();

        var result = await fx.Service.UpdateAsync(int.MaxValue,
            new UpdateCustomerProfileDto { FullName = "Name" });

        Assert.Equal(404, result.StatusCode);
    }

    // ---- PUT validation (400, errors map per decisions O5). Validation runs before any query. ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Put_rejects_empty_or_whitespace_full_name(string name)
    {
        await using var fx = new Fixture();

        var result = await fx.Service.UpdateAsync(1, new UpdateCustomerProfileDto { FullName = name });

        Assert.Equal(400, result.StatusCode);
        Assert.Contains("fullName", result.ValidationErrors!.Keys);
        Assert.DoesNotContain("email", result.ValidationErrors.Keys);
    }

    [Fact]
    public async Task Put_rejects_a_full_name_longer_than_100_characters()
    {
        await using var fx = new Fixture();

        var result = await fx.Service.UpdateAsync(1, new UpdateCustomerProfileDto { FullName = new string('a', 101) });

        Assert.Equal(400, result.StatusCode);
        Assert.Contains("fullName", result.ValidationErrors!.Keys);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b@c.com")]
    [InlineData("@test.local")]
    [InlineData("Name <a@test.local>")]
    public async Task Put_rejects_an_invalid_email(string email)
    {
        await using var fx = new Fixture();

        var result = await fx.Service.UpdateAsync(1, new UpdateCustomerProfileDto { FullName = "Name", Email = email });

        Assert.Equal(400, result.StatusCode);
        Assert.Contains("email", result.ValidationErrors!.Keys);
        Assert.DoesNotContain("fullName", result.ValidationErrors.Keys);
    }

    [Fact]
    public async Task Put_rejects_an_email_longer_than_255_characters()
    {
        await using var fx = new Fixture();
        var longEmail = new string('a', 251) + "@x.io"; // 256 characters, one over the NVARCHAR(255) limit

        var result = await fx.Service.UpdateAsync(1, new UpdateCustomerProfileDto { FullName = "Name", Email = longEmail });

        Assert.Equal(256, longEmail.Length);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("email", result.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task Put_reports_every_invalid_field_in_one_response()
    {
        await using var fx = new Fixture();

        var result = await fx.Service.UpdateAsync(1, new UpdateCustomerProfileDto { FullName = "", Email = "nope" });

        Assert.Equal(400, result.StatusCode);
        Assert.Contains("fullName", result.ValidationErrors!.Keys);
        Assert.Contains("email", result.ValidationErrors.Keys);
    }

    [Fact]
    public async Task Put_with_invalid_input_changes_nothing_in_the_database()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customer = await fx.AddCustomerAsync("Keep Me", "keep@test.local");

        var result = await fx.Service.UpdateAsync(customer.CustomerId,
            new UpdateCustomerProfileDto { FullName = "Changed", Email = "invalid" });

        Assert.Equal(400, result.StatusCode);
        var stored = await ReloadAsync(customer.CustomerId);
        Assert.Equal("Keep Me", stored.FullName);
        Assert.Equal("keep@test.local", stored.Email);
        Assert.Equal(customer.UpdatedAt, stored.UpdatedAt);
    }

    // ---- controller: id comes from ICurrentUser, status mapping (no database) ----

    private sealed class RecordingProfileService : ICustomerProfileService
    {
        public int? LastGetId { get; private set; }
        public int? LastUpdateId { get; private set; }
        public CustomerProfileResult Result { get; set; } = CustomerProfileResult.Ok(new CustomerProfileDto { CustomerId = 5 });

        public Task<CustomerProfileResult> GetAsync(int customerId, CancellationToken ct = default)
        {
            LastGetId = customerId;
            return Task.FromResult(Result);
        }

        public Task<CustomerProfileResult> UpdateAsync(int customerId, UpdateCustomerProfileDto request, CancellationToken ct = default)
        {
            LastUpdateId = customerId;
            return Task.FromResult(Result);
        }
    }

    private sealed class TestUser(int? id) : ICurrentUser
    {
        public bool IsAuthenticated => id != null;
        public int? UserId => id;
        public UserRole? Role => id == null ? null : UserRole.Customer;
    }

    private static CustomersController CreateController(ICustomerProfileService service, int? userId) =>
        new(service, new TestUser(userId)) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    [Fact]
    public async Task Controller_uses_the_id_of_the_signed_in_customer_for_get_and_put()
    {
        var service = new RecordingProfileService();
        var controller = CreateController(service, userId: 42);

        await controller.GetProfile(CancellationToken.None);
        await controller.UpdateProfile(new UpdateCustomerProfileDto { FullName = "Name" }, CancellationToken.None);

        Assert.Equal(42, service.LastGetId);
        Assert.Equal(42, service.LastUpdateId);
    }

    [Fact]
    public async Task Controller_returns_200_with_the_profile_envelope()
    {
        var controller = CreateController(new RecordingProfileService(), userId: 5);

        var action = await controller.GetProfile(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action);
        var body = Assert.IsType<ApiResponse<CustomerProfileDto>>(ok.Value);
        Assert.True(body.Success);
        Assert.Equal(5, body.Data!.CustomerId);
    }

    [Fact]
    public async Task Controller_returns_401_when_there_is_no_customer_id()
    {
        var service = new RecordingProfileService();
        var controller = CreateController(service, userId: null);

        var get = await controller.GetProfile(CancellationToken.None);
        var put = await controller.UpdateProfile(new UpdateCustomerProfileDto { FullName = "Name" }, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(get);
        Assert.IsType<UnauthorizedObjectResult>(put);
        Assert.Null(service.LastGetId);
        Assert.Null(service.LastUpdateId);
    }

    [Fact]
    public async Task Controller_returns_400_with_errors_map_and_404_for_missing_customer()
    {
        var service = new RecordingProfileService
        {
            Result = CustomerProfileResult.ValidationError(new Dictionary<string, string[]> { ["fullName"] = ["required"] })
        };
        var controller = CreateController(service, userId: 5);

        var bad = await controller.UpdateProfile(new UpdateCustomerProfileDto(), CancellationToken.None);
        var badBody = Assert.IsType<ApiResponse<object>>(Assert.IsType<BadRequestObjectResult>(bad).Value);
        Assert.False(badBody.Success);
        Assert.NotNull(badBody.Data);

        service.Result = CustomerProfileResult.NotFound();
        var missing = await controller.GetProfile(CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(missing);
    }

    // ---- authorization: CustomerOnly (401 anonymous, 403 other roles) ----

    [Fact]
    public void Controller_requires_the_CustomerOnly_policy()
    {
        var attribute = typeof(CustomersController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("CustomerOnly", attribute!.Policy);
    }

    private static ServiceProvider BuildAuthorization()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new IdentityModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal Principal(string? role) =>
        role == null
            ? new ClaimsPrincipal(new ClaimsIdentity())
            : new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, role)], "Bearer"));

    [Theory]
    [InlineData("Customer", true)]
    [InlineData("Worker", false)]
    [InlineData("Partner", false)]
    [InlineData("Admin", false)]
    [InlineData(null, false)]
    public async Task CustomerOnly_policy_allows_only_authenticated_customers(string? role, bool allowed)
    {
        await using var provider = BuildAuthorization();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(Principal(role), null, "CustomerOnly");

        Assert.Equal(allowed, result.Succeeded);
    }

    [Fact]
    public void CustomersModule_registers_the_profile_service()
    {
        var services = new ServiceCollection();

        new CustomersModule().ConfigureServices(services, new ConfigurationBuilder().Build());

        Assert.Contains(services, d => d.ServiceType == typeof(ICustomerProfileService)
                                       && d.ImplementationType == typeof(CustomerProfileService));
    }
}
