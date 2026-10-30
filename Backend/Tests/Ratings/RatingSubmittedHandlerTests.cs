using CommonService.Application.Common.Options;
using CommonService.Application.Features.Ratings;
using CommonService.Application.Features.Ratings.Services;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Ratings;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Tests.Ratings;

/// <summary>
/// ADMIN-UAT-10 finding A and decisions Q14: a saved rating moves WORKER.rating_avg (which dispatch, the profile, the favourite list and
/// order tracking read) and raises the two Q14 flags as audit rows.
/// </summary>
public class RatingSubmittedHandlerTests
{
    private const string ConnectionString =
        "Server=localhost;Database=ChoThueGiupViec;Integrated Security=True;TrustServerCertificate=True;";

    private const long AssignmentId = 70;
    private const int WorkerId = 5;
    private const int CustomerId = 9;

    private sealed class MemoryRatings : IRatingRepository
    {
        public Dictionary<long, RatingAssignmentInfo> Assignments { get; } = [];

        public Task<RatingAssignmentInfo?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Assignments.GetValueOrDefault(assignmentId));

        public Task<bool> ExistsAsync(long assignmentId, string raterRole, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> TryAddAsync(TwoWayRating rating, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class MemoryStore : IWorkerRatingStore
    {
        public Dictionary<int, Worker> Workers { get; } = [];

        /// <summary>The ratings workers gave the customer, newest first.</summary>
        public List<RecentCustomerRating> CustomerRatings { get; } = [];

        public int SetAverageCalls { get; private set; }

        public Task<WorkerAverageChange?> SetAverageAsync(int workerId, decimal average, CancellationToken cancellationToken = default)
        {
            SetAverageCalls++;
            if (!Workers.TryGetValue(workerId, out var worker)) return Task.FromResult<WorkerAverageChange?>(null);

            var previous = worker.RatingAvg;
            worker.RatingAvg = average;
            return Task.FromResult<WorkerAverageChange?>(new WorkerAverageChange(previous, worker.WorkerType));
        }

        public Task<IReadOnlyList<RecentCustomerRating>> GetRecentRatingsOfCustomerAsync(int customerId, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RecentCustomerRating>>(CustomerRatings.Take(take).ToList());
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

        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default) => action();
    }

    private sealed class Harness
    {
        public MemoryRatings Ratings { get; } = new();
        public MemoryStore Store { get; } = new();
        public StubReputation Reputation { get; } = new();
        public FakeAuditLog Audit { get; } = new();
        public CountingUnitOfWork Uow { get; } = new();
        public RatingSubmittedHandler Handler { get; }

        public Harness()
        {
            Ratings.Assignments[AssignmentId] = new RatingAssignmentInfo(AssignmentId, CustomerId, WorkerId, JobAssignmentStatus.Completed, DateTime.UtcNow);
            Handler = new RatingSubmittedHandler(Ratings, Store, Reputation, Audit, Uow, Microsoft.Extensions.Options.Options.Create(new BusinessRules()));
        }

        public Worker AddFreelancer(decimal storedAverage, decimal newAverage, int completedJobs)
        {
            var worker = Worker.CreateFreelancer("0901234567", "079000000001", "Tho");
            worker.WorkerId = WorkerId;
            worker.RatingAvg = storedAverage;
            Store.Workers[WorkerId] = worker;
            Reputation.Items[WorkerId] = new WorkerReputationDto(WorkerId, newAverage, completedJobs, 1m);
            return worker;
        }

        public Task CustomerRates() => Handler.Handle(new RatingSubmitted(1, AssignmentId, "CUSTOMER", 1, DateTime.UtcNow), CancellationToken.None);

        public Task WorkerRates() => Handler.Handle(new RatingSubmitted(2, AssignmentId, "WORKER", 1, DateTime.UtcNow), CancellationToken.None);
    }

    // ---- A: the column ---------------------------------------------------------------------------

    [Fact]
    public async Task A_customer_rating_writes_the_new_average_to_the_worker_column_and_saves_once()
    {
        var h = new Harness();
        var worker = h.AddFreelancer(storedAverage: 5.00m, newAverage: 3.50m, completedJobs: 3);

        await h.CustomerRates();

        Assert.Equal(3.50m, worker.RatingAvg);
        Assert.Equal(1, h.Uow.Saves);
        Assert.Empty(h.Audit.Entries); // 3 jobs: far from the 10 of Q14
    }

    [Fact]
    public async Task An_agency_worker_gets_the_column_too_but_never_a_low_rating_flag()
    {
        var h = new Harness();
        var staff = Worker.CreateAgencyStaff(7, "0901234568", "079000000002", "Staff");
        staff.WorkerId = WorkerId;
        staff.RatingAvg = 5.00m;
        h.Store.Workers[WorkerId] = staff;
        h.Reputation.Items[WorkerId] = new WorkerReputationDto(WorkerId, 3.00m, 30, 1m);

        await h.CustomerRates();

        Assert.Equal(3.00m, staff.RatingAvg);
        Assert.Empty(h.Audit.Entries); // Q14: ratings never touch an agency's SLA
    }

    [Fact]
    public async Task Nothing_happens_for_an_unknown_assignment_an_unknown_worker_or_a_worker_without_ratings()
    {
        var h = new Harness();
        await h.Handler.Handle(new RatingSubmitted(1, 999, "CUSTOMER", 1, DateTime.UtcNow), CancellationToken.None);
        await h.CustomerRates(); // no reputation for the worker
        h.Reputation.Items[WorkerId] = new WorkerReputationDto(WorkerId, 0m, 0, 0m); // reputation says "no rating yet"
        await h.CustomerRates();
        h.Reputation.Items[WorkerId] = new WorkerReputationDto(WorkerId, 2m, 1, 0m);
        await h.CustomerRates(); // reputation exists, the worker row does not

        Assert.Equal(1, h.Store.SetAverageCalls);
        Assert.Equal(0, h.Uow.Saves);
        Assert.Empty(h.Audit.Entries);
    }

    // ---- D (b): freelancer under 4.00 after 10 jobs ----------------------------------------------

    [Fact]
    public async Task A_freelancer_who_falls_under_the_threshold_after_ten_jobs_is_flagged_once_with_a_system_audit_row()
    {
        var h = new Harness();
        h.AddFreelancer(storedAverage: 4.10m, newAverage: 3.99m, completedJobs: 10);

        await h.CustomerRates();

        var flag = Assert.Single(h.Audit.Entries);
        Assert.Equal((AuditActorType.System, (int?)null, "WORKER", "5", "low_rating_flag", "false", "true"),
            (flag.ActorType, flag.AdminId, flag.EntityType, flag.EntityId, flag.FieldName, flag.OldValue, flag.NewValue));
        Assert.Contains("3.99", flag.Reason);
        Assert.Contains("10 completed jobs", flag.Reason);
    }

    [Theory]
    [InlineData(4.10, 4.00, 10)] // exactly 4.00 is not below it
    [InlineData(4.10, 3.50, 9)]  // nine jobs: the rule needs ten
    [InlineData(3.80, 3.50, 12)] // it was already below: the flag was raised when it crossed
    public async Task No_flag_on_the_boundaries_or_when_the_worker_was_already_below(double stored, double now, int jobs)
    {
        var h = new Harness();
        h.AddFreelancer((decimal)stored, (decimal)now, jobs);

        await h.CustomerRates();

        Assert.Empty(h.Audit.Entries);
    }

    // ---- D (a): a customer with three low ratings in a row ---------------------------------------

    private static RecentCustomerRating R(int workerId, byte stars) => new(workerId, stars);

    [Fact]
    public async Task Three_low_ratings_from_three_different_workers_flag_the_customer_once()
    {
        var h = new Harness();
        h.Store.CustomerRatings.AddRange([R(1, 2), R(2, 1), R(3, 2)]);

        await h.WorkerRates();

        var flag = Assert.Single(h.Audit.Entries);
        Assert.Equal((AuditActorType.System, "CUSTOMER", "9", "low_rating_flag"), (flag.ActorType, flag.EntityType, flag.EntityId, flag.FieldName));
        Assert.Equal(0, h.Store.SetAverageCalls); // a worker's rating of a customer never touches a worker average and blocks nobody (Q14)
        Assert.Equal(1, h.Uow.Saves);
    }

    [Theory]
    [InlineData(2, 2, 3, 3, 0)]  // two lows then a 3: three stars is not low
    [InlineData(2, 5, 2, 2, 0)]  // a good rating breaks the streak
    [InlineData(2, 2, 2, 5, 1)]  // an older good rating does not matter
    public async Task The_streak_must_be_three_consecutive_low_ratings(int s1, int s2, int s3, int s4, int expectedFlags)
    {
        var h = new Harness();
        h.Store.CustomerRatings.AddRange([R(1, (byte)s1), R(2, (byte)s2), R(3, (byte)s3), R(4, (byte)s4)]);

        await h.WorkerRates();

        Assert.Equal(expectedFlags, h.Audit.Entries.Count);
    }

    [Fact]
    public async Task Two_ratings_from_the_same_worker_do_not_make_three_different_workers()
    {
        var h = new Harness();
        h.Store.CustomerRatings.AddRange([R(1, 1), R(1, 1), R(2, 1)]);

        await h.WorkerRates();

        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Only_the_rating_that_completes_the_streak_raises_the_flag_not_the_ones_after_it()
    {
        var h = new Harness();
        h.Store.CustomerRatings.AddRange([R(4, 1), R(3, 2), R(2, 2), R(1, 2)]); // a fourth low rating on top of a streak that was flagged already

        await h.WorkerRates();

        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Fewer_than_three_ratings_never_flag()
    {
        var h = new Harness();
        h.Store.CustomerRatings.AddRange([R(1, 1), R(2, 1)]);

        await h.WorkerRates();

        Assert.Empty(h.Audit.Entries);
    }

    // ---- H: one source for the Q14 numbers --------------------------------------------------------

    [Fact]
    public void The_matching_exclusion_defaults_are_the_BusinessRules_Rating_MinAvgAfterJobs_numbers()
    {
        var matching = new CommonService.Application.Features.Dispatch.MatchingScoreOptions();
        var rule = new BusinessRules().Rating.MinAvgAfterJobs;

        Assert.Equal((4.00m, 10), (rule.Rating, rule.Jobs)); // decisions Q14
        Assert.Equal((rule.Rating, rule.Jobs), (matching.MinRatingThreshold, matching.MinJobsForRatingFilter));
    }

    // ---- the Entity Framework store, on a real SQL Server ----------------------------------------

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

    [Fact]
    public async Task Real_database_the_store_sets_the_column_on_the_tracked_row_and_returns_the_previous_value()
    {
        if (!IsSqlServerAvailable()) return;

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        var suffix = Random.Shared.Next(100000, 999999).ToString();
        int workerId;
        await using (var seed = new AppDbContext(options))
        {
            var worker = Worker.CreateFreelancer("09" + suffix + "11", "0790" + suffix + "11", "Rated Worker");
            worker.RatingAvg = 5.00m;
            worker.CreatedAt = DateTime.UtcNow;
            worker.UpdatedAt = worker.CreatedAt;
            seed.Workers.Add(worker);
            await seed.SaveChangesAsync();
            workerId = worker.WorkerId;
        }

        try
        {
            await using (var db = new AppDbContext(options))
            {
                var store = new EfWorkerRatingStore(db);
                var change = await store.SetAverageAsync(workerId, 3.25m);
                Assert.Equal(new WorkerAverageChange(5.00m, WorkerType.Freelancer), change);
                Assert.Null(await store.SetAverageAsync(-1, 1m));
                await db.SaveChangesAsync(); // the caller saves
            }

            await using var verify = new AppDbContext(options);
            Assert.Equal(3.25m, (await verify.Workers.AsNoTracking().SingleAsync(w => w.WorkerId == workerId)).RatingAvg);
        }
        finally
        {
            await using var cleanup = new AppDbContext(options);
            await cleanup.Workers.Where(w => w.WorkerId == workerId).ExecuteDeleteAsync();
        }
    }
}
