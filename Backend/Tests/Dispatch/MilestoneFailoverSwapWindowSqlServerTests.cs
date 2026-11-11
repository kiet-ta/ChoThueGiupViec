using CommonService.Application.Common.Options;
using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Agencies;
using CommonService.Infrastructure.Modules.Dispatch;
using CommonService.Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace CommonService.Tests.Dispatch;

/// <summary>
/// BE-DISPATCH-REFACTOR-01 against a real SQL Server: the 30-minute self-swap window is a row of <c>AGENCY_SWAP_WINDOW</c> read through
/// <see cref="EfAgencySwapRepository"/>, so a brand-new service and a brand-new <see cref="AppDbContext"/> (a server restart) still see it.
/// Connection: env var <c>DISPATCH_SQL</c>, default LocalDB database <c>DispatchSwapVerify</c> with every migration applied.
/// Without a reachable server each test returns at once.
/// </summary>
public class MilestoneFailoverSwapWindowSqlServerTests
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("DISPATCH_SQL")
        ?? @"Server=(localdb)\MSSQLLocalDB;Database=DispatchSwapVerify;Integrated Security=True;TrustServerCertificate=True;";

    private static readonly DateTime Now = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc);
    private const string Morning = "SHIFT_MORNING";

    // A day of its own per test: leftovers of earlier runs in a shared database must not take part.
    private readonly DateOnly Day = DateOnly.FromDayNumber(new DateOnly(2026, 10, 16).DayNumber + Random.Shared.Next(1, 30_000));

    private static bool Available()
    {
        try
        {
            using var conn = new SqlConnection(ConnectionString + "Connect Timeout=3;");
            conn.Open();
            using var cmd = new SqlCommand("SELECT COUNT(*) FROM AGENCY_SWAP_WINDOW", conn);
            cmd.ExecuteScalar();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

    private static string Digits(int n) => new(Guid.NewGuid().ToByteArray().Select(b => (char)('0' + (b % 10))).ToArray()[..n]);

    private class Unused : IMediator, IAgencyCapacityService, ISlaPenaltyService
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> HasCapacityAsync(CapacityRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CapacityReservation?> TryReserveAsync(CapacityRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CapacityReservation?> GetReservationByOrderAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReleaseByOrderAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SlaPenaltyResult> ApplyAsync(SlaPenaltyRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>One service over one fresh <see cref="AppDbContext"/>: building another one is what a server restart does.</summary>
    private static (MilestoneFailoverService Service, AppDbContext Db) Build(FakeClock clock)
    {
        var db = NewDb();
        var unused = new Unused();
        var service = new MilestoneFailoverService(
            new EfDispatchRepository(db), new EfAgencySwapRepository(db), unused, unused, clock, unused, new UnitOfWork(db),
            MicrosoftOptions.Create(new BusinessRules()), NullLogger<MilestoneFailoverService>.Instance);
        return (service, db);
    }

    private static FakeClock ClockAt(DateTime utc)
    {
        var clock = new FakeClock();
        clock.Set(utc);
        return clock;
    }

    private record Job(int AgencyId, int OldWorkerId, int NewWorkerId, long AssignmentId);

    private async Task<Job> SeedAssignedAgencyJobAsync()
    {
        await using var db = NewDb();
        var agency = new PartnerAgency
        {
            TaxCode = Digits(10),
            LegalName = "Failover Agency " + Digits(5),
            LegalRepresentative = "Rep",
            ContactPhone = "09" + Digits(8),
            ContactEmail = "f" + Digits(8) + "@example.test",
            EscrowDepositBalance = 5_000_000m,
            SlaScore = 100m,
            WorkerQuota = 5,
            BankAccountNo = "123",
            BankName = "VCB",
            AgencyStatus = "ACTIVE",
            CreatedAt = Now,
        };
        var customer = new Customer { PhoneNumber = "09" + Digits(8), FullName = "C", AccountStatus = CustomerAccountStatus.Active, CreatedAt = Now, UpdatedAt = Now };
        db.PartnerAgencies.Add(agency);
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
            CreatedAt = Now,
        };
        db.CustomerAddresses.Add(address);
        var oldWorker = Worker.CreateAgencyStaff(agency.AgencyId, "09" + Digits(8), Digits(12), "Old " + Digits(4));
        var newWorker = Worker.CreateAgencyStaff(agency.AgencyId, "09" + Digits(8), Digits(12), "New " + Digits(4));
        foreach (var w in new[] { oldWorker, newWorker })
        {
            w.CreatedAt = Now;
            w.UpdatedAt = Now;
            db.Workers.Add(w);
        }

        await db.SaveChangesAsync();
        var slot = new BookingSlot
        {
            WorkerId = oldWorker.WorkerId,
            AgencyId = agency.AgencyId,
            SlotDate = Day,
            ShiftCode = Morning,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(12, 0),
            SlotSource = "AGENCY",
            SlotStatus = "BOOKED",
            UpdatedAt = Now,
        };
        db.BookingSlots.Add(slot);
        var order = new JobOrder
        {
            OrderCode = "T" + Digits(12),
            CustomerId = customer.CustomerId,
            AddressId = address.AddressId,
            ServiceTier = ServiceTier.Premium,
            ScheduledDate = Day,
            ShiftCode = Morning,
            AreaSnapshotM2 = 50m,
            RequiredWorkers = 1,
            TotalAmount = 400000m,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.JobOrders.Add(order);
        await db.SaveChangesAsync();
        var assignment = new JobAssignment
        {
            OrderId = order.OrderId,
            CustomerId = customer.CustomerId,
            WorkerId = oldWorker.WorkerId,
            AgencyId = agency.AgencyId,
            SlotId = slot.SlotId,
            ServiceTier = ServiceTier.Premium,
            AssignmentSeq = 1,
            DispatchRadiusKm = 5,
            GrossAmount = 400000m,
            CommissionRate = 0.2m,
            PayoutAmount = 320000m,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        db.JobAssignments.Add(assignment);
        await db.SaveChangesAsync();
        return new Job(agency.AgencyId, oldWorker.WorkerId, newWorker.WorkerId, assignment.AssignmentId);
    }

    private static async Task<AgencySwapWindow?> ReadWindowAsync(long assignmentId)
    {
        await using var db = NewDb();
        return await db.AgencySwapWindows.AsNoTracking().SingleOrDefaultAsync(w => w.AssignmentId == assignmentId);
    }

    private static async Task<int> WorkerOfAsync(long assignmentId)
    {
        await using var db = NewDb();
        return await db.JobAssignments.AsNoTracking().Where(a => a.AssignmentId == assignmentId).Select(a => a.WorkerId).SingleAsync();
    }

    [Fact]
    public async Task Reporting_unavailability_saves_an_OPEN_window_row_and_a_second_report_keeps_it()
    {
        if (!Available()) return;
        var job = await SeedAssignedAgencyJobAsync();
        var (service, db) = Build(ClockAt(Now));
        using var _ = db;

        var first = await service.ReportUnavailabilityAsync(job.AssignmentId, "Thợ ốm");
        var row = await ReadWindowAsync(job.AssignmentId);

        Assert.Equal(FailoverStatus.SelfSwapWindowOpened, first.Status);
        Assert.NotNull(row);
        Assert.Equal((SwapWindowStatuses.Open, job.AgencyId), (row.Status, row.AgencyId));
        Assert.Equal(TimeSpan.FromMinutes(30), row.DeadlineAt - row.OpenedAt);

        var (again, db2) = Build(ClockAt(Now.AddMinutes(5)));
        using var _2 = db2;
        var second = await again.ReportUnavailabilityAsync(job.AssignmentId, "Báo lại");
        Assert.Equal(row.DeadlineAt, second.SelfSwapDeadlineUtc);
    }

    [Fact]
    public async Task A_swap_after_a_restart_inside_the_30_minutes_changes_the_worker_and_closes_the_window_as_SWAPPED()
    {
        if (!Available()) return;
        var job = await SeedAssignedAgencyJobAsync();
        var (before, dbBefore) = Build(ClockAt(Now));
        using (dbBefore)
        {
            await before.ReportUnavailabilityAsync(job.AssignmentId, "Thợ ốm");
        }

        var (after, dbAfter) = Build(ClockAt(Now.AddMinutes(10))); // new service, new DbContext: nothing in memory
        using var _ = dbAfter;
        var result = await after.ExecuteAgencySelfSwapAsync(job.AssignmentId, job.NewWorkerId);

        Assert.Equal(FailoverStatus.AgencySelfSwapped, result.Status);
        Assert.Equal(job.NewWorkerId, await WorkerOfAsync(job.AssignmentId));
        var row = await ReadWindowAsync(job.AssignmentId);
        Assert.Equal((SwapWindowStatuses.Swapped, job.NewWorkerId), (row!.Status, row.NewWorkerId));
        Assert.NotNull(row.ClosedAt);
    }

    [Fact]
    public async Task A_swap_on_a_window_the_sweep_already_expired_is_rejected_and_the_worker_stays()
    {
        if (!Available()) return;
        var job = await SeedAssignedAgencyJobAsync();
        var (before, dbBefore) = Build(ClockAt(Now));
        using (dbBefore)
        {
            await before.ReportUnavailabilityAsync(job.AssignmentId, "Thợ ốm");
        }

        await using (var sweepDb = NewDb())
        {
            var closed = await new EfAgencySwapRepository(sweepDb).TryCloseWindowAsync(job.AssignmentId, SwapWindowStatuses.Expired, null, Now.AddMinutes(31));
            Assert.True(closed);
        }

        var (after, dbAfter) = Build(ClockAt(Now.AddMinutes(32)));
        using var _ = dbAfter;
        var result = await after.ExecuteAgencySelfSwapAsync(job.AssignmentId, job.NewWorkerId);

        Assert.False(result.Success);
        Assert.Equal(job.OldWorkerId, await WorkerOfAsync(job.AssignmentId));
        Assert.Equal(SwapWindowStatuses.Expired, (await ReadWindowAsync(job.AssignmentId))!.Status);
    }
}
