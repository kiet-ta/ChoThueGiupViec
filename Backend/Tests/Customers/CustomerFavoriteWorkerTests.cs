using System.Reflection;
using CommonService.Application.Common.Models;
using CommonService.Application.Features.Customers;
using CommonService.Application.Features.Customers.Dtos;
using CommonService.Application.Features.Customers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Customers;
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
/// BE-M1-06: favorite workers (contract customers.md §2.3, decisions Q21 C3).
/// DB-backed tests run against the local SQL Server of BASE-07 with real WORKER rows (FAVORITE_WORKER has a foreign
/// key to WORKER) and delete everything they create. Worker display data comes from the port Fake, never from WORKER.
/// </summary>
public class CustomerFavoriteWorkerTests
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

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;

    private static AppDbContext CreateContext() => new(Options());

    private sealed class TestClock(DateTime initialUtc) : IClock
    {
        public DateTime UtcNow { get; set; } = initialUtc;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => local.AddHours(-7);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(UtcNow));
    }

    /// <summary>Counts calls so the list can be shown to use one batched port call.</summary>
    private sealed class CountingWorkerProfileQuery(IWorkerProfileQuery inner) : IWorkerProfileQuery
    {
        public int GetCalls { get; private set; }
        public int GetManyCalls { get; private set; }

        public Task<WorkerProfileSummary?> GetAsync(int workerId, CancellationToken cancellationToken = default)
        {
            GetCalls++;
            return inner.GetAsync(workerId, cancellationToken);
        }

        public Task<IReadOnlyDictionary<int, WorkerProfileSummary>> GetManyAsync(IEnumerable<int> workerIds, CancellationToken cancellationToken = default)
        {
            GetManyCalls++;
            return inner.GetManyAsync(workerIds, cancellationToken);
        }

        public Task<bool> ExistsAsync(int workerId, CancellationToken cancellationToken = default) =>
            inner.ExistsAsync(workerId, cancellationToken);
    }

    /// <summary>One service graph on a real context, plus the rows a test created (removed on dispose).</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly List<int> _customerIds = [];
        private readonly List<int> _workerIds = [];

        public AppDbContext Db { get; } = CreateContext();
        public TestClock Clock { get; } = new(new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc));
        public FakeWorkerProfileQuery Port { get; } = new();
        public CountingWorkerProfileQuery CountingPort { get; }
        public CustomerFavoriteWorkerService Service { get; }

        public Fixture()
        {
            CountingPort = new CountingWorkerProfileQuery(Port);
            Service = BuildService(Db, Clock, CountingPort);
        }

        public static CustomerFavoriteWorkerService BuildService(AppDbContext db, IClock clock, IWorkerProfileQuery port) =>
            new(new CustomerRepository(db), new FavoriteWorkerRepository(db), port, clock);

        public async Task<int> AddCustomerAsync()
        {
            var created = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
            var customer = new Customer
            {
                PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}",
                FullName = "Favorite Test",
                OtpVerifiedAt = created,
                TrustScore = 0.00m,
                AccountStatus = CustomerAccountStatus.Active,
                CreatedAt = created,
                UpdatedAt = created
            };
            Db.Customers.Add(customer);
            await Db.SaveChangesAsync();
            _customerIds.Add(customer.CustomerId);
            return customer.CustomerId;
        }

        /// <summary>Creates a real WORKER row (FK target) and registers the same worker in the port Fake.</summary>
        public async Task<int> AddWorkerAsync(
            string name = "Tran Thi B",
            decimal rating = 4.80m,
            int completedJobs = 25,
            WorkStatus status = WorkStatus.Idle,
            bool registerInPort = true)
        {
            var now = Clock.UtcNow;
            var worker = Worker.CreateFreelancer(
                $"03{Random.Shared.Next(10000000, 99999999)}",
                Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString(),
                name);
            worker.KycStatus = "APPROVED";
            worker.RatingAvg = rating;
            worker.CompletedJobs = completedJobs;
            worker.CreatedAt = now;
            worker.UpdatedAt = now;
            Db.Workers.Add(worker);
            await Db.SaveChangesAsync();
            Db.Entry(worker).State = EntityState.Detached;
            _workerIds.Add(worker.WorkerId);

            if (registerInPort)
            {
                Port.Add(new WorkerProfileSummary(worker.WorkerId, name, rating, completedJobs, status));
            }

            return worker.WorkerId;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var id in _customerIds)
            {
                await Db.FavoriteWorkers.Where(f => f.CustomerId == id).ExecuteDeleteAsync();
                await Db.Customers.Where(c => c.CustomerId == id).ExecuteDeleteAsync();
            }

            foreach (var id in _workerIds)
            {
                await Db.FavoriteWorkers.Where(f => f.WorkerId == id).ExecuteDeleteAsync();
                await Db.Workers.Where(w => w.WorkerId == id).ExecuteDeleteAsync();
            }

            await Db.DisposeAsync();
        }
    }

    private static async Task<List<FavoriteWorker>> StoredAsync(int customerId)
    {
        await using var db = CreateContext();
        return await db.FavoriteWorkers.AsNoTracking().Where(f => f.CustomerId == customerId).ToListAsync();
    }

    // ---- list ----

    [Fact]
    public async Task List_of_a_customer_without_favorites_is_an_empty_200_and_never_calls_the_port()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();

        var result = await fx.Service.ListAsync(customerId);

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.Empty(result.Data!);
        Assert.Equal(0, fx.CountingPort.GetManyCalls);
    }

    [Fact]
    public async Task List_is_newest_first_with_worker_fields_from_the_port_and_one_batched_call()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var first = await fx.AddWorkerAsync("First", 4.50m, 10, WorkStatus.Idle);
        var second = await fx.AddWorkerAsync("Second", 4.90m, 80, WorkStatus.Busy);
        var third = await fx.AddWorkerAsync("Third", 3.75m, 3, WorkStatus.Locked);

        await fx.Service.AddAsync(customerId, first);
        fx.Clock.UtcNow = fx.Clock.UtcNow.AddMinutes(1);
        await fx.Service.AddAsync(customerId, second);
        fx.Clock.UtcNow = fx.Clock.UtcNow.AddMinutes(1);
        await fx.Service.AddAsync(customerId, third);
        var getCallsBefore = fx.CountingPort.GetCalls;

        var result = await fx.Service.ListAsync(customerId);

        var list = result.Data!;
        Assert.Equal(new[] { third, second, first }, list.Select(x => x.WorkerId).ToArray());
        Assert.Equal("Third", list[0].FullName);
        Assert.Equal(3.75m, list[0].RatingAvg);
        Assert.Equal(3, list[0].CompletedJobs);
        Assert.Equal("LOCKED", list[0].WorkStatus);
        Assert.Equal("BUSY", list[1].WorkStatus);
        Assert.Equal("IDLE", list[2].WorkStatus);
        Assert.Equal(1, fx.CountingPort.GetManyCalls);          // one batched call for three rows
        Assert.Equal(getCallsBefore, fx.CountingPort.GetCalls);  // no per-row lookups
    }

    [Fact]
    public async Task List_leaves_out_a_favorite_whose_worker_the_port_does_not_return()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var known = await fx.AddWorkerAsync("Known");
        var unknown = await fx.AddWorkerAsync("Unknown", registerInPort: false);
        // The unknown worker is favorited directly: PUT would answer 404 because the port does not know it.
        await using (var db = CreateContext())
        {
            db.FavoriteWorkers.Add(new FavoriteWorker { CustomerId = customerId, WorkerId = unknown, CreatedAt = fx.Clock.UtcNow });
            await db.SaveChangesAsync();
        }

        await fx.Service.AddAsync(customerId, known);

        var list = (await fx.Service.ListAsync(customerId)).Data!;

        Assert.Equal(known, Assert.Single(list).WorkerId);
        Assert.Equal(2, (await StoredAsync(customerId)).Count); // the row is still stored, only not displayed
    }

    [Fact]
    public async Task Favorites_of_two_customers_are_isolated()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var alice = await fx.AddCustomerAsync();
        var bob = await fx.AddCustomerAsync();
        var shared = await fx.AddWorkerAsync("Shared");
        var onlyAlice = await fx.AddWorkerAsync("Only Alice");

        await fx.Service.AddAsync(alice, shared);
        await fx.Service.AddAsync(alice, onlyAlice);
        await fx.Service.AddAsync(bob, shared);

        Assert.Equal(2, (await fx.Service.ListAsync(alice)).Data!.Count);
        Assert.Equal(shared, Assert.Single((await fx.Service.ListAsync(bob)).Data!).WorkerId);

        await fx.Service.RemoveAsync(bob, shared);

        Assert.Empty((await fx.Service.ListAsync(bob)).Data!);
        Assert.Equal(2, (await fx.Service.ListAsync(alice)).Data!.Count); // Alice keeps hers
    }

    // ---- add (PUT) ----

    [Fact]
    public async Task Add_returns_the_favorite_with_port_data_and_a_server_set_added_at()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var workerId = await fx.AddWorkerAsync("Le Van C", 4.20m, 12, WorkStatus.Busy);

        var result = await fx.Service.AddAsync(customerId, workerId);

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(workerId, result.Data!.WorkerId);
        Assert.Equal("Le Van C", result.Data.FullName);
        Assert.Equal(4.20m, result.Data.RatingAvg);
        Assert.Equal(12, result.Data.CompletedJobs);
        Assert.Equal("BUSY", result.Data.WorkStatus);
        Assert.Equal(fx.Clock.UtcNow, result.Data.AddedAt);
        Assert.Single(await StoredAsync(customerId));
    }

    [Fact]
    public async Task Adding_the_same_worker_twice_keeps_one_row_and_the_original_added_at()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var workerId = await fx.AddWorkerAsync();

        var first = await fx.Service.AddAsync(customerId, workerId);
        var firstTime = fx.Clock.UtcNow;
        fx.Clock.UtcNow = fx.Clock.UtcNow.AddHours(3);
        var second = await fx.Service.AddAsync(customerId, workerId);

        Assert.True(second.Success);
        Assert.Equal(200, second.StatusCode);
        Assert.Equal(firstTime, second.Data!.AddedAt);
        Assert.Equal(first.Data!.AddedAt, second.Data.AddedAt);
        var stored = Assert.Single(await StoredAsync(customerId));
        Assert.Equal(firstTime, stored.CreatedAt);
    }

    [Fact]
    public async Task Concurrent_adds_of_the_same_pair_yield_one_row_and_no_error()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var workerId = await fx.AddWorkerAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = CreateContext();
            return await Fixture.BuildService(db, fx.Clock, fx.Port).AddAsync(customerId, workerId);
        }));

        Assert.All(results, r => Assert.True(r.Success));
        Assert.Single(await StoredAsync(customerId));
    }

    /// <summary>A context that inserts the same pair through another connection right before its own save: a deterministic double-click.</summary>
    private sealed class RacingDbContext(DbContextOptions<AppDbContext> options, int customerId, int workerId, DateTime racerCreatedAt)
        : AppDbContext(options)
    {
        private bool _raced;

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_raced)
            {
                _raced = true;
                await using var other = new AppDbContext(options);
                other.FavoriteWorkers.Add(new FavoriteWorker { CustomerId = customerId, WorkerId = workerId, CreatedAt = racerCreatedAt });
                await other.SaveChangesAsync(cancellationToken);
            }

            return await base.SaveChangesAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task A_duplicate_key_from_a_racing_request_is_turned_into_the_existing_row_not_an_error()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var workerId = await fx.AddWorkerAsync();
        var racerTime = fx.Clock.UtcNow.AddMinutes(-5);
        await using var racing = new RacingDbContext(Options(), customerId, workerId, racerTime);
        var repository = new FavoriteWorkerRepository(racing);

        var row = await repository.AddIfMissingAsync(customerId, workerId, fx.Clock.UtcNow);

        Assert.Equal(racerTime, row.CreatedAt); // the row the racing request inserted first wins
        var stored = Assert.Single(await StoredAsync(customerId));
        Assert.Equal(racerTime, stored.CreatedAt);
    }

    [Fact]
    public async Task Add_returns_404_for_a_worker_the_port_does_not_know_and_stores_nothing()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var hiddenWorker = await fx.AddWorkerAsync(registerInPort: false);

        var unknownToPort = await fx.Service.AddAsync(customerId, hiddenWorker);
        var nonexistent = await fx.Service.AddAsync(customerId, int.MaxValue);

        Assert.Equal(404, unknownToPort.StatusCode);
        Assert.Equal(404, nonexistent.StatusCode);
        Assert.Empty(await StoredAsync(customerId));
    }

    [Fact]
    public async Task Add_for_a_removed_customer_returns_404()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var workerId = await fx.AddWorkerAsync();

        var result = await fx.Service.AddAsync(int.MaxValue, workerId);

        Assert.Equal(404, result.StatusCode);
    }

    // ---- remove (DELETE) ----

    [Fact]
    public async Task Remove_deletes_the_row_and_a_second_remove_is_still_a_success()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var workerId = await fx.AddWorkerAsync();
        await fx.Service.AddAsync(customerId, workerId);

        var first = await fx.Service.RemoveAsync(customerId, workerId);
        var second = await fx.Service.RemoveAsync(customerId, workerId);

        Assert.True(first.Success);
        Assert.Equal(200, first.StatusCode);
        Assert.Null(first.Data);
        Assert.True(second.Success);
        Assert.Empty(await StoredAsync(customerId));
    }

    [Fact]
    public async Task Remove_of_a_worker_that_was_never_a_favorite_or_does_not_exist_is_a_success()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var workerId = await fx.AddWorkerAsync();

        Assert.True((await fx.Service.RemoveAsync(customerId, workerId)).Success);
        Assert.True((await fx.Service.RemoveAsync(customerId, int.MaxValue)).Success);
    }

    [Fact]
    public async Task A_removed_worker_can_be_added_again()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var workerId = await fx.AddWorkerAsync();
        await fx.Service.AddAsync(customerId, workerId);
        await fx.Service.RemoveAsync(customerId, workerId);
        fx.Clock.UtcNow = fx.Clock.UtcNow.AddDays(1);

        var again = await fx.Service.AddAsync(customerId, workerId);

        Assert.True(again.Success);
        Assert.Equal(fx.Clock.UtcNow, again.Data!.AddedAt);
    }

    // ---- the Customers module only reads workers through the port ----

    [Fact]
    public void Service_depends_on_the_port_and_not_on_the_database_context()
    {
        var parameters = typeof(CustomerFavoriteWorkerService).GetConstructors().Single()
            .GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.Contains(typeof(IWorkerProfileQuery), parameters);
        Assert.DoesNotContain(typeof(AppDbContext), parameters);
    }

    // ---- controller: ids from ICurrentUser, status mapping (no database) ----

    private sealed class RecordingService : ICustomerFavoriteWorkerService
    {
        public int? LastCustomerId { get; private set; }
        public int? LastWorkerId { get; private set; }
        public FavoriteWorkerResult<FavoriteWorkerDto> AddResult { get; set; } =
            FavoriteWorkerResult<FavoriteWorkerDto>.Ok(new FavoriteWorkerDto { WorkerId = 9 });

        public Task<FavoriteWorkerResult<IReadOnlyList<FavoriteWorkerDto>>> ListAsync(int customerId, CancellationToken ct = default)
        {
            LastCustomerId = customerId;
            return Task.FromResult(FavoriteWorkerResult<IReadOnlyList<FavoriteWorkerDto>>.Ok(new List<FavoriteWorkerDto>()));
        }

        public Task<FavoriteWorkerResult<FavoriteWorkerDto>> AddAsync(int customerId, int workerId, CancellationToken ct = default)
        {
            LastCustomerId = customerId;
            LastWorkerId = workerId;
            return Task.FromResult(AddResult);
        }

        public Task<FavoriteWorkerResult<object?>> RemoveAsync(int customerId, int workerId, CancellationToken ct = default)
        {
            LastCustomerId = customerId;
            LastWorkerId = workerId;
            return Task.FromResult(FavoriteWorkerResult<object?>.Ok(null));
        }
    }

    private sealed class TestUser(int? id) : ICurrentUser
    {
        public bool IsAuthenticated => id != null;
        public int? UserId => id;
        public UserRole? Role => id == null ? null : UserRole.Customer;
    }

    private static CustomerFavoriteWorkersController CreateController(ICustomerFavoriteWorkerService service, int? userId) =>
        new(service, new TestUser(userId)) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    [Fact]
    public async Task Controller_uses_the_id_of_the_signed_in_customer_for_every_endpoint()
    {
        var service = new RecordingService();

        await CreateController(service, 42).List(CancellationToken.None);
        Assert.Equal(42, service.LastCustomerId);

        await CreateController(service, 43).Add(7, CancellationToken.None);
        Assert.Equal((43, 7), (service.LastCustomerId, service.LastWorkerId));

        await CreateController(service, 44).Remove(8, CancellationToken.None);
        Assert.Equal((44, 8), (service.LastCustomerId, service.LastWorkerId));
    }

    [Fact]
    public async Task Controller_returns_200_with_the_envelope_for_list_add_and_remove()
    {
        var controller = CreateController(new RecordingService(), 5);

        var list = Assert.IsType<OkObjectResult>(await controller.List(CancellationToken.None));
        Assert.Empty(Assert.IsType<ApiResponse<IReadOnlyList<FavoriteWorkerDto>>>(list.Value).Data!);

        var add = Assert.IsType<OkObjectResult>(await controller.Add(9, CancellationToken.None));
        Assert.Equal(9, Assert.IsType<ApiResponse<FavoriteWorkerDto>>(add.Value).Data!.WorkerId);

        var remove = Assert.IsType<OkObjectResult>(await controller.Remove(9, CancellationToken.None));
        var body = Assert.IsType<ApiResponse<object?>>(remove.Value);
        Assert.True(body.Success);
        Assert.Null(body.Data);
    }

    [Fact]
    public async Task Controller_maps_a_missing_worker_to_404_with_a_failure_envelope()
    {
        var service = new RecordingService { AddResult = FavoriteWorkerResult<FavoriteWorkerDto>.NotFound("Worker not found.") };

        var action = await CreateController(service, 5).Add(1, CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.False(Assert.IsType<ApiResponse<object>>(result.Value).Success);
    }

    [Fact]
    public async Task Controller_returns_401_for_every_endpoint_when_there_is_no_customer_id()
    {
        var service = new RecordingService();
        var controller = CreateController(service, null);

        Assert.IsType<UnauthorizedObjectResult>(await controller.List(CancellationToken.None));
        Assert.IsType<UnauthorizedObjectResult>(await controller.Add(1, CancellationToken.None));
        Assert.IsType<UnauthorizedObjectResult>(await controller.Remove(1, CancellationToken.None));
        Assert.Null(service.LastCustomerId);
    }

    // ---- authorization, routes, registration ----

    [Fact]
    public void Controller_requires_the_CustomerOnly_policy_so_other_roles_get_403_and_anonymous_401()
    {
        var attribute = typeof(CustomerFavoriteWorkersController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("CustomerOnly", attribute!.Policy); // the policy itself is covered by CustomerOnly_policy_allows_only_authenticated_customers
    }

    [Fact]
    public void Controller_routes_and_verbs_match_the_contract()
    {
        var type = typeof(CustomerFavoriteWorkersController);

        Assert.Equal("api/customers/me/favorite-workers", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("{workerId:int}", type.GetMethod(nameof(CustomerFavoriteWorkersController.Add))!.GetCustomAttribute<HttpPutAttribute>()!.Template);
        Assert.Equal("{workerId:int}", type.GetMethod(nameof(CustomerFavoriteWorkersController.Remove))!.GetCustomAttribute<HttpDeleteAttribute>()!.Template);
        Assert.NotNull(type.GetMethod(nameof(CustomerFavoriteWorkersController.List))!.GetCustomAttribute<HttpGetAttribute>());
    }

    [Fact]
    public void CustomersModule_registers_the_favorite_worker_repository_and_service()
    {
        var services = new ServiceCollection();

        new CustomersModule().ConfigureServices(services, new ConfigurationBuilder().Build());

        Assert.Contains(services, d => d.ServiceType == typeof(ICustomerFavoriteWorkerService)
                                       && d.ImplementationType == typeof(CustomerFavoriteWorkerService));
        Assert.Contains(services, d => d.ServiceType == typeof(IFavoriteWorkerRepository)
                                       && d.ImplementationType == typeof(FavoriteWorkerRepository));
    }
}
