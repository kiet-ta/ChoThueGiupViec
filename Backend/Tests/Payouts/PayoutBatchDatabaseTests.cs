using CommonService.Application.Features.Payouts;
using CommonService.Application.Features.Payouts.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Modules.Admin;
using CommonService.Infrastructure.Modules.Disputes;
using CommonService.Infrastructure.Modules.Payouts;
using CommonService.Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Tests.Payouts;

/// <summary>
/// BE-M6-04 on the local SQL Server: the real queries, the month boundaries in Asia/Ho_Chi_Minh, the penalty source over resolved disputes,
/// the item locking and the application lock. The months used (2094-2098) are far in the future so no real data falls in them; the
/// clock is fixed in 2099 so they all count as finished. Every test removes what it seeded.
/// </summary>
public class PayoutBatchDatabaseTests
{
    private const string ConnectionString =
        "Server=localhost;Database=ChoThueGiupViec;Integrated Security=True;TrustServerCertificate=True;";

    private static readonly DateTime Now = new(2099, 6, 1, 4, 0, 0, DateTimeKind.Utc);

    internal static bool IsSqlServerAvailable()
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

    internal static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;

    internal class RecordingPublisher : IPublisher
    {
        public List<PayoutBatchClosed> Events { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            if (notification is PayoutBatchClosed e) Events.Add(e);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            Publish((object)notification, cancellationToken);
    }

    /// <summary>Everything one test seeded, so it can be removed.</summary>
    internal class Seed
    {
        public int AdminId { get; set; }
        public int CustomerId { get; set; }
        public int AddressId { get; set; }
        public int AgencyId { get; set; }
        public List<int> WorkerIds { get; } = [];
        public List<long> OrderIds { get; } = [];
        public List<int> SlotIds { get; } = [];
        public List<long> AssignmentIds { get; } = [];
        public List<int> BatchIds { get; } = [];
    }

