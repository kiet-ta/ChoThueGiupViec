using System.Reflection;
using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Admin;
using CommonService.Application.Features.Admin.Dtos;
using CommonService.Application.Features.Admin.Services;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Admin;
using CommonService.Infrastructure.Persistence;
using CommonService.Tests.Persistence;
using CommonService.WebAPI.Controllers.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Tests.Admin;

/// <summary>ADMIN-UAT-14 (#872): GET /api/v1/admin/workers, the Admin lookup of a worker (contract admin.md section 2.8).</summary>
public class AdminWorkerSearchTests
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

    private class RecordingRepository : IAdminWorkerSearchRepository
    {
        public AdminWorkerFilter? Filter { get; private set; }
        public int Page { get; private set; }
        public int PageSize { get; private set; }
        public int Calls { get; private set; }
        public List<AdminWorkerRow> Rows { get; } = [];
        public int Total { get; set; }

        public Task<(IReadOnlyList<AdminWorkerRow> Items, int Total)> SearchAsync(
            AdminWorkerFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            Calls++;
            Filter = filter;
            Page = page;
            PageSize = pageSize;
            return Task.FromResult(((IReadOnlyList<AdminWorkerRow>)Rows, Total));
        }
    }

    private static AdminWorkerQueryService Service(RecordingRepository repo) => new(repo);

    // ---- service: defaults, parsing, validation (no SQL Server needed) ---------------------------

    [Fact]
    public async Task Defaults_are_page_1_size_20_and_no_filter()
    {
        var repo = new RecordingRepository();

        var result = await Service(repo).SearchAsync(null, null, null, null, null).Outcome();

        Assert.True(result.Success);
        Assert.Equal((1, 20), (repo.Page, repo.PageSize));
        Assert.Equal(new AdminWorkerFilter(null, null, null, null), repo.Filter);
        Assert.Equal((1, 20, 0), (result.Data!.Page, result.Data.PageSize, result.Data.Total));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public async Task An_empty_q_is_no_text_filter(string? q)
    {
        var repo = new RecordingRepository();

        await Service(repo).SearchAsync(q, null, null, null, null).Outcome();

        Assert.Equal(new AdminWorkerFilter(null, null, null, null), repo.Filter);
    }

    [Fact]
    public async Task A_whole_number_is_also_a_worker_number_and_text_is_trimmed()
    {
        var repo = new RecordingRepository();

        await Service(repo).SearchAsync("  12 ", null, null, null, null).Outcome();
        Assert.Equal(new AdminWorkerFilter("12", 12, null, null), repo.Filter);

        await Service(repo).SearchAsync(" Tran Van A ", null, null, null, null).Outcome();
        Assert.Equal(new AdminWorkerFilter("Tran Van A", null, null, null), repo.Filter);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("99999999999")]
    [InlineData("12.5")]
    public async Task Text_that_is_not_a_worker_number_has_no_id(string q)
    {
        var repo = new RecordingRepository();

        await Service(repo).SearchAsync(q, null, null, null, null).Outcome();

        Assert.Null(repo.Filter!.WorkerId);
        Assert.Equal(q, repo.Filter.Text);
    }

    [Fact]
    public async Task Filters_are_case_insensitive_and_parsed_to_the_enums()
    {
        var repo = new RecordingRepository();

        await Service(repo).SearchAsync(null, " agency_staff ", "locked", null, null).Outcome();

        Assert.Equal(new AdminWorkerFilter(null, null, WorkerType.AgencyStaff, WorkStatus.Locked), repo.Filter);
    }

    [Theory]
    [InlineData("PENDING", WorkStatus.Pending)]
    [InlineData("IDLE", WorkStatus.Idle)]
    [InlineData("BUSY", WorkStatus.Busy)]
    [InlineData("LOCKED", WorkStatus.Locked)]
    [InlineData("RETIRED", WorkStatus.Retired)]
    public async Task Every_work_status_of_the_ticket_is_accepted(string text, WorkStatus expected)
    {
        var repo = new RecordingRepository();

        await Service(repo).SearchAsync(null, null, text, null, null).Outcome();

        Assert.Equal(expected, repo.Filter!.WorkStatus);
    }

    [Theory]
    [InlineData("page", "0", null)]
    [InlineData("page", "-1", null)]
    [InlineData("page", "x", null)]
    [InlineData("pageSize", null, "0")]
    [InlineData("pageSize", null, "51")]
    [InlineData("pageSize", null, "abc")]
    [InlineData("workerType", null, null)]
    [InlineData("workStatus", null, null)]
    [InlineData("q", null, null)]
    public async Task A_bad_value_is_a_400_naming_the_field_and_the_repository_is_not_called(string field, string? page, string? pageSize)
    {
        var repo = new RecordingRepository();

        var result = await Service(repo).SearchAsync(
            field == "q" ? new string('a', 101) : null,
            field == "workerType" ? "ADMIN" : null,
            field == "workStatus" ? "SLEEPING" : null,
            page, pageSize).Outcome();

        Assert.Equal(400, result.StatusCode);
        Assert.Contains(field, result.ValidationErrors!.Keys);
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task Page_size_boundaries_1_and_50_are_accepted_and_the_text_limit_is_100_characters()
    {
        var repo = new RecordingRepository();

        Assert.True((await Service(repo).SearchAsync(new string('a', 100), null, null, "3", "1").Outcome()).Success);
        Assert.Equal((3, 1), (repo.Page, repo.PageSize));
        Assert.True((await Service(repo).SearchAsync(null, null, null, null, "50").Outcome()).Success);
        Assert.Equal(50, repo.PageSize);
    }

    [Fact]
    public async Task Several_bad_values_are_all_reported_at_once()
    {
        var result = await Service(new RecordingRepository()).SearchAsync(null, "X", "Y", "0", "99").Outcome();

        Assert.Equal(["page", "pageSize", "workStatus", "workerType"], result.ValidationErrors!.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task A_row_is_returned_with_the_wire_values_of_its_enums()
    {
        var repo = new RecordingRepository { Total = 1 };
        repo.Rows.Add(new AdminWorkerRow(5, "Tran Van A", "0901234567", "079000000001", WorkerType.AgencyStaff, 3, "Cong ty A", WorkStatus.Locked, "APPROVED", true, 4.85m, 61));

        var data = (await Service(repo).SearchAsync("a", null, null, null, null).Outcome()).Data!;

        var item = Assert.Single(data.Items);
        Assert.Equal(("AGENCY_STAFF", "LOCKED", 3, "Cong ty A"), (item.WorkerType, item.WorkStatus, item.AgencyId, item.AgencyName));
        Assert.Equal((5, "Tran Van A", "0901234567", "079000000001", "APPROVED", true, 4.85m, 61),
            (item.WorkerId, item.FullName, item.PhoneNumber, item.NationalId, item.KycStatus, item.IsSuperFreelancer, item.RatingAvg, item.CompletedJobs));
        Assert.Equal(1, data.Total);
    }

    // ---- controller -----------------------------------------------------------------------------

    [Fact]
    public void Controller_requires_the_AdminOnly_policy_and_has_one_read_action_on_the_contract_route()
    {
        var type = typeof(AdminWorkersController);
        Assert.Equal("AdminOnly", type.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal("api/v1/admin/workers", type.GetCustomAttribute<RouteAttribute>()!.Template);

        var actions = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).ToList();
        var action = Assert.Single(actions);
        Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
        var verb = Assert.Single(action.GetCustomAttributes<HttpMethodAttribute>());
        Assert.Equal(["GET"], verb.HttpMethods);
        Assert.Null(verb.Template); // GET api/v1/admin/workers itself; ekyc-queue and the {id} actions stay in their own controllers
    }

    [Fact]
    public async Task Controller_returns_200_with_the_envelope_and_lets_a_400_reach_the_error_middleware()
    {
        var page = new AdminWorkerPageDto { Page = 1, PageSize = 20, Total = 0 };
        var ok = await new AdminWorkersController(new StubQueries(() => page)).Search(null, null, null, null, null, default);
        var body = Assert.IsType<ApiResponse<AdminWorkerPageDto>>(Assert.IsType<OkObjectResult>(ok).Value);
        Assert.True(body.Success);

        var errors = new Dictionary<string, string[]> { ["page"] = ["bad"] };
        var thrown = await Assert.ThrowsAsync<ValidationException>(() =>
            new AdminWorkersController(new StubQueries(() => throw new ValidationException("Validation failed", errors))).Search(null, null, null, "x", null, default));
        Assert.Same(errors, thrown.Errors);
    }

    private class StubQueries(Func<AdminWorkerPageDto> search) : IAdminWorkerQueryService
    {
        public Task<AdminWorkerPageDto> SearchAsync(string? q, string? workerType, string? workStatus, string? page, string? pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(search());
    }

    // ---- repository on the local SQL Server (the test rolls its transaction back) ----------------

    private static AppDbContext NewDb() => new StampingDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

    private static string Digits(int n) => string.Concat(Enumerable.Range(0, n).Select(_ => Random.Shared.Next(0, 10)));

    private static Worker Freelancer(AppDbContext db, string name, string phone, string nationalId)
    {
        var worker = Worker.CreateFreelancer(phone, nationalId, name);
        worker.SetKycResult(92m, "APPROVED");
        db.Workers.Add(worker);
        return worker;
    }

    [Fact]
    public async Task Repository_finds_by_name_phone_national_id_and_number_and_filters_and_pages_on_SQL_Server()
    {
        if (!IsSqlServerAvailable()) return;

        await using var db = NewDb();
        await using var transaction = await db.Database.BeginTransactionAsync();

        // A marker in every text so the test sees only its own workers whatever else the database holds.
        var marker = "Zq" + Digits(6);
        var phoneStem = "09" + Digits(4);
        var nid = Digits(7);
        var an = Freelancer(db, $"{marker} Tran Van An", phoneStem + "1111", "0" + nid + "01");
        var binh = Freelancer(db, $"{marker} Le Thi Binh", phoneStem + "2222", "0" + nid + "02");
        var agencyId = await SqlSeed.AgencyAsync(db);
        var staff = Worker.CreateAgencyStaff(agencyId, phoneStem + "3333", "0" + nid + "03", $"{marker} Pham Nhan Vien");
        db.Workers.Add(staff);
        await db.SaveChangesAsync();
        binh.TransitionTo(WorkStatus.Locked);
        await db.SaveChangesAsync();

        var repo = new EfAdminWorkerSearchRepository(db);
        async Task<List<int>> Ids(AdminWorkerFilter filter, int page = 1, int size = 50) => (await repo.SearchAsync(filter, page, size)).Items.Select(i => i.WorkerId).ToList();

        // by name (substring, case-insensitive), phone and national id
        Assert.Equal([staff.WorkerId, binh.WorkerId, an.WorkerId], await Ids(new AdminWorkerFilter(marker, null, null, null)));
        Assert.Equal([binh.WorkerId], await Ids(new AdminWorkerFilter(marker.ToLowerInvariant() + " le thi", null, null, null)));
        Assert.Equal([an.WorkerId], await Ids(new AdminWorkerFilter(phoneStem + "1111", null, null, null)));
        Assert.Equal([staff.WorkerId], await Ids(new AdminWorkerFilter("0" + nid + "03", null, null, null)));

        // by number: that worker comes first even when others match the digits, and a number matches the id as well as the text
        var byNumber = await repo.SearchAsync(new AdminWorkerFilter(binh.WorkerId.ToString(), binh.WorkerId, null, null), 1, 50);
        Assert.Equal(binh.WorkerId, byNumber.Items[0].WorkerId);

        // filters: type, status, both, and no match
        Assert.Equal([staff.WorkerId], await Ids(new AdminWorkerFilter(marker, null, WorkerType.AgencyStaff, null)));
        Assert.Equal([binh.WorkerId, an.WorkerId], await Ids(new AdminWorkerFilter(marker, null, WorkerType.Freelancer, null)));
        Assert.Equal([binh.WorkerId], await Ids(new AdminWorkerFilter(marker, null, null, WorkStatus.Locked)));
        Assert.Empty(await Ids(new AdminWorkerFilter(marker, null, WorkerType.AgencyStaff, WorkStatus.Locked)));
        Assert.Empty(await Ids(new AdminWorkerFilter(marker + "nothing", null, null, null)));

        // the agency name of an agency worker comes from the join; a freelancer has none
        var rows = (await repo.SearchAsync(new AdminWorkerFilter(marker, null, null, null), 1, 50)).Items.ToDictionary(r => r.WorkerId);
        Assert.Equal((agencyId, true), (rows[staff.WorkerId].AgencyId, rows[staff.WorkerId].AgencyName!.StartsWith("Test Agency")));
        Assert.Equal((null, null), (rows[an.WorkerId].AgencyId, rows[an.WorkerId].AgencyName));
        Assert.Equal((WorkerType.AgencyStaff, WorkStatus.Locked, "APPROVED"), (rows[staff.WorkerId].WorkerType, rows[binh.WorkerId].WorkStatus, rows[an.WorkerId].KycStatus));

        // paging: the total counts every match, the page holds a slice, beyond the last page is empty
        var page1 = await repo.SearchAsync(new AdminWorkerFilter(marker, null, null, null), 1, 2);
        Assert.Equal((3, 2), (page1.Total, page1.Items.Count));
        var page2 = await repo.SearchAsync(new AdminWorkerFilter(marker, null, null, null), 2, 2);
        Assert.Equal((3, 1), (page2.Total, page2.Items.Count));
        Assert.Empty((await repo.SearchAsync(new AdminWorkerFilter(marker, null, null, null), 9, 2)).Items);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Repository_with_no_text_lists_every_worker_newest_first_and_treats_percent_and_underscore_as_text_on_SQL_Server()
    {
        if (!IsSqlServerAvailable()) return;

        await using var db = NewDb();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var first = Freelancer(db, "Zq Percent 100% Worker", "09" + Digits(8), Digits(12));
        var second = Freelancer(db, "Zq Under_score Worker", "09" + Digits(8), Digits(12));
        await db.SaveChangesAsync();

        var repo = new EfAdminWorkerSearchRepository(db);
        var all = await repo.SearchAsync(new AdminWorkerFilter(null, null, null, null), 1, 50);
        Assert.True(all.Total >= 2);
        Assert.Equal(all.Items.Select(i => i.WorkerId).OrderByDescending(i => i), all.Items.Select(i => i.WorkerId)); // newest first
        Assert.Contains(first.WorkerId, all.Items.Select(i => i.WorkerId));

        Assert.Equal([first.WorkerId], (await repo.SearchAsync(new AdminWorkerFilter("100%", null, null, null), 1, 50)).Items.Select(i => i.WorkerId));
        Assert.Equal([second.WorkerId], (await repo.SearchAsync(new AdminWorkerFilter("Under_score", null, null, null), 1, 50)).Items.Select(i => i.WorkerId));

        await transaction.RollbackAsync();
    }
}
