using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Application.Features.Dispatch;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Agencies;
using CommonService.Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Tests.Agencies;

/// <summary>
/// AGENCY-FIX-01 against a real SQL Server: what the in-memory world cannot prove (a change to a read-only copy is lost, the conditional
/// <c>ExecuteUpdate</c> of the swap, the foreign key a slot delete must respect, the sweep saving its change).
/// Connection: env var <c>M5_SQL</c>, default LocalDB database <c>M5Verify</c> with every migration applied. Without a reachable server each test returns at once.
/// </summary>
public class WorkerRetirementSqlServerTests
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("M5_SQL")
        ?? @"Server=(localdb)\MSSQLLocalDB;Database=M5Verify;Integrated Security=True;TrustServerCertificate=True;";

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

    private sealed class Recorder : IPublisher
    {
        public List<object> Published { get; } = [];
        public Task Publish(object notification, CancellationToken cancellationToken = default) { Published.Add(notification); return Task.CompletedTask; }
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
        { Published.Add(notification!); return Task.CompletedTask; }
    }

    private sealed class NoQuota : IWorkerQuotaGuard
    {
        public Task EnsureCanAddWorkersAsync(int agencyId, int additionalWorkers, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed record Seed(int AgencyId, int CustomerId, int AddressId);

    private static async Task<Seed> SeedAgencyAsync()
    {
        await using var db = NewDb();
        var agency = new PartnerAgency
        {
            TaxCode = Digits(10),
            LegalName = "Retire Agency " + Digits(5),
            LegalRepresentative = "Rep",
            ContactPhone = "09" + Digits(8),
            ContactEmail = "r" + Digits(8) + "@example.test",
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
        await db.SaveChangesAsync();
        return new Seed(agency.AgencyId, customer.CustomerId, address.AddressId);
    }

    private static async Task<int> AddWorkerAsync(Seed s)
    {
        await using var db = NewDb();
        var worker = Worker.CreateAgencyStaff(s.AgencyId, "09" + Digits(8), Digits(12), "Worker " + Digits(4));
        worker.CreatedAt = Now;
        worker.UpdatedAt = Now;
        db.Workers.Add(worker);
        await db.SaveChangesAsync();
        return worker.WorkerId;
    }

    private async Task<int> AddSlotAsync(Seed s, int workerId, string status, DateOnly? date = null, string shift = Morning)
    {
        await using var db = NewDb();
        var slot = new BookingSlot
        {
            WorkerId = workerId,
            AgencyId = s.AgencyId,
            SlotDate = date ?? Day,
            ShiftCode = shift,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(12, 0),
            SlotSource = "AGENCY",
            SlotStatus = status,
            UpdatedAt = Now,
        };
        db.BookingSlots.Add(slot);
        await db.SaveChangesAsync();
        return slot.SlotId;
    }

    private async Task<(long AssignmentId, long OrderId)> AddAssignedJobAsync(Seed s, int workerId, int slotId)
    {
        await using var db = NewDb();
        var order = new JobOrder
        {
            OrderCode = "T" + Digits(12),
            CustomerId = s.CustomerId,
            AddressId = s.AddressId,
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
        var a = new JobAssignment
        {
            OrderId = order.OrderId,
            CustomerId = s.CustomerId,
            WorkerId = workerId,
            AgencyId = s.AgencyId,
            SlotId = slotId,
            ServiceTier = ServiceTier.Premium,
            AssignmentSeq = 1,
            DispatchRadiusKm = 5,
            GrossAmount = 400000m,
            CommissionRate = 0.2m,
            PayoutAmount = 320000m,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        a.TransitionTo(JobAssignmentStatus.Assigned);
        db.JobAssignments.Add(a);
        await db.SaveChangesAsync();
        return (a.AssignmentId, order.OrderId);
    }

    private static (WorkerRetirementService Service, Recorder Events, AppDbContext Db) Build(Seed s)
    {
        var db = NewDb();
        var clock = new FakeClock();
        clock.Set(Now);
        var user = new FakeCurrentUser();
        user.SignIn(s.AgencyId, UserRole.Partner);
        var events = new Recorder();
        var service = new WorkerRetirementService(
            new EfAgencyRepository(db), new EfAgencySwapRepository(db), new NoQuota(), new UnitOfWork(db), events, user, clock);
        return (service, events, db);
    }

    [Fact]
    public async Task Retiring_a_worker_without_jobs_is_saved_and_removes_its_future_AVAILABLE_slots_but_not_one_a_job_points_to()
    {
        if (!Available()) return;
        var s = await SeedAgencyAsync();
        var worker = await AddWorkerAsync(s);
        var free = await AddSlotAsync(s, worker, "AVAILABLE");
        var past = await AddSlotAsync(s, worker, "AVAILABLE", new DateOnly(2026, 10, 1));
        var kept = await AddSlotAsync(s, worker, "AVAILABLE", Day.AddDays(1));
        var other = await AddWorkerAsync(s);
        await AddAssignedJobAsync(s, other, kept); // a slot that an assignment points to must survive the delete (foreign key)
        var (service, _, db) = Build(s);
        using var keep = db;

        var r = await service.RetireWorkerAsync(worker, new RetireWorkerRequest());

        Assert.Equal("RETIRED", r.WorkStatus);
        await using var check = NewDb();
        Assert.Equal(WorkStatus.Retired, (await check.Workers.SingleAsync(w => w.WorkerId == worker)).WorkStatus);
        Assert.False(await check.BookingSlots.AnyAsync(x => x.SlotId == free));
        Assert.True(await check.BookingSlots.AnyAsync(x => x.SlotId == past));
        Assert.True(await check.BookingSlots.AnyAsync(x => x.SlotId == kept));
    }

    [Fact]
    public async Task An_existing_worker_takes_the_job_through_the_real_conditional_swap()
    {
        if (!Available()) return;
        var s = await SeedAgencyAsync();
        var oldWorker = await AddWorkerAsync(s);
        var newWorker = await AddWorkerAsync(s);
        var oldSlot = await AddSlotAsync(s, oldWorker, "BOOKED");
        var newSlot = await AddSlotAsync(s, newWorker, "AVAILABLE");
        var (assignmentId, _) = await AddAssignedJobAsync(s, oldWorker, oldSlot);
        var (service, events, db) = Build(s);
        using var keep = db;

        var r = await service.RetireWorkerAsync(oldWorker, new RetireWorkerRequest { Replacement = new RetireWorkerReplacement { ExistingWorkerId = newWorker } });

        Assert.Equal(("RETIRED", newWorker), (r.WorkStatus, r.ReplacementWorkerId));
        await using var check = NewDb();
        var job = await check.JobAssignments.SingleAsync(a => a.AssignmentId == assignmentId);
        Assert.Equal((newWorker, newSlot), (job.WorkerId, job.SlotId));
        Assert.Equal("BOOKED", (await check.BookingSlots.SingleAsync(x => x.SlotId == newSlot)).SlotStatus);
        Assert.Equal(WorkStatus.Retired, (await check.Workers.SingleAsync(w => w.WorkerId == oldWorker)).WorkStatus);
        Assert.Empty(events.Published);
    }

    [Fact]
    public async Task A_new_worker_replaces_in_one_transaction_and_keeps_the_phone_reserved_afterwards()
    {
        if (!Available()) return;
        var s = await SeedAgencyAsync();
        var oldWorker = await AddWorkerAsync(s);
        var oldSlot = await AddSlotAsync(s, oldWorker, "BOOKED");
        var (assignmentId, _) = await AddAssignedJobAsync(s, oldWorker, oldSlot);
        var phone = "09" + Digits(8);
        var nid = Digits(12);
        var (service, _, db) = Build(s);
        using var keep = db;
        var request = new RetireWorkerRequest
        {
            Replacement = new RetireWorkerReplacement { NewWorker = new NewWorkerDetails { FullName = "Newcomer", PhoneNumber = phone, NationalId = nid } }
        };

        var r = await service.RetireWorkerAsync(oldWorker, request);

        Assert.Equal("RETIRED", r.WorkStatus);
        await using var check = NewDb();
        var created = await check.Workers.SingleAsync(w => w.WorkerId == r.ReplacementWorkerId);
        Assert.Equal((WorkStatus.Idle, s.AgencyId), (created.WorkStatus, created.AgencyId!.Value));
        Assert.Equal(created.WorkerId, (await check.BookingSlots.SingleAsync(x => x.SlotId == oldSlot)).WorkerId);
        Assert.Equal(created.WorkerId, (await check.JobAssignments.SingleAsync(a => a.AssignmentId == assignmentId)).WorkerId);

        // the retired worker keeps its phone and national id reserved: a second new worker with the same phone is refused
        var (again, _, db2) = Build(s);
        using var keep2 = db2;
        var another = await AddWorkerAsync(s);
        var ex = await Assert.ThrowsAsync<CommonService.Application.Exceptions.BusinessRuleViolationException>(
            () => again.RetireWorkerAsync(another, new RetireWorkerRequest { Replacement = new RetireWorkerReplacement { NewWorker = new NewWorkerDetails { FullName = "X", PhoneNumber = phone, NationalId = Digits(12) } } }));
        Assert.Equal(RetireErrorCodes.DuplicatePhoneOrId, ex.Code);
    }

    [Fact]
    public async Task Confirmed_rescue_publishes_the_real_order_id_after_the_worker_is_saved_as_Retiring_and_the_sweep_repeats_it()
    {
        if (!Available()) return;
        var s = await SeedAgencyAsync();
        var worker = await AddWorkerAsync(s);
        var slot = await AddSlotAsync(s, worker, "BOOKED");
        var (assignmentId, orderId) = await AddAssignedJobAsync(s, worker, slot);
        var (service, events, db) = Build(s);
        using var keep = db;

        var preview = await service.RetireWorkerAsync(worker, new RetireWorkerRequest());
        Assert.True(preview.RequiresConfirmation);
        Assert.Empty(events.Published);

        var r = await service.RetireWorkerAsync(worker, new RetireWorkerRequest { ConfirmRescue = true });

        Assert.Equal("RETIRING", r.WorkStatus);
        var e = Assert.IsType<AgencySwapWindowExpired>(Assert.Single(events.Published));
        Assert.Equal((assignmentId, orderId, s.AgencyId), (e.AssignmentId, e.OrderId, e.AgencyId));
        await using var check = NewDb();
        Assert.Equal(WorkStatus.Retiring, (await check.Workers.SingleAsync(w => w.WorkerId == worker)).WorkStatus);

        // the rescue did not happen (nobody handled the event): the sweep publishes it again and the worker stays Retiring
        await using var sweepDb = NewDb();
        var clock = new FakeClock();
        clock.Set(Now);
        var sweepEvents = new Recorder();
        var sweep = new WorkerRetirementSweepService(new EfAgencyRepository(sweepDb), new UnitOfWork(sweepDb), sweepEvents, clock);
        var (retired, republished) = await sweep.SweepAsync();
        Assert.True(republished >= 1);
        Assert.Contains(sweepEvents.Published.OfType<AgencySwapWindowExpired>(), x => x.AssignmentId == assignmentId && x.OrderId == orderId);
        Assert.Equal(WorkStatus.Retiring, (await NewDb().Workers.SingleAsync(w => w.WorkerId == worker)).WorkStatus);
        Assert.Equal(0, retired);
    }

    [Fact]
    public async Task The_sweep_saves_Retired_for_a_Retiring_worker_whose_job_is_gone()
    {
        if (!Available()) return;
        var s = await SeedAgencyAsync();
        var worker = await AddWorkerAsync(s);
        await using (var db = NewDb())
        {
            var w = await db.Workers.SingleAsync(x => x.WorkerId == worker);
            w.TransitionTo(WorkStatus.Retiring);
            await db.SaveChangesAsync();
        }

        await using var sweepDb = NewDb();
        var clock = new FakeClock();
        clock.Set(Now);
        var sweep = new WorkerRetirementSweepService(new EfAgencyRepository(sweepDb), new UnitOfWork(sweepDb), new Recorder(), clock);

        var (retired, _) = await sweep.SweepAsync();

        Assert.True(retired >= 1);
        Assert.Equal(WorkStatus.Retired, (await NewDb().Workers.SingleAsync(w => w.WorkerId == worker)).WorkStatus);
    }
}
