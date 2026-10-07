using System.Reflection;
using CommonService.Application.Common.Models;
using CommonService.Application.Common.Options;
using CommonService.Application.Features.Admin;
using CommonService.Application.Features.Admin.Services;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure;
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

/// <summary>BE-M6-09c: Super-Freelancer approve, revoke and auto-revoke (decisions Q12, G-5; contract admin.md 2.5).</summary>
public class SuperFreelancerTests
{
    private const string ConnectionString =
        "Server=localhost;Database=ChoThueGiupViec;Integrated Security=True;TrustServerCertificate=True;";

    private static readonly DateTime Now = new(2026, 10, 7, 4, 0, 0, DateTimeKind.Utc);

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

    private sealed class MemoryWorkers : ISuperFreelancerRepository
    {
        public Dictionary<int, Worker> Rows { get; } = [];
        public Dictionary<long, int> AssignmentWorkers { get; } = [];
        public bool RecentUpheldDispute { get; set; }
        public DateTime? AskedSince { get; private set; }

        public Task<Worker?> FindWorkerAsync(int workerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.GetValueOrDefault(workerId));

        public Task<bool> HasUpheldFreelancerDisputeSinceAsync(int workerId, DateTime sinceUtc, CancellationToken cancellationToken = default)
        {
            AskedSince = sinceUtc;
            return Task.FromResult(RecentUpheldDispute);
        }

