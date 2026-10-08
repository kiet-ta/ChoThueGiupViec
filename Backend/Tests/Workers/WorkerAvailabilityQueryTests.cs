using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Workers;
using CommonService.Infrastructure.Persistence;
using CommonService.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CommonService.Tests.Workers;

public class WorkerAvailabilityQueryTests
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
    public async Task FindAvailableFreelancersAsync_ReturnsMatchingFreelancersWithinRadius()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var geoService = new HaversineGeoService();
        var availabilityQuery = new WorkerAvailabilityQuery(db, geoService);

        var slotDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        string shiftCode = "SHIFT_MORNING";

        // Center location: District 1 HCM (10.7760, 106.7000)
        var center = new GeoPoint(10.7760, 106.7000);

        // Worker 1: ~1.5 km away
        string phone1 = $"098{Random.Shared.Next(1000000, 9999999)}";
        string nid1 = $"012{Random.Shared.Next(100000000, 999999999)}";
        var w1 = Worker.CreateFreelancer(phone1, nid1, "Worker Close");
        w1.SetKycResult(92.00m, "APPROVED");
        w1.CurrentLat = 10.7800m;
        w1.CurrentLng = 106.7100m;
        db.Workers.Add(w1);
        await db.SaveChangesAsync();

        db.BookingSlots.Add(new BookingSlot
        {
            WorkerId = w1.WorkerId,
            SlotDate = slotDate,
            ShiftCode = shiftCode,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(12, 0),
            SlotSource = "FREELANCER",
            SlotStatus = "AVAILABLE",
            UpdatedAt = DateTime.UtcNow
        });

        // Worker 2: ~7.5 km away (District 2 / Binh Thanh boundary)
        string phone2 = $"097{Random.Shared.Next(1000000, 9999999)}";
        string nid2 = $"012{Random.Shared.Next(100000000, 999999999)}";
        var w2 = Worker.CreateFreelancer(phone2, nid2, "Worker Mid");
        w2.SetKycResult(92.00m, "APPROVED");
        w2.CurrentLat = 10.8200m;
        w2.CurrentLng = 106.7500m;
        db.Workers.Add(w2);
        await db.SaveChangesAsync();

        db.BookingSlots.Add(new BookingSlot
        {
            WorkerId = w2.WorkerId,
            SlotDate = slotDate,
            ShiftCode = shiftCode,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(12, 0),
            SlotSource = "FREELANCER",
            SlotStatus = "AVAILABLE",
            UpdatedAt = DateTime.UtcNow
        });

        // Worker 3: Far away (~50 km in Dong Nai)
        string phone3 = $"096{Random.Shared.Next(1000000, 9999999)}";
        string nid3 = $"012{Random.Shared.Next(100000000, 999999999)}";
        var w3 = Worker.CreateFreelancer(phone3, nid3, "Worker Far");
        w3.SetKycResult(92.00m, "APPROVED");
        w3.CurrentLat = 11.1000m;
        w3.CurrentLng = 107.0000m;
        db.Workers.Add(w3);
        await db.SaveChangesAsync();

        db.BookingSlots.Add(new BookingSlot
        {
            WorkerId = w3.WorkerId,
            SlotDate = slotDate,
            ShiftCode = shiftCode,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(12, 0),
            SlotSource = "FREELANCER",
            SlotStatus = "AVAILABLE",
            UpdatedAt = DateTime.UtcNow
        });

        // Query radius 5.0 km
        var res5km = await availabilityQuery.FindAvailableFreelancersAsync(
            new AvailabilityQuery(slotDate, shiftCode, center, RadiusKm: 5.0, Limit: 10),
            CancellationToken.None);

        Assert.Single(res5km);
        Assert.Equal(w1.WorkerId, res5km[0].WorkerId);

        // Query radius 10.0 km
        var res10km = await availabilityQuery.FindAvailableFreelancersAsync(
            new AvailabilityQuery(slotDate, shiftCode, center, RadiusKm: 10.0, Limit: 10),
            CancellationToken.None);

        Assert.Equal(2, res10km.Count);
        Assert.Equal(w1.WorkerId, res10km[0].WorkerId); // Closest first
        Assert.Equal(w2.WorkerId, res10km[1].WorkerId);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task FindAvailableFreelancersAsync_ExcludesAgencyStaffOrUnapprovedOrBusyWorkers()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var geoService = new HaversineGeoService();
        var availabilityQuery = new WorkerAvailabilityQuery(db, geoService);

        var slotDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        string shiftCode = "SHIFT_MORNING";
        var center = new GeoPoint(10.7760, 106.7000);

        // 1. Agency Staff
        string phone1 = $"095{Random.Shared.Next(1000000, 9999999)}";
        string nid1 = $"012{Random.Shared.Next(100000000, 999999999)}";
        var agencyWorker = Worker.CreateAgencyStaff(1, phone1, nid1, "Agency Worker");
        agencyWorker.CurrentLat = 10.7760m;
        agencyWorker.CurrentLng = 106.7000m;
        db.Workers.Add(agencyWorker);

        // 2. Unapproved Freelancer
        string phone2 = $"094{Random.Shared.Next(1000000, 9999999)}";
        string nid2 = $"012{Random.Shared.Next(100000000, 999999999)}";
        var unapprovedWorker = Worker.CreateFreelancer(phone2, nid2, "Unapproved Worker");
        unapprovedWorker.SetKycResult(50.00m, "MANUAL_REVIEW");
        unapprovedWorker.CurrentLat = 10.7760m;
        unapprovedWorker.CurrentLng = 106.7000m;
        db.Workers.Add(unapprovedWorker);

        // 3. Busy Freelancer
        string phone3 = $"093{Random.Shared.Next(1000000, 9999999)}";
        string nid3 = $"012{Random.Shared.Next(100000000, 999999999)}";
        var busyWorker = Worker.CreateFreelancer(phone3, nid3, "Busy Worker");
        busyWorker.SetKycResult(92.00m, "APPROVED");
        busyWorker.TransitionTo(WorkStatus.Busy);
        busyWorker.CurrentLat = 10.7760m;
        busyWorker.CurrentLng = 106.7000m;
        db.Workers.Add(busyWorker);

        await db.SaveChangesAsync();

        foreach (var w in new[] { agencyWorker, unapprovedWorker, busyWorker })
        {
            db.BookingSlots.Add(new BookingSlot
            {
                WorkerId = w.WorkerId,
                SlotDate = slotDate,
                ShiftCode = shiftCode,
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(12, 0),
                SlotSource = "FREELANCER",
                SlotStatus = "AVAILABLE",
                UpdatedAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();

        // Act
        var results = await availabilityQuery.FindAvailableFreelancersAsync(
            new AvailabilityQuery(slotDate, shiftCode, center, RadiusKm: 10.0, Limit: 10),
            CancellationToken.None);

        // Assert
        Assert.Empty(results);

        await transaction.RollbackAsync();
    }
}
