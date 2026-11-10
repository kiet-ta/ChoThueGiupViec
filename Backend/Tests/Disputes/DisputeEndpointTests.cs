using System.Reflection;
using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Disputes;
using CommonService.Application.Features.Disputes.Dtos;
using CommonService.Application.Features.Disputes.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Disputes;
using CommonService.Infrastructure.Persistence;
using CommonService.WebAPI.Controllers.Disputes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Tests.Disputes;

/// <summary>BE-M6-02a: controllers (policies, routes, verbs, status mapping) and the repository on the local SQL Server.</summary>
public class DisputeEndpointTests
{
    internal const string ConnectionString =
        "Server=localhost;Database=ChoThueGiupViec;Integrated Security=True;TrustServerCertificate=True;";

    internal static bool IsSqlServerAvailable()
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

    // ---- controllers ----------------------------------------------------------------------------

    [Theory]
    [InlineData(typeof(CustomerDisputesController), "CustomerOnly", "api/v1/customers/me/disputes")]
    [InlineData(typeof(WorkerDisputesController), "WorkerOnly", "api/v1/workers/me/disputes")]
    [InlineData(typeof(AdminDisputesController), "AdminOnly", "api/v1/admin/disputes")]
    public void Each_controller_has_its_policy_and_the_contract_route(Type controller, string policy, string route)
    {
        Assert.Equal(policy, controller.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal(route, controller.GetCustomAttribute<RouteAttribute>()!.Template);
        foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
        }
    }

    private static string[] Routes(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Select(a => $"{a.HttpMethods.Single()} {a.Template}".Trim()))
            .Order()
            .ToArray();

    [Fact]
    public void The_actions_are_exactly_the_ones_of_the_contract()
    {
        Assert.Equal(["GET", "GET {disputeId:int}", "POST", "POST evidence"], Routes(typeof(CustomerDisputesController)));
        Assert.Equal(["GET", "GET {disputeId:int}", "POST", "POST evidence"], Routes(typeof(WorkerDisputesController)));
        Assert.Equal(
            ["GET", "GET {disputeId:int}", "POST {disputeId:int}/resolve", "POST {disputeId:int}/take"],
            Routes(typeof(AdminDisputesController)));
    }

    /// <summary>What the service throws for a failure status; ErrorHandlingMiddleware turns it into that status (Tests/Infrastructure/MiddlewareTests.cs).</summary>
    private static Exception Failure(int status) => status switch
    {
        400 => new ValidationException("Validation failed", new Dictionary<string, string[]> { ["field"] = ["bad"] }),
        404 => new NotFoundException("Not found."),
        409 => new BusinessRuleViolationException("decided"),
        _ => new BadGatewayException("refund refused"),
    };

    private static readonly Dictionary<int, Type> FailureType = new()
    {
        [400] = typeof(ValidationException),
        [404] = typeof(NotFoundException),
        [409] = typeof(BusinessRuleViolationException),
        [502] = typeof(BadGatewayException),
    };

    private class StubVerdicts(int status) : IDisputeVerdictService
    {
        public (int Admin, int Dispute)? LastCall { get; private set; }

        public Task<AdminDisputeDto> ResolveAsync(int adminId, int disputeId, ResolveDisputeRequestDto request, CancellationToken cancellationToken = default)
        {
            LastCall = (adminId, disputeId);
            return status == 200 ? Task.FromResult(new AdminDisputeDto { DisputeId = disputeId }) : throw Failure(status);
        }
    }

