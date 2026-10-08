using System.Reflection;
using CommonService.Application.Common.Models;
using CommonService.Application.Features.Admin;
using CommonService.Application.Features.Admin.Dtos;
using CommonService.Application.Features.Admin.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Admin;
using CommonService.Infrastructure.Persistence;
using CommonService.WebAPI.Controllers.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Tests.Admin;

/// <summary>BE-M6-06b: the Admin operations dashboard (contract admin.md 2.3, question A2).</summary>
public class AdminDashboardTests
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

    private sealed class TestClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => local.AddHours(-7);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(utcNow));
    }

    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0, int s = 0) => new(y, m, d, h, min, s, DateTimeKind.Utc);

    // ---- the windows ----------------------------------------------------------------------------

    [Fact]
    public void A_Wednesday_morning_gives_that_local_day_and_the_week_from_Monday_00_00_to_the_next_Monday_00_00()
    {
        // 2026-10-07 04:00 UTC = Wednesday 11:00 in Ho Chi Minh.
        var window = AdminDashboardService.WindowFor(Utc(2026, 10, 7, 4), new TestClock(Utc(2026, 10, 7, 4)), 6);

        Assert.Equal(Utc(2026, 10, 6, 17), window.TodayStartUtc); // 2026-10-07 00:00 +07:00
        Assert.Equal(Utc(2026, 10, 7, 17), window.TodayEndUtc);
        Assert.Equal(Utc(2026, 10, 4, 17), window.WeekStartUtc); // Monday 2026-10-05 00:00 +07:00
        Assert.Equal(Utc(2026, 10, 11, 17), window.WeekEndUtc); // Monday 2026-10-12 00:00 +07:00
        Assert.Equal(Utc(2026, 10, 7, 10), window.NearSlaBeforeUtc);
        Assert.All(new[] { window.TodayStartUtc, window.TodayEndUtc, window.WeekStartUtc, window.WeekEndUtc, window.NearSlaBeforeUtc },
            d => Assert.Equal(DateTimeKind.Utc, d.Kind));
    }

    [Fact]
    public void Sunday_evening_in_Ho_Chi_Minh_still_belongs_to_the_week_that_is_ending()
    {
        // 2026-10-11 16:59 UTC = Sunday 23:59 local.
        var window = AdminDashboardService.WindowFor(Utc(2026, 10, 11, 16, 59), new TestClock(Utc(2026, 10, 11, 16, 59)), 6);

        Assert.Equal(Utc(2026, 10, 10, 17), window.TodayStartUtc); // Sunday 2026-10-11 00:00 +07:00
        Assert.Equal(Utc(2026, 10, 4, 17), window.WeekStartUtc); // the week of Monday 2026-10-05
        Assert.Equal(Utc(2026, 10, 11, 17), window.WeekEndUtc);
    }

    [Fact]
    public void One_minute_later_it_is_Monday_local_even_though_the_UTC_date_is_still_Sunday()
    {
        // 2026-10-11 17:00 UTC is Monday 2026-10-12 00:00 local: a new day and a new week, while the UTC calendar still says the 11th.
        var window = AdminDashboardService.WindowFor(Utc(2026, 10, 11, 17), new TestClock(Utc(2026, 10, 11, 17)), 6);

        Assert.Equal(Utc(2026, 10, 11, 17), window.TodayStartUtc);
        Assert.Equal(Utc(2026, 10, 12, 17), window.TodayEndUtc);
        Assert.Equal(Utc(2026, 10, 11, 17), window.WeekStartUtc); // Monday is both the first day of the week and today
        Assert.Equal(Utc(2026, 10, 18, 17), window.WeekEndUtc);
    }

    [Fact]
    public void The_first_instant_of_Monday_is_in_the_new_week_and_the_last_second_of_Sunday_is_not()
    {
        var monday = AdminDashboardService.WindowFor(Utc(2026, 10, 4, 17), new TestClock(Utc(2026, 10, 4, 17)), 6); // Monday 2026-10-05 00:00 local
        var sunday = AdminDashboardService.WindowFor(Utc(2026, 10, 4, 16, 59, 59), new TestClock(Utc(2026, 10, 4, 16, 59, 59)), 6); // Sunday 23:59:59

        Assert.Equal(Utc(2026, 10, 4, 17), monday.WeekStartUtc);
        Assert.Equal(Utc(2026, 9, 27, 17), sunday.WeekStartUtc); // the previous week, Monday 2026-09-28
    }

    [Theory]
    [InlineData(6, 10)]
    [InlineData(12, 16)]
    [InlineData(0, 4)]
    public void The_near_SLA_limit_is_now_plus_the_configured_hours(int hours, int expectedHourUtc)
    {
        var window = AdminDashboardService.WindowFor(Utc(2026, 10, 7, 4), new TestClock(Utc(2026, 10, 7, 4)), hours);

        Assert.Equal(Utc(2026, 10, 7, expectedHourUtc), window.NearSlaBeforeUtc);
    }

    // ---- the service ----------------------------------------------------------------------------

    private sealed class StubRepository(DashboardCounts counts) : IAdminDashboardRepository
    {
        public DashboardWindow? Asked { get; private set; }

        public Task<DashboardCounts> GetCountsAsync(DashboardWindow window, CancellationToken cancellationToken = default)
        {
            Asked = window;
            return Task.FromResult(counts);
        }
    }

    [Fact]
    public async Task The_counts_are_mapped_into_the_three_groups_and_the_window_comes_from_the_clock_and_the_option()
    {
        var repo = new StubRepository(new DashboardCounts(3, 17, 5, 9, 7, 2));
        var service = new AdminDashboardService(repo, new TestClock(Utc(2026, 10, 7, 4)),
            Microsoft.Extensions.Options.Options.Create(new AdminDashboardOptions { DisputeNearSlaHours = 12 }));

        var dto = await service.GetAsync();

        Assert.Equal(Utc(2026, 10, 7, 4), dto.GeneratedAt);
        Assert.Equal(DateTimeKind.Utc, dto.GeneratedAt.Kind);
        Assert.Equal((3, 17), (dto.Orders.Today, dto.Orders.ThisWeek));
        Assert.Equal((5, 9), (dto.Shifts.InProgress, dto.Shifts.CompletedToday));
        Assert.Equal((7, 2), (dto.Disputes.Open, dto.Disputes.NearSla));
        Assert.Equal(Utc(2026, 10, 7, 16), repo.Asked!.NearSlaBeforeUtc);
    }

    [Fact]
    public void The_default_near_SLA_threshold_is_6_hours()
    {
        Assert.Equal(6, new AdminDashboardOptions().DisputeNearSlaHours);
    }

    [Fact]
    public void The_response_has_exactly_the_three_groups_of_the_contract_and_no_other_metric()
    {
        string[] Names(Type t) => t.GetProperties().Select(p => p.Name).Order().ToArray();

        Assert.Equal(["Disputes", "GeneratedAt", "Orders", "Shifts"], Names(typeof(DashboardDto)));
        Assert.Equal(["ThisWeek", "Today"], Names(typeof(DashboardOrdersDto)));
        Assert.Equal(["CompletedToday", "InProgress"], Names(typeof(DashboardShiftsDto)));
        Assert.Equal(["NearSla", "Open"], Names(typeof(DashboardDisputesDto)));
    }

    // ---- controller -----------------------------------------------------------------------------

    private sealed class StubService : IAdminDashboardService
    {
        public Task<DashboardDto> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DashboardDto { Orders = new DashboardOrdersDto { Today = 4 } });
    }

    [Fact]
    public async Task The_controller_is_admin_only_on_the_contract_route_and_answers_the_envelope()
    {
        var type = typeof(AdminDashboardController);
        Assert.Equal("AdminOnly", type.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal("api/admin/dashboard", type.GetCustomAttribute<RouteAttribute>()!.Template);
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.All(methods, m => Assert.Null(m.GetCustomAttribute<AllowAnonymousAttribute>()));
        Assert.Equal(["GET"], methods.SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Select(a => a.HttpMethods.Single())).ToArray());

        var result = Assert.IsType<OkObjectResult>(await new AdminDashboardController(new StubService()).Get(default));
        var body = Assert.IsType<ApiResponse<DashboardDto>>(result.Value);
        Assert.True(body.Success);
        Assert.Equal(4, body.Data!.Orders.Today);
    }

    // ---- SQL Server -----------------------------------------------------------------------------

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;

    private static int _dayCounter;

    private sealed class Seed
    {
        public int CustomerId { get; set; }
        public int AddressId { get; set; }
        public int WorkerId { get; set; }
        public List<long> OrderIds { get; } = [];
        public List<int> SlotIds { get; } = [];
        public List<long> AssignmentIds { get; } = [];
    }

    private static async Task<Seed> SeedBaseAsync(DateTime now)
    {
        var seed = new Seed();
        await using var db = new AppDbContext(Options());

        var customer = new Customer
        {
            PhoneNumber = "091" + Random.Shared.Next(1000000, 9999999),
            FullName = "Dashboard Customer",
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

        var worker = new Worker
        {
            PhoneNumber = "092" + Random.Shared.Next(1000000, 9999999),
            NationalId = "079" + Random.Shared.Next(100000000, 999999999),
            FullName = "Dashboard Worker",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        seed.CustomerId = customer.CustomerId;
        seed.AddressId = address.AddressId;
        seed.WorkerId = worker.WorkerId;
        return seed;
    }

    private static async Task<long> AddOrderAsync(Seed seed, DateTime createdAt)
    {
        await using var db = new AppDbContext(Options());
        var order = new JobOrder
        {
            OrderCode = "ORD" + Random.Shared.Next(100000, 999999),
            CustomerId = seed.CustomerId,
            AddressId = seed.AddressId,
            ServiceTier = ServiceTier.Economy,
            ScheduledDate = new DateOnly(2090, 1, 1).AddDays(Interlocked.Increment(ref _dayCounter)),
            ShiftCode = "SHIFT1",
            AreaSnapshotM2 = 80m,
            RequiredWorkers = 1,
            TotalAmount = 260000m,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
        db.JobOrders.Add(order);
        await db.SaveChangesAsync();
        seed.OrderIds.Add(order.OrderId);
        return order.OrderId;
    }

    private static readonly DateTime Old = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static async Task AddAssignmentAsync(Seed seed, JobAssignmentStatus status, DateTime? completedAt = null)
    {
        var orderId = await AddOrderAsync(seed, Old); // an old order, so it never counts as "created today"
        await using var db = new AppDbContext(Options());

        var slotDate = (await db.JobOrders.AsNoTracking().SingleAsync(o => o.OrderId == orderId)).ScheduledDate;
        var slot = new BookingSlot
        {
            WorkerId = seed.WorkerId,
            SlotDate = slotDate,
            ShiftCode = "SHIFT1",
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(12, 0),
            SlotSource = "MANUAL",
            SlotStatus = "LOCKED",
            UpdatedAt = Old,
        };
        db.BookingSlots.Add(slot);
        await db.SaveChangesAsync();

        var assignment = new JobAssignment
        {
            OrderId = orderId,
            CustomerId = seed.CustomerId,
            WorkerId = seed.WorkerId,
            SlotId = slot.SlotId,
            ServiceTier = ServiceTier.Economy,
            AssignmentSeq = 1,
            DispatchRadiusKm = 5,
            GrossAmount = 260000m,
            CommissionRate = 0.200m,
            PayoutAmount = 208000m,
            CreatedAt = Old,
            UpdatedAt = Old,
        };
        var path = status switch
        {
            JobAssignmentStatus.Assigned => new[] { JobAssignmentStatus.Assigned },
            JobAssignmentStatus.CheckedIn => [JobAssignmentStatus.Assigned, JobAssignmentStatus.CheckedIn],
            JobAssignmentStatus.InProgress => [JobAssignmentStatus.Assigned, JobAssignmentStatus.CheckedIn, JobAssignmentStatus.InProgress],
            JobAssignmentStatus.AwaitingAcceptance => [JobAssignmentStatus.Assigned, JobAssignmentStatus.CheckedIn, JobAssignmentStatus.InProgress, JobAssignmentStatus.AwaitingAcceptance],
            JobAssignmentStatus.Completed => [JobAssignmentStatus.Assigned, JobAssignmentStatus.CheckedIn, JobAssignmentStatus.InProgress, JobAssignmentStatus.AwaitingAcceptance, JobAssignmentStatus.Completed],
            JobAssignmentStatus.Cancelled => [JobAssignmentStatus.Assigned, JobAssignmentStatus.Cancelled],
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
        foreach (var step in path) assignment.TransitionTo(step);
        assignment.CompletedAt = completedAt;
        db.JobAssignments.Add(assignment);
        await db.SaveChangesAsync();

        seed.SlotIds.Add(slot.SlotId);
        seed.AssignmentIds.Add(assignment.AssignmentId);
    }

    private static async Task AddDisputeAsync(Seed seed, string status, DateTime slaDueAt)
    {
        var orderId = await AddOrderAsync(seed, Old); // DISPUTE_TICKET.order_id is unique: one order per ticket
        await using var db = new AppDbContext(Options());
        db.DisputeTickets.Add(new DisputeTicket
        {
            OrderId = orderId,
            RaisedBy = "CUSTOMER",
            Category = "QUALITY",
            Description = "Dirty",
            DisputeStatus = status,
            SlaDueAt = slaDueAt,
            CreatedAt = Old,
        });
        await db.SaveChangesAsync();
    }

    private static async Task CleanAsync(Seed seed)
    {
        await using var db = new AppDbContext(Options());
        await db.DisputeTickets.Where(d => seed.OrderIds.Contains(d.OrderId)).ExecuteDeleteAsync();
        await db.JobAssignments.Where(a => seed.AssignmentIds.Contains(a.AssignmentId)).ExecuteDeleteAsync();
        await db.BookingSlots.Where(b => seed.SlotIds.Contains(b.SlotId)).ExecuteDeleteAsync();
        await db.JobOrders.Where(o => seed.OrderIds.Contains(o.OrderId)).ExecuteDeleteAsync();
        await db.CustomerAddresses.Where(a => a.AddressId == seed.AddressId).ExecuteDeleteAsync();
        await db.Customers.Where(c => c.CustomerId == seed.CustomerId).ExecuteDeleteAsync();
        await db.Workers.Where(w => w.WorkerId == seed.WorkerId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Real_database_every_count_follows_the_definitions_of_A2_on_the_real_tables()
    {
        if (!IsSqlServerAvailable()) return;

        // A Wednesday far in the future, so no real order is created "today" or "this week" in the window.
        var wednesday = new DateTime(2092, 5, 11, 4, 0, 0, DateTimeKind.Utc);
        while (wednesday.AddHours(7).DayOfWeek != DayOfWeek.Wednesday) wednesday = wednesday.AddDays(1);
        var clock = new TestClock(wednesday);
        var window = AdminDashboardService.WindowFor(wednesday, clock, 6);
        var seed = await SeedBaseAsync(wednesday);
        try
        {
            await using var baselineDb = new AppDbContext(Options());
            var before = await new EfAdminDashboardRepository(baselineDb).GetCountsAsync(window);
            Assert.Equal((0, 0, 0), (before.OrdersToday, before.OrdersThisWeek, before.ShiftsCompletedToday)); // windows in 2092 hold nothing yet

            // Orders: the edges of the day and of the week (Wednesday: yesterday is in the same week).
            await AddOrderAsync(seed, window.TodayStartUtc); // today
            await AddOrderAsync(seed, window.TodayEndUtc.AddSeconds(-1)); // today
            await AddOrderAsync(seed, window.TodayStartUtc.AddSeconds(-1)); // yesterday: this week only
            await AddOrderAsync(seed, window.WeekStartUtc); // Monday 00:00: this week only
            await AddOrderAsync(seed, window.WeekEndUtc.AddSeconds(-1)); // Sunday 23:59:59: this week only
            await AddOrderAsync(seed, window.WeekStartUtc.AddSeconds(-1)); // last Sunday: neither
            await AddOrderAsync(seed, window.WeekEndUtc); // next Monday 00:00: neither

            // Shifts: three statuses are "in progress", the others are not.
            await AddAssignmentAsync(seed, JobAssignmentStatus.CheckedIn);
            await AddAssignmentAsync(seed, JobAssignmentStatus.InProgress);
            await AddAssignmentAsync(seed, JobAssignmentStatus.AwaitingAcceptance);
            await AddAssignmentAsync(seed, JobAssignmentStatus.Assigned);
            await AddAssignmentAsync(seed, JobAssignmentStatus.Cancelled);
            await AddAssignmentAsync(seed, JobAssignmentStatus.Completed, window.TodayStartUtc); // completed today
            await AddAssignmentAsync(seed, JobAssignmentStatus.Completed, window.TodayEndUtc.AddSeconds(-1)); // completed today
            await AddAssignmentAsync(seed, JobAssignmentStatus.Completed, window.TodayStartUtc.AddSeconds(-1)); // yesterday
            await AddAssignmentAsync(seed, JobAssignmentStatus.Completed, window.TodayEndUtc); // tomorrow 00:00

            // Disputes: due in 3 h, overdue, due in exactly 6 h (the limit is inclusive), due in 30 h, decided.
            await AddDisputeAsync(seed, "OPEN", wednesday.AddHours(3));
            await AddDisputeAsync(seed, "IN_REVIEW", wednesday.AddHours(-2));
            await AddDisputeAsync(seed, "OPEN", wednesday.AddHours(6));
            await AddDisputeAsync(seed, "OPEN", wednesday.AddHours(30));
            await AddDisputeAsync(seed, "RESOLVED", wednesday.AddHours(1));
            await AddDisputeAsync(seed, "DISMISSED", wednesday.AddHours(-5));

            await using var db = new AppDbContext(Options());
            var dto = await new AdminDashboardService(new EfAdminDashboardRepository(db), clock,
                Microsoft.Extensions.Options.Options.Create(new AdminDashboardOptions())).GetAsync();

            Assert.Equal((2, 5), (dto.Orders.Today, dto.Orders.ThisWeek));
            Assert.Equal(2, dto.Shifts.CompletedToday);
            Assert.Equal(3, dto.Shifts.InProgress - before.ShiftsInProgress);
            Assert.Equal(4, dto.Disputes.Open - before.DisputesOpen);
            Assert.Equal(3, dto.Disputes.NearSla - before.DisputesNearSla);
            Assert.Equal(wednesday, dto.GeneratedAt);
        }
        finally
        {
            await CleanAsync(seed);
        }
    }
}
