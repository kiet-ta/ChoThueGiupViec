using System.Reflection;
using CommonService.Application.Common.Models;
using CommonService.Application.Common.Options;
using CommonService.Application.Features.Admin;
using CommonService.Application.Features.Admin.Dtos;
using CommonService.Application.Features.Admin.Services;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Admin;
using CommonService.Infrastructure.Persistence;
using CommonService.WebAPI.Controllers.Admin;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Tests.Admin;

/// <summary>BE-M6-03: Admin queue and approval of "customer absent" reports (BR-05, decision Q10, contract admin.md 2.2).</summary>
public class AbsenceReportTests
{
    private const string ConnectionString =
        "Server=localhost;Database=ChoThueGiupViec;Integrated Security=True;TrustServerCertificate=True;";

    private static readonly DateTime Now = new(2026, 10, 7, 4, 0, 0, DateTimeKind.Utc);
    private const int AdminId = 3;
    private const long AssignmentId = 70;

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

    private sealed class TestClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => local.AddHours(-7);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(utcNow));
    }

    private sealed class RecordingPublisher : IPublisher
    {
        public List<CustomerAbsentApproved> Events { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            if (notification is CustomerAbsentApproved e) Events.Add(e);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            Publish((object)notification, cancellationToken);
    }

    private sealed class RefusingRefunds : IRefundService
    {
        public int Calls { get; private set; }

        public Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new RefundResult(false, 0m, "The wallet is closed."));
        }
    }

    private sealed class TrackingUnitOfWork : IUnitOfWork
    {
        public int Committed { get; private set; }
        public int RolledBack { get; private set; }
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.FromResult(0);
        }

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await action();
                Committed++;
                return result;
            }
            catch
            {
                RolledBack++;
                throw;
            }
        }
    }

    private sealed class MemoryAbsence : IAbsenceRepository
    {
        public List<AbsenceReportRow> Rows { get; } = [];
        public int MarkCalls { get; private set; }
        public Action? BeforeMark { get; set; }
        public string? LastStatus { get; private set; }
        public int LastPage { get; private set; }
        public int LastPageSize { get; private set; }

        public Task<(IReadOnlyList<AbsenceReportRow> Items, int Total)> SearchAsync(string status, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            LastStatus = status;
            LastPage = page;
            LastPageSize = pageSize;
            var matching = Rows
                .Where(r => status switch
                {
                    AbsenceConstants.Approved => r.AssignmentStatus == JobAssignmentStatus.Absent,
                    AbsenceConstants.Rejected => r.AssignmentStatus != JobAssignmentStatus.Absent && r.Rejected,
                    _ => r.AssignmentStatus != JobAssignmentStatus.Absent && !r.Rejected,
                })
                .OrderBy(r => r.CustomerAbsentAt).ThenBy(r => r.AssignmentId)
                .ToList();
            return Task.FromResult<(IReadOnlyList<AbsenceReportRow>, int)>((matching.Skip((page - 1) * pageSize).Take(pageSize).ToList(), matching.Count));
        }

        public Task<AbsenceReportRow?> GetAsync(long assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.FirstOrDefault(r => r.AssignmentId == assignmentId));

        public List<int> ActiveAdmins { get; } = [];
        public Dictionary<long, int> CustomerOfOrder { get; } = [];

        public Task<IReadOnlyList<int>> GetActiveAdminIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<int>>(ActiveAdmins.ToList());

        public Task<int?> GetCustomerIdOfOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(CustomerOfOrder.TryGetValue(orderId, out var id) ? id : (int?)null);

        public Task<bool> TryMarkAbsentAsync(long assignmentId, decimal absenceFeeAmount, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            MarkCalls++;
            BeforeMark?.Invoke();
            var i = Rows.FindIndex(r => r.AssignmentId == assignmentId);
            if (i < 0 || Rows[i].AssignmentStatus != JobAssignmentStatus.CheckedIn) return Task.FromResult(false);
            Rows[i] = Rows[i] with { AssignmentStatus = JobAssignmentStatus.Absent, AbsenceFeeAmount = absenceFeeAmount };
            return Task.FromResult(true);
        }
    }

    private sealed class Harness
    {
        public MemoryAbsence Repo { get; } = new();
        public FakeRefundService Refunds { get; } = new();
        public FakeAuditLog Audit { get; } = new();
        public TrackingUnitOfWork Uow { get; } = new();
        public RecordingPublisher Publisher { get; } = new();
        public IRefundService RefundPort { get; set; }
        public DateTime ClockNow { get; set; } = Now;

        public Harness()
        {
            RefundPort = Refunds;
        }

        public AbsenceReportService Service() =>
            new(Repo, RefundPort, Audit, Uow, new TestClock(ClockNow), Publisher,
                Microsoft.Extensions.Options.Options.Create(new BusinessRules()));

        /// <summary>A report whose Q10 conditions all hold: GPS verified, 2 calls, checked in 30 minutes ago.</summary>
        public AbsenceReportRow Add(long id = AssignmentId, decimal gross = 260000m, Func<AbsenceReportRow, AbsenceReportRow>? change = null)
        {
            var row = new AbsenceReportRow(
                id, 900 + id, "ORD" + id, 40, "Worker A", WorkerType.Freelancer, null, "Customer B",
                JobAssignmentStatus.CheckedIn, gross, null,
                Now.AddMinutes(-30), Now.AddMinutes(-2), true, 12.5m, 10.7m, 106.6m, 2, "/uploads/door.jpg", false);
            if (change is not null) row = change(row);
            Repo.Rows.Add(row);
            return row;
        }
    }

    // ---- queue ----------------------------------------------------------------------------------

    [Fact]
    public async Task The_queue_defaults_to_PENDING_oldest_report_first_and_each_item_says_whether_it_can_be_approved()
    {
        var h = new Harness();
        h.Add(3, change: r => r with { CustomerAbsentAt = Now.AddMinutes(-1) });
        h.Add(1, change: r => r with { CustomerAbsentAt = Now.AddMinutes(-9) });
        h.Add(2, change: r => r with { GpsVerified = false, CallAttempts = 1, CustomerAbsentAt = Now.AddMinutes(-5) });
        h.Add(4, change: r => r with { AssignmentStatus = JobAssignmentStatus.Absent, AbsenceFeeAmount = 104000m });

        var result = await h.Service().SearchAsync(null, null, null);

        Assert.True(result.Success);
        Assert.Equal("PENDING", h.Repo.LastStatus);
        Assert.Equal([1L, 2L, 3L], result.Data!.Items.Select(i => i.AssignmentId).ToArray());
        Assert.Equal(3, result.Data.Total);
        Assert.Equal((1, 20), (result.Data.Page, result.Data.PageSize));
        Assert.True(result.Data.Items[0].CanApprove);
        Assert.Empty(result.Data.Items[0].BlockReasons);
        var blocked = result.Data.Items[1];
        Assert.False(blocked.CanApprove);
        Assert.Equal(["GPS_NOT_VERIFIED", "CALLS_BELOW_MINIMUM"], blocked.BlockReasons.ToArray());
    }

    [Fact]
    public async Task The_other_statuses_list_decided_reports_and_paging_is_applied()
    {
        var h = new Harness();
        h.Add(1, change: r => r with { AssignmentStatus = JobAssignmentStatus.Absent, AbsenceFeeAmount = 104000m });
        h.Add(2, change: r => r with { Rejected = true });
        h.Add(3);

        var approved = await h.Service().SearchAsync("approved", "1", "1");
        var rejected = await h.Service().SearchAsync("REJECTED", null, null);

        Assert.Equal([1L], approved.Data!.Items.Select(i => i.AssignmentId).ToArray());
        Assert.Equal("APPROVED", approved.Data.Items[0].Status);
        Assert.Equal(104000m, approved.Data.Items[0].AbsenceFeeAmount); // the stored fee once approved
        Assert.False(approved.Data.Items[0].CanApprove);
        Assert.Equal([2L], rejected.Data!.Items.Select(i => i.AssignmentId).ToArray());
        Assert.Equal("REJECTED", rejected.Data.Items[0].Status);
        Assert.False(rejected.Data.Items[0].CanApprove);
    }

    [Theory]
    [InlineData("DONE", null, null, "status")]
    [InlineData(null, "0", null, "page")]
    [InlineData(null, "x", null, "page")]
    [InlineData(null, null, "0", "pageSize")]
    [InlineData(null, null, "101", "pageSize")]
    public async Task Bad_query_values_are_a_400_with_the_field_name(string? status, string? page, string? pageSize, string field)
    {
        var result = await new Harness().Service().SearchAsync(status, page, pageSize);

        Assert.Equal(400, result.StatusCode);
        Assert.Contains(field, result.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task A_report_shows_the_40_percent_preview_in_whole_VND_the_refund_and_the_waited_minutes()
    {
        var h = new Harness();
        h.Add(gross: 260001m);

        var result = await h.Service().GetAsync(AssignmentId);

        var dto = result.Data!;
        Assert.Equal(104000m, dto.AbsenceFeeAmount); // 104000.4 rounded
        Assert.Equal(156001m, dto.CustomerRefundAmount);
        Assert.Equal(30, dto.WaitedMinutes);
        Assert.Equal("FREELANCER", dto.WorkerType);
        Assert.Equal("/uploads/door.jpg", dto.PhotoUrl);
        Assert.Equal(DateTimeKind.Utc, dto.CustomerAbsentAt.Kind);
        Assert.Equal(404, (await h.Service().GetAsync(999)).StatusCode);
    }

    // ---- approve: refusals ----------------------------------------------------------------------

    [Fact]
    public async Task A_missing_report_is_a_404()
    {
        var result = await new Harness().Service().ApproveAsync(AdminId, 999);

        Assert.Equal(404, result.StatusCode);
    }

    [Theory]
    [InlineData("gps", "GPS_NOT_VERIFIED")]
    [InlineData("calls", "CALLS_BELOW_MINIMUM")]
    [InlineData("wait", "WAIT_BELOW_MINIMUM")]
    public async Task Each_Q10_condition_alone_refuses_the_approval_and_names_itself(string which, string expected)
    {
        var h = new Harness();
        h.Add(change: r => which switch
        {
            "gps" => r with { GpsVerified = false },
            "calls" => r with { CallAttempts = 1 },
            _ => r with { CheckedInAt = Now.AddMinutes(-14).AddSeconds(-59) },
        });

        var result = await h.Service().ApproveAsync(AdminId, AssignmentId);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal([expected], result.BlockReasons!.ToArray());
        Assert.Empty(h.Refunds.Requests);
        Assert.Empty(h.Audit.Entries);
        Assert.Equal(0, h.Repo.MarkCalls);
    }

    [Fact]
    public async Task All_failing_conditions_are_listed_together()
    {
        var h = new Harness();
        h.Add(change: r => r with { GpsVerified = false, CallAttempts = 0, CheckedInAt = Now.AddMinutes(-3) });

        var result = await h.Service().ApproveAsync(AdminId, AssignmentId);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal(["GPS_NOT_VERIFIED", "CALLS_BELOW_MINIMUM", "WAIT_BELOW_MINIMUM"], result.BlockReasons!.ToArray());
    }

    [Fact]
    public async Task Exactly_15_minutes_and_exactly_2_calls_are_enough()
    {
        var h = new Harness();
        h.Add(change: r => r with { CheckedInAt = Now.AddMinutes(-15), CallAttempts = 2 });

        var result = await h.Service().ApproveAsync(AdminId, AssignmentId);

        Assert.True(result.Success);
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("rejected")]
    public async Task A_decided_report_is_a_409_and_a_second_call_changes_nothing(string state)
    {
        var h = new Harness();
        h.Add(change: r => state == "approved"
            ? r with { AssignmentStatus = JobAssignmentStatus.Absent, AbsenceFeeAmount = 104000m }
            : r with { Rejected = true });

        var result = await h.Service().ApproveAsync(AdminId, AssignmentId);

        Assert.Equal(409, result.StatusCode);
        Assert.Null(result.BlockReasons);
        Assert.Empty(h.Refunds.Requests);
        Assert.Empty(h.Audit.Entries);
        Assert.Empty(h.Publisher.Events);
    }

    [Fact]
    public async Task An_assignment_that_is_no_longer_CHECKED_IN_cannot_become_ABSENT_and_is_a_409()
    {
        var h = new Harness();
        h.Add(change: r => r with { AssignmentStatus = JobAssignmentStatus.InProgress }); // the customer came and the work started

        var result = await h.Service().ApproveAsync(AdminId, AssignmentId);

        Assert.Equal(409, result.StatusCode);
        Assert.Contains("IN_PROGRESS", result.ErrorMessage);
        Assert.Empty(h.Refunds.Requests);
        Assert.Equal(0, h.Repo.MarkCalls);
    }

    [Fact]
    public async Task Losing_the_race_to_another_admin_is_a_409_with_no_refund_audit_or_event()
    {
        var h = new Harness();
        h.Add();
        h.Repo.BeforeMark = () => h.Repo.Rows[0] = h.Repo.Rows[0] with { AssignmentStatus = JobAssignmentStatus.Absent };

        var result = await h.Service().ApproveAsync(AdminId, AssignmentId);

        Assert.Equal(409, result.StatusCode);
        Assert.Empty(h.Refunds.Requests);
        Assert.Empty(h.Audit.Entries);
        Assert.Empty(h.Publisher.Events);
    }

    // ---- approve: effects -----------------------------------------------------------------------

    [Fact]
    public async Task An_approval_marks_ABSENT_with_the_40_percent_fee_refunds_the_60_percent_audits_once_and_publishes()
    {
        var h = new Harness();
        h.Add(gross: 260001m);

        var result = await h.Service().ApproveAsync(AdminId, AssignmentId);

        Assert.True(result.Success);
        Assert.Equal("APPROVED", result.Data!.Status);
        Assert.Equal(104000m, result.Data.AbsenceFeeAmount);
        Assert.Equal(156001m, result.Data.CustomerRefundAmount);
        Assert.Equal(JobAssignmentStatus.Absent, h.Repo.Rows[0].AssignmentStatus);
        Assert.Equal(104000m, h.Repo.Rows[0].AbsenceFeeAmount);

        var refund = Assert.Single(h.Refunds.Requests);
        Assert.Equal(900 + AssignmentId, refund.OrderId);
        Assert.Equal(156001m, refund.Amount); // gross - fee, so the two parts add up exactly

        var row = Assert.Single(h.Audit.Entries);
        Assert.Equal(AuditActorType.Admin, row.ActorType);
        Assert.Equal(AdminId, row.AdminId);
        Assert.Equal("JOB_ASSIGNMENT", row.EntityType);
        Assert.Equal("70", row.EntityId);
        Assert.Equal("absence_report", row.FieldName);
        Assert.Equal("PENDING", row.OldValue);
        Assert.Equal("APPROVED", row.NewValue);
        Assert.Equal("Customer absent approved: fee 104000, refund 156001", row.Reason);

        var published = Assert.Single(h.Publisher.Events);
        Assert.Equal(AssignmentId, published.AssignmentId);
        Assert.Equal(970L, published.OrderId);
        Assert.Equal(40, published.WorkerId);
        Assert.Equal(104000m, published.CompensationAmount);
        Assert.Equal(156001m, published.RefundAmount);
        Assert.Equal(Now, published.ApprovedAtUtc);
        Assert.Equal(1, h.Uow.Committed);
    }

    [Fact]
    public async Task A_refused_refund_is_a_502_and_the_unit_of_work_is_undone_with_no_audit_or_event()
    {
        var h = new Harness();
        h.Add();
        var refusing = new RefusingRefunds();
        h.RefundPort = refusing;

        var result = await h.Service().ApproveAsync(AdminId, AssignmentId);

        Assert.Equal(502, result.StatusCode);
        Assert.Equal("The wallet is closed.", result.ErrorMessage);
        Assert.Equal(1, refusing.Calls);
        Assert.Equal(1, h.Uow.RolledBack);
        Assert.Equal(0, h.Uow.Committed);
        Assert.Empty(h.Audit.Entries);
        Assert.Empty(h.Publisher.Events);
    }

    // ---- reject ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task A_rejection_needs_a_reason(string? reason)
    {
        var h = new Harness();
        h.Add();

        var result = await h.Service().RejectAsync(AdminId, AssignmentId, reason);

        Assert.Equal(400, result.StatusCode);
        Assert.Contains("reason", result.ValidationErrors!.Keys);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task A_reason_of_256_characters_is_a_400_and_255_is_accepted()
    {
        var h = new Harness();
        h.Add();

        var tooLong = await h.Service().RejectAsync(AdminId, AssignmentId, new string('x', 256));
        var ok = await h.Service().RejectAsync(AdminId, AssignmentId, new string('x', 255));

        Assert.Equal(400, tooLong.StatusCode);
        Assert.True(ok.Success);
    }

    [Fact]
    public async Task A_rejection_writes_one_audit_row_and_leaves_the_assignment_and_the_money_alone()
    {
        var h = new Harness();
        h.Add();

        var result = await h.Service().RejectAsync(AdminId, AssignmentId, "  The customer was at home  ");

        Assert.True(result.Success);
        var row = Assert.Single(h.Audit.Entries);
        Assert.Equal("absence_report", row.FieldName);
        Assert.Equal("PENDING", row.OldValue);
        Assert.Equal("REJECTED", row.NewValue);
        Assert.Equal("The customer was at home", row.Reason);
        Assert.Equal(1, h.Uow.Saves);
        Assert.Equal(JobAssignmentStatus.CheckedIn, h.Repo.Rows[0].AssignmentStatus); // question A4
        Assert.Empty(h.Refunds.Requests);
        Assert.Empty(h.Publisher.Events);
    }

    [Fact]
    public async Task Rejecting_a_missing_or_decided_report_is_a_404_or_409()
    {
        var h = new Harness();
        h.Add(1, change: r => r with { Rejected = true });
        h.Add(2, change: r => r with { AssignmentStatus = JobAssignmentStatus.Absent });

        Assert.Equal(404, (await h.Service().RejectAsync(AdminId, 99, "x")).StatusCode);
        Assert.Equal(409, (await h.Service().RejectAsync(AdminId, 1, "x")).StatusCode);
        Assert.Equal(409, (await h.Service().RejectAsync(AdminId, 2, "x")).StatusCode);
        Assert.Empty(h.Audit.Entries);
    }

    // ---- controller -----------------------------------------------------------------------------

    private sealed class FakeUser(int? id) : ICurrentUser
    {
        public bool IsAuthenticated => id is not null;
        public int? UserId => id;
        public UserRole? Role => null;
    }

    private sealed class StubService(int status) : IAbsenceReportService
    {
        public (int Admin, long Assignment)? LastCall { get; private set; }

        private AbsenceResult<AbsenceReportDto> Answer() => status switch
        {
            200 => AbsenceResult<AbsenceReportDto>.Ok(new AbsenceReportDto { AssignmentId = 1 }),
            400 => AbsenceResult<AbsenceReportDto>.ValidationError(new Dictionary<string, string[]> { ["reason"] = ["bad"] }),
            404 => AbsenceResult<AbsenceReportDto>.NotFound(),
            409 => AbsenceResult<AbsenceReportDto>.Blocked(["GPS_NOT_VERIFIED"]),
            _ => AbsenceResult<AbsenceReportDto>.BadGateway("refund refused"),
        };

        public Task<AbsenceResult<AbsenceReportPageDto>> SearchAsync(string? status2, string? page, string? pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(AbsenceResult<AbsenceReportPageDto>.Ok(new AbsenceReportPageDto()));

        public Task<AbsenceResult<AbsenceReportDto>> GetAsync(long assignmentId, CancellationToken cancellationToken = default) => Task.FromResult(Answer());

        public Task<AbsenceResult<AbsenceReportDto>> ApproveAsync(int adminId, long assignmentId, CancellationToken cancellationToken = default)
        {
            LastCall = (adminId, assignmentId);
            return Task.FromResult(Answer());
        }

        public Task<AbsenceResult<AbsenceReportDto>> RejectAsync(int adminId, long assignmentId, string? reason, CancellationToken cancellationToken = default)
        {
            LastCall = (adminId, assignmentId);
            return Task.FromResult(Answer());
        }
    }

    [Fact]
    public void The_controller_is_admin_only_with_the_contract_route_and_actions()
    {
        var type = typeof(AdminAbsenceReportsController);
        Assert.Equal("AdminOnly", type.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal("api/admin/absence-reports", type.GetCustomAttribute<RouteAttribute>()!.Template);

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.All(methods, m => Assert.Null(m.GetCustomAttribute<AllowAnonymousAttribute>()));
        var routes = methods
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Select(a => $"{a.HttpMethods.Single()} {a.Template}".Trim()))
            .Order().ToArray();
        Assert.Equal(
            ["GET", "GET {assignmentId:long}", "POST {assignmentId:long}/approve", "POST {assignmentId:long}/reject"], routes);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(502)]
    public async Task Approve_and_reject_map_the_result_and_take_the_admin_id_from_the_token(int status)
    {
        var service = new StubService(status);
        var controller = new AdminAbsenceReportsController(service, new FakeUser(7));

        var approve = Assert.IsType<ObjectResult>(await controller.Approve(5, default));
        Assert.Equal(status, approve.StatusCode);
        Assert.Equal((7, 5L), service.LastCall);

        var reject = Assert.IsType<ObjectResult>(await controller.Reject(6, new RejectAbsenceRequestDto { Reason = "x" }, default));
        Assert.Equal(status, reject.StatusCode);
        Assert.Equal((7, 6L), service.LastCall);
    }

    [Fact]
    public async Task A_409_with_block_reasons_carries_them_in_the_data_and_a_400_carries_the_errors_map()
    {
        var blocked = Assert.IsType<ObjectResult>(await new AdminAbsenceReportsController(new StubService(409), new FakeUser(7)).Approve(5, default));
        var data = Assert.IsType<ApiResponse<object>>(blocked.Value).Data!;
        Assert.Contains("blockReasons", data.GetType().GetProperties().Select(p => p.Name));

        var bad = Assert.IsType<ObjectResult>(await new AdminAbsenceReportsController(new StubService(400), new FakeUser(7)).Reject(5, new RejectAbsenceRequestDto(), default));
        Assert.Contains("errors", Assert.IsType<ApiResponse<object>>(bad.Value).Data!.GetType().GetProperties().Select(p => p.Name));
    }

    [Fact]
    public async Task Without_a_user_id_the_decisions_answer_401_and_a_missing_reject_body_is_a_400_before_the_service_is_called()
    {
        var service = new StubService(200);
        var anonymous = new AdminAbsenceReportsController(service, new FakeUser(null));
        Assert.IsType<UnauthorizedObjectResult>(await anonymous.Approve(5, default));
        Assert.IsType<UnauthorizedObjectResult>(await anonymous.Reject(5, new RejectAbsenceRequestDto(), default));
        Assert.IsType<BadRequestObjectResult>(await new AdminAbsenceReportsController(service, new FakeUser(7)).Reject(5, null!, default));
        Assert.Null(service.LastCall);
    }

    // ---- SQL Server -----------------------------------------------------------------------------

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;

    private sealed record Seeded(int AdminId, int CustomerId, int AddressId, int WorkerId, long OrderId, int SlotId, long AssignmentId);

    /// <summary>A customer, a freelancer and one CHECKED_IN assignment of 260000 VND with a check-in that reported the customer absent.</summary>
    private static async Task<Seeded> SeedAsync(DateTime now, bool gpsVerified = true, byte calls = 2)
    {
        await using var db = new AppDbContext(Options());

        var admin = new AdminAccount
        {
            Email = "abs-" + Guid.NewGuid().ToString("N")[..10] + "@example.test",
            FullName = "Absence Admin",
            PasswordHash = "x",
            AdminRole = "SUPER_ADMIN",
            IsActive = true,
            CreatedAt = now,
        };
        db.Admins.Add(admin);

        var customer = new Customer
        {
            PhoneNumber = "091" + Random.Shared.Next(1000000, 9999999),
            FullName = "Absent Customer",
            AccountStatus = CustomerAccountStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var address = new CustomerAddress
        {
            CustomerId = customer.CustomerId,
            Label = "Home",
            AddressLine = "1 Test St",
            District = "D1",
            City = "HCMC",
            HousingType = HousingType.House,
            FloorAreaM2 = 40m,
            NumFloors = 2,
            Latitude = 10.76m,
            Longitude = 106.66m,
            IsDefault = true,
            CreatedAt = now,
        };
        db.CustomerAddresses.Add(address);

        var worker = new Worker
        {
            PhoneNumber = "092" + Random.Shared.Next(1000000, 9999999),
            NationalId = "079" + Random.Shared.Next(100000000, 999999999),
            FullName = "Absent Worker",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var order = new JobOrder
        {
            OrderCode = "ORD" + Random.Shared.Next(100000, 999999),
            CustomerId = customer.CustomerId,
            AddressId = address.AddressId,
            ServiceTier = ServiceTier.Economy,
            ScheduledDate = DateOnly.FromDateTime(now),
            ShiftCode = "SHIFT1",
            AreaSnapshotM2 = 80m,
            RequiredWorkers = 1,
            TotalAmount = 260000m,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.JobOrders.Add(order);
        await db.SaveChangesAsync();

        var slot = new BookingSlot
        {
            WorkerId = worker.WorkerId,
            SlotDate = order.ScheduledDate,
            ShiftCode = "SHIFT1",
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(12, 0),
            SlotSource = "MANUAL",
            SlotStatus = "LOCKED",
            UpdatedAt = now,
        };
        db.BookingSlots.Add(slot);
        await db.SaveChangesAsync();

        var assignment = new JobAssignment
        {
            OrderId = order.OrderId,
            CustomerId = customer.CustomerId,
            WorkerId = worker.WorkerId,
            SlotId = slot.SlotId,
            ServiceTier = ServiceTier.Economy,
            AssignmentSeq = 1,
            DispatchRadiusKm = 5,
            GrossAmount = 260000m,
            CommissionRate = 0.200m,
            PayoutAmount = 208000m,
            CreatedAt = now,
            UpdatedAt = now,
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        assignment.TransitionTo(JobAssignmentStatus.CheckedIn);
        db.JobAssignments.Add(assignment);
        await db.SaveChangesAsync();

        db.CheckInLogs.Add(new CheckInLog
        {
            AssignmentId = assignment.AssignmentId,
            DeviceLat = 10.76m,
            DeviceLng = 106.66m,
            DistanceM = 12.5m,
            GpsVerified = gpsVerified,
            CallAttempts = calls,
            CustomerAbsentAt = now.AddMinutes(-1),
            CheckedInAt = now.AddMinutes(-30),
        });
        await db.SaveChangesAsync();

        return new Seeded(admin.AdminId, customer.CustomerId, address.AddressId, worker.WorkerId, order.OrderId, slot.SlotId, assignment.AssignmentId);
    }

    private static async Task CleanAsync(Seeded s)
    {
        await using var db = new AppDbContext(Options());
        await db.AdminAuditLogs.Where(a => a.EntityType == "JOB_ASSIGNMENT" && a.EntityId == s.AssignmentId.ToString()).ExecuteDeleteAsync();
        await db.CheckInLogs.Where(c => c.AssignmentId == s.AssignmentId).ExecuteDeleteAsync();
        await db.JobAssignments.Where(a => a.AssignmentId == s.AssignmentId).ExecuteDeleteAsync();
        await db.BookingSlots.Where(b => b.SlotId == s.SlotId).ExecuteDeleteAsync();
        await db.JobOrders.Where(o => o.OrderId == s.OrderId).ExecuteDeleteAsync();
        await db.CustomerAddresses.Where(a => a.AddressId == s.AddressId).ExecuteDeleteAsync();
        await db.Customers.Where(c => c.CustomerId == s.CustomerId).ExecuteDeleteAsync();
        await db.Workers.Where(w => w.WorkerId == s.WorkerId).ExecuteDeleteAsync();
        await db.Admins.Where(a => a.AdminId == s.AdminId).ExecuteDeleteAsync();
    }

    private static AbsenceReportService Real(AppDbContext db, DateTime now, IRefundService refunds, RecordingPublisher publisher) =>
        new(new EfAbsenceRepository(db), refunds, new EfAuditLog(db, new TestClock(now)), new UnitOfWork(db), new TestClock(now), publisher,
            Microsoft.Extensions.Options.Options.Create(new BusinessRules()));

    [Fact]
    public async Task Real_database_the_report_moves_from_PENDING_to_APPROVED_with_the_fee_stored_the_refund_and_one_audit_row()
    {
        if (!IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var s = await SeedAsync(now);
        try
        {
            var refunds = new FakeRefundService();
            var publisher = new RecordingPublisher();

            await using (var db = new AppDbContext(Options()))
            {
                var service = Real(db, now, refunds, publisher);

                var pending = await service.GetAsync(s.AssignmentId);
                Assert.Equal("PENDING", pending.Data!.Status);
                Assert.True(pending.Data.CanApprove);
                Assert.Equal("Absent Customer", pending.Data.CustomerName);
                Assert.Equal("Absent Worker", pending.Data.WorkerName);
                Assert.Equal(104000m, pending.Data.AbsenceFeeAmount);

                var queue = await service.SearchAsync(null, null, "100");
                Assert.Contains(queue.Data!.Items, i => i.AssignmentId == s.AssignmentId);

                var approved = await service.ApproveAsync(s.AdminId, s.AssignmentId);
                Assert.True(approved.Success);
                Assert.Equal("APPROVED", approved.Data!.Status);
            }

            await using var verify = new AppDbContext(Options());
            var row = await verify.JobAssignments.AsNoTracking().SingleAsync(a => a.AssignmentId == s.AssignmentId);
            Assert.Equal(JobAssignmentStatus.Absent, row.AssignmentStatus);
            Assert.Equal(104000m, row.AbsenceFeeAmount);

            var refund = Assert.Single(refunds.Requests);
            Assert.Equal(s.OrderId, refund.OrderId);
            Assert.Equal(156000m, refund.Amount);

            var audit = Assert.Single(await verify.AdminAuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "JOB_ASSIGNMENT" && a.EntityId == s.AssignmentId.ToString()).ToListAsync());
            Assert.Equal("absence_report", audit.FieldName);
            Assert.Equal("APPROVED", audit.NewValue);

            var published = Assert.Single(publisher.Events);
            Assert.Equal(104000m, published.CompensationAmount);
            Assert.Equal(156000m, published.RefundAmount);

            await using var db2 = new AppDbContext(Options());
            var service2 = Real(db2, now, refunds, publisher);
            Assert.DoesNotContain((await service2.SearchAsync(null, null, "100")).Data!.Items, i => i.AssignmentId == s.AssignmentId);
            Assert.Contains((await service2.SearchAsync("APPROVED", null, "100")).Data!.Items, i => i.AssignmentId == s.AssignmentId);
            Assert.Equal(409, (await service2.ApproveAsync(s.AdminId, s.AssignmentId)).StatusCode);
        }
        finally
        {
            await CleanAsync(s);
        }
    }

    [Fact]
    public async Task Real_database_missing_GPS_refuses_the_approval_and_changes_nothing()
    {
        if (!IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var s = await SeedAsync(now, gpsVerified: false, calls: 1);
        try
        {
            var refunds = new FakeRefundService();
            await using var db = new AppDbContext(Options());

            var result = await Real(db, now, refunds, new RecordingPublisher()).ApproveAsync(s.AdminId, s.AssignmentId);

            Assert.Equal(409, result.StatusCode);
            Assert.Equal(["GPS_NOT_VERIFIED", "CALLS_BELOW_MINIMUM"], result.BlockReasons!.ToArray());
            Assert.Empty(refunds.Requests);
            await using var verify = new AppDbContext(Options());
            Assert.Equal(JobAssignmentStatus.CheckedIn,
                (await verify.JobAssignments.AsNoTracking().SingleAsync(a => a.AssignmentId == s.AssignmentId)).AssignmentStatus);
        }
        finally
        {
            await CleanAsync(s);
        }
    }

    [Fact]
    public async Task Real_database_a_rejection_moves_the_report_to_REJECTED_and_leaves_the_assignment_CHECKED_IN()
    {
        if (!IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var s = await SeedAsync(now);
        try
        {
            await using (var db = new AppDbContext(Options()))
            {
                var result = await Real(db, now, new FakeRefundService(), new RecordingPublisher()).RejectAsync(s.AdminId, s.AssignmentId, "The customer was at home");
                Assert.True(result.Success);
                Assert.Equal("REJECTED", result.Data!.Status);
            }

            await using var verify = new AppDbContext(Options());
            var row = await verify.JobAssignments.AsNoTracking().SingleAsync(a => a.AssignmentId == s.AssignmentId);
            Assert.Equal(JobAssignmentStatus.CheckedIn, row.AssignmentStatus);
            var service = Real(verify, now, new FakeRefundService(), new RecordingPublisher());
            Assert.Contains((await service.SearchAsync("REJECTED", null, "100")).Data!.Items, i => i.AssignmentId == s.AssignmentId);
            Assert.DoesNotContain((await service.SearchAsync(null, null, "100")).Data!.Items, i => i.AssignmentId == s.AssignmentId);
            Assert.Equal(409, (await service.ApproveAsync(s.AdminId, s.AssignmentId)).StatusCode);
        }
        finally
        {
            await CleanAsync(s);
        }
    }

    [Fact]
    public async Task Real_database_a_refused_refund_rolls_the_status_the_fee_and_the_audit_row_back()
    {
        if (!IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var s = await SeedAsync(now);
        try
        {
            var publisher = new RecordingPublisher();
            await using (var db = new AppDbContext(Options()))
            {
                var result = await Real(db, now, new RefusingRefunds(), publisher).ApproveAsync(s.AdminId, s.AssignmentId);
                Assert.Equal(502, result.StatusCode);
            }

            await using var verify = new AppDbContext(Options());
            var row = await verify.JobAssignments.AsNoTracking().SingleAsync(a => a.AssignmentId == s.AssignmentId);
            Assert.Equal(JobAssignmentStatus.CheckedIn, row.AssignmentStatus);
            Assert.Null(row.AbsenceFeeAmount);
            Assert.Equal(0, await verify.AdminAuditLogs.CountAsync(a => a.EntityType == "JOB_ASSIGNMENT" && a.EntityId == s.AssignmentId.ToString()));
            Assert.Empty(publisher.Events);
        }
        finally
        {
            await CleanAsync(s);
        }
    }

    [Fact]
    public async Task Real_database_six_simultaneous_approvals_give_one_200_five_409_and_one_refund()
    {
        if (!IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var s = await SeedAsync(now);
        try
        {
            var refunds = new FakeRefundService();
            using var gate = new ManualResetEventSlim(false);

            async Task<int> Approve()
            {
                await using var db = new AppDbContext(Options());
                var service = Real(db, now, refunds, new RecordingPublisher());
                gate.Wait();
                return (await service.ApproveAsync(s.AdminId, s.AssignmentId)).StatusCode;
            }

            var tasks = Enumerable.Range(0, 6).Select(_ => Task.Run(Approve)).ToArray();
            await Task.Delay(300);
            gate.Set();
            var codes = await Task.WhenAll(tasks);

            Assert.Equal(1, codes.Count(c => c == 200));
            Assert.Equal(5, codes.Count(c => c == 409));
            Assert.Single(refunds.Requests);
            await using var verify = new AppDbContext(Options());
            Assert.Equal(1, await verify.AdminAuditLogs.CountAsync(a => a.EntityType == "JOB_ASSIGNMENT" && a.EntityId == s.AssignmentId.ToString()));
        }
        finally
        {
            await CleanAsync(s);
        }
    }

    [Fact]
    public async Task Real_database_the_notification_reads_find_the_active_admins_and_the_customer_of_the_order()
    {
        if (!IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var s = await SeedAsync(now);
        try
        {
            await using var db = new AppDbContext(Options());
            var repo = new EfAbsenceRepository(db);

            Assert.Contains(s.AdminId, await repo.GetActiveAdminIdsAsync());
            Assert.Equal(s.CustomerId, await repo.GetCustomerIdOfOrderAsync(s.OrderId));
            Assert.Null(await repo.GetCustomerIdOfOrderAsync(long.MaxValue));
        }
        finally
        {
            await CleanAsync(s);
        }
    }
}