        public Task<int?> GetWorkerIdOfAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AssignmentWorkers.TryGetValue(assignmentId, out var id) ? id : (int?)null);
    }

    private sealed class StubReputation : IWorkerReputation
    {
        public Dictionary<int, WorkerReputationDto> Items { get; } = [];

        public Task<WorkerReputationDto?> GetAsync(int workerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.GetValueOrDefault(workerId));
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

        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default) =>
            action();
    }

    private sealed class Harness
    {
        public MemoryWorkers Workers { get; } = new();
        public StubReputation Reputation { get; } = new();
        public FakeAuditLog Audit { get; } = new();
        public CountingUnitOfWork Uow { get; } = new();
        public SuperFreelancerService Service { get; }

        public Harness()
        {
            Service = new SuperFreelancerService(Workers, Reputation, Audit, Uow, new TestClock(Now),
                Microsoft.Extensions.Options.Options.Create(new BusinessRules()));
        }

        /// <summary>A freelancer who meets every Q12 criterion.</summary>
        public Worker AddEligible(int id = 5)
        {
            var worker = Worker.CreateFreelancer("0901234567", "079000000001", "Eligible");
            worker.WorkerId = id;
            worker.KycStatus = "APPROVED";
            Workers.Rows[id] = worker;
            Reputation.Items[id] = new WorkerReputationDto(id, 4.90m, 60, 0.95m);
            return worker;
        }
    }

    // ---- approve --------------------------------------------------------------------------------

    [Fact]
    public async Task An_eligible_freelancer_is_approved_and_one_audit_row_is_written_in_the_same_save()
    {
        var h = new Harness();
        var worker = h.AddEligible();

        var result = await h.Service.ApproveAsync(1, 5, "  top rated  ");

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.True(result.Data!.IsSuperFreelancer);
        Assert.True(worker.IsSuperFreelancer);
        Assert.Equal(1, h.Uow.Saves); // the flag and the audit row go through one SaveChanges
        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal(new AuditEntry(AuditActorType.Admin, 1, "WORKER", "5", "is_super_freelancer", "false", "true", "top rated"), entry);
    }

    [Theory]
    [InlineData(4.79, 60, "APPROVED", false, new[] { "RATING_BELOW_MINIMUM" })]
    [InlineData(4.90, 49, "APPROVED", false, new[] { "COMPLETED_JOBS_BELOW_MINIMUM" })]
    [InlineData(4.90, 60, "PENDING", false, new[] { "KYC_NOT_APPROVED" })]
    [InlineData(4.90, 60, "APPROVED", true, new[] { "UPHELD_DISPUTE_RECENT" })]
    [InlineData(4.10, 3, "REJECTED", true, new[] { "RATING_BELOW_MINIMUM", "COMPLETED_JOBS_BELOW_MINIMUM", "KYC_NOT_APPROVED", "UPHELD_DISPUTE_RECENT" })]
    public async Task Every_failing_criterion_is_listed_and_nothing_changes(double rating, int jobs, string kyc, bool dispute, string[] expected)
    {
        var h = new Harness();
        var worker = h.AddEligible();
        worker.KycStatus = kyc;
        h.Reputation.Items[5] = new WorkerReputationDto(5, (decimal)rating, jobs, 0.9m);
        h.Workers.RecentUpheldDispute = dispute;

        var result = await h.Service.ApproveAsync(1, 5, "why");

        Assert.False(result.Success);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(expected, result.FailedCriteria!.ToArray());
        Assert.False(worker.IsSuperFreelancer);
        Assert.Empty(h.Audit.Entries);
        Assert.Equal(0, h.Uow.Saves);
    }

    [Fact]
    public async Task The_thresholds_are_inclusive_4_80_and_50_jobs_pass()
    {
        var h = new Harness();
        h.AddEligible();
        h.Reputation.Items[5] = new WorkerReputationDto(5, 4.80m, 50, 0.9m);
        Assert.Equal(200, (await h.Service.ApproveAsync(1, 5, "edge")).StatusCode);
    }

    [Fact]
    public async Task The_dispute_look_back_is_180_days_from_now()
    {
        var h = new Harness();
        h.AddEligible();
        await h.Service.ApproveAsync(1, 5, "x");
        Assert.Equal(Now.AddDays(-180), h.Workers.AskedSince);
    }

    [Fact]
    public async Task An_unknown_reputation_counts_as_zero_and_fails_both_numbers()
    {
        var h = new Harness();
        h.AddEligible();
        h.Reputation.Items.Clear();
        var result = await h.Service.ApproveAsync(1, 5, "x");
        Assert.Equal(["RATING_BELOW_MINIMUM", "COMPLETED_JOBS_BELOW_MINIMUM"], result.FailedCriteria!.ToArray());
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("Approved")]
    [InlineData("APPROVED")]
    public async Task The_approved_kyc_status_is_compared_case_insensitively(string status)
    {
        var h = new Harness();
        h.AddEligible().KycStatus = status;
        Assert.Equal(200, (await h.Service.ApproveAsync(1, 5, "x")).StatusCode);
    }

    [Fact]
    public async Task An_agency_worker_is_refused_as_NOT_FREELANCER_and_an_unknown_worker_is_404()
    {
        var h = new Harness();
        var staff = Worker.CreateAgencyStaff(3, "0907654321", "079000000002", "Staff");
        staff.WorkerId = 9;
        h.Workers.Rows[9] = staff;

        var refused = await h.Service.ApproveAsync(1, 9, "x");
        Assert.Equal(409, refused.StatusCode);
        Assert.Equal(["NOT_FREELANCER"], refused.FailedCriteria!.ToArray());

        Assert.Equal(404, (await h.Service.ApproveAsync(1, 404, "x")).StatusCode);
        Assert.Equal(404, (await h.Service.RevokeAsync(1, 404, "x")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_reason_is_a_400_for_both_actions_and_nothing_is_read_or_written(string? reason)
    {
        var h = new Harness();
        h.AddEligible();

        var approve = await h.Service.ApproveAsync(1, 5, reason);
        var revoke = await h.Service.RevokeAsync(1, 5, reason);

        Assert.Equal(400, approve.StatusCode);
        Assert.Contains("reason", approve.ValidationErrors!.Keys);
        Assert.Equal(400, revoke.StatusCode);
        Assert.Empty(h.Audit.Entries);
        Assert.Equal(0, h.Uow.Saves);
    }

    [Fact]
    public async Task A_reason_of_256_characters_is_rejected_and_255_is_accepted()
    {
        var h = new Harness();
        h.AddEligible();
        Assert.Equal(400, (await h.Service.ApproveAsync(1, 5, new string('a', 256))).StatusCode);
        Assert.Equal(200, (await h.Service.ApproveAsync(1, 5, new string('a', 255))).StatusCode);
    }

    [Fact]
    public async Task Approving_twice_or_revoking_a_non_super_worker_is_a_200_that_writes_nothing()
    {
        var h = new Harness();
        h.AddEligible();
        await h.Service.ApproveAsync(1, 5, "first");

        var again = await h.Service.ApproveAsync(2, 5, "second");
        Assert.Equal(200, again.StatusCode);
        Assert.Single(h.Audit.Entries);
        Assert.Equal(1, h.Uow.Saves);

        var h2 = new Harness();
        h2.AddEligible();
        var revoke = await h2.Service.RevokeAsync(1, 5, "not super");
        Assert.Equal(200, revoke.StatusCode);
        Assert.False(revoke.Data!.IsSuperFreelancer);
        Assert.Empty(h2.Audit.Entries);
    }

    // ---- revoke ---------------------------------------------------------------------------------

    [Fact]
    public async Task Revoke_clears_the_flag_and_writes_one_audit_row_with_the_admin_and_the_reason()
    {
        var h = new Harness();
        var worker = h.AddEligible();
        worker.IsSuperFreelancer = true;

        var result = await h.Service.RevokeAsync(2, 5, "complaints");

        Assert.False(result.Data!.IsSuperFreelancer);
        Assert.False(worker.IsSuperFreelancer);
        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal(new AuditEntry(AuditActorType.Admin, 2, "WORKER", "5", "is_super_freelancer", "true", "false", "complaints"), entry);
        Assert.Equal(1, h.Uow.Saves);
    }

    // ---- auto-revoke ----------------------------------------------------------------------------

    private static Harness SuperWith(decimal rating)
    {
        var h = new Harness();
        h.AddEligible().IsSuperFreelancer = true;
        h.Reputation.Items[5] = new WorkerReputationDto(5, rating, 60, 0.9m);
        return h;
    }

    [Fact]
    public async Task Below_4_70_the_flag_is_cleared_by_the_system_with_no_admin_id()
    {
        var h = SuperWith(4.69m);
        await h.Service.AutoRevokeIfBelowThresholdAsync(5);

        Assert.False(h.Workers.Rows[5].IsSuperFreelancer);
        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal(AuditActorType.System, entry.ActorType);
        Assert.Null(entry.AdminId);
        Assert.Equal("WORKER", entry.EntityType);
        Assert.Equal("5", entry.EntityId);
        Assert.Equal("is_super_freelancer", entry.FieldName);
        Assert.Equal("rating below 4.70", entry.Reason);
        Assert.Equal(1, h.Uow.Saves);
    }

    [Theory]
    [InlineData(4.70)]
    [InlineData(4.71)]
    [InlineData(5.00)]
    public async Task At_or_above_4_70_the_flag_stays(double rating)
    {
        var h = SuperWith((decimal)rating);
        await h.Service.AutoRevokeIfBelowThresholdAsync(5);
        Assert.True(h.Workers.Rows[5].IsSuperFreelancer);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Auto_revoke_ignores_a_non_super_worker_an_unknown_worker_and_an_unknown_reputation()
    {
        var notSuper = new Harness();
        notSuper.AddEligible();
        notSuper.Reputation.Items[5] = new WorkerReputationDto(5, 1m, 60, 0.9m);
        await notSuper.Service.AutoRevokeIfBelowThresholdAsync(5);
        Assert.Empty(notSuper.Audit.Entries);

        await new Harness().Service.AutoRevokeIfBelowThresholdAsync(404);

        var noReputation = SuperWith(1m);
        noReputation.Reputation.Items.Clear();
        await noReputation.Service.AutoRevokeIfBelowThresholdAsync(5);
        Assert.True(noReputation.Workers.Rows[5].IsSuperFreelancer);
        Assert.Empty(noReputation.Audit.Entries);
    }

    [Fact]
    public async Task The_RatingSubmitted_handler_acts_on_CUSTOMER_ratings_only_through_the_assignments_worker()
    {
        var h = SuperWith(4.00m);
        h.Workers.AssignmentWorkers[700] = 5;
        var handler = new RatingSubmittedSuperFreelancerHandler(h.Workers, h.Service);

        await handler.Handle(new RatingSubmitted(1, 700, "WORKER", 1, Now), default);
        Assert.True(h.Workers.Rows[5].IsSuperFreelancer); // a worker-to-customer rating never moves the worker's average

        await handler.Handle(new RatingSubmitted(2, 999, "CUSTOMER", 1, Now), default);
        Assert.True(h.Workers.Rows[5].IsSuperFreelancer); // unknown assignment: nothing to do

        await handler.Handle(new RatingSubmitted(3, 700, "CUSTOMER", 1, Now), default);
        Assert.False(h.Workers.Rows[5].IsSuperFreelancer);
        Assert.Single(h.Audit.Entries);
    }

    // ---- controller -----------------------------------------------------------------------------

    private sealed class FakeUser(int? id) : ICurrentUser
    {
        public bool IsAuthenticated => id is not null;
        public int? UserId => id;
        public UserRole? Role => UserRole.Admin;
    }

    [Fact]
    public void Controller_requires_AdminOnly_serves_the_contract_route_with_POST_and_DELETE_only()
    {
        var type = typeof(AdminSuperFreelancerController);
        Assert.Equal("AdminOnly", type.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal("api/admin/workers/{workerId:int}/super-freelancer", type.GetCustomAttribute<RouteAttribute>()!.Template);

        var verbs = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods))
            .Order()
            .ToArray();
        Assert.Equal(["DELETE", "POST"], verbs);
    }

    [Fact]
    public async Task The_controller_passes_the_token_admin_id_and_maps_200_400_404_409_with_their_data()
    {
        var h = new Harness();
        h.AddEligible();
        var controller = new AdminSuperFreelancerController(h.Service, new FakeUser(42));

        var ok = Assert.IsType<OkObjectResult>(await controller.Approve(5, new SuperFreelancerReasonRequest { Reason = "r" }, default));
        Assert.True(Assert.IsType<ApiResponse<SuperFreelancerDto>>(ok.Value).Data!.IsSuperFreelancer);
        Assert.Equal(42, h.Audit.Entries.Single().AdminId);

        var bad = Assert.IsType<ObjectResult>(await controller.Revoke(5, new SuperFreelancerReasonRequest { Reason = " " }, default));
        Assert.Equal(400, bad.StatusCode);
        Assert.Contains("errors", Assert.IsType<ApiResponse<object>>(bad.Value).Data!.GetType().GetProperties().Select(p => p.Name));

        Assert.Equal(404, Assert.IsType<ObjectResult>(await controller.Approve(404, new SuperFreelancerReasonRequest { Reason = "r" }, default)).StatusCode);

        var weak = new Harness();
        weak.AddEligible().KycStatus = "PENDING";
        var refused = Assert.IsType<ObjectResult>(
            await new AdminSuperFreelancerController(weak.Service, new FakeUser(1)).Approve(5, new SuperFreelancerReasonRequest { Reason = "r" }, default));
        Assert.Equal(409, refused.StatusCode);
        Assert.Contains("failedCriteria", Assert.IsType<ApiResponse<object>>(refused.Value).Data!.GetType().GetProperties().Select(p => p.Name));
    }

    [Fact]
    public async Task Without_a_user_id_in_the_token_both_actions_answer_401()
    {
        var controller = new AdminSuperFreelancerController(new Harness().Service, new FakeUser(null));
        Assert.IsType<UnauthorizedObjectResult>(await controller.Approve(1, new SuperFreelancerReasonRequest { Reason = "r" }, default));
        Assert.IsType<UnauthorizedObjectResult>(await controller.Revoke(1, new SuperFreelancerReasonRequest { Reason = "r" }, default));
    }

    // ---- SQL Server -----------------------------------------------------------------------------

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;

    private sealed record Seeded(int AdminId, int CustomerId, int AddressId, long OrderId, int WorkerId, int OtherWorkerId, List<int> SlotIds, List<long> AssignmentIds, long OtherOrderId);

    private static async Task<Seeded> SeedAsync(DateTime now)
    {
        await using var db = new AppDbContext(Options());

        var admin = new AdminAccount
        {
            Email = "sf-" + Guid.NewGuid().ToString("N")[..10] + "@example.test",
            FullName = "SF Admin",
            PasswordHash = "x",
            AdminRole = "SUPER_ADMIN",
            IsActive = true,
            CreatedAt = now,
        };
        db.Admins.Add(admin);

        var customer = new Customer
        {
            PhoneNumber = "091" + Random.Shared.Next(1000000, 9999999),
            FullName = "SF Customer",
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
        await db.SaveChangesAsync();

        JobOrder NewOrder() => new()
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

        var order = NewOrder();
        var otherOrder = NewOrder();
        db.JobOrders.AddRange(order, otherOrder);

        Worker NewWorker(string name) => new()
        {
            PhoneNumber = "092" + Random.Shared.Next(1000000, 9999999),
            NationalId = "079" + Random.Shared.Next(100000000, 999999999),
            FullName = name,
            KycStatus = "APPROVED",
            CreatedAt = now,
            UpdatedAt = now,
        };

        var worker = NewWorker("SF Worker");
        var other = NewWorker("SF Other");
        db.Workers.AddRange(worker, other);
        await db.SaveChangesAsync();

        var slots = new List<BookingSlot>();
        foreach (var (w, day) in new[] { (worker, 1), (other, 1) })
        {
            slots.Add(new BookingSlot
            {
                WorkerId = w.WorkerId,
                SlotDate = order.ScheduledDate.AddDays(day),
                ShiftCode = "SHIFT1",
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(12, 0),
                SlotSource = "MANUAL",
                SlotStatus = "LOCKED",
                UpdatedAt = now,
            });
        }

        db.BookingSlots.AddRange(slots);
        await db.SaveChangesAsync();

        JobAssignment Assign(JobOrder o, Worker w, BookingSlot s) => new()
        {
            OrderId = o.OrderId,
            CustomerId = customer.CustomerId,
            WorkerId = w.WorkerId,
            SlotId = s.SlotId,
            ServiceTier = ServiceTier.Economy,
            AssignmentSeq = 1,
            DispatchRadiusKm = 5,
            GrossAmount = 260000m,
            CommissionRate = 0.200m,
            PayoutAmount = 208000m,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var mine = Assign(order, worker, slots[0]);
        var theirs = Assign(otherOrder, other, slots[1]);
        db.JobAssignments.AddRange(mine, theirs);
        await db.SaveChangesAsync();

        return new Seeded(admin.AdminId, customer.CustomerId, address.AddressId, order.OrderId, worker.WorkerId, other.WorkerId,
            slots.Select(s => s.SlotId).ToList(), [mine.AssignmentId, theirs.AssignmentId], otherOrder.OrderId);
    }

    private static async Task CleanAsync(Seeded s)
    {
        await using var db = new AppDbContext(Options());
        await db.AdminAuditLogs.Where(a => a.EntityType == "WORKER" && (a.EntityId == s.WorkerId.ToString() || a.EntityId == s.OtherWorkerId.ToString())).ExecuteDeleteAsync();
        await db.DisputeTickets.Where(d => d.OrderId == s.OrderId || d.OrderId == s.OtherOrderId).ExecuteDeleteAsync();
        await db.JobAssignments.Where(a => s.AssignmentIds.Contains(a.AssignmentId)).ExecuteDeleteAsync();
        await db.BookingSlots.Where(b => s.SlotIds.Contains(b.SlotId)).ExecuteDeleteAsync();
        await db.JobOrders.Where(o => o.OrderId == s.OrderId || o.OrderId == s.OtherOrderId).ExecuteDeleteAsync();
        await db.CustomerAddresses.Where(a => a.AddressId == s.AddressId).ExecuteDeleteAsync();
        await db.Customers.Where(c => c.CustomerId == s.CustomerId).ExecuteDeleteAsync();
        await db.Workers.Where(w => w.WorkerId == s.WorkerId || w.WorkerId == s.OtherWorkerId).ExecuteDeleteAsync();
        await db.Admins.Where(a => a.AdminId == s.AdminId).ExecuteDeleteAsync();
    }

    private static DisputeTicket Ticket(long orderId, FaultParty? fault, DateTime? resolvedAt, DateTime now) => new()
    {
        OrderId = orderId,
        RaisedBy = "CUSTOMER",
        Category = "QUALITY",
        Description = "test",
        DisputeStatus = resolvedAt is null ? "OPEN" : "RESOLVED",
        FaultParty = fault,
        SlaDueAt = now.AddDays(2),
        ResolvedAt = resolvedAt,
        CreatedAt = now.AddDays(-200),
    };

    [Fact]
    public async Task The_180_day_dispute_rule_on_the_real_database_counts_only_this_workers_freelancer_fault_resolved_in_the_window()
    {
        if (!IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var s = await SeedAsync(now);
        try
        {
            // DISPUTE_TICKET has a UNIQUE index on order_id (one ticket per order), so the cases are run one after the other on one ticket.
            await using (var seed = new AppDbContext(Options()))
            {
                seed.DisputeTickets.AddRange(
                    Ticket(s.OrderId, FaultParty.Agency, now.AddDays(-10), now),          // case 1: Agency fault, not a Freelancer fault
                    Ticket(s.OtherOrderId, FaultParty.Freelancer, now.AddDays(-5), now)); // someone else's order
                await seed.SaveChangesAsync();
            }

            await using var db = new AppDbContext(Options());
            var repo = new EfSuperFreelancerRepository(db);
            var since = now.AddDays(-180);

            async Task SetMine(FaultParty? fault, DateTime? resolvedAt)
            {
                await using var change = new AppDbContext(Options());
                await change.DisputeTickets.Where(d => d.OrderId == s.OrderId)
                    .ExecuteUpdateAsync(u => u.SetProperty(d => d.FaultParty, fault).SetProperty(d => d.ResolvedAt, resolvedAt));
            }

            Assert.False(await repo.HasUpheldFreelancerDisputeSinceAsync(s.WorkerId, since));     // case 1: Agency fault
            Assert.True(await repo.HasUpheldFreelancerDisputeSinceAsync(s.OtherWorkerId, since)); // the other worker's order does count for the other worker

            await SetMine(FaultParty.Freelancer, null);                                            // case 2: Freelancer fault, not resolved yet
            Assert.False(await repo.HasUpheldFreelancerDisputeSinceAsync(s.WorkerId, since));

            await SetMine(FaultParty.Freelancer, now.AddDays(-181));                               // case 3: resolved too long ago
            Assert.False(await repo.HasUpheldFreelancerDisputeSinceAsync(s.WorkerId, since));

            await SetMine(FaultParty.Freelancer, now.AddDays(-179));                               // case 4: inside the window
            Assert.True(await repo.HasUpheldFreelancerDisputeSinceAsync(s.WorkerId, since));
        }
        finally
        {
            await CleanAsync(s);
        }
    }

    [Fact]
    public async Task Approve_on_the_real_database_saves_the_flag_and_the_audit_row_together_and_revoke_clears_it()
    {
        if (!IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var s = await SeedAsync(now);
        try
        {
            await using var db = new AppDbContext(Options());
            var reputation = new StubReputation();
            reputation.Items[s.WorkerId] = new WorkerReputationDto(s.WorkerId, 4.9m, 60, 0.9m);
            var service = new SuperFreelancerService(new EfSuperFreelancerRepository(db), reputation, new EfAuditLog(db, new TestClock(now)),
                new UnitOfWork(db), new TestClock(now), Microsoft.Extensions.Options.Options.Create(new BusinessRules()));

            var approved = await service.ApproveAsync(s.AdminId, s.WorkerId, "real database");
            Assert.Equal(200, approved.StatusCode);

            await using (var verify = new AppDbContext(Options()))
            {
                Assert.True((await verify.Workers.AsNoTracking().SingleAsync(w => w.WorkerId == s.WorkerId)).IsSuperFreelancer);
                var row = await verify.AdminAuditLogs.AsNoTracking().SingleAsync(a => a.EntityType == "WORKER" && a.EntityId == s.WorkerId.ToString());
                Assert.Equal(AuditActorType.Admin, row.ActorType);
                Assert.Equal(s.AdminId, row.AdminId);
                Assert.Equal("is_super_freelancer", row.FieldName);
                Assert.Equal("false", row.OldValue);
                Assert.Equal("true", row.NewValue);
                Assert.Equal("real database", row.Reason);
            }

            var revoked = await service.RevokeAsync(s.AdminId, s.WorkerId, "done");
            Assert.False(revoked.Data!.IsSuperFreelancer);

            await using var after = new AppDbContext(Options());
            Assert.False((await after.Workers.AsNoTracking().SingleAsync(w => w.WorkerId == s.WorkerId)).IsSuperFreelancer);
            Assert.Equal(2, await after.AdminAuditLogs.CountAsync(a => a.EntityType == "WORKER" && a.EntityId == s.WorkerId.ToString()));
        }
        finally
        {
            await CleanAsync(s);
        }
    }

    [Fact]
    public async Task The_system_auto_revoke_on_the_real_database_writes_a_SYSTEM_row_without_an_admin()
    {
        if (!IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var s = await SeedAsync(now);
        try
        {
            await using (var seed = new AppDbContext(Options()))
            {
                (await seed.Workers.SingleAsync(w => w.WorkerId == s.WorkerId)).IsSuperFreelancer = true;
                await seed.SaveChangesAsync();
            }

            await using var db = new AppDbContext(Options());
            var reputation = new StubReputation();
            reputation.Items[s.WorkerId] = new WorkerReputationDto(s.WorkerId, 4.5m, 60, 0.9m);
            var service = new SuperFreelancerService(new EfSuperFreelancerRepository(db), reputation, new EfAuditLog(db, new TestClock(now)),
                new UnitOfWork(db), new TestClock(now), Microsoft.Extensions.Options.Options.Create(new BusinessRules()));
            var handler = new RatingSubmittedSuperFreelancerHandler(new EfSuperFreelancerRepository(db), service);

            await handler.Handle(new RatingSubmitted(1, s.AssignmentIds[0], "CUSTOMER", 1, now), default);

            await using var verify = new AppDbContext(Options());
            Assert.False((await verify.Workers.AsNoTracking().SingleAsync(w => w.WorkerId == s.WorkerId)).IsSuperFreelancer);
            var row = await verify.AdminAuditLogs.AsNoTracking().SingleAsync(a => a.EntityType == "WORKER" && a.EntityId == s.WorkerId.ToString());
            Assert.Equal(AuditActorType.System, row.ActorType);
            Assert.Null(row.AdminId);
            Assert.Equal("rating below 4.70", row.Reason);
        }
        finally
        {
            await CleanAsync(s);
        }
    }
}
