using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Tests.Persistence;

/// <summary>
/// Real rows for the SQL Server integration tests: the rows a test points at through a foreign key (customer, order, slot, agency, admin)
/// exist, so the tests no longer invent ids such as <c>CustomerId = 200</c>. Every method only adds and saves; the test rolls its transaction back.
/// </summary>
internal static class SqlSeed
{
    private static string Digits(int n) => string.Concat(Enumerable.Range(0, n).Select(_ => Random.Shared.Next(0, 10)));

    public static async Task<int> AgencyAsync(AppDbContext db)
    {
        var agency = new PartnerAgency
        {
            TaxCode = Digits(10),
            LegalName = "Test Agency " + Digits(4),
            LegalRepresentative = "Rep",
            ContactPhone = "09" + Digits(8),
            ContactEmail = "a" + Digits(8) + "@example.test",
            EscrowDepositBalance = 5_000_000m,
            SlaScore = 100m,
            WorkerQuota = 3,
            BankAccountNo = "123456",
            BankName = "VCB",
            AgencyStatus = "ACTIVE",
            CreatedAt = DateTime.UtcNow,
        };
        db.PartnerAgencies.Add(agency);
        await db.SaveChangesAsync();
        return agency.AgencyId;
    }

    public static async Task<int> AdminAsync(AppDbContext db)
    {
        var admin = new AdminAccount
        {
            Email = "t" + Digits(8) + "@example.test",
            FullName = "Test Admin",
            PasswordHash = "x",
            AdminRole = "SUPER_ADMIN",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.Admins.Add(admin);
        await db.SaveChangesAsync();
        return admin.AdminId;
    }

    /// <summary>A customer with an address, an order of it and a booked slot of <paramref name="workerId"/>: the ids a job assignment points at.</summary>
    public static async Task<(long OrderId, int CustomerId, int SlotId)> OrderWithSlotAsync(
        AppDbContext db, int workerId, ServiceTier tier = ServiceTier.Economy)
    {
        var now = DateTime.UtcNow;
        var customer = new Customer { PhoneNumber = "09" + Digits(8), FullName = "C", AccountStatus = CustomerAccountStatus.Active, CreatedAt = now, UpdatedAt = now };
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
        await db.SaveChangesAsync();

        var day = DateOnly.FromDateTime(now).AddDays(Random.Shared.Next(1, 3000));
        var order = new JobOrder
        {
            OrderCode = "T" + Digits(12),
            CustomerId = customer.CustomerId,
            AddressId = address.AddressId,
            ServiceTier = tier,
            ScheduledDate = day,
            ShiftCode = "SHIFT_MORNING",
            AreaSnapshotM2 = 40m,
            RequiredWorkers = 1,
            TotalAmount = 260000m,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var slot = new BookingSlot
        {
            WorkerId = workerId,
            SlotDate = day,
            ShiftCode = "SHIFT_MORNING",
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(12, 0),
            SlotSource = "FREELANCER",
            SlotStatus = "BOOKED",
            UpdatedAt = now,
        };
        db.JobOrders.Add(order);
        db.BookingSlots.Add(slot);
        await db.SaveChangesAsync();
        return (order.OrderId, customer.CustomerId, slot.SlotId);
    }
}
