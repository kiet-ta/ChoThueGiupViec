using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CommonService.Tests.Persistence;

public class SqlServerPersistenceTests
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

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task All_27_Tables_Exist_In_SqlServer()
    {
        if (!IsSqlServerAvailable()) return;

        using var context = CreateContext();
        var tableNames = new List<string>();

        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE';";
        await context.Database.OpenConnectionAsync();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tableNames.Add(reader.GetString(0));
        }

        var expectedTables = new[]
        {
            "ADMIN",
            "ADMIN_AUDIT_LOG",
            "BOOKING_SLOT",
            "CHECK_IN_LOG",
            "CUSTOMER",
            "CUSTOMER_ADDRESS",
            "DISPUTE_TICKET",
            "ESCROW_TRANSACTION",
            "FAVORITE_WORKER",
            "INCIDENT_LOG",
            "JOB_ASSIGNMENT",
            "JOB_ORDER",
            "JOB_ORDER_EXTENSION",
            "JOB_PHOTO",
            "OTP_CODE",
            "PARTNER_AGENCY",
            "PARTNER_SUBSCRIPTION",
            "PAYMENT_TRANSACTION",
            "PAYOUT_BATCH",
            "PAYOUT_ITEM",
            "PRICE_RULE",
            "REFRESH_TOKEN",
            "SKILL",
            "SUBSCRIPTION_PACKAGE",
            "TWO_WAY_RATING",
            "WORKER",
            "WORKER_SKILL"
        };

        foreach (var expected in expectedTables)
        {
            Assert.Contains(expected, tableNames);
        }
    }

    [Fact]
    public async Task Unique_Constraint_Rejects_Duplicate_Slot()
    {
        if (!IsSqlServerAvailable()) return;

        using var context = CreateContext();

        var phone = "090" + Random.Shared.Next(1000000, 9999999);
        var nationalId = "079" + Random.Shared.Next(100000000, 999999999);
        var worker = new Worker
        {
            PhoneNumber = phone,
            NationalId = nationalId,
            FullName = "Test Worker",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Workers.Add(worker);
        await context.SaveChangesAsync();

        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        var slot1 = new BookingSlot
        {
            WorkerId = worker.WorkerId,
            SlotDate = date,
            ShiftCode = "SHIFT1",
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(12, 0),
            SlotSource = "MANUAL",
            SlotStatus = "AVAILABLE",
            UpdatedAt = DateTime.UtcNow
        };
        context.BookingSlots.Add(slot1);
        await context.SaveChangesAsync();

        var slot2 = new BookingSlot
        {
            WorkerId = worker.WorkerId,
            SlotDate = date,
            ShiftCode = "SHIFT1",
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(12, 0),
            SlotSource = "MANUAL",
            SlotStatus = "AVAILABLE",
            UpdatedAt = DateTime.UtcNow
        };
        context.BookingSlots.Add(slot2);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.NotNull(ex.InnerException);
        Assert.Contains("IX_BOOKING_SLOT_worker_id_slot_date_shift_code", ex.InnerException.Message);
    }

    [Fact]
    public async Task JobAssignment_Filtered_Unique_Slot_Index_Rejects_Duplicate_Active_And_Allows_Reassignment_After_Cancel()
    {
        if (!IsSqlServerAvailable()) return;

        using var context = CreateContext();

        // Setup customer, address, order, worker, slot
        var customerPhone = "091" + Random.Shared.Next(1000000, 9999999);
        var customer = new Customer
        {
            PhoneNumber = customerPhone,
            FullName = "Customer Test",
            AccountStatus = CustomerAccountStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var address = new CustomerAddress
        {
            CustomerId = customer.CustomerId,
            Label = "Home",
            AddressLine = "123 Test St",
            District = "District 1",
            City = "HCMC",
            HousingType = HousingType.House,
            FloorAreaM2 = 40.0m,
            NumFloors = 2,
            Latitude = 10.762622m,
            Longitude = 106.660172m,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        };
        context.CustomerAddresses.Add(address);
        await context.SaveChangesAsync();

        var order = new JobOrder
        {
            OrderCode = "ORD" + Random.Shared.Next(100000, 999999),
            CustomerId = customer.CustomerId,
            AddressId = address.AddressId,
            ServiceTier = ServiceTier.Economy,
            ScheduledDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            ShiftCode = "SHIFT1",
            AreaSnapshotM2 = 80.0m,
            RequiredWorkers = 1,
            TotalAmount = 260000m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.JobOrders.Add(order);
        await context.SaveChangesAsync();

        var workerPhone = "092" + Random.Shared.Next(1000000, 9999999);
        var workerNationalId = "079" + Random.Shared.Next(100000000, 999999999);
        var worker = new Worker
        {
            PhoneNumber = workerPhone,
            NationalId = workerNationalId,
            FullName = "Worker Test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Workers.Add(worker);
        await context.SaveChangesAsync();

        var slot = new BookingSlot
        {
            WorkerId = worker.WorkerId,
            SlotDate = order.ScheduledDate,
            ShiftCode = order.ShiftCode,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(12, 0),
            SlotSource = "MANUAL",
            SlotStatus = "LOCKED",
            UpdatedAt = DateTime.UtcNow
        };
        context.BookingSlots.Add(slot);
        await context.SaveChangesAsync();

        // 1. Create first active assignment
        var assign1 = new JobAssignment
        {
            OrderId = order.OrderId,
            CustomerId = customer.CustomerId,
            WorkerId = worker.WorkerId,
            SlotId = slot.SlotId,
            ServiceTier = ServiceTier.Economy,
            AssignmentSeq = 1,
            DispatchRadiusKm = 5,
            GrossAmount = 260000m,
            CommissionRate = 0.200m,
            PayoutAmount = 208000m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        assign1.TransitionTo(JobAssignmentStatus.Assigned);
        context.JobAssignments.Add(assign1);
        await context.SaveChangesAsync();

        // 2. Attempting a second active assignment on the same slot must fail
        var assign2 = new JobAssignment
        {
            OrderId = order.OrderId,
            CustomerId = customer.CustomerId,
            WorkerId = worker.WorkerId,
            SlotId = slot.SlotId,
            ServiceTier = ServiceTier.Economy,
            AssignmentSeq = 2,
            DispatchRadiusKm = 5,
            GrossAmount = 260000m,
            CommissionRate = 0.200m,
            PayoutAmount = 208000m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        assign2.TransitionTo(JobAssignmentStatus.Assigned);
        context.JobAssignments.Add(assign2);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.NotNull(ex.InnerException);
        Assert.Contains("IX_JOB_ASSIGNMENT_slot_id", ex.InnerException.Message);

        // 3. Mark the first assignment as CANCELLED_BY_WORKER
        context.Entry(assign2).State = EntityState.Detached;
        assign1.TransitionTo(JobAssignmentStatus.CancelledByWorker);
        await context.SaveChangesAsync();

        // 4. Now a new active assignment on that same slot succeeds!
        var assign3 = new JobAssignment
        {
            OrderId = order.OrderId,
            CustomerId = customer.CustomerId,
            WorkerId = worker.WorkerId,
            SlotId = slot.SlotId,
            ServiceTier = ServiceTier.Economy,
            AssignmentSeq = 2,
            DispatchRadiusKm = 5,
            GrossAmount = 260000m,
            CommissionRate = 0.200m,
            PayoutAmount = 208000m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        assign3.TransitionTo(JobAssignmentStatus.Assigned);
        context.JobAssignments.Add(assign3);
        await context.SaveChangesAsync();

        Assert.True(assign3.AssignmentId > 0);
    }

    [Fact]
    public async Task Worker_STI_Check_Constraint_Rejects_Invalid_Row()
    {
        if (!IsSqlServerAvailable()) return;

        using var context = CreateContext();

        // Freelancer with an agency_id must be rejected
        var invalidSql = @"
INSERT INTO [WORKER] (phone_number, national_id, agency_id, full_name, worker_type, is_super_freelancer, kyc_status, rating_avg, completed_jobs, work_status, created_at, updated_at)
VALUES ('0999999001', '079999990001', 99999, N'Invalid Worker', 'FREELANCER', 0, 'PENDING', 0.0, 0, 'IDLE', SYSUTCDATETIME(), SYSUTCDATETIME());";

        var ex = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(invalidSql));
        Assert.Contains("CK_WORKER_type_agency", ex.Message);
    }

    [Fact]
    public async Task PaymentTransaction_Check_Constraint_Rejects_Invalid_Row()
    {
        if (!IsSqlServerAvailable()) return;

        using var context = CreateContext();

        // Purpose ORDER with order_id NULL must be rejected
        var invalidSql = @"
INSERT INTO [PAYMENT_TRANSACTION] (gateway_txn_ref, order_id, purpose, gateway, amount, txn_status, created_at)
VALUES ('INVALID_TXN_REF_01', NULL, 'ORDER', 'MOMO', 100000.00, 'PENDING', SYSUTCDATETIME());";

        var ex = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(invalidSql));
        Assert.Contains("CK_PAYMENT_TRANSACTION_purpose", ex.Message);
    }

    [Fact]
    public async Task PayoutItem_Check_Constraint_Rejects_Invalid_Row()
    {
        if (!IsSqlServerAvailable()) return;

        using var context = CreateContext();

        // Payee FREELANCER with worker_id NULL must be rejected
        var invalidSql = @"
INSERT INTO [PAYOUT_ITEM] (batch_id, worker_id, agency_id, payee_type, job_count, gross_amount, commission_amount, penalty_amount, net_amount, bank_account_no, item_status)
VALUES (1, NULL, NULL, 'FREELANCER', 1, 100000, 20000, 0, 80000, '0123456789', 'PENDING');";

        var ex = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(invalidSql));
        Assert.Contains("CK_PAYOUT_ITEM_payee", ex.Message);
    }

    [Fact]
    public async Task DateTime_Utc_Converter_Enforces_Utc_Kind()
    {
        if (!IsSqlServerAvailable()) return;

        using var context = CreateContext();

        // Non-UTC (Unspecified or Local) must throw InvalidOperationException
        var nonUtcCustomer = new Customer
        {
            PhoneNumber = "094" + Random.Shared.Next(1000000, 9999999),
            FullName = "Non Utc Test",
            AccountStatus = CustomerAccountStatus.Active,
            CreatedAt = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Unspecified), // NOT Utc!
            UpdatedAt = DateTime.UtcNow
        };
        context.Customers.Add(nonUtcCustomer);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.NotNull(ex.InnerException);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("DateTime must have Kind=Utc", ex.InnerException.Message);

        context.Entry(nonUtcCustomer).State = EntityState.Detached;

        // UTC must succeed and read back as DateTimeKind.Utc
        var utcCustomer = new Customer
        {
            PhoneNumber = "095" + Random.Shared.Next(1000000, 9999999),
            FullName = "Utc Test",
            AccountStatus = CustomerAccountStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Customers.Add(utcCustomer);
        await context.SaveChangesAsync();

        var reloaded = await context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == utcCustomer.CustomerId);
        Assert.NotNull(reloaded);
        Assert.Equal(DateTimeKind.Utc, reloaded.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, reloaded.UpdatedAt.Kind);
    }
}
