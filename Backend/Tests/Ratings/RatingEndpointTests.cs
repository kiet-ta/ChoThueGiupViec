using System.Reflection;
using CommonService.Application.Common.Models;
using CommonService.Application.Common.Options;
using CommonService.Application.Features.Ratings;
using CommonService.Application.Features.Ratings.Dtos;
using CommonService.Application.Features.Ratings.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Ratings;
using CommonService.Infrastructure.Persistence;
using CommonService.WebAPI.Controllers.Ratings;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CommonService.Tests.Ratings;

/// <summary>BE-M6-01a: the four endpoints (policies, routes, status mapping) and the repository on the local SQL Server.</summary>
public class RatingEndpointTests
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

    // ---- controllers ----------------------------------------------------------------------------

    [Theory]
    [InlineData(typeof(CustomerRatingsController), "CustomerOnly", "api/customers/me/assignments/{assignmentId:long}")]
    [InlineData(typeof(WorkerRatingsController), "WorkerOnly", "api/workers/me/assignments/{assignmentId:long}")]
    public void Each_controller_has_its_role_policy_and_the_contract_route(Type controller, string policy, string route)
    {
        Assert.Equal(policy, controller.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal(route, controller.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Null(controller.GetMethod("GetWindow")!.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(controller.GetMethod("Submit")!.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Theory]
    [InlineData(typeof(CustomerRatingsController))]
    [InlineData(typeof(WorkerRatingsController))]
    public void Each_controller_has_exactly_the_window_GET_and_the_rating_POST(Type controller)
    {
        var actions = controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => (m.Name, Verb: m.GetCustomAttributes<HttpMethodAttribute>().Select(a => (a.HttpMethods.Single(), a.Template)).Single()))
            .OrderBy(a => a.Name)
            .ToArray();

        Assert.Equal(2, actions.Length);
        Assert.Equal(("GetWindow", ("GET", "rating-window")), (actions[0].Name, actions[0].Verb));
        Assert.Equal(("Submit", ("POST", "rating")), (actions[1].Name, actions[1].Verb));
    }

    private sealed class FakeUser(int? id) : ICurrentUser
    {
        public bool IsAuthenticated => id is not null;
        public int? UserId => id;
        public UserRole? Role => null;
    }

    private sealed class StubService(int status, string? message = null) : IRatingService
    {
        public (RaterSide Side, int Caller, long Assignment)? LastCall { get; private set; }

        public Task<RatingResult<RatingWindowDto>> GetWindowAsync(RaterSide side, int callerId, long assignmentId, CancellationToken cancellationToken = default)
        {
            LastCall = (side, callerId, assignmentId);
            return Task.FromResult(status == 200
                ? RatingResult<RatingWindowDto>.Ok(new RatingWindowDto { AssignmentId = assignmentId, Reason = "OPEN", CanRate = true })
                : RatingResult<RatingWindowDto>.NotFound());
        }

        public Task<RatingResult<RatingDto>> SubmitAsync(RaterSide side, int callerId, long assignmentId, SubmitRatingRequestDto request, CancellationToken cancellationToken = default)
        {
            LastCall = (side, callerId, assignmentId);
            return Task.FromResult(status switch
            {
                201 => RatingResult<RatingDto>.Created(new RatingDto { AssignmentId = assignmentId, RaterRole = "CUSTOMER", Stars = 5 }),
                400 => RatingResult<RatingDto>.ValidationError(new Dictionary<string, string[]> { ["stars"] = ["bad"] }),
                404 => RatingResult<RatingDto>.NotFound(),
                _ => RatingResult<RatingDto>.Conflict(message ?? "ALREADY_RATED"),
            });
        }
    }

    [Theory]
    [InlineData(201, 201)]
    [InlineData(400, 400)]
    [InlineData(404, 404)]
    [InlineData(409, 409)]
    public async Task Submit_maps_the_result_to_the_status_and_the_envelope(int status, int expected)
    {
        var service = new StubService(status);
        var controller = new CustomerRatingsController(service, new FakeUser(11));

        var response = await controller.Submit(77, new SubmitRatingRequestDto { Stars = 5 }, default);

        var objectResult = Assert.IsType<ObjectResult>(response);
        Assert.Equal(expected, objectResult.StatusCode);
        Assert.Equal(expected is 201, Assert.IsAssignableFrom<IApiResponseMarker>(AsMarker(objectResult.Value)).Success);
        Assert.Equal((RaterSide.Customer, 11, 77L), service.LastCall);
    }

    // A small adapter so one assertion covers ApiResponse<T> for any T.
    private interface IApiResponseMarker { bool Success { get; } }
    private sealed record Marker(bool Success) : IApiResponseMarker;
    private static IApiResponseMarker AsMarker(object? value) =>
        new Marker((bool)value!.GetType().GetProperty(nameof(ApiResponse<object>.Success))!.GetValue(value)!);

    [Fact]
    public async Task The_worker_controller_passes_the_worker_side_and_the_token_id_not_anything_from_the_request()
    {
        var service = new StubService(200);
        var controller = new WorkerRatingsController(service, new FakeUser(22));

        var response = await controller.GetWindow(88, default);

        Assert.Equal(200, Assert.IsType<ObjectResult>(response).StatusCode);
        Assert.Equal((RaterSide.Worker, 22, 88L), service.LastCall);
    }

    [Fact]
    public async Task Without_a_user_id_in_the_token_every_action_answers_401()
    {
        var customer = new CustomerRatingsController(new StubService(200), new FakeUser(null));
        Assert.IsType<UnauthorizedObjectResult>(await customer.GetWindow(1, default));
        Assert.IsType<UnauthorizedObjectResult>(await customer.Submit(1, new SubmitRatingRequestDto(), default));

        var worker = new WorkerRatingsController(new StubService(200), new FakeUser(null));
        Assert.IsType<UnauthorizedObjectResult>(await worker.GetWindow(1, default));
        Assert.IsType<UnauthorizedObjectResult>(await worker.Submit(1, new SubmitRatingRequestDto(), default));
    }

    [Fact]
    public async Task A_missing_body_is_a_400_before_the_service_is_called()
    {
        var service = new StubService(201);
        var controller = new CustomerRatingsController(service, new FakeUser(11));
        var response = await controller.Submit(1, null!, default);
        Assert.IsType<BadRequestObjectResult>(response);
        Assert.Null(service.LastCall);
    }

    [Fact]
    public async Task A_400_carries_the_errors_map_in_data()
    {
        var controller = new CustomerRatingsController(new StubService(400), new FakeUser(11));
        var result = Assert.IsType<ObjectResult>(await controller.Submit(1, new SubmitRatingRequestDto(), default));
        var body = Assert.IsType<ApiResponse<object>>(result.Value);
        Assert.NotNull(body.Data);
        Assert.Contains("errors", body.Data!.GetType().GetProperties().Select(p => p.Name));
    }

    // ---- SQL Server: the real repository, the unique index and a real race ----------------------

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => local.AddHours(-7);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(utcNow));
    }

    private sealed class NullPublisher : IPublisher
    {
        public int Count;
        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            Interlocked.Increment(ref Count);
            return Task.CompletedTask;
        }
    }

    private sealed record Seeded(int CustomerId, int WorkerId, int AddressId, long OrderId, int SlotId, long AssignmentId);

    /// <summary>One COMPLETED assignment with every row it needs (FKs), completed one hour before <paramref name="now"/>.</summary>
    private static async Task<Seeded> SeedCompletedAssignmentAsync(DateTime now)
    {
        await using var db = new AppDbContext(Options());
        var customer = new Customer
        {
            PhoneNumber = "091" + Random.Shared.Next(1000000, 9999999),
            FullName = "Rating Test",
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

        var worker = new Worker
        {
            PhoneNumber = "092" + Random.Shared.Next(1000000, 9999999),
            NationalId = "079" + Random.Shared.Next(100000000, 999999999),
            FullName = "Rating Worker",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var slot = new BookingSlot
        {
            WorkerId = worker.WorkerId,
            SlotDate = order.ScheduledDate,
            ShiftCode = order.ShiftCode,
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
        foreach (var step in new[]
                 {
                     JobAssignmentStatus.Assigned, JobAssignmentStatus.CheckedIn, JobAssignmentStatus.InProgress,
                     JobAssignmentStatus.AwaitingAcceptance, JobAssignmentStatus.Completed,
                 })
        {
            assignment.TransitionTo(step);
        }

        assignment.CompletedAt = now.AddHours(-1);
        db.JobAssignments.Add(assignment);
        await db.SaveChangesAsync();

        return new Seeded(customer.CustomerId, worker.WorkerId, address.AddressId, order.OrderId, slot.SlotId, assignment.AssignmentId);
    }

    private static async Task CleanAsync(Seeded s)
    {
        await using var db = new AppDbContext(Options());
        await db.TwoWayRatings.Where(r => r.AssignmentId == s.AssignmentId).ExecuteDeleteAsync();
        await db.JobAssignments.Where(a => a.AssignmentId == s.AssignmentId).ExecuteDeleteAsync();
        await db.BookingSlots.Where(b => b.SlotId == s.SlotId).ExecuteDeleteAsync();
        await db.JobOrders.Where(o => o.OrderId == s.OrderId).ExecuteDeleteAsync();
        await db.CustomerAddresses.Where(a => a.AddressId == s.AddressId).ExecuteDeleteAsync();
        await db.Customers.Where(c => c.CustomerId == s.CustomerId).ExecuteDeleteAsync();
        await db.Workers.Where(w => w.WorkerId == s.WorkerId).ExecuteDeleteAsync();
    }

    private static RatingService NewService(AppDbContext db, DateTime now, NullPublisher publisher) =>
        new(new EfRatingRepository(db), new FixedClock(now), Microsoft.Extensions.Options.Options.Create(new BusinessRules()), publisher);

    private static SubmitRatingRequestDto CustomerBody() => new()
    {
        Stars = 4,
        Criteria = new Dictionary<string, int> { ["punctuality"] = 4, ["cleaningQuality"] = 5, ["attitude"] = 3 },
        Comment = "ok",
    };

    [Fact]
    public async Task Real_database_flow_reads_the_assignment_writes_the_rating_and_answers_a_second_one_with_409()
    {
        if (!IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var seeded = await SeedCompletedAssignmentAsync(now);
        try
        {
            var publisher = new NullPublisher();
            await using var db = new AppDbContext(Options());
            var service = NewService(db, now, publisher);

            var window = await service.GetWindowAsync(RaterSide.Customer, seeded.CustomerId, seeded.AssignmentId);
            Assert.Equal("OPEN", window.Data!.Reason);

            var first = await service.SubmitAsync(RaterSide.Customer, seeded.CustomerId, seeded.AssignmentId, CustomerBody());
            Assert.Equal(201, first.StatusCode);
            Assert.True(first.Data!.RatingId > 0);

            await using var verify = new AppDbContext(Options());
            var row = await verify.TwoWayRatings.AsNoTracking().SingleAsync(r => r.AssignmentId == seeded.AssignmentId);
            Assert.Equal("CUSTOMER", row.RaterRole);
            Assert.Equal(seeded.WorkerId, row.WorkerId);
            Assert.Equal((byte)4, row.Stars);
            Assert.Contains("cleaning_quality", row.CriteriaJson);
            Assert.Equal(1, publisher.Count);

            // same context and a fresh one both see "already rated"
            var second = await service.SubmitAsync(RaterSide.Customer, seeded.CustomerId, seeded.AssignmentId, CustomerBody());
            Assert.Equal(409, second.StatusCode);
            await using var other = new AppDbContext(Options());
            var window2 = await NewService(other, now, publisher).GetWindowAsync(RaterSide.Customer, seeded.CustomerId, seeded.AssignmentId);
            Assert.Equal("ALREADY_RATED", window2.Data!.Reason);

            // the worker side of the same assignment is a separate rating
            var worker = await NewService(other, now, publisher).SubmitAsync(RaterSide.Worker, seeded.WorkerId, seeded.AssignmentId,
                new SubmitRatingRequestDto { Stars = 3, Criteria = new Dictionary<string, int> { ["cooperation"] = 3, ["workingConditions"] = 4 } });
            Assert.Equal(201, worker.StatusCode);

            // another customer cannot see the assignment
            Assert.Equal(404, (await NewService(other, now, publisher).GetWindowAsync(RaterSide.Customer, seeded.CustomerId + 100000, seeded.AssignmentId)).StatusCode);
        }
        finally
        {
            await CleanAsync(seeded);
        }
    }

    [Fact]
    public async Task Two_simultaneous_ratings_of_the_same_role_give_one_201_one_409_and_one_row()
    {
        if (!IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var seeded = await SeedCompletedAssignmentAsync(now);
        try
        {
            var publisher = new NullPublisher();
            using var gate = new ManualResetEventSlim(false);

            async Task<int> Submit()
            {
                await using var db = new AppDbContext(Options());
                var service = NewService(db, now, publisher);
                gate.Wait();
                return (await service.SubmitAsync(RaterSide.Customer, seeded.CustomerId, seeded.AssignmentId, CustomerBody())).StatusCode;
            }

            var tasks = Enumerable.Range(0, 6).Select(_ => Task.Run(Submit)).ToArray();
            await Task.Delay(300); // let every task build its context and wait at the gate
            gate.Set();
            var codes = await Task.WhenAll(tasks);

            Assert.Equal(1, codes.Count(c => c == 201));
            Assert.Equal(5, codes.Count(c => c == 409));
            Assert.Equal(1, publisher.Count);
            await using var verify = new AppDbContext(Options());
            Assert.Equal(1, await verify.TwoWayRatings.CountAsync(r => r.AssignmentId == seeded.AssignmentId && r.RaterRole == "CUSTOMER"));
        }
        finally
        {
            await CleanAsync(seeded);
        }
    }

    [Fact]
    public async Task A_completed_assignment_past_48_hours_is_closed_on_the_real_database()
    {
        if (!IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var seeded = await SeedCompletedAssignmentAsync(now); // completed 1 h before "now"
        try
        {
            await using var db = new AppDbContext(Options());
            var later = now.AddHours(48); // 49 h after completion
            var service = NewService(db, later, new NullPublisher());
            var result = await service.SubmitAsync(RaterSide.Customer, seeded.CustomerId, seeded.AssignmentId, CustomerBody());
            Assert.Equal(409, result.StatusCode);
            Assert.Contains("closed", result.ErrorMessage);
            Assert.False(await db.TwoWayRatings.AnyAsync(r => r.AssignmentId == seeded.AssignmentId));
        }
        finally
        {
            await CleanAsync(seeded);
        }
    }
}
