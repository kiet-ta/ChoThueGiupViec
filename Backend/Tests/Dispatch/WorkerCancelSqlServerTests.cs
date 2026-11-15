using CommonService.Application.Features.Dispatch;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Dispatch;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CommonService.Tests.Dispatch;

/// <summary>
/// DISPATCH-CANCEL-01 (#902), decisions D35 against a real SQL Server: the cancel transaction of <see cref="EfWorkerCancelRepository"/>
/// (assignment, slot, order, auto-accept switch, lock) and its queries. Connection: env var <c>DISPATCH_SQL</c>, default LocalDB database
/// <c>DispatchSwapVerify</c> with every migration applied. Without a reachable server each test returns at once.
/// </summary>
public class WorkerCancelSqlServerTests
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("DISPATCH_SQL")
        ?? @"Server=(localdb)\MSSQLLocalDB;Database=DispatchSwapVerify;Integrated Security=True;TrustServerCertificate=True;";

    private static readonly DateTime Now = new(2026, 11, 15, 1, 0, 0, DateTimeKind.Utc);
    private const string Morning = "SHIFT_MORNING";

    private readonly DateOnly _day = DateOnly.FromDayNumber(new DateOnly(2027, 1, 1).DayNumber + Random.Shared.Next(1, 30_000));

    private static bool Available()
    {
        try
        {
            using var conn = new SqlConnection(ConnectionString + "Connect Timeout=3;");
            conn.Open();
            using var cmd = new SqlCommand("SELECT TOP 0 start_time FROM JOB_ORDER", conn);
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

    private sealed record Seed(int CustomerId, int AddressId, int WorkerId, int SlotId, long OrderId, long AssignmentId);

    /// <summary>A freelancer (IDLE, auto-accept on) whose accepted whole-shift job holds a BOOKED slot; the order is ASSIGNED.</summary>
    private async Task<Seed> SeedAsync(AppDbContext db)
    {
        var customer = new Customer { PhoneNumber = "09" + Digits(8), FullName = "C", AccountStatus = CustomerAccountStatus.Active, CreatedAt = Now, UpdatedAt = Now };
        db.Customers.Add(customer);
        var worker = Worker.CreateFreelancer("09" + Digits(8), Digits(12), "W " + Digits(4));
        worker.TransitionTo(WorkStatus.Idle);
        worker.AutoAcceptOffers = true;
        worker.CreatedAt = Now;
        worker.UpdatedAt = Now;
        db.Workers.Add(worker);
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
            NumFloors = 1,
            Latitude = 10.76m,
            Longitude = 106.66m,
            IsDefault = true,
            CreatedAt = Now,
        };
        db.CustomerAddresses.Add(address);
        var slot = new BookingSlot
        {
            WorkerId = worker.WorkerId,
            SlotDate = _day,
            ShiftCode = Morning,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(12, 0),
            SlotSource = "FREELANCER",
            SlotStatus = DispatchSlotStatuses.Booked,
            UpdatedAt = Now,
        };
        db.BookingSlots.Add(slot);
        await db.SaveChangesAsync();

        var order = new JobOrder
        {
            OrderCode = "C" + Digits(12),
            CustomerId = customer.CustomerId,
            AddressId = address.AddressId,
            ServiceTier = ServiceTier.Economy,
            ScheduledDate = _day,
            ShiftCode = Morning,
            AreaSnapshotM2 = 40m,
            RequiredWorkers = 1,
            TotalAmount = 260000m,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        order.TransitionTo(JobOrderStatus.Paid);
        order.TransitionTo(JobOrderStatus.Dispatching);
        order.TransitionTo(JobOrderStatus.Assigned);
        db.JobOrders.Add(order);
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
            CommissionRate = 0.2m,
            PayoutAmount = 208000m,
            AcceptedAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        db.JobAssignments.Add(assignment);
        await db.SaveChangesAsync();
        return new Seed(customer.CustomerId, address.AddressId, worker.WorkerId, slot.SlotId, order.OrderId, assignment.AssignmentId);
    }

    /// <summary>An earlier worker-cancelled job of the same worker on its own order, cancelled at <paramref name="cancelledAt"/>.</summary>
    private static async Task AddEarlierCancelAsync(AppDbContext db, Seed s, DateTime cancelledAt)
    {
        var order = new JobOrder
        {
            OrderCode = "E" + Digits(12),
            CustomerId = s.CustomerId,
            AddressId = s.AddressId,
            ServiceTier = ServiceTier.Economy,
            ScheduledDate = new DateOnly(2026, 10, 1),
            ShiftCode = Morning,
            AreaSnapshotM2 = 40m,
            RequiredWorkers = 1,
            TotalAmount = 260000m,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.JobOrders.Add(order);
        await db.SaveChangesAsync();
        var old = new JobAssignment
        {
            OrderId = order.OrderId,
            CustomerId = s.CustomerId,
            WorkerId = s.WorkerId,
            SlotId = s.SlotId,
            ServiceTier = ServiceTier.Economy,
            AssignmentSeq = 1,
            DispatchRadiusKm = 5,
            GrossAmount = 260000m,
            CommissionRate = 0.2m,
            PayoutAmount = 208000m,
            CreatedAt = cancelledAt,
            UpdatedAt = cancelledAt,
        };
        old.TransitionTo(JobAssignmentStatus.Assigned);
        old.TransitionTo(JobAssignmentStatus.CancelledByWorker);
        db.JobAssignments.Add(old);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Cancel_writes_assignment_slot_order_and_switch_in_one_go_and_a_second_cancel_changes_nothing()
    {
        if (!Available()) return;
        await using var db = NewDb();
        var s = await SeedAsync(db);
        var repo = new EfWorkerCancelRepository(db);

        var commit = await repo.TryCancelAsync(s.AssignmentId, s.WorkerId, Now, Now.AddDays(-30), 3);

        Assert.Equal(new WorkerCancelCommit(1, false, AutoAcceptTurnedOff: true), commit);
        await using var check = NewDb();
        Assert.Equal(JobAssignmentStatus.CancelledByWorker, (await check.JobAssignments.SingleAsync(a => a.AssignmentId == s.AssignmentId)).AssignmentStatus);
        Assert.Equal(DispatchSlotStatuses.Available, (await check.BookingSlots.SingleAsync(x => x.SlotId == s.SlotId)).SlotStatus);
        Assert.Equal(JobOrderStatus.Dispatching, (await check.JobOrders.SingleAsync(o => o.OrderId == s.OrderId)).OrderStatus);
        var worker = await check.Workers.SingleAsync(w => w.WorkerId == s.WorkerId);
        Assert.False(worker.AutoAcceptOffers);
        Assert.Equal(WorkStatus.Idle, worker.WorkStatus);

        Assert.Null(await repo.TryCancelAsync(s.AssignmentId, s.WorkerId, Now, Now.AddDays(-30), 3)); // no longer ASSIGNED
        Assert.True(await repo.HasWorkerCancelledSeatAsync(s.OrderId, 1));
        Assert.False(await repo.HasWorkerCancelledSeatAsync(s.OrderId, 2));
        Assert.Contains(await repo.GetRedispatchingAfterWorkerCancelAsync(_day, _day), o => o.OrderId == s.OrderId);
    }

    [Fact]
    public async Task Another_workers_cancel_is_refused_and_writes_nothing()
    {
        if (!Available()) return;
        await using var db = NewDb();
        var s = await SeedAsync(db);

        Assert.Null(await new EfWorkerCancelRepository(db).TryCancelAsync(s.AssignmentId, s.WorkerId + 100_000, Now, Now.AddDays(-30), 3));

        await using var check = NewDb();
        Assert.Equal(JobAssignmentStatus.Assigned, (await check.JobAssignments.SingleAsync(a => a.AssignmentId == s.AssignmentId)).AssignmentStatus);
        Assert.Equal(DispatchSlotStatuses.Booked, (await check.BookingSlots.SingleAsync(x => x.SlotId == s.SlotId)).SlotStatus);
        Assert.True((await check.Workers.SingleAsync(w => w.WorkerId == s.WorkerId)).AutoAcceptOffers);
    }

    [Fact]
    public async Task The_third_cancel_inside_the_window_locks_the_freelancer_and_an_older_one_does_not_count()
    {
        if (!Available()) return;
        await using var db = NewDb();
        var s = await SeedAsync(db);
        await AddEarlierCancelAsync(db, s, Now.AddDays(-31)); // outside the 30 days
        await AddEarlierCancelAsync(db, s, Now.AddDays(-10));
        await AddEarlierCancelAsync(db, s, Now.AddDays(-1));
        var repo = new EfWorkerCancelRepository(db);
        Assert.Equal(2, await repo.CountWorkerCancelsSinceAsync(s.WorkerId, Now.AddDays(-30)));

        var commit = await repo.TryCancelAsync(s.AssignmentId, s.WorkerId, Now, Now.AddDays(-30), 3);

        Assert.Equal(new WorkerCancelCommit(3, true, AutoAcceptTurnedOff: true), commit);
        await using var check = NewDb();
        Assert.Equal(WorkStatus.Locked, (await check.Workers.SingleAsync(w => w.WorkerId == s.WorkerId)).WorkStatus);
    }
}
