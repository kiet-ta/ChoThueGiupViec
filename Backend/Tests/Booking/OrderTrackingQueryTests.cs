using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Booking;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CommonService.Tests.Booking;

/// <summary>BE-M2-10: order progress and customer history read JOB_ASSIGNMENT only (PRD 5.2, 0 JOIN).</summary>
public sealed class OrderTrackingQueryTests
{
    private const string ConnectionString =
        "Server=localhost;Database=ChoThueGiupViec;Integrated Security=True;TrustServerCertificate=True;";

    private static readonly DateTime Now = new(2099, 6, 1, 4, 0, 0, DateTimeKind.Utc);

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

    [Fact]
    public void OrderProgressQuery_ReadsOnlyTheJobAssignmentTable_WithoutJoin()
    {
        using var context = CreateContext();

        var sql = new OrderTrackingQuery(context).OrderProgressQuery(42).ToQueryString();

        AssertSingleTable(sql);
        Assert.Contains("[order_id]", sql);
    }

    [Fact]
    public void CustomerHistoryQuery_ReadsOnlyTheJobAssignmentTable_WithoutJoin()
    {
        using var context = CreateContext();

        var sql = new OrderTrackingQuery(context).CustomerHistoryQuery(7).ToQueryString();

        AssertSingleTable(sql);
        Assert.Contains("[customer_id]", sql);
    }

    private static void AssertSingleTable(string sql)
    {
        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM [JOB_ASSIGNMENT]", sql);
        Assert.Equal(1, sql.Split("FROM [", StringSplitOptions.None).Length - 1);
    }

    private static async Task<Customer> AddCustomerAsync(AppDbContext context)
    {
        var customer = new Customer
        {
            PhoneNumber = "091" + Random.Shared.Next(1000000, 9999999),
            FullName = "Tracking Customer",
            AccountStatus = CustomerAccountStatus.Active,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();
        return customer;
    }

    private static async Task<(long OrderId, int AddressId)> AddOrderAsync(AppDbContext context, int customerId)
    {
        var address = new CustomerAddress
        {
            CustomerId = customerId,
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
        context.CustomerAddresses.Add(address);
        await context.SaveChangesAsync();

        var order = new JobOrder
        {
            OrderCode = "TR" + Random.Shared.Next(100000, 999999),
            CustomerId = customerId,
            AddressId = address.AddressId,
            ServiceTier = ServiceTier.Economy,
            ScheduledDate = new DateOnly(2099, 6, 2),
            ShiftCode = "SHIFT_MORNING",
            AreaSnapshotM2 = 90m,
            RequiredWorkers = 2,
            TotalAmount = 520000m,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        context.JobOrders.Add(order);
        await context.SaveChangesAsync();
        return (order.OrderId, address.AddressId);
    }

    private static async Task<JobAssignment> AddAssignmentAsync(
        AppDbContext context, long orderId, int customerId, byte seq, DateTime createdAt, JobAssignmentStatus status)
    {
        var worker = Worker.CreateFreelancer(
            $"03{Random.Shared.Next(10000000, 99999999)}",
            Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString(),
            "Tracking Worker");
        context.Workers.Add(worker);
        await context.SaveChangesAsync();

        var slot = new BookingSlot
        {
            WorkerId = worker.WorkerId,
            SlotDate = new DateOnly(2099, 6, 2),
            ShiftCode = "SHIFT_MORNING",
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(12, 0),
            SlotSource = "MANUAL",
            SlotStatus = "LOCKED",
            UpdatedAt = Now,
        };
        context.BookingSlots.Add(slot);
        await context.SaveChangesAsync();

        var assignment = new JobAssignment
        {
            OrderId = orderId,
            CustomerId = customerId,
            WorkerId = worker.WorkerId,
            SlotId = slot.SlotId,
            ServiceTier = ServiceTier.Economy,
            AssignmentSeq = seq,
            DispatchRadiusKm = 5,
            GrossAmount = 260000m,
            CommissionRate = 0.200m,
            PayoutAmount = 208000m,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
        if (status != JobAssignmentStatus.Offered)
        {
            assignment.TransitionTo(JobAssignmentStatus.Assigned);
        }

        context.JobAssignments.Add(assignment);
        await context.SaveChangesAsync();
        return assignment;
    }

    [Fact]
    public async Task GetOrderProgressAsync_ReturnsOnlyTheRowsOfThatOrder_InSeqOrder()
    {
        if (!IsSqlServerAvailable())
        {
            return;
        }

        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var customer = await AddCustomerAsync(context);
        var (orderA, _) = await AddOrderAsync(context, customer.CustomerId);
        var (orderB, _) = await AddOrderAsync(context, customer.CustomerId);
        var second = await AddAssignmentAsync(context, orderA, customer.CustomerId, 2, Now, JobAssignmentStatus.Assigned);
        var first = await AddAssignmentAsync(context, orderA, customer.CustomerId, 1, Now, JobAssignmentStatus.Assigned);
        var offered = await AddAssignmentAsync(context, orderA, customer.CustomerId, 3, Now, JobAssignmentStatus.Offered);
        await AddAssignmentAsync(context, orderB, customer.CustomerId, 1, Now, JobAssignmentStatus.Assigned);

        var progress = await new OrderTrackingQuery(context).GetOrderProgressAsync(orderA);

        Assert.Equal([first.AssignmentId, second.AssignmentId, offered.AssignmentId], progress.Select(p => p.AssignmentId));
        Assert.Equal(JobAssignmentStatus.Offered, progress[2].AssignmentStatus);
        Assert.Equal(JobAssignmentStatus.Assigned, progress[0].AssignmentStatus);
    }

    [Fact]
    public async Task GetOrderProgressAsync_ReturnsEmpty_WhenTheOrderHasNoAssignment()
    {
        if (!IsSqlServerAvailable())
        {
            return;
        }

        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var customer = await AddCustomerAsync(context);
        var (order, _) = await AddOrderAsync(context, customer.CustomerId);

        var progress = await new OrderTrackingQuery(context).GetOrderProgressAsync(order);

        Assert.Empty(progress);
    }

    [Fact]
    public async Task GetCustomerHistoryAsync_ReturnsOnlyThatCustomersRows_NewestFirst()
    {
        if (!IsSqlServerAvailable())
        {
            return;
        }

        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var mine = await AddCustomerAsync(context);
        var other = await AddCustomerAsync(context);
        var (myOrder, _) = await AddOrderAsync(context, mine.CustomerId);
        var (otherOrder, _) = await AddOrderAsync(context, other.CustomerId);
        var older = await AddAssignmentAsync(context, myOrder, mine.CustomerId, 1, Now.AddDays(-2), JobAssignmentStatus.Assigned);
        var newer = await AddAssignmentAsync(context, myOrder, mine.CustomerId, 2, Now.AddDays(-1), JobAssignmentStatus.Assigned);
        await AddAssignmentAsync(context, otherOrder, other.CustomerId, 1, Now, JobAssignmentStatus.Assigned);

        var history = await new OrderTrackingQuery(context).GetCustomerHistoryAsync(mine.CustomerId);
        var none = await new OrderTrackingQuery(context).GetCustomerHistoryAsync(int.MaxValue);

        Assert.Equal([newer.AssignmentId, older.AssignmentId], history.Select(h => h.AssignmentId));
        Assert.All(history, h => Assert.Equal(myOrder, h.OrderId));
        Assert.Empty(none);
    }
}
