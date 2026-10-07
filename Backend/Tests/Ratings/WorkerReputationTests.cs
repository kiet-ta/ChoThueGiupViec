using CommonService.Application.Features.Ratings;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Ratings;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Tests.Ratings;

/// <summary>BE-M6-01b: the numbers of IWorkerReputation (definitions in ReputationFormula).</summary>
public class WorkerReputationTests
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

    // ---- formula --------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0, 0.00)]
    [InlineData(5, 1, 5.00)]
    [InlineData(13, 3, 4.33)]   // 4.3333...
    [InlineData(14, 3, 4.67)]   // 4.6666...
    [InlineData(9, 2, 4.50)]
    [InlineData(1, 1, 1.00)]
    public void Rating_average_is_rounded_to_2_decimals_and_zero_without_ratings(long sum, int count, double expected)
    {
        Assert.Equal((decimal)expected, ReputationFormula.RatingAverage(sum, count));
    }

    [Fact]
    public void Rating_average_rounds_a_half_away_from_zero()
    {
        // 4.125 would be 4.12 with banker's rounding; the project rounds half away from zero (G-2).
        Assert.Equal(4.13m, ReputationFormula.RatingAverage(33, 8));
    }

    [Theory]
    [InlineData(0, 0, 0.0)]      // no accepted job that ended: 0, not a division error
    [InlineData(4, 0, 1.0)]
    [InlineData(0, 3, 0.0)]
    [InlineData(2, 1, 0.667)]
    [InlineData(1, 2, 0.333)]
    [InlineData(9, 1, 0.9)]
    public void Success_rate_is_completed_over_completed_plus_cancelled_by_worker(int completed, int cancelledByWorker, double expected)
    {
        Assert.Equal((decimal)expected, ReputationFormula.SuccessRate(completed, cancelledByWorker));
    }

    [Fact]
    public void Success_rate_stays_between_0_and_1()
    {
        for (var completed = 0; completed <= 30; completed++)
        {
            for (var cancelled = 0; cancelled <= 30; cancelled++)
            {
                var rate = ReputationFormula.SuccessRate(completed, cancelled);
                Assert.InRange(rate, 0m, 1m);
            }
        }
    }

    // ---- SQL Server -----------------------------------------------------------------------------

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;

    private sealed record Seeded(int CustomerId, int WorkerId, int OtherWorkerId, int AddressId, long OrderId, List<int> SlotIds, List<long> AssignmentIds);

    private static JobAssignment NewAssignment(JobOrder order, Customer customer, Worker worker, BookingSlot slot, byte seq, params JobAssignmentStatus[] path)
    {
        var assignment = new JobAssignment
        {
            OrderId = order.OrderId,
            CustomerId = customer.CustomerId,
            WorkerId = worker.WorkerId,
            SlotId = slot.SlotId,
            ServiceTier = ServiceTier.Economy,
            AssignmentSeq = seq,
            DispatchRadiusKm = 5,
            GrossAmount = 260000m,
            CommissionRate = 0.200m,
            PayoutAmount = 208000m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        foreach (var step in path) assignment.TransitionTo(step);
        return assignment;
    }

    private static async Task<Seeded> SeedAsync()
    {
        var now = DateTime.UtcNow;
        await using var db = new AppDbContext(Options());

        var customer = new Customer
        {
            PhoneNumber = "091" + Random.Shared.Next(1000000, 9999999),
            FullName = "Reputation Test",
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

        Worker NewWorker(string name) => new()
        {
            PhoneNumber = "092" + Random.Shared.Next(1000000, 9999999),
            NationalId = "079" + Random.Shared.Next(100000000, 999999999),
            FullName = name,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var worker = NewWorker("Reputation Worker");
        var other = NewWorker("Reputation Other");
        db.Workers.AddRange(worker, other);
        await db.SaveChangesAsync();

        var slots = new List<BookingSlot>();
        for (var i = 0; i < 6; i++)
        {
            slots.Add(new BookingSlot
            {
                WorkerId = worker.WorkerId,
                SlotDate = order.ScheduledDate.AddDays(i + 1),
                ShiftCode = "SHIFT1",
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(12, 0),
                SlotSource = "MANUAL",
                SlotStatus = "LOCKED",
                UpdatedAt = now,
            });
        }

        slots.Add(new BookingSlot
        {
            WorkerId = other.WorkerId,
            SlotDate = order.ScheduledDate.AddDays(1),
            ShiftCode = "SHIFT1",
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(12, 0),
            SlotSource = "MANUAL",
            SlotStatus = "LOCKED",
            UpdatedAt = now,
        });
        db.BookingSlots.AddRange(slots);
        await db.SaveChangesAsync();

        var done = new[] { JobAssignmentStatus.Assigned, JobAssignmentStatus.CheckedIn, JobAssignmentStatus.InProgress, JobAssignmentStatus.AwaitingAcceptance, JobAssignmentStatus.Completed };
        var assignments = new List<JobAssignment>
        {
            NewAssignment(order, customer, worker, slots[0], 1, done),                                            // completed
            NewAssignment(order, customer, worker, slots[1], 2, done),                                            // completed
            NewAssignment(order, customer, worker, slots[2], 3, JobAssignmentStatus.Assigned, JobAssignmentStatus.CancelledByWorker),
            NewAssignment(order, customer, worker, slots[3], 4, JobAssignmentStatus.Assigned, JobAssignmentStatus.CheckedIn, JobAssignmentStatus.Absent),
            NewAssignment(order, customer, worker, slots[4], 5, JobAssignmentStatus.Assigned, JobAssignmentStatus.Incident),
            NewAssignment(order, customer, worker, slots[5], 6, JobAssignmentStatus.Cancelled),
            NewAssignment(order, customer, other, slots[6], 7, done),                                             // someone else's completed job
        };
        db.JobAssignments.AddRange(assignments);
        await db.SaveChangesAsync();

        // CUSTOMER ratings 5, 4, 4 on three of the worker's jobs (avg 4.33); a WORKER rating and another worker's rating must not count
        db.TwoWayRatings.AddRange(
            Rating(assignments[0], worker.WorkerId, "CUSTOMER", 5),
            Rating(assignments[1], worker.WorkerId, "CUSTOMER", 4),
            Rating(assignments[2], worker.WorkerId, "CUSTOMER", 4),
            Rating(assignments[0], worker.WorkerId, "WORKER", 1),
            Rating(assignments[6], other.WorkerId, "CUSTOMER", 1));
        await db.SaveChangesAsync();

        return new Seeded(customer.CustomerId, worker.WorkerId, other.WorkerId, address.AddressId, order.OrderId,
            slots.Select(s => s.SlotId).ToList(), assignments.Select(a => a.AssignmentId).ToList());
    }

    private static TwoWayRating Rating(JobAssignment a, int workerId, string role, byte stars) => new()
    {
        AssignmentId = a.AssignmentId,
        WorkerId = workerId,
        RaterRole = role,
        Stars = stars,
        CreatedAt = DateTime.UtcNow,
    };

    private static async Task CleanAsync(Seeded s)
    {
        await using var db = new AppDbContext(Options());
        await db.TwoWayRatings.Where(r => s.AssignmentIds.Contains(r.AssignmentId)).ExecuteDeleteAsync();
        await db.JobAssignments.Where(a => s.AssignmentIds.Contains(a.AssignmentId)).ExecuteDeleteAsync();
        await db.BookingSlots.Where(b => s.SlotIds.Contains(b.SlotId)).ExecuteDeleteAsync();
        await db.JobOrders.Where(o => o.OrderId == s.OrderId).ExecuteDeleteAsync();
        await db.CustomerAddresses.Where(a => a.AddressId == s.AddressId).ExecuteDeleteAsync();
        await db.Customers.Where(c => c.CustomerId == s.CustomerId).ExecuteDeleteAsync();
        await db.Workers.Where(w => w.WorkerId == s.WorkerId || w.WorkerId == s.OtherWorkerId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Real_database_counts_only_the_workers_own_customer_ratings_and_completed_or_cancelled_by_worker_jobs()
    {
        if (!IsSqlServerAvailable()) return;

        var seeded = await SeedAsync();
        try
        {
            await using var db = new AppDbContext(Options());
            var reputation = await new EfWorkerReputation(db).GetAsync(seeded.WorkerId);

            Assert.NotNull(reputation);
            Assert.Equal(seeded.WorkerId, reputation!.WorkerId);
            Assert.Equal(4.33m, reputation.RatingAvg);      // (5 + 4 + 4) / 3; the WORKER rating and the other worker's rating are ignored
            Assert.Equal(2, reputation.CompletedJobs);      // two COMPLETED; ABSENT, INCIDENT, CANCELLED and the other worker's job do not count
            Assert.Equal(0.667m, reputation.SuccessRate);   // 2 / (2 + 1 CANCELLED_BY_WORKER)
            Assert.Empty(db.ChangeTracker.Entries());       // AsNoTracking

            var other = await new EfWorkerReputation(db).GetAsync(seeded.OtherWorkerId);
            Assert.Equal(1.00m, other!.RatingAvg);
            Assert.Equal(1, other.CompletedJobs);
            Assert.Equal(1.000m, other.SuccessRate);
        }
        finally
        {
            await CleanAsync(seeded);
        }
    }

    [Fact]
    public async Task A_worker_without_any_job_or_rating_gets_zeros_and_an_unknown_worker_gets_null()
    {
        if (!IsSqlServerAvailable()) return;

        var seeded = await SeedAsync();
        await using var other = new AppDbContext(Options());
        var emptyWorker = new Worker
        {
            PhoneNumber = "093" + Random.Shared.Next(1000000, 9999999),
            NationalId = "079" + Random.Shared.Next(100000000, 999999999),
            FullName = "Reputation Empty",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        other.Workers.Add(emptyWorker);
        await other.SaveChangesAsync();
        try
        {
            await using var db = new AppDbContext(Options());
            var reputation = await new EfWorkerReputation(db).GetAsync(emptyWorker.WorkerId);
            Assert.Equal(new CommonService.Application.Interfaces.Ports.WorkerReputationDto(emptyWorker.WorkerId, 0m, 0, 0m), reputation);

            Assert.Null(await new EfWorkerReputation(db).GetAsync(int.MaxValue));
        }
        finally
        {
            await other.Workers.Where(w => w.WorkerId == emptyWorker.WorkerId).ExecuteDeleteAsync();
            await CleanAsync(seeded);
        }
    }
}
