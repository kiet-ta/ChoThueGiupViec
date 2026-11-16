using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Dispatch;
using CommonService.Infrastructure.Modules.Workers;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CommonService.Tests.Booking;

/// <summary>
/// BOOK-BUSY-01 (decisions D41) against a real SQL Server: a timed job keeps the shift slot AVAILABLE (D34), so <see cref="WorkerFreeShiftQuery"/>
/// must say which times of the day the worker's live jobs take (widened by the 30-minute travel buffer) and how many hours are left, and
/// <c>IsFreeForJobAsync</c> must refuse a job that overlaps. Connection: env var <c>DISPATCH_SQL</c>, default LocalDB database
/// <c>DispatchSwapVerify</c> with every migration applied. Without a reachable server each test returns at once.
/// </summary>
public class FreeShiftBusyWindowsSqlServerTests
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("DISPATCH_SQL")
        ?? @"Server=(localdb)\MSSQLLocalDB;Database=DispatchSwapVerify;Integrated Security=True;TrustServerCertificate=True;";

    private static readonly DateTime Now = new(2026, 11, 15, 1, 0, 0, DateTimeKind.Utc);
    private const string Morning = "SHIFT_MORNING";
    private const string Afternoon = "SHIFT_AFTERNOON";
    private const string Evening = "SHIFT_EVENING";

    // A day of its own per test: leftovers of earlier runs in a shared database must not take part.
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

    private sealed record Seed(int CustomerId, int AddressId, int WorkerId);

    private async Task<Seed> SeedAsync(AppDbContext db)
    {
        var customer = new Customer { PhoneNumber = "09" + Digits(8), FullName = "C", AccountStatus = CustomerAccountStatus.Active, CreatedAt = Now, UpdatedAt = Now };
        db.Customers.Add(customer);
        var worker = Worker.CreateFreelancer("09" + Digits(8), Digits(12), "W " + Digits(4));
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
        await db.SaveChangesAsync();
        return new Seed(customer.CustomerId, address.AddressId, worker.WorkerId);
    }

    private async Task<int> SlotAsync(AppDbContext db, Seed s, string shift, string status = DispatchSlotStatuses.Available)
    {
        var (start, end) = shift switch
        {
            Morning => (new TimeOnly(8, 0), new TimeOnly(12, 0)),
            Afternoon => (new TimeOnly(13, 0), new TimeOnly(17, 0)),
            _ => (new TimeOnly(17, 30), new TimeOnly(20, 30)),
        };
        var slot = new BookingSlot
        {
            WorkerId = s.WorkerId,
            SlotDate = _day,
            ShiftCode = shift,
            StartTime = start,
            EndTime = end,
            SlotSource = "FREELANCER",
            SlotStatus = status,
            UpdatedAt = Now,
        };
        db.BookingSlots.Add(slot);
        await db.SaveChangesAsync();
        return slot.SlotId;
    }

    /// <summary>A live (OFFERED) job of the worker in the shift, timed or whole-shift, through the real offer transaction.</summary>
    private async Task<long> JobAsync(AppDbContext db, Seed s, int slotId, string shift, TimeOnly? start, TimeOnly? end)
    {
        var order = new JobOrder
        {
            OrderCode = "T" + Digits(12),
            CustomerId = s.CustomerId,
            AddressId = s.AddressId,
            ServiceTier = ServiceTier.Economy,
            ScheduledDate = _day,
            ShiftCode = shift,
            StartTime = start,
            EndTime = end,
            AreaSnapshotM2 = 40m,
            RequiredWorkers = 1,
            TotalAmount = 130000m,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.JobOrders.Add(order);
        await db.SaveChangesAsync();

        var offer = await new EfDispatchRepository(db).TryCreateOfferAsync(new JobAssignment
        {
            OrderId = order.OrderId,
            CustomerId = s.CustomerId,
            WorkerId = s.WorkerId,
            SlotId = slotId,
            ServiceTier = ServiceTier.Economy,
            AssignmentSeq = 1,
            DispatchRadiusKm = 5,
            GrossAmount = 130000m,
            CommissionRate = 0.2m,
            PayoutAmount = 104000m,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        Assert.NotNull(offer);
        return offer!.AssignmentId;
    }

    private static TimeOnly T(int h, int m = 0) => new(h, m);

    [Fact]
    public async Task ATimedJob_LeavesTheShiftListed_WithItsOwnTimeAndTheHoursLeft()
    {
        if (!Available()) return;
        await using var db = NewDb();
        var s = await SeedAsync(db);
        var slot = await SlotAsync(db, s, Morning);
        await JobAsync(db, s, slot, Morning, T(8), T(10, 30));

        var shifts = await new WorkerFreeShiftQuery(db).GetFreeShiftsAsync(s.WorkerId, _day, _day);

        var morning = Assert.Single(shifts);
        Assert.Equal(Morning, morning.ShiftCode);
        var busy = Assert.Single(morning.BusyWindows!);
        Assert.Equal(("08:00", "10:30"), (busy.StartTime, busy.EndTime)); // no travel buffer by default (D42): the job's own time
        Assert.Equal(5.5m, morning.RemainingHours); // 8 h a day - 2.5 h
    }

    [Theory]
    [InlineData(8, 0, 10, 0, false)]    // the same time
    [InlineData(9, 0, 11, 0, false)]    // overlaps the middle
    [InlineData(10, 0, 12, 0, false)]   // overlaps the end
    [InlineData(10, 15, 12, 0, false)]  // 15 minutes before the job ends
    [InlineData(10, 30, 12, 0, true)]   // right after the job: free (1.5 h is not a bookable length, the rule is only about the overlap)
    [InlineData(7, 0, 8, 0, true)]      // right before the job
    public async Task IsFreeForJob_RefusesATimeThatOverlapsALiveJob(int h1, int m1, int h2, int m2, bool free)
    {
        if (!Available()) return;
        await using var db = NewDb();
        var s = await SeedAsync(db);
        var slot = await SlotAsync(db, s, Morning);
        await JobAsync(db, s, slot, Morning, T(8), T(10, 30));

        var result = await new WorkerFreeShiftQuery(db).IsFreeForJobAsync(s.WorkerId, _day, Morning, T(h1, m1), T(h2, m2));

        Assert.Equal(free, result);
    }

    [Fact]
    public async Task AWholeShiftJob_IsRefused_WhenATimedJobAlreadyUsesTheShift_AndTheOtherShiftsAreFree()
    {
        if (!Available()) return;
        await using var db = NewDb();
        var s = await SeedAsync(db);
        var morning = await SlotAsync(db, s, Morning);
        await SlotAsync(db, s, Afternoon);
        await JobAsync(db, s, morning, Morning, T(8), T(10));
        var query = new WorkerFreeShiftQuery(db);

        Assert.False(await query.IsFreeForJobAsync(s.WorkerId, _day, Morning, null, null));
        Assert.True(await query.IsFreeForJobAsync(s.WorkerId, _day, Afternoon, null, null));
        Assert.True(await query.IsFreeForJobAsync(s.WorkerId, _day, Afternoon, T(13), T(15)));
    }

    [Fact]
    public async Task ABlockedOrMissingSlot_IsNotFree()
    {
        if (!Available()) return;
        await using var db = NewDb();
        var s = await SeedAsync(db);
        await SlotAsync(db, s, Morning, "BLOCKED");
        var query = new WorkerFreeShiftQuery(db);

        Assert.False(await query.IsFreeForJobAsync(s.WorkerId, _day, Morning, T(8), T(10)));
        Assert.False(await query.IsFreeForJobAsync(s.WorkerId, _day, Evening, T(18), T(20)));
        Assert.Empty(await query.GetFreeShiftsAsync(s.WorkerId, _day, _day));
    }

    [Fact]
    public async Task ACancelledByWorkerJob_TakesNoTime()
    {
        if (!Available()) return;
        await using var db = NewDb();
        var s = await SeedAsync(db);
        var slot = await SlotAsync(db, s, Morning);
        var assignmentId = await JobAsync(db, s, slot, Morning, T(8), T(10, 30));
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE JOB_ASSIGNMENT SET assignment_status = 'CANCELLED_BY_WORKER' WHERE assignment_id = {0}", assignmentId);
        var query = new WorkerFreeShiftQuery(db);

        var shifts = await query.GetFreeShiftsAsync(s.WorkerId, _day, _day);

        var morning = Assert.Single(shifts);
        Assert.Empty(morning.BusyWindows!);
        Assert.Equal(8m, morning.RemainingHours);
        Assert.True(await query.IsFreeForJobAsync(s.WorkerId, _day, Morning, T(8), T(10)));
    }

    [Fact]
    public async Task TwoFourHourJobs_UseUpTheDay_SoNoMoreHoursAreLeftAndTheEveningIsRefused()
    {
        if (!Available()) return;
        await using var db = NewDb();
        var s = await SeedAsync(db);
        var morning = await SlotAsync(db, s, Morning);
        var afternoon = await SlotAsync(db, s, Afternoon);
        await SlotAsync(db, s, Evening);
        await JobAsync(db, s, morning, Morning, T(8), T(12));
        await JobAsync(db, s, afternoon, Afternoon, T(13), T(17));
        var query = new WorkerFreeShiftQuery(db);

        var shifts = await query.GetFreeShiftsAsync(s.WorkerId, _day, _day);

        Assert.Equal(3, shifts.Count);
        Assert.All(shifts, x => Assert.Equal(0m, x.RemainingHours));
        Assert.False(await query.IsFreeForJobAsync(s.WorkerId, _day, Evening, T(18), T(20)));
    }

    [Fact]
    public async Task AWorkerWith8To10_CanTakeAnother10To12InTheSameShift_AndTheShiftStaysListed()
    {
        // SHIFT-MULTI-01 (decisions D42): one shift, several orders; only an overlap is refused.
        if (!Available()) return;
        await using var db = NewDb();
        var s = await SeedAsync(db);
        var slot = await SlotAsync(db, s, Morning);
        await JobAsync(db, s, slot, Morning, T(8), T(10));
        var query = new WorkerFreeShiftQuery(db);

        Assert.True(await query.IsFreeForJobAsync(s.WorkerId, _day, Morning, T(10), T(12)));
        Assert.False(await query.IsFreeForJobAsync(s.WorkerId, _day, Morning, T(9), T(11)));
        var morning = Assert.Single(await query.GetFreeShiftsAsync(s.WorkerId, _day, _day));
        Assert.Equal(6m, morning.RemainingHours);
    }

    [Fact]
    public async Task ARaisedTravelBuffer_WidensTheBusyTimeAgain()
    {
        if (!Available()) return;
        await using var db = NewDb();
        var s = await SeedAsync(db);
        var slot = await SlotAsync(db, s, Morning);
        await JobAsync(db, s, slot, Morning, T(8), T(10, 30));
        var rules = new CommonService.Application.Common.Options.BusinessRules();
        rules.Booking.TravelBufferMinutes = 30;
        var query = new WorkerFreeShiftQuery(db, Microsoft.Extensions.Options.Options.Create(rules));

        var busy = Assert.Single(Assert.Single(await query.GetFreeShiftsAsync(s.WorkerId, _day, _day)).BusyWindows!);

        Assert.Equal(("07:30", "11:00"), (busy.StartTime, busy.EndTime));
        Assert.False(await query.IsFreeForJobAsync(s.WorkerId, _day, Morning, T(10, 30), T(12)));
    }
}
