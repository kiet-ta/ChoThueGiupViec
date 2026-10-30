using System.Reflection;
using CommonService.Application.Common.Models;
using CommonService.Application.Features.Admin;
using CommonService.Application.Features.Admin.Services;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Admin;
using CommonService.Infrastructure.Persistence;
using CommonService.WebAPI.Controllers.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Tests.Admin;

/// <summary>ADMIN-UAT-10 finding C: the Admin unlock of a locked freelancer (decisions Q15 "Admin can unlock", G-5; contract admin.md 2.6).</summary>
public class WorkerUnlockTests
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

    private sealed class MemoryWorkers : IWorkerLockRepository
    {
        public Dictionary<int, Worker> Rows { get; } = [];

        public Task<Worker?> FindWorkerAsync(int workerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.GetValueOrDefault(workerId));
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.FromResult(1);
        }

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default) => action();
    }

    private sealed class FakeUser(int? id) : ICurrentUser
    {
        public bool IsAuthenticated => id is not null;
        public int? UserId => id;
        public UserRole? Role => UserRole.Admin;
    }

    private sealed class Harness
    {
        public MemoryWorkers Workers { get; } = new();
        public FakeAuditLog Audit { get; } = new();
        public CountingUnitOfWork Uow { get; } = new();
        public WorkerUnlockService Service { get; }

        public Harness() => Service = new WorkerUnlockService(Workers, Audit, Uow);

        /// <summary>A locked freelancer with an approved eKYC: the one case that may be unlocked.</summary>
        public Worker AddLocked(int id = 5, string kyc = "APPROVED")
        {
            var worker = Worker.CreateFreelancer("0901234567", "079000000001", "Locked Worker");
            worker.WorkerId = id;
            worker.KycStatus = kyc;
            worker.TransitionTo(WorkStatus.Idle);
            worker.TransitionTo(WorkStatus.Locked);
            Workers.Rows[id] = worker;
            return worker;
        }
    }

    [Fact]
    public async Task A_locked_freelancer_with_an_approved_ekyc_goes_back_to_idle_with_one_audit_row_in_the_same_save()
    {
        var h = new Harness();
        var worker = h.AddLocked();

        var result = await h.Service.UnlockAsync(42, 5, "  warning served  ");

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("IDLE", result.Data!.WorkStatus);
        Assert.Equal(WorkStatus.Idle, worker.WorkStatus);
        Assert.Equal(1, h.Uow.Saves);
        var row = Assert.Single(h.Audit.Entries);
        Assert.Equal((AuditActorType.Admin, (int?)42, "WORKER", "5", "work_status", "LOCKED", "IDLE", "warning served"),
            (row.ActorType, row.AdminId, row.EntityType, row.EntityId, row.FieldName, row.OldValue, row.NewValue, row.Reason));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_reason_is_required(string? reason)
    {
        var h = new Harness();
        var worker = h.AddLocked();

        var result = await h.Service.UnlockAsync(1, 5, reason);

        Assert.Equal(400, result.StatusCode);
        Assert.True(result.ValidationErrors!.ContainsKey("reason"));
        Assert.Equal(WorkStatus.Locked, worker.WorkStatus);
        Assert.Empty(h.Audit.Entries);
        Assert.Equal(0, h.Uow.Saves);
    }

    [Fact]
    public async Task The_reason_has_at_most_255_characters()
    {
        var h = new Harness();
        h.AddLocked();

        Assert.Equal(200, (await h.Service.UnlockAsync(1, 5, new string('a', 255))).StatusCode);
        h.Workers.Rows[5].TransitionTo(WorkStatus.Locked);
        Assert.Equal(400, (await h.Service.UnlockAsync(1, 5, new string('a', 256))).StatusCode);
    }

    [Fact]
    public async Task An_unknown_worker_is_404()
    {
        var result = await new Harness().Service.UnlockAsync(1, 404, "r");

        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task A_worker_whose_ekyc_is_not_approved_is_refused_so_the_unlock_is_never_a_way_around_ekyc()
    {
        var h = new Harness();
        var worker = h.AddLocked(kyc: "REJECTED");

        var result = await h.Service.UnlockAsync(1, 5, "r");

        Assert.Equal(409, result.StatusCode);
        Assert.Equal([WorkerUnlockService.KycNotApproved], result.BlockReasons);
        Assert.Equal(WorkStatus.Locked, worker.WorkStatus);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task A_worker_that_is_not_locked_is_refused_and_nothing_is_audited()
    {
        var h = new Harness();
        var worker = h.AddLocked();
        worker.Unlock();

        var result = await h.Service.UnlockAsync(1, 5, "r");

        Assert.Equal(409, result.StatusCode);
        Assert.Equal([WorkerUnlockService.NotLocked], result.BlockReasons);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task An_agency_worker_is_refused_because_the_agency_quota_locks_it_and_the_agency_unlocks_it()
    {
        var h = new Harness();
        var staff = Worker.CreateAgencyStaff(7, "0901234568", "079000000002", "Staff");
        staff.WorkerId = 8;
        staff.KycStatus = "APPROVED";
        staff.TransitionTo(WorkStatus.Locked);
        h.Workers.Rows[8] = staff;

        var result = await h.Service.UnlockAsync(1, 8, "r");

        Assert.Equal(409, result.StatusCode);
        Assert.Equal([WorkerUnlockService.NotFreelancer], result.BlockReasons);
        Assert.Equal(WorkStatus.Locked, staff.WorkStatus);
    }

    [Fact]
    public async Task All_the_refusals_are_listed_together()
    {
        var h = new Harness();
        var staff = Worker.CreateAgencyStaff(7, "0901234568", "079000000002", "Staff");
        staff.WorkerId = 8;
        staff.KycStatus = "PENDING"; // not locked, not a freelancer, eKYC not approved
        h.Workers.Rows[8] = staff;

        var result = await h.Service.UnlockAsync(1, 8, "r");

        Assert.Equal(
            [WorkerUnlockService.NotFreelancer, WorkerUnlockService.NotLocked, WorkerUnlockService.KycNotApproved],
            result.BlockReasons);
    }

    // ---- controller -----------------------------------------------------------------------------

    [Fact]
    public void Controller_requires_AdminOnly_and_serves_the_contract_route_with_POST_only()
    {
        var type = typeof(AdminWorkerUnlockController);
        Assert.Equal("AdminOnly", type.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal("api/admin/workers/{workerId:int}/unlock", type.GetCustomAttribute<RouteAttribute>()!.Template);

        var verbs = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods))
            .ToArray();
        Assert.Equal(["POST"], verbs);
    }

    [Fact]
    public async Task The_controller_passes_the_token_admin_id_and_maps_200_400_404_409_with_their_data()
    {
        var h = new Harness();
        h.AddLocked();
        var controller = new AdminWorkerUnlockController(h.Service, new FakeUser(42));

        var ok = Assert.IsType<OkObjectResult>(await controller.Unlock(5, new WorkerUnlockRequest { Reason = "r" }, default));
        Assert.Equal("IDLE", Assert.IsType<ApiResponse<WorkerUnlockDto>>(ok.Value).Data!.WorkStatus);
        Assert.Equal(42, h.Audit.Entries.Single().AdminId);

        var bad = Assert.IsType<ObjectResult>(await controller.Unlock(5, new WorkerUnlockRequest { Reason = " " }, default));
        Assert.Equal(400, bad.StatusCode);
        Assert.Contains("errors", Assert.IsType<ApiResponse<object>>(bad.Value).Data!.GetType().GetProperties().Select(p => p.Name));

        Assert.Equal(404, Assert.IsType<ObjectResult>(await controller.Unlock(404, new WorkerUnlockRequest { Reason = "r" }, default)).StatusCode);

        var refused = Assert.IsType<ObjectResult>(await controller.Unlock(5, new WorkerUnlockRequest { Reason = "r" }, default)); // already idle now
        Assert.Equal(409, refused.StatusCode);
        Assert.Contains("blockReasons", Assert.IsType<ApiResponse<object>>(refused.Value).Data!.GetType().GetProperties().Select(p => p.Name));
    }

    [Fact]
    public async Task A_missing_body_is_a_400_and_a_token_without_a_user_id_is_a_401()
    {
        var h = new Harness();
        h.AddLocked();

        var noBody = Assert.IsType<ObjectResult>(await new AdminWorkerUnlockController(h.Service, new FakeUser(1)).Unlock(5, null!, default));
        Assert.Equal(400, noBody.StatusCode);

        Assert.IsType<UnauthorizedObjectResult>(
            await new AdminWorkerUnlockController(h.Service, new FakeUser(null)).Unlock(5, new WorkerUnlockRequest { Reason = "r" }, default));
    }

    // ---- SQL Server -----------------------------------------------------------------------------

    private sealed class TestClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => local.AddHours(-7);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(utcNow));
    }

    private static DbContextOptions<AppDbContext> Options() => new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;

    [Fact]
    public async Task Real_database_the_unlock_saves_the_new_status_and_its_audit_row_together()
    {
        if (!IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var suffix = Random.Shared.Next(100000, 999999).ToString();
        int adminId;
        int workerId;
        await using (var seed = new AppDbContext(Options()))
        {
            var admin = new AdminAccount
            {
                Email = "unlock-" + Guid.NewGuid().ToString("N")[..10] + "@example.test",
                FullName = "Unlock Admin",
                PasswordHash = "x",
                AdminRole = "SUPER_ADMIN",
                IsActive = true,
                CreatedAt = now,
            };
            seed.Admins.Add(admin);

            var worker = Worker.CreateFreelancer("08" + suffix + "21", "0780" + suffix + "21", "Unlock Worker");
            worker.KycStatus = "APPROVED";
            worker.CreatedAt = now;
            worker.UpdatedAt = now;
            worker.TransitionTo(WorkStatus.Idle);
            worker.TransitionTo(WorkStatus.Locked);
            seed.Workers.Add(worker);
            await seed.SaveChangesAsync();
            adminId = admin.AdminId;
            workerId = worker.WorkerId;
        }

        try
        {
            await using (var db = new AppDbContext(Options()))
            {
                var service = new WorkerUnlockService(new EfWorkerLockRepository(db), new EfAuditLog(db, new TestClock(now)), new UnitOfWork(db));

                var done = await service.UnlockAsync(adminId, workerId, "real database");
                Assert.Equal(200, done.StatusCode);
            }

            await using var verify = new AppDbContext(Options());
            Assert.Equal(WorkStatus.Idle, (await verify.Workers.AsNoTracking().SingleAsync(w => w.WorkerId == workerId)).WorkStatus);
            var audit = await verify.AdminAuditLogs.AsNoTracking().SingleAsync(a => a.EntityType == "WORKER" && a.EntityId == workerId.ToString());
            Assert.Equal(("work_status", "LOCKED", "IDLE", "real database", adminId), (audit.FieldName, audit.OldValue, audit.NewValue, audit.Reason, audit.AdminId));
        }
        finally
        {
            await using var db = new AppDbContext(Options());
            await db.AdminAuditLogs.Where(a => a.EntityType == "WORKER" && a.EntityId == workerId.ToString()).ExecuteDeleteAsync();
            await db.Workers.Where(w => w.WorkerId == workerId).ExecuteDeleteAsync();
            await db.Admins.Where(a => a.AdminId == adminId).ExecuteDeleteAsync();
        }
    }
}
