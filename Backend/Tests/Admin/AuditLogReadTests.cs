using System.Reflection;
using CommonService.Application.Common.Models;
using CommonService.Application.Features.Admin;
using CommonService.Application.Features.Admin.Dtos;
using CommonService.Application.Features.Admin.Services;
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

/// <summary>BE-M6-09b: read endpoint of ADMIN_AUDIT_LOG (contract admin.md section 2.4).</summary>
public class AuditLogReadTests
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

    private sealed class RecordingRepository : IAuditLogReadRepository
    {
        public AuditLogFilter? Filter { get; private set; }
        public int Page { get; private set; }
        public int PageSize { get; private set; }
        public int Calls { get; private set; }
        public List<AdminAuditLog> Rows { get; } = [];
        public int Total { get; set; }

        public Task<(IReadOnlyList<AdminAuditLog> Items, int Total)> SearchAsync(
            AuditLogFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            Calls++;
            Filter = filter;
            Page = page;
            PageSize = pageSize;
            return Task.FromResult(((IReadOnlyList<AdminAuditLog>)Rows, Total));
        }
    }

    private static AuditLogQueryService Service(RecordingRepository repo) => new(repo);

    // ---- service: defaults, parsing, validation -------------------------------------------------

    [Fact]
    public async Task Defaults_are_page_1_size_20_and_no_filter()
    {
        var repo = new RecordingRepository();
        var result = await Service(repo).SearchAsync(null, null, null, null, null, null, null);

        Assert.True(result.Success);
        Assert.Equal(1, repo.Page);
        Assert.Equal(20, repo.PageSize);
        Assert.Equal(new AuditLogFilter(null, null, null, null, null), repo.Filter);
        Assert.Equal(1, result.Data!.Page);
        Assert.Equal(20, result.Data.PageSize);
    }

    [Fact]
    public async Task Blank_text_filters_count_as_not_given_and_are_trimmed()
    {
        var repo = new RecordingRepository();
        await Service(repo).SearchAsync("  PRICE_RULE ", "   ", "", " ", "", "", "");
        Assert.Equal("PRICE_RULE", repo.Filter!.EntityType);
        Assert.Null(repo.Filter.EntityId);
        Assert.Equal(20, repo.PageSize);
    }

    [Theory]
    [InlineData("ADMIN", AuditActorType.Admin)]
    [InlineData("admin", AuditActorType.Admin)]
    [InlineData("SYSTEM", AuditActorType.System)]
    [InlineData(" system ", AuditActorType.System)]
    public async Task ActorType_is_read_case_insensitively(string text, AuditActorType expected)
    {
        var repo = new RecordingRepository();
        await Service(repo).SearchAsync(null, null, text, null, null, null, null);
        Assert.Equal(expected, repo.Filter!.ActorType);
    }

    [Fact]
    public async Task From_and_to_are_read_as_UTC_instants_and_date_only_is_midnight_UTC()
    {
        var repo = new RecordingRepository();
        await Service(repo).SearchAsync(null, null, null, "2026-10-06", "2026-10-07T03:00:00+07:00", null, null);
        Assert.Equal(new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc), repo.Filter!.FromUtc);
        Assert.Equal(new DateTime(2026, 10, 6, 20, 0, 0, DateTimeKind.Utc), repo.Filter.ToUtc);
        Assert.Equal(DateTimeKind.Utc, repo.Filter.FromUtc!.Value.Kind);
    }

    [Fact]
    public async Task Page_and_size_are_passed_through_when_valid_and_100_is_the_largest_size()
    {
        var repo = new RecordingRepository();
        var result = await Service(repo).SearchAsync(null, null, null, null, null, "3", "100");
        Assert.True(result.Success);
        Assert.Equal(3, repo.Page);
        Assert.Equal(100, repo.PageSize);
    }

    [Theory]
    [InlineData("0", null, "page")]
    [InlineData("-1", null, "page")]
    [InlineData("abc", null, "page")]
    [InlineData("1.5", null, "page")]
    [InlineData(null, "0", "pageSize")]
    [InlineData(null, "101", "pageSize")]
    [InlineData(null, "-5", "pageSize")]
    [InlineData(null, "ten", "pageSize")]
    public async Task Bad_paging_is_a_400_with_the_field_named_and_the_repository_is_not_called(
        string? page, string? size, string field)
    {
        var repo = new RecordingRepository();
        var result = await Service(repo).SearchAsync(null, null, null, null, null, page, size);

        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains(field, result.ValidationErrors!.Keys);
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task Bad_actor_bad_instants_and_an_inverted_range_are_all_reported_together()
    {
        var repo = new RecordingRepository();
        var result = await Service(repo).SearchAsync(null, null, "ROOT", "not-a-date", "2026-13-45", null, null);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(["actorType", "from", "to"], result.ValidationErrors!.Keys.Order().ToArray());

        var inverted = await Service(repo).SearchAsync(null, null, null, "2026-10-07", "2026-10-06", null, null);
        Assert.Equal(400, inverted.StatusCode);
        Assert.Contains("to", inverted.ValidationErrors!.Keys);

        var equal = await Service(repo).SearchAsync(null, null, null, "2026-10-06", "2026-10-06", null, null);
        Assert.Equal(400, equal.StatusCode); // from is inclusive and to exclusive: an empty range is rejected
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task Rows_are_mapped_to_camelCase_dtos_with_ADMIN_SYSTEM_and_UTC_time()
    {
        var repo = new RecordingRepository { Total = 41 };
        repo.Rows.Add(new AdminAuditLog
        {
            LogId = 9,
            ActorType = AuditActorType.System,
            AdminId = null,
            EntityType = "WORKER",
            EntityId = "12",
            FieldName = "is_super_freelancer",
            OldValue = "true",
            NewValue = "false",
            Reason = "rating below 4.70",
            ChangedAt = new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Unspecified),
        });

        var result = await Service(repo).SearchAsync(null, null, null, null, null, "2", "20");

        Assert.Equal(41, result.Data!.Total);
        Assert.Equal(2, result.Data.Page);
        var item = Assert.Single(result.Data.Items);
        Assert.Equal("SYSTEM", item.ActorType);
        Assert.Null(item.AdminId);
        Assert.Equal(DateTimeKind.Utc, item.ChangedAt.Kind);
        Assert.Equal(9, item.LogId);
    }

    // ---- controller -----------------------------------------------------------------------------

    private sealed class StubQueries(AuditLogQueryResult result) : IAuditLogQueryService
    {
        public Task<AuditLogQueryResult> SearchAsync(string? entityType, string? entityId, string? actorType,
            string? from, string? to, string? page, string? pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    [Fact]
    public void Controller_requires_the_AdminOnly_policy_for_every_action()
    {
        var type = typeof(AdminAuditLogsController);
        var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("AdminOnly", authorize!.Policy);
        Assert.Null(type.GetMethod("Search")!.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void Controller_has_read_actions_only()
    {
        var actions = typeof(AdminAuditLogsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToList();
        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            var verbs = action.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods).ToList();
            Assert.All(verbs, v => Assert.Equal("GET", v));
        }
    }

    [Fact]
    public async Task Controller_returns_200_with_the_envelope_and_400_with_the_errors_map()
    {
        var page = new AuditLogPageDto { Page = 1, PageSize = 20, Total = 0 };
        var ok = await new AdminAuditLogsController(new StubQueries(AuditLogQueryResult.Ok(page)))
            .Search(null, null, null, null, null, null, null, default);
        var okBody = Assert.IsType<ApiResponse<AuditLogPageDto>>(Assert.IsType<OkObjectResult>(ok).Value);
        Assert.True(okBody.Success);

        var errors = new Dictionary<string, string[]> { ["page"] = ["bad"] };
        var bad = await new AdminAuditLogsController(new StubQueries(AuditLogQueryResult.ValidationError(errors)))
            .Search(null, null, null, null, null, "x", null, default);
        var badResult = Assert.IsType<ObjectResult>(bad);
        Assert.Equal(400, badResult.StatusCode);
        var badBody = Assert.IsType<ApiResponse<object>>(badResult.Value);
        Assert.False(badBody.Success);
        Assert.NotNull(badBody.Data);
    }

    // ---- repository on the local SQL Server (skipped when the database is missing) --------------

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;

    private static async Task<string> SeedAsync(AppDbContext db, params (AuditActorType actor, string entityId, DateTime at)[] rows)
    {
        var marker = "RD" + Guid.NewGuid().ToString("N")[..10]; // entity_type is VARCHAR(30)
        foreach (var (actor, entityId, at) in rows)
        {
            db.AdminAuditLogs.Add(new AdminAuditLog
            {
                ActorType = actor,
                EntityType = marker,
                EntityId = entityId,
                FieldName = "f",
                NewValue = "v",
                Reason = actor == AuditActorType.Admin ? "r" : null,
                ChangedAt = at,
            });
        }

        await db.SaveChangesAsync();
        return marker;
    }

    private static async Task CleanAsync(string marker)
    {
        await using var db = new AppDbContext(Options());
        await db.AdminAuditLogs.Where(x => x.EntityType == marker).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Repository_orders_newest_first_pages_counts_and_filters_on_SQL_Server()
    {
        if (!IsSqlServerAvailable()) return;

        var t0 = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        string marker;
        await using (var seed = new AppDbContext(Options()))
        {
            marker = await SeedAsync(seed,
                (AuditActorType.System, "a", t0),
                (AuditActorType.Admin, "b", t0.AddHours(1)),
                (AuditActorType.System, "c", t0.AddHours(2)),
                (AuditActorType.System, "d", t0.AddHours(2)), // same instant as "c": log_id breaks the tie, newest id first
                (AuditActorType.Admin, "e", t0.AddHours(5)));
        }

        try
        {
            await using var db = new AppDbContext(Options());
            var repo = new EfAuditLogReadRepository(db);

            var all = await repo.SearchAsync(new AuditLogFilter(marker, null, null, null, null), 1, 20);
            Assert.Equal(5, all.Total);
            Assert.Equal(["e", "d", "c", "b", "a"], all.Items.Select(x => x.EntityId).ToArray());

            var page2 = await repo.SearchAsync(new AuditLogFilter(marker, null, null, null, null), 2, 2);
            Assert.Equal(5, page2.Total);
            Assert.Equal(["c", "b"], page2.Items.Select(x => x.EntityId).ToArray());

            var beyond = await repo.SearchAsync(new AuditLogFilter(marker, null, null, null, null), 9, 2);
            Assert.Equal(5, beyond.Total);
            Assert.Empty(beyond.Items);

            var byActor = await repo.SearchAsync(new AuditLogFilter(marker, null, AuditActorType.Admin, null, null), 1, 20);
            Assert.Equal(["e", "b"], byActor.Items.Select(x => x.EntityId).ToArray());

            var byId = await repo.SearchAsync(new AuditLogFilter(marker, "c", null, null, null), 1, 20);
            Assert.Equal(["c"], byId.Items.Select(x => x.EntityId).ToArray());

            // from is inclusive, to is exclusive
            var range = await repo.SearchAsync(
                new AuditLogFilter(marker, null, null, t0.AddHours(1), t0.AddHours(5)), 1, 20);
            Assert.Equal(["d", "c", "b"], range.Items.Select(x => x.EntityId).ToArray());

            // filters combine with AND
            var combined = await repo.SearchAsync(
                new AuditLogFilter(marker, null, AuditActorType.System, t0.AddHours(1), null), 1, 20);
            Assert.Equal(["d", "c"], combined.Items.Select(x => x.EntityId).ToArray());

            Assert.Empty(db.ChangeTracker.Entries()); // AsNoTracking
        }
        finally
        {
            await CleanAsync(marker);
        }
    }

    [Fact]
    public async Task Service_over_the_real_repository_returns_ADMIN_and_SYSTEM_text_and_UTC_times()
    {
        if (!IsSqlServerAvailable()) return;

        var at = new DateTime(2026, 10, 2, 9, 30, 0, DateTimeKind.Utc);
        string marker;
        await using (var seed = new AppDbContext(Options()))
        {
            marker = await SeedAsync(seed, (AuditActorType.Admin, "x", at), (AuditActorType.System, "y", at.AddMinutes(1)));
        }

        try
        {
            await using var db = new AppDbContext(Options());
            var result = await new AuditLogQueryService(new EfAuditLogReadRepository(db))
                .SearchAsync(marker, null, null, "2026-10-02T09:30:00Z", "2026-10-02T09:32:00Z", null, null);

            Assert.True(result.Success);
            Assert.Equal(["SYSTEM", "ADMIN"], result.Data!.Items.Select(i => i.ActorType).ToArray());
            Assert.All(result.Data.Items, i => Assert.Equal(DateTimeKind.Utc, i.ChangedAt.Kind));
            Assert.Equal(at, result.Data.Items[1].ChangedAt);
        }
        finally
        {
            await CleanAsync(marker);
        }
    }
}
