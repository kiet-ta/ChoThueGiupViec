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
/// Decisions D34 (BOOK-TIME-01) against a real SQL Server: a timed job leaves the worker's shift slot AVAILABLE, so a second job that does
/// not overlap (with the 30-minute travel buffer) is offered in the same shift, and an overlapping one is refused inside the offer
/// transaction. Connection: env var <c>DISPATCH_SQL</c>, default LocalDB database <c>DispatchSwapVerify</c> with every migration applied.
/// Without a reachable server (or without the D34 columns) each test returns at once.
/// </summary>
public class TimedJobOfferSqlServerTests
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("DISPATCH_SQL")
        ?? @"Server=(localdb)\MSSQLLocalDB;Database=DispatchSwapVerify;Integrated Security=True;TrustServerCertificate=True;";

    private static readonly DateTime Now = new(2026, 11, 15, 1, 0, 0, DateTimeKind.Utc);
    private const string Morning = "SHIFT_MORNING";

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

    private sealed record Seed(int CustomerId, int AddressId, int WorkerId, int SlotId);

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
        var slot = new BookingSlot
        {
            WorkerId = worker.WorkerId,
            SlotDate = _day,
            ShiftCode = Morning,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(12, 0),
            SlotSource = "FREELANCER",
            SlotStatus = DispatchSlotStatuses.Available,
            UpdatedAt = Now,
        };
        db.BookingSlots.Add(slot);
        await db.SaveChangesAsync();
        return new Seed(customer.CustomerId, address.AddressId, worker.WorkerId, slot.SlotId);
    }

    private async Task<long> OrderAsync(AppDbContext db, Seed s, TimeOnly? start, TimeOnly? end)
    {
        var order = new JobOrder
        {
            OrderCode = "T" + Digits(12),
            CustomerId = s.CustomerId,
            AddressId = s.AddressId,
            ServiceTier = ServiceTier.Economy,
            ScheduledDate = _day,
            ShiftCode = Morning,
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
        return order.OrderId;
    }

    private static JobAssignment Offer(Seed s, long orderId) => new()
    {
        OrderId = orderId,
        CustomerId = s.CustomerId,
        WorkerId = s.WorkerId,
        SlotId = s.SlotId,
        ServiceTier = ServiceTier.Economy,
        AssignmentSeq = 1,
        DispatchRadiusKm = 5,
        GrossAmount = 130000m,
        CommissionRate = 0.2m,
        PayoutAmount = 104000m,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private static async Task<string> SlotStatusAsync(int slotId)
    {
        await using var db = NewDb();
        return await db.BookingSlots.AsNoTracking().Where(x => x.SlotId == slotId).Select(x => x.SlotStatus).SingleAsync();
    }

    [Fact]
    public async Task TwoTimedJobsOneAfterTheOther_ShareOneShift_AndTheSlotStaysOpen()
    {
        // SHIFT-MULTI-01 (decisions D42): no travel buffer by default, so 08:00-10:00 and 10:00-12:00 are two jobs of one 4 h shift.
        if (!Available()) return;
        await using var db = NewDb();
        var s = await SeedAsync(db);
        var first = await OrderAsync(db, s, new TimeOnly(8, 0), new TimeOnly(10, 0));
        var second = await OrderAsync(db, s, new TimeOnly(10, 0), new TimeOnly(12, 0));
        var repo = new EfDispatchRepository(db);

        var a = await repo.TryCreateOfferAsync(Offer(s, first));
        Assert.NotNull(a);
        Assert.True(await repo.TryAcceptOfferAsync(a!.AssignmentId, s.WorkerId, Now.AddMinutes(-1), Now));
        var b = await repo.TryCreateOfferAsync(Offer(s, second));

        Assert.NotNull(b);
        Assert.Equal(DispatchSlotStatuses.Available, await SlotStatusAsync(s.SlotId));
    }

    [Theory]
    [InlineData(9, 0, 11, 0)]   // overlaps 08:00-10:00
    [InlineData(9, 45, 12, 0)]  // 15 minutes before the first job ends
    public async Task AnOverlappingTimedJob_IsNotOffered(int h1, int m1, int h2, int m2)
    {
        if (!Available()) return;
        await using var db = NewDb();
        var s = await SeedAsync(db);
        var first = await OrderAsync(db, s, new TimeOnly(8, 0), new TimeOnly(10, 0));
        var clash = await OrderAsync(db, s, new TimeOnly(h1, m1), new TimeOnly(h2, m2));
        var repo = new EfDispatchRepository(db);

        Assert.NotNull(await repo.TryCreateOfferAsync(Offer(s, first)));
        Assert.Null(await repo.TryCreateOfferAsync(Offer(s, clash)));
        Assert.Equal(DispatchSlotStatuses.Available, await SlotStatusAsync(s.SlotId));
    }

    [Fact]
    public async Task AWholeShiftJob_StillHoldsTheSlot_AndBlocksTimedJobs()
    {
        if (!Available()) return;
        await using var db = NewDb();
        var s = await SeedAsync(db);
        var whole = await OrderAsync(db, s, null, null);
        var timed = await OrderAsync(db, s, new TimeOnly(8, 0), new TimeOnly(10, 0));
        var repo = new EfDispatchRepository(db);

        Assert.NotNull(await repo.TryCreateOfferAsync(Offer(s, whole)));
        Assert.Equal(DispatchSlotStatuses.Held, await SlotStatusAsync(s.SlotId));
        Assert.Null(await repo.TryCreateOfferAsync(Offer(s, timed)));
    }

    [Fact]
    public async Task AWholeShiftJob_IsNotOffered_WhenATimedJobAlreadyUsesTheShift()
    {
        if (!Available()) return;
        await using var db = NewDb();
        var s = await SeedAsync(db);
        var timed = await OrderAsync(db, s, new TimeOnly(8, 0), new TimeOnly(10, 0));
        var whole = await OrderAsync(db, s, null, null);
        var repo = new EfDispatchRepository(db);

        Assert.NotNull(await repo.TryCreateOfferAsync(Offer(s, timed)));
        Assert.Null(await repo.TryCreateOfferAsync(Offer(s, whole)));
        Assert.Equal(DispatchSlotStatuses.Available, await SlotStatusAsync(s.SlotId)); // the refused HELD was rolled back
    }
}
