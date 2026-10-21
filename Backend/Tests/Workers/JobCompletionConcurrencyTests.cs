using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Workers;
using CommonService.Infrastructure.Persistence;
using CommonService.Tests.Persistence;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>
/// WORKERS-DATA-01: of two accepts of the same assignment only one completes it. The service tests use a stub repository; the database
/// tests use two contexts (two requests), so the rows are committed and removed at the end.
/// </summary>
public class JobCompletionConcurrencyTests
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

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

    private sealed class Customer_(int id) : ICurrentUser
    {
        public int? UserId { get; } = id;
        public UserRole? Role => UserRole.Customer;
        public bool IsAuthenticated => true;
    }

    private sealed class RecordingPublisher : IPublisher
    {
        private int _published;
        public int Published => _published;

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _published);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
        {
            Interlocked.Increment(ref _published);
            return Task.CompletedTask;
        }
    }

    // ---- service level, no database -------------------------------------------------------------------------------------------

    private const long StubAssignmentId = 55;
    private const int StubWorkerId = 7;
    private const int StubCustomerId = 21;

    private sealed class StubRepository(bool completeSucceeds) : IWorkerRepository
    {
        private JobAssignment _assignment = Build(JobAssignmentStatus.AwaitingAcceptance);
        public int CompleteCalls { get; private set; }
        public decimal? CommissionRate { get; private set; }
        public decimal? PayoutAmount { get; private set; }

        private static JobAssignment Build(JobAssignmentStatus status)
        {
            var a = new JobAssignment
            {
                AssignmentId = StubAssignmentId,
                WorkerId = StubWorkerId,
                CustomerId = StubCustomerId,
                OrderId = 9,
                GrossAmount = 260000m,
                CommissionRate = 0m,
            };
            foreach (var step in new[]
            {
                JobAssignmentStatus.Assigned, JobAssignmentStatus.CheckedIn, JobAssignmentStatus.InProgress,
                JobAssignmentStatus.AwaitingAcceptance, JobAssignmentStatus.Completed,
            })
            {
                a.TransitionTo(step);
                if (step == status)
                {
                    break;
                }
            }

            return a;
        }

        public Task<JobAssignment?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<JobAssignment?>(_assignment);

        public Task<bool> TryCompleteAssignmentAsync(
            long assignmentId, int workerId, decimal commissionRate, decimal payoutAmount, DateTime completedAtUtc, CancellationToken cancellationToken = default)
        {
            CompleteCalls++;
            CommissionRate = commissionRate;
            PayoutAmount = payoutAmount;
            if (!completeSucceeds)
            {
                // Another request completed it first: the next read sees the stored status.
                _assignment = Build(JobAssignmentStatus.Completed);
            }

            return Task.FromResult(completeSucceeds);
        }

        public Task<Worker?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker?> GetByNationalIdAsync(string nationalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByPhoneOrNationalIdAsync(string phoneNumber, string nationalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(IReadOnlyList<Worker> Items, int TotalCount)> GetEkycQueueAsync(int page, int pageSize, string status = EkycQueueStatus.Pending, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BookingSlot>> GetWorkerSlotsAsync(int workerId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BookingSlot?> GetWorkerSlotByShiftAsync(int workerId, DateOnly slotDate, string shiftCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddSlotAsync(BookingSlot slot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void UpdateSlot(BookingSlot slot) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobPhoto>> GetJobPhotosAsync(long assignmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobPhoto>> GetAcceptedJobPhotosByPhaseAsync(long assignmentId, string photoPhase, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddJobPhotoAsync(JobPhoto photo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Worker>> ListAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker> AddAsync(Worker entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Update(Worker entity) => throw new NotSupportedException();
        public void Delete(Worker entity) => throw new NotSupportedException();
    }

    [Fact]
    public async Task AcceptAsync_WhenTheConditionalCompleteLosesTheRace_Throws409WithTheStoredStatus_AndPublishesNothing()
    {
        var repository = new StubRepository(completeSucceeds: false);
        var publisher = new RecordingPublisher();
        var service = new JobCompletionService(repository, publisher, new Customer_(StubCustomerId), new FakeClock());

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => service.AcceptAsync(StubAssignmentId, null));

        Assert.Equal("Cannot accept assignment in status Completed.", ex.Message);
        Assert.Equal(1, repository.CompleteCalls);
        Assert.Equal(0, publisher.Published);
    }

    [Fact]
    public async Task AcceptAsync_WhenTheConditionalCompleteWins_PublishesOnce_WithTheComputedPayout()
    {
        var repository = new StubRepository(completeSucceeds: true);
        var publisher = new RecordingPublisher();
        var service = new JobCompletionService(repository, publisher, new Customer_(StubCustomerId), new FakeClock());

        var response = await service.AcceptAsync(StubAssignmentId, null);

        Assert.Equal("COMPLETED", response.Data!.Status);
        Assert.Equal(0.20m, repository.CommissionRate); // freelancer: 20%
        Assert.Equal(208000m, repository.PayoutAmount); // 80% of 260k
        Assert.Equal(1, publisher.Published);
    }

    // ---- two requests, real database ------------------------------------------------------------------------------------------

    private sealed record Seeded(int WorkerId, int CustomerId, long OrderId, int SlotId, long AssignmentId);

    /// <summary>A BUSY freelancer with an AWAITING_ACCEPTANCE assignment, committed so that other contexts see it.</summary>
    private static async Task<Seeded> SeedCommittedAsync(AppDbContext db)
    {
        var worker = Worker.CreateFreelancer($"099{Random.Shared.Next(1000000, 9999999)}", $"012{Random.Shared.Next(100000000, 999999999)}", "Race Worker");
        worker.CreatedAt = DateTime.UtcNow;
        worker.SetKycResult(90.0m, "APPROVED");
        worker.TransitionTo(WorkStatus.Busy);
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var seed = await SqlSeed.OrderWithSlotAsync(db, worker.WorkerId);
        var assignment = new JobAssignment
        {
            OrderId = seed.OrderId,
            CustomerId = seed.CustomerId,
            WorkerId = worker.WorkerId,
            SlotId = seed.SlotId,
            ServiceTier = ServiceTier.Economy,
            GrossAmount = 260000m,
            CommissionRate = 0.20m,
            PayoutAmount = 0m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        assignment.TransitionTo(JobAssignmentStatus.CheckedIn);
        assignment.TransitionTo(JobAssignmentStatus.InProgress);
        assignment.TransitionTo(JobAssignmentStatus.AwaitingAcceptance);
        db.JobAssignments.Add(assignment);
        await db.SaveChangesAsync();

        return new Seeded(worker.WorkerId, seed.CustomerId, seed.OrderId, seed.SlotId, assignment.AssignmentId);
    }

    private static async Task CleanUpAsync(AppDbContext db, Seeded s)
    {
        await db.JobAssignments.Where(a => a.AssignmentId == s.AssignmentId).ExecuteDeleteAsync();
        await db.JobOrders.Where(o => o.OrderId == s.OrderId).ExecuteDeleteAsync();
        await db.BookingSlots.Where(b => b.SlotId == s.SlotId).ExecuteDeleteAsync();
        await db.CustomerAddresses.Where(a => a.CustomerId == s.CustomerId).ExecuteDeleteAsync();
        await db.Customers.Where(c => c.CustomerId == s.CustomerId).ExecuteDeleteAsync();
        await db.Workers.Where(w => w.WorkerId == s.WorkerId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task AcceptAsync_ARequestThatReadTheAssignmentBeforeAnotherCompletedIt_IsRefused_AndCompletesNothingTwice()
    {
        if (!IsSqlServerAvailable()) return;

        using var master = CreateContext();
        var seeded = await SeedCommittedAsync(master);
        try
        {
            var user = new Customer_(seeded.CustomerId);

            // Request B has already read the assignment (still AWAITING_ACCEPTANCE) when request A completes it: the window of the race.
            using var dbB = CreateContext();
            var repositoryB = new EfWorkerRepository(dbB);
            var staleCopy = await repositoryB.GetAssignmentByIdAsync(seeded.AssignmentId);
            Assert.Equal(JobAssignmentStatus.AwaitingAcceptance, staleCopy!.AssignmentStatus);

            var publisherA = new RecordingPublisher();
            using var dbA = CreateContext();
            await new JobCompletionService(new EfWorkerRepository(dbA), publisherA, user, new FakeClock()).AcceptAsync(seeded.AssignmentId, null);

            var publisherB = new RecordingPublisher();
            var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
                new JobCompletionService(repositoryB, publisherB, user, new FakeClock()).AcceptAsync(seeded.AssignmentId, null));

            Assert.Equal("Cannot accept assignment in status Completed.", ex.Message);
            Assert.Equal(1, publisherA.Published);
            Assert.Equal(0, publisherB.Published);

            using var check = CreateContext();
            var worker = await check.Workers.AsNoTracking().SingleAsync(w => w.WorkerId == seeded.WorkerId);
            var assignment = await check.JobAssignments.AsNoTracking().SingleAsync(a => a.AssignmentId == seeded.AssignmentId);
            Assert.Equal(1, worker.CompletedJobs);
            Assert.Equal(WorkStatus.Idle, worker.WorkStatus);
            Assert.Equal(JobAssignmentStatus.Completed, assignment.AssignmentStatus);
            Assert.Equal(208000m, assignment.PayoutAmount);
            Assert.NotNull(assignment.CompletedAt);
        }
        finally
        {
            await CleanUpAsync(master, seeded);
        }
    }

    [Fact]
    public async Task AcceptAsync_TwoRequestsAtTheSameTime_CompleteTheAssignmentOnce()
    {
        if (!IsSqlServerAvailable()) return;

        using var master = CreateContext();
        var seeded = await SeedCommittedAsync(master);
        try
        {
            var user = new Customer_(seeded.CustomerId);
            var publisher = new RecordingPublisher();
            var start = new TaskCompletionSource();

            async Task<bool> Request()
            {
                using var db = CreateContext();
                var service = new JobCompletionService(new EfWorkerRepository(db), publisher, user, new FakeClock());
                await start.Task;
                try
                {
                    await service.AcceptAsync(seeded.AssignmentId, null);
                    return true;
                }
                catch (BusinessRuleViolationException)
                {
                    return false;
                }
            }

            var first = Task.Run(Request);
            var second = Task.Run(Request);
            start.SetResult();
            var results = await Task.WhenAll(first, second);

            Assert.Single(results, ok => ok);
            Assert.Equal(1, publisher.Published);

            using var check = CreateContext();
            var worker = await check.Workers.AsNoTracking().SingleAsync(w => w.WorkerId == seeded.WorkerId);
            Assert.Equal(1, worker.CompletedJobs);
            Assert.Equal(WorkStatus.Idle, worker.WorkStatus);
        }
        finally
        {
            await CleanUpAsync(master, seeded);
        }
    }
}