    [Fact]
    public async Task Resolve_answers_200_and_takes_the_admin_id_from_the_token()
    {
        var verdicts = new StubVerdicts(200);
        var controller = new AdminDisputesController(null!, verdicts, new FakeUser(7));

        var result = Assert.IsType<OkObjectResult>(await controller.Resolve(5, new ResolveDisputeRequestDto(), default));

        Assert.True(SuccessOf(result.Value));
        Assert.Equal((7, 5), verdicts.LastCall);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(502)]
    public async Task Resolve_lets_a_service_failure_reach_the_error_middleware(int status)
    {
        var controller = new AdminDisputesController(null!, new StubVerdicts(status), new FakeUser(7));

        var thrown = await Record.ExceptionAsync(() => controller.Resolve(5, new ResolveDisputeRequestDto(), default));

        Assert.IsType(FailureType[status], thrown);
    }

    [Fact]
    public async Task Resolve_without_a_body_is_a_400_and_without_a_user_id_a_401_before_the_service_is_called()
    {
        var verdicts = new StubVerdicts(200);
        Assert.IsType<BadRequestObjectResult>(await new AdminDisputesController(null!, verdicts, new FakeUser(7)).Resolve(5, null!, default));
        Assert.IsType<UnauthorizedObjectResult>(await new AdminDisputesController(null!, verdicts, new FakeUser(null)).Resolve(5, new ResolveDisputeRequestDto(), default));
        Assert.Null(verdicts.LastCall);
    }

    private class FakeUser(int? id) : ICurrentUser
    {
        public bool IsAuthenticated => id is not null;
        public int? UserId => id;
        public UserRole? Role => null;
    }

    internal static IDisputeFilingService Filing() => new StubFiling(201);

    internal static readonly IDisputeEvidenceService Evidence = new DisputeEvidenceService(new FakeFileStorage());

    private class StubFiling(int status) : IDisputeFilingService
    {
        public (DisputeSide Side, int Caller)? LastCall { get; private set; }

        public Task<DisputeDto> FileAsync(DisputeSide side, int callerId, FileDisputeRequestDto request, CancellationToken cancellationToken = default)
        {
            LastCall = (side, callerId);
            return status == 201 ? Task.FromResult(new DisputeDto { DisputeId = 1 }) : throw Failure(status);
        }

        public Task<IReadOnlyList<DisputeDto>> ListAsync(DisputeSide side, int callerId, CancellationToken cancellationToken = default)
        {
            LastCall = (side, callerId);
            return Task.FromResult<IReadOnlyList<DisputeDto>>([]);
        }

        public Task<DisputeDto> GetAsync(DisputeSide side, int callerId, int disputeId, CancellationToken cancellationToken = default)
        {
            LastCall = (side, callerId);
            throw new NotFoundException("Not found.");
        }
    }

    [Fact]
    public async Task Filing_answers_201_with_the_envelope_for_both_roles()
    {
        var customerService = new StubFiling(201);
        var customer = Assert.IsType<ObjectResult>(
            await new CustomerDisputesController(customerService, Evidence, new FakeUser(11)).File(new FileDisputeRequestDto(), default));
        Assert.Equal(201, customer.StatusCode);
        Assert.True(SuccessOf(customer.Value));
        Assert.Equal((DisputeSide.Customer, 11), customerService.LastCall);

        var workerService = new StubFiling(201);
        var worker = Assert.IsType<ObjectResult>(
            await new WorkerDisputesController(workerService, Evidence, new FakeUser(22)).File(new FileDisputeRequestDto(), default));
        Assert.Equal(201, worker.StatusCode);
        Assert.Equal((DisputeSide.Worker, 22), workerService.LastCall);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(409)]
    public async Task Filing_lets_a_service_failure_reach_the_error_middleware_for_both_roles(int status)
    {
        var customer = new CustomerDisputesController(new StubFiling(status), Evidence, new FakeUser(11));
        Assert.IsType(FailureType[status], await Record.ExceptionAsync(() => customer.File(new FileDisputeRequestDto(), default)));

        var worker = new WorkerDisputesController(new StubFiling(status), Evidence, new FakeUser(22));
        Assert.IsType(FailureType[status], await Record.ExceptionAsync(() => worker.File(new FileDisputeRequestDto(), default)));
    }

