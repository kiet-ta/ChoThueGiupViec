using CommonService.Application.Features.Booking;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Workers;
using CommonService.Infrastructure.Persistence;
using CommonService.Tests.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>
/// WORKERS-EXT-01 (BE-M4-09) against SQL Server (skipped when none is reachable): the lookup of the extension of an assignment and the conditional accept.
/// The rows are committed, so each test removes what it made.
/// </summary>
public class WorkerExtensionDatabaseTests
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

    private sealed record Seeded(int WorkerId, int OtherWorkerId, int CustomerId, long OrderId, int SlotId, long AssignmentId, int ExtensionId);

    private static async Task<Worker> NewWorkerAsync(AppDbContext db, string name)
    {
        var worker = Worker.CreateFreelancer($"098{Random.Shared.Next(1000000, 9999999)}", $"013{Random.Shared.Next(100000000, 999999999)}", name);
        worker.CreatedAt = DateTime.UtcNow;
        worker.SetKycResult(90.0m, "APPROVED");
        worker.TransitionTo(WorkStatus.Busy);
        db.Workers.Add(worker);
        await db.SaveChangesAsync();
        return worker;
    }

    private static async Task<Seeded> SeedAsync(AppDbContext db, string extStatus = ExtensionStatuses.Paid)
    {
        var worker = await NewWorkerAsync(db, "Ext Worker");
        var other = await NewWorkerAsync(db, "Ext Other");
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
        db.JobAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var extension = new JobOrderExtension
        {
            OrderId = seed.OrderId,
            WorkerId = worker.WorkerId,
            ExtraHours = 1.5m,
            ExtraAmount = 97500m,
            WorkerDecision = WorkerDecisions.Pending,
            ExtStatus = extStatus,
            RequestedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
        db.JobOrderExtensions.Add(extension);
        await db.SaveChangesAsync();

        return new Seeded(worker.WorkerId, other.WorkerId, seed.CustomerId, seed.OrderId, seed.SlotId, assignment.AssignmentId, extension.ExtensionId);
    }

    private static async Task CleanUpAsync(AppDbContext db, Seeded s)
    {
        await db.JobOrderExtensions.Where(e => e.ExtensionId == s.ExtensionId).ExecuteDeleteAsync();
        await db.JobAssignments.Where(a => a.AssignmentId == s.AssignmentId).ExecuteDeleteAsync();
        await db.JobOrders.Where(o => o.OrderId == s.OrderId).ExecuteDeleteAsync();
        await db.BookingSlots.Where(b => b.SlotId == s.SlotId).ExecuteDeleteAsync();
        await db.CustomerAddresses.Where(a => a.CustomerId == s.CustomerId).ExecuteDeleteAsync();
        await db.Customers.Where(c => c.CustomerId == s.CustomerId).ExecuteDeleteAsync();
        await db.Workers.Where(w => w.WorkerId == s.WorkerId || w.WorkerId == s.OtherWorkerId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task FindForWorkerAsync_ReturnsTheExtensionOfTheOwner_AndNothingForAnotherWorkerOrAnUnknownAssignment()
    {
        if (!IsSqlServerAvailable()) return;

        using var master = CreateContext();
        var seeded = await SeedAsync(master);
        try
        {
            using var db = CreateContext();
            var repo = new EfWorkerExtensionRepository(db);

            var found = await repo.FindForWorkerAsync(seeded.AssignmentId, seeded.WorkerId);

            Assert.NotNull(found);
            Assert.Equal(seeded.ExtensionId, found!.ExtensionId);
            Assert.Equal(seeded.OrderId, found.OrderId);
            Assert.Equal(1.5m, found.ExtraHours);
            Assert.Equal(97500m, found.ExtraAmount);
            Assert.Equal(ExtensionStatuses.Paid, found.ExtStatus);
            Assert.False(string.IsNullOrEmpty(found.ShiftCode));

            Assert.Null(await repo.FindForWorkerAsync(seeded.AssignmentId, seeded.OtherWorkerId));
            Assert.Null(await repo.FindForWorkerAsync(seeded.AssignmentId + 100000, seeded.WorkerId));
        }
        finally
        {
            await CleanUpAsync(master, seeded);
        }
    }

    [Fact]
    public async Task TryAcceptAsync_AcceptsAPaidExtension_Once()
    {
        if (!IsSqlServerAvailable()) return;

        using var master = CreateContext();
        var seeded = await SeedAsync(master);
        try
        {
            var decidedAt = new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);
            using var db = CreateContext();
            var repo = new EfWorkerExtensionRepository(db);

            Assert.True(await repo.TryAcceptAsync(seeded.ExtensionId, decidedAt));
            Assert.False(await repo.TryAcceptAsync(seeded.ExtensionId, decidedAt));

            using var check = CreateContext();
            var row = await check.JobOrderExtensions.AsNoTracking().SingleAsync(e => e.ExtensionId == seeded.ExtensionId);
            Assert.Equal(ExtensionStatuses.Accepted, row.ExtStatus);
            Assert.Equal(WorkerDecisions.Accepted, row.WorkerDecision);
            Assert.Equal(decidedAt, row.DecidedAt);
        }
        finally
        {
            await CleanUpAsync(master, seeded);
        }
    }

    [Theory]
    [InlineData(ExtensionStatuses.PendingPayment)]
    [InlineData(ExtensionStatuses.Declined)]
    [InlineData(ExtensionStatuses.Expired)]
    public async Task TryAcceptAsync_DoesNotTouchAnExtensionThatIsNotPaid(string status)
    {
        if (!IsSqlServerAvailable()) return;

        using var master = CreateContext();
        var seeded = await SeedAsync(master, status);
        try
        {
            using var db = CreateContext();

            Assert.False(await new EfWorkerExtensionRepository(db).TryAcceptAsync(seeded.ExtensionId, DateTime.UtcNow));

            using var check = CreateContext();
            var row = await check.JobOrderExtensions.AsNoTracking().SingleAsync(e => e.ExtensionId == seeded.ExtensionId);
            Assert.Equal(status, row.ExtStatus);
            Assert.Equal(WorkerDecisions.Pending, row.WorkerDecision);
            Assert.Null(row.DecidedAt);
        }
        finally
        {
            await CleanUpAsync(master, seeded);
        }
    }

    [Fact]
    public async Task TryAcceptAsync_TwoRequestsAtTheSameTime_DecideOnce()
    {
        if (!IsSqlServerAvailable()) return;

        using var master = CreateContext();
        var seeded = await SeedAsync(master);
        try
        {
            var start = new TaskCompletionSource();

            async Task<bool> Request()
            {
                using var db = CreateContext();
                var repo = new EfWorkerExtensionRepository(db);
                await start.Task;
                return await repo.TryAcceptAsync(seeded.ExtensionId, DateTime.UtcNow);
            }

            var first = Task.Run(Request);
            var second = Task.Run(Request);
            start.SetResult();
            var results = await Task.WhenAll(first, second);

            Assert.Single(results, r => r);
        }
        finally
        {
            await CleanUpAsync(master, seeded);
        }
    }
}
