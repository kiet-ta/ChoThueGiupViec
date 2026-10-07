using System.Reflection;
using CommonService.Application.Common.Models;
using CommonService.Application.Features.Admin;
using CommonService.Application.Features.Admin.Dtos;
using CommonService.Application.Features.Admin.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Admin;
using CommonService.Infrastructure.Persistence;
using CommonService.WebAPI.Controllers.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Tests.Admin;

/// <summary>BE-M6-06a: GET /api/admin/me (contract admin.md section 2.1).</summary>
public class AdminProfileTests
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

    private sealed class MemoryAdmins : IAdminProfileReader
    {
        public Dictionary<int, AdminAccount> Rows { get; } = [];
        public List<int> Asked { get; } = [];

        public Task<AdminAccount?> FindAsync(int adminId, CancellationToken cancellationToken = default)
        {
            Asked.Add(adminId);
            return Task.FromResult(Rows.GetValueOrDefault(adminId));
        }
    }

    private static AdminAccount Row(int id = 7) => new()
    {
        AdminId = id,
        Email = "ops@example.test",
        FullName = "Ops Admin",
        PasswordHash = "SECRET-HASH",
        AdminRole = "SUPER_ADMIN",
        IsActive = true,
        CreatedAt = new DateTime(2026, 10, 1, 3, 0, 0, DateTimeKind.Unspecified),
        FailedLoginCount = 3,
        LockedUntil = new DateTime(2026, 10, 7, 1, 0, 0, DateTimeKind.Utc),
    };

    private sealed class FakeUser(int? id) : ICurrentUser
    {
        public bool IsAuthenticated => id is not null;
        public int? UserId => id;
        public UserRole? Role => UserRole.Admin;
    }

    // ---- service --------------------------------------------------------------------------------

    [Fact]
    public async Task The_profile_shows_exactly_the_contract_fields_with_UTC_time()
    {
        var admins = new MemoryAdmins();
        admins.Rows[7] = Row();

        var result = await new AdminProfileService(admins).GetAsync(7);

        Assert.True(result.Success);
        var dto = result.Data!;
        Assert.Equal(7, dto.AdminId);
        Assert.Equal("ops@example.test", dto.Email);
        Assert.Equal("Ops Admin", dto.FullName);
        Assert.Equal("SUPER_ADMIN", dto.AdminRole);
        Assert.True(dto.IsActive);
        Assert.Equal(DateTimeKind.Utc, dto.CreatedAt.Kind);
        Assert.Equal(new DateTime(2026, 10, 1, 3, 0, 0), dto.CreatedAt);
    }

    [Fact]
    public void The_dto_has_the_six_contract_properties_and_nothing_that_could_leak_a_secret()
    {
        var names = typeof(AdminProfileDto).GetProperties().Select(p => p.Name).Order().ToArray();
        Assert.Equal(["AdminId", "AdminRole", "CreatedAt", "Email", "FullName", "IsActive"], names);
    }

    [Fact]
    public async Task The_serialised_profile_contains_no_hash_and_no_lockout_data()
    {
        var admins = new MemoryAdmins();
        admins.Rows[7] = Row();
        var result = await new AdminProfileService(admins).GetAsync(7);

        var json = System.Text.Json.JsonSerializer.Serialize(result.Data);
        Assert.DoesNotContain("SECRET-HASH", json);
        Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failedLoginCount", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lockedUntil", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_removed_row_is_404()
    {
        var result = await new AdminProfileService(new MemoryAdmins()).GetAsync(999);
        Assert.Equal(404, result.StatusCode);
        Assert.Null(result.Data);
    }

    // ---- controller -----------------------------------------------------------------------------

    [Fact]
    public void Controller_requires_AdminOnly_serves_api_admin_me_and_has_only_the_GET()
    {
        var type = typeof(AdminProfileController);
        Assert.Equal("AdminOnly", type.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal("api/admin/me", type.GetCustomAttribute<RouteAttribute>()!.Template);

        var actions = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).ToList();
        var action = Assert.Single(actions);
        Assert.Equal(["GET"], action.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods).ToArray());
        Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Empty(action.GetParameters().Where(p => p.ParameterType != typeof(CancellationToken))); // no id from URL or body
    }

    [Fact]
    public async Task The_id_used_is_the_one_of_the_token()
    {
        var admins = new MemoryAdmins();
        admins.Rows[7] = Row(7);
        admins.Rows[8] = Row(8);
        var controller = new AdminProfileController(new AdminProfileService(admins), new FakeUser(8));

        var response = await controller.Get(default);

        var body = Assert.IsType<ApiResponse<AdminProfileDto>>(Assert.IsType<OkObjectResult>(response).Value);
        Assert.Equal(8, body.Data!.AdminId);
        Assert.Equal([8], admins.Asked);
    }

    [Fact]
    public async Task No_id_in_the_token_is_401_and_a_removed_row_is_404_in_the_envelope()
    {
        var anonymous = new AdminProfileController(new AdminProfileService(new MemoryAdmins()), new FakeUser(null));
        Assert.IsType<UnauthorizedObjectResult>(await anonymous.Get(default));

        var gone = new AdminProfileController(new AdminProfileService(new MemoryAdmins()), new FakeUser(5));
        var result = Assert.IsType<ObjectResult>(await gone.Get(default));
        Assert.Equal(404, result.StatusCode);
        Assert.False(Assert.IsType<ApiResponse<object>>(result.Value).Success);
    }

    // ---- SQL Server (skipped when the database is missing or invariant globalization is on) ------

    [Fact]
    public async Task A_real_ADMIN_row_is_read_back_without_the_hash_and_removed_afterwards()
    {
        if (!IsSqlServerAvailable()) return;

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        var email = "profile-" + Guid.NewGuid().ToString("N")[..10] + "@example.test";
        int id;
        await using (var seed = new AppDbContext(options))
        {
            var row = new AdminAccount
            {
                Email = email,
                FullName = "Profile Test",
                PasswordHash = "HASH-NOT-FOR-DISPLAY",
                AdminRole = "SUPER_ADMIN",
                IsActive = true,
                CreatedAt = new DateTime(2026, 10, 2, 4, 5, 6, DateTimeKind.Utc),
            };
            seed.Admins.Add(row);
            await seed.SaveChangesAsync();
            id = row.AdminId;
        }

        try
        {
            await using var db = new AppDbContext(options);
            var result = await new AdminProfileService(new EfAdminProfileReader(db)).GetAsync(id);

            Assert.True(result.Success);
            Assert.Equal(email, result.Data!.Email);
            Assert.Equal("Profile Test", result.Data.FullName);
            Assert.Equal(new DateTime(2026, 10, 2, 4, 5, 6), result.Data.CreatedAt);
            Assert.DoesNotContain("HASH-NOT-FOR-DISPLAY", System.Text.Json.JsonSerializer.Serialize(result.Data));
            Assert.Empty(db.ChangeTracker.Entries()); // AsNoTracking

            Assert.Equal(404, (await new AdminProfileService(new EfAdminProfileReader(db)).GetAsync(id + 100000)).StatusCode);
        }
        finally
        {
            await using var clean = new AppDbContext(options);
            await clean.Admins.Where(a => a.AdminId == id).ExecuteDeleteAsync();
        }
    }
}