    private static bool SuccessOf(object? value) => (bool)value!.GetType().GetProperty(nameof(ApiResponse<object>.Success))!.GetValue(value)!;

    [Fact]
    public async Task Without_a_user_id_every_action_answers_401()
    {
        foreach (var user in new[] { new FakeUser(null) })
        {
            var customer = new CustomerDisputesController(new StubFiling(201), Evidence, user);
            Assert.IsType<UnauthorizedObjectResult>(await customer.File(new FileDisputeRequestDto(), default));
            Assert.IsType<UnauthorizedObjectResult>(await customer.List(default));
            Assert.IsType<UnauthorizedObjectResult>(await customer.Get(1, default));

            var worker = new WorkerDisputesController(new StubFiling(201), Evidence, user);
            Assert.IsType<UnauthorizedObjectResult>(await worker.File(new FileDisputeRequestDto(), default));
            Assert.IsType<UnauthorizedObjectResult>(await worker.List(default));
            Assert.IsType<UnauthorizedObjectResult>(await worker.Get(1, default));
        }
    }

    [Fact]
    public async Task A_missing_body_is_a_400_before_the_service_is_called()
    {
        var service = new StubFiling(201);
        Assert.IsType<BadRequestObjectResult>(await new CustomerDisputesController(service, Evidence, new FakeUser(11)).File(null!, default));
        Assert.IsType<BadRequestObjectResult>(await new WorkerDisputesController(service, Evidence, new FakeUser(22)).File(null!, default));
        Assert.Null(service.LastCall);
    }

    // ---- SQL Server -----------------------------------------------------------------------------

    internal static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;