    internal static async Task<Seed> SeedBaseAsync(bool withAgency = false)
    {
        var seed = new Seed();
        await using var db = new AppDbContext(Options());

        var admin = new AdminAccount
        {
            Email = "pay-" + Guid.NewGuid().ToString("N")[..10] + "@example.test",
            FullName = "Payout Admin",
            PasswordHash = "x",
            AdminRole = "SUPER_ADMIN",
            IsActive = true,
            CreatedAt = Now,
        };
        db.Admins.Add(admin);

        var customer = new Customer
        {
            PhoneNumber = "091" + Random.Shared.Next(1000000, 9999999),
            FullName = "Payout Customer",
            AccountStatus = CustomerAccountStatus.Active,
            CreatedAt = Now,
            UpdatedAt = Now,
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
            CreatedAt = Now,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        seed.AdminId = admin.AdminId;
        seed.CustomerId = customer.CustomerId;
        seed.AddressId = address.AddressId;

        if (withAgency)
        {
            var agency = new PartnerAgency
            {
                TaxCode = "TX" + Guid.NewGuid().ToString("N")[..10],
                LegalName = "Payout Agency Co",
                LegalRepresentative = "Boss",
                ContactPhone = "093" + Random.Shared.Next(1000000, 9999999),
                ContactEmail = "agency-" + Guid.NewGuid().ToString("N")[..8] + "@example.test",
                BankAccountNo = "777888",
                BankName = "VCB",
                AgencyStatus = "ACTIVE",
                CreatedAt = Now,
            };
            db.PartnerAgencies.Add(agency);
            await db.SaveChangesAsync();
            seed.AgencyId = agency.AgencyId;
        }

        return seed;
    }

    internal static async Task<int> AddWorkerAsync(Seed seed, string name, string? bankNo, bool agencyStaff = false)
    {
        await using var db = new AppDbContext(Options());
        var phone = "092" + Random.Shared.Next(1000000, 9999999);
        var national = "079" + Random.Shared.Next(100000000, 999999999);
        var worker = agencyStaff
            ? Worker.CreateAgencyStaff(seed.AgencyId, phone, national, name)
            : Worker.CreateFreelancer(phone, national, name);
        worker.BankAccountNo = bankNo;
        worker.BankName = bankNo is null ? null : "ACB";
        worker.CreatedAt = Now;
        worker.UpdatedAt = Now;
        db.Workers.Add(worker);
        await db.SaveChangesAsync();
        seed.WorkerIds.Add(worker.WorkerId);
        return worker.WorkerId;
    }

    private static int _dayCounter;

    /// <summary>One order, one slot and one assignment; COMPLETED at <paramref name="completedAtUtc"/> or ABSENT with a fee dated <paramref name="completedAtUtc"/>.</summary>
    internal static async Task<long> AddAssignmentAsync(
        Seed seed, int workerId, DateTime completedAtUtc, decimal gross = 260000m, decimal rate = 0.200m, int? agencyId = null, decimal? absenceFee = null)
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
            TotalAmount = gross,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.JobOrders.Add(order);
        await db.SaveChangesAsync();

        var slot = new BookingSlot
        {
            WorkerId = workerId,
            SlotDate = order.ScheduledDate,
            ShiftCode = "SHIFT1",
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(12, 0),
            SlotSource = "MANUAL",
            SlotStatus = "LOCKED",
            UpdatedAt = Now,
        };
        db.BookingSlots.Add(slot);
        await db.SaveChangesAsync();

        var assignment = new JobAssignment
        {
            OrderId = order.OrderId,
            CustomerId = seed.CustomerId,
            WorkerId = workerId,
            AgencyId = agencyId,
            SlotId = slot.SlotId,
            ServiceTier = ServiceTier.Economy,
            AssignmentSeq = 1,
            DispatchRadiusKm = 5,
            GrossAmount = gross,
            CommissionRate = rate,
            PayoutAmount = gross - Math.Round(gross * rate),
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        assignment.TransitionTo(JobAssignmentStatus.CheckedIn);
        if (absenceFee is { } fee)
        {
            assignment.TransitionTo(JobAssignmentStatus.Absent);
            assignment.AbsenceFeeAmount = fee;
            assignment.UpdatedAt = completedAtUtc;
        }
        else
        {
            assignment.TransitionTo(JobAssignmentStatus.InProgress);
            assignment.TransitionTo(JobAssignmentStatus.AwaitingAcceptance);
            assignment.TransitionTo(JobAssignmentStatus.Completed);
            assignment.CompletedAt = completedAtUtc;
        }

        db.JobAssignments.Add(assignment);
        await db.SaveChangesAsync();

        seed.OrderIds.Add(order.OrderId);
        seed.SlotIds.Add(slot.SlotId);
        seed.AssignmentIds.Add(assignment.AssignmentId);
        return assignment.AssignmentId;
    }

    private static async Task AddResolvedDisputeAsync(long orderId, FaultParty fault, decimal compensation, DateTime resolvedAtUtc, string category = "QUALITY")
    {
        await using var db = new AppDbContext(Options());
        db.DisputeTickets.Add(new DisputeTicket
        {
            OrderId = orderId,
            RaisedBy = "CUSTOMER",
            Category = category,
            Description = "Dirty",
            DisputeStatus = "RESOLVED",
            FaultParty = fault,
            CompensationAmount = compensation,
            SlaDueAt = resolvedAtUtc,
            ResolvedAt = resolvedAtUtc,
            CreatedAt = resolvedAtUtc.AddHours(-1),
        });
        await db.SaveChangesAsync();
    }

    internal static async Task CleanAsync(Seed seed, params string[] periods)
    {
        await using var db = new AppDbContext(Options());
        var batchIds = await db.PayoutBatches.Where(b => periods.Contains(b.PeriodMonth)).Select(b => b.BatchId).ToListAsync();
        var batchIdTexts = batchIds.Select(id => id.ToString()).ToList();

        await db.AdminAuditLogs.Where(a => a.EntityType == "PAYOUT_BATCH" && batchIdTexts.Contains(a.EntityId)).ExecuteDeleteAsync();
        await db.JobAssignments.Where(a => seed.AssignmentIds.Contains(a.AssignmentId)).ExecuteUpdateAsync(s => s.SetProperty(a => a.PayoutItemId, (int?)null));
        await db.PayoutItems.Where(i => batchIds.Contains(i.BatchId)).ExecuteDeleteAsync();
        await db.PayoutBatches.Where(b => batchIds.Contains(b.BatchId)).ExecuteDeleteAsync();
        await db.DisputeTickets.Where(d => seed.OrderIds.Contains(d.OrderId)).ExecuteDeleteAsync();
        await db.JobAssignments.Where(a => seed.AssignmentIds.Contains(a.AssignmentId)).ExecuteDeleteAsync();
        await db.BookingSlots.Where(b => seed.SlotIds.Contains(b.SlotId)).ExecuteDeleteAsync();
        await db.JobOrders.Where(o => seed.OrderIds.Contains(o.OrderId)).ExecuteDeleteAsync();
        await db.CustomerAddresses.Where(a => a.AddressId == seed.AddressId).ExecuteDeleteAsync();
        await db.Customers.Where(c => c.CustomerId == seed.CustomerId).ExecuteDeleteAsync();
        await db.Workers.Where(w => seed.WorkerIds.Contains(w.WorkerId)).ExecuteDeleteAsync();
        if (seed.AgencyId != 0) await db.PartnerAgencies.Where(a => a.AgencyId == seed.AgencyId).ExecuteDeleteAsync();
        await db.Admins.Where(a => a.AdminId == seed.AdminId).ExecuteDeleteAsync();
    }

    internal static DateTime Utc(int y, int m, int d, int h = 0, int min = 0, int s = 0) => new(y, m, d, h, min, s, DateTimeKind.Utc);
}