    internal class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => local.AddHours(-7);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(utcNow));
    }

    internal record Seeded(
        int AdminId, int CustomerId, int AddressId, int WorkerId, List<long> OrderIds, List<int> SlotIds, List<long> AssignmentIds);

    /// <summary>A customer, a worker and <paramref name="orders"/> orders with a COMPLETED assignment each (completed one hour before now).</summary>
    internal static async Task<Seeded> SeedAsync(DateTime now, int orders = 1)
    {
        await using var db = new AppDbContext(Options());

        var admin = new AdminAccount
        {
            Email = "dsp-" + Guid.NewGuid().ToString("N")[..10] + "@example.test",
            FullName = "Dispute Admin",
            PasswordHash = "x",
            AdminRole = "SUPER_ADMIN",
            IsActive = true,
            CreatedAt = now,
        };
        db.Admins.Add(admin);

        var customer = new Customer
        {
            PhoneNumber = "091" + Random.Shared.Next(1000000, 9999999),
            FullName = "Dispute Customer",
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
            FullName = "Dispute Worker",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var orderRows = new List<JobOrder>();
        for (var i = 0; i < orders; i++)
        {
            orderRows.Add(new JobOrder
            {
                OrderCode = "ORD" + Random.Shared.Next(100000, 999999),
                CustomerId = customer.CustomerId,
                AddressId = address.AddressId,
                ServiceTier = ServiceTier.Economy,
                ScheduledDate = DateOnly.FromDateTime(now).AddDays(-i),
                ShiftCode = "SHIFT1",
                AreaSnapshotM2 = 80m,
                RequiredWorkers = 1,
                TotalAmount = 260000m,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        db.JobOrders.AddRange(orderRows);
        await db.SaveChangesAsync();

        var slots = orderRows.Select((o, i) => new BookingSlot
        {
            WorkerId = worker.WorkerId,
            SlotDate = o.ScheduledDate,
            ShiftCode = "SHIFT1",
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(12, 0),
            SlotSource = "MANUAL",
            SlotStatus = "LOCKED",
            UpdatedAt = now,
        }).ToList();
        db.BookingSlots.AddRange(slots);
        await db.SaveChangesAsync();

        var done = new[]
        {
            JobAssignmentStatus.Assigned, JobAssignmentStatus.CheckedIn, JobAssignmentStatus.InProgress,
            JobAssignmentStatus.AwaitingAcceptance, JobAssignmentStatus.Completed,
        };
        var assignments = new List<JobAssignment>();
        for (var i = 0; i < orderRows.Count; i++)
        {
            var a = new JobAssignment
            {
                OrderId = orderRows[i].OrderId,
                CustomerId = customer.CustomerId,
                WorkerId = worker.WorkerId,
                SlotId = slots[i].SlotId,
                ServiceTier = ServiceTier.Economy,
                AssignmentSeq = 1,
                DispatchRadiusKm = 5,
                GrossAmount = 260000m,
                CommissionRate = 0.200m,
                PayoutAmount = 208000m,
                CreatedAt = now,
                UpdatedAt = now,
            };
            foreach (var step in done) a.TransitionTo(step);
            a.CompletedAt = now.AddHours(-1);
            assignments.Add(a);
        }

        db.JobAssignments.AddRange(assignments);
        await db.SaveChangesAsync();

        return new Seeded(admin.AdminId, customer.CustomerId, address.AddressId, worker.WorkerId,
            orderRows.Select(o => o.OrderId).ToList(), slots.Select(s => s.SlotId).ToList(), assignments.Select(a => a.AssignmentId).ToList());
    }

    internal static async Task CleanAsync(Seeded s)
    {
        await using var db = new AppDbContext(Options());
        await db.DisputeTickets.Where(d => s.OrderIds.Contains(d.OrderId)).ExecuteDeleteAsync();
        await db.JobPhotos.Where(p => s.AssignmentIds.Contains(p.AssignmentId)).ExecuteDeleteAsync();
        await db.CheckInLogs.Where(c => s.AssignmentIds.Contains(c.AssignmentId)).ExecuteDeleteAsync();
        await db.JobAssignments.Where(a => s.AssignmentIds.Contains(a.AssignmentId)).ExecuteDeleteAsync();
        await db.BookingSlots.Where(b => s.SlotIds.Contains(b.SlotId)).ExecuteDeleteAsync();
        await db.JobOrders.Where(o => s.OrderIds.Contains(o.OrderId)).ExecuteDeleteAsync();
        await db.CustomerAddresses.Where(a => a.AddressId == s.AddressId).ExecuteDeleteAsync();
        await db.Customers.Where(c => c.CustomerId == s.CustomerId).ExecuteDeleteAsync();
        await db.Workers.Where(w => w.WorkerId == s.WorkerId).ExecuteDeleteAsync();
        await db.Admins.Where(a => a.AdminId == s.AdminId).ExecuteDeleteAsync();
    }

    private static DisputeFilingService Filing(AppDbContext db, DateTime now) =>
        new(new EfDisputeRepository(db), new FixedClock(now), Microsoft.Extensions.Options.Options.Create(new DisputeOptions()));

    private static AdminDisputeService Admin(AppDbContext db, DateTime now) =>
        new(new EfDisputeRepository(db), new FixedClock(now), Microsoft.Extensions.Options.Options.Create(new DisputeOptions()));

    private static FileDisputeRequestDto Body(long orderId) => new()
    {
        OrderId = orderId,
        Category = "QUALITY",
        Description = "The kitchen is still dirty",
        EvidenceUrls = ["/uploads/a.jpg", "/uploads/b.jpg"],
    };

    [Fact]
    public async Task Real_database_flow_files_lists_queues_shows_the_case_file_and_takes_a_ticket()
    {
        if (!IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var s = await SeedAsync(now);
        try
        {
            await using (var extra = new AppDbContext(Options()))
            {
                extra.CheckInLogs.Add(new CheckInLog
                {
                    AssignmentId = s.AssignmentIds[0],
                    DeviceLat = 10.76m,
                    DeviceLng = 106.66m,
                    DistanceM = 42.5m,
                    GpsVerified = true,
                    CallAttempts = 0,
                    CheckedInAt = now.AddHours(-5),
                });
                extra.JobPhotos.AddRange(
                    new JobPhoto { AssignmentId = s.AssignmentIds[0], PhotoPhase = "BEFORE", AngleNo = 1, ImageUrl = "/b1.jpg", VolScore = 91, IsAccepted = true, CapturedAt = now.AddHours(-4) },
                    new JobPhoto { AssignmentId = s.AssignmentIds[0], PhotoPhase = "AFTER", AngleNo = 1, ImageUrl = "/a1.jpg", VolScore = 82, IsAccepted = true, CapturedAt = now.AddHours(-2) });
                await extra.SaveChangesAsync();
            }

            await using var db = new AppDbContext(Options());

            // another customer or worker is not on the order: 404
            Assert.Equal(404, (await Filing(db, now).FileAsync(DisputeSide.Customer, s.CustomerId + 100000, Body(s.OrderIds[0])).Outcome()).StatusCode);
            Assert.Equal(404, (await Filing(db, now).FileAsync(DisputeSide.Worker, s.WorkerId + 100000, Body(s.OrderIds[0])).Outcome()).StatusCode);

            var filed = await Filing(db, now).FileAsync(DisputeSide.Customer, s.CustomerId, Body(s.OrderIds[0])).Outcome();
            Assert.True(filed.Success);
            var id = filed.Data!.DisputeId;
            Assert.True(id > 0);

            await using (var verify = new AppDbContext(Options()))
            {
                var row = await verify.DisputeTickets.AsNoTracking().SingleAsync(d => d.DisputeId == id);
                Assert.Equal("OPEN", row.DisputeStatus);
                Assert.Equal("CUSTOMER", row.RaisedBy);
                Assert.Equal("QUALITY", row.Category);
                Assert.Equal(["/uploads/a.jpg", "/uploads/b.jpg"], System.Text.Json.JsonSerializer.Deserialize<string[]>(row.EvidenceUrls!)!);
                Assert.Equal(48, Math.Round((row.SlaDueAt - row.CreatedAt).TotalHours));
                Assert.Null(row.FaultParty);
            }

            // one ticket per order: the worker's filing for the same order is 409
            Assert.Equal(409, (await Filing(db, now).FileAsync(DisputeSide.Worker, s.WorkerId, Body(s.OrderIds[0])).Outcome()).StatusCode);

            Assert.Equal(id, Assert.Single((await Filing(db, now).ListAsync(DisputeSide.Customer, s.CustomerId).Outcome()).Data!).DisputeId);
            Assert.Equal(id, Assert.Single((await Filing(db, now).ListAsync(DisputeSide.Worker, s.WorkerId).Outcome()).Data!).DisputeId);
            Assert.Equal(id, (await Filing(db, now).GetAsync(DisputeSide.Worker, s.WorkerId, id).Outcome()).Data!.DisputeId);
            Assert.Equal(404, (await Filing(db, now).GetAsync(DisputeSide.Customer, s.CustomerId + 100000, id).Outcome()).StatusCode);

            // admin queue with names, priority and the SLA
            var queue = await Admin(db, now).SearchAsync("OPEN", null, null, null, null).Outcome();
            var item = Assert.Single(queue.Data!.Items, i => i.DisputeId == id);
            Assert.Equal("Dispute Customer", item.CustomerName);
            Assert.Equal("Dispute Worker", Assert.Single(item.Workers).FullName);
            Assert.Equal("FREELANCER", item.Workers[0].WorkerType);
            Assert.Equal("LOW", item.Priority); // 48 h left
            Assert.InRange(item.SlaSecondsRemaining, 48 * 3600 - 5, 48 * 3600);

            // case file: the ticket, the check-in, both photos and the timeline
            var file = (await Admin(db, now).GetAsync(id).Outcome()).Data!;
            Assert.Equal(["CHECK_IN", "PHOTO_AFTER", "CHECK_OUT", "CUSTOMER_DISPUTED"], file.ShiftTimeline.Select(e => e.Type).ToArray());
            Assert.Equal(42.5m, file.ShiftTimeline[0].DistanceM);
            Assert.Equal(["AFTER/1", "BEFORE/1"], file.Photos.Select(p => $"{p.Phase}/{p.AngleNo}").ToArray());

            // take: OPEN -> IN_REVIEW with the admin as handler (FK to ADMIN), a second take is 409
            var taken = await Admin(db, now).TakeAsync(s.AdminId, id).Outcome();
            Assert.Equal("IN_REVIEW", taken.Data!.DisputeStatus);
            Assert.Equal(s.AdminId, taken.Data.ResolvedBy);
            Assert.Equal(409, (await Admin(db, now).TakeAsync(s.AdminId, id).Outcome()).StatusCode);

            await using var after = new AppDbContext(Options());
            var saved = await after.DisputeTickets.AsNoTracking().SingleAsync(d => d.DisputeId == id);
            Assert.Equal("IN_REVIEW", saved.DisputeStatus);
            Assert.Equal(s.AdminId, saved.ResolvedBy);
        }
        finally
        {
            await CleanAsync(s);
        }
    }

    [Fact]
    public async Task Six_simultaneous_filings_for_one_order_give_one_201_five_409_and_one_row()
    {
        if (!IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var s = await SeedAsync(now);
        try
        {
            using var gate = new ManualResetEventSlim(false);

            async Task<int> File(DisputeSide side, int caller)
            {
                await using var db = new AppDbContext(Options());
                var service = Filing(db, now);
                gate.Wait();
                return (await service.FileAsync(side, caller, Body(s.OrderIds[0])).Outcome()).StatusCode;
            }

            var tasks = Enumerable.Range(0, 6)
                .Select(i => Task.Run(() => i % 2 == 0 ? File(DisputeSide.Customer, s.CustomerId) : File(DisputeSide.Worker, s.WorkerId)))
                .ToArray();
            await Task.Delay(300);
            gate.Set();
            var codes = await Task.WhenAll(tasks);

            Assert.Equal(1, codes.Count(c => c == 200));
            Assert.Equal(5, codes.Count(c => c == 409));
            await using var verify = new AppDbContext(Options());
            Assert.Equal(1, await verify.DisputeTickets.CountAsync(d => d.OrderId == s.OrderIds[0]));
        }
        finally
        {
            await CleanAsync(s);
        }
    }

    [Fact]
    public async Task The_24h_window_is_applied_to_the_real_completion_and_shift_times_and_an_unfinished_order_is_409()
    {
        if (!IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var s = await SeedAsync(now);
        try
        {
            await using var db = new AppDbContext(Options());

            // two days later both the completion (1 h before now) and today's shift end are far outside 24 h
            var late = await Filing(db, now.AddDays(2)).FileAsync(DisputeSide.Customer, s.CustomerId, Body(s.OrderIds[0])).Outcome();
            Assert.Equal(409, late.StatusCode);
            Assert.Contains("passed", late.ErrorMessage);
            Assert.False(await db.DisputeTickets.AnyAsync(d => d.OrderId == s.OrderIds[0]));

            // an assignment still IN_PROGRESS has nothing to dispute yet
            await db.Database.ExecuteSqlRawAsync("UPDATE JOB_ASSIGNMENT SET assignment_status = 'IN_PROGRESS' WHERE assignment_id = {0}", s.AssignmentIds[0]);
            var early = await Filing(db, now).FileAsync(DisputeSide.Customer, s.CustomerId, Body(s.OrderIds[0])).Outcome();
            Assert.Equal(409, early.StatusCode);
            Assert.Contains("nothing to dispute", early.ErrorMessage);
        }
        finally
        {
            await CleanAsync(s);
        }
    }
}
