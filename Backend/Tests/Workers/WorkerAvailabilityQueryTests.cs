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
        return new CommonService.Tests.Persistence.StampingDbContext(options);
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
        w1.WorkLat = 10.7800m;
        w1.WorkLng = 106.7100m;
        w1.WorkRadiusKm = 15;
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
        w2.WorkLat = 10.8200m;
        w2.WorkLng = 106.7500m;
        w2.WorkRadiusKm = 15;
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
        w3.WorkLat = 11.1000m;
        w3.WorkLng = 107.0000m;
        w3.WorkRadiusKm = 15;
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
        var agencyId = await CommonService.Tests.Persistence.SqlSeed.AgencyAsync(db);
        var agencyWorker = Worker.CreateAgencyStaff(agencyId, phone1, nid1, "Agency Worker");
        agencyWorker.WorkLat = 10.7760m;
        agencyWorker.WorkLng = 106.7000m;
        agencyWorker.WorkRadiusKm = 15;
        db.Workers.Add(agencyWorker);

        // 2. Unapproved Freelancer
        string phone2 = $"094{Random.Shared.Next(1000000, 9999999)}";
        string nid2 = $"012{Random.Shared.Next(100000000, 999999999)}";
        var unapprovedWorker = Worker.CreateFreelancer(phone2, nid2, "Unapproved Worker");
        unapprovedWorker.SetKycResult(50.00m, "MANUAL_REVIEW");
        unapprovedWorker.WorkLat = 10.7760m;
        unapprovedWorker.WorkLng = 106.7000m;
        unapprovedWorker.WorkRadiusKm = 15;
        db.Workers.Add(unapprovedWorker);

        // 3. Busy Freelancer
        string phone3 = $"093{Random.Shared.Next(1000000, 9999999)}";
        string nid3 = $"012{Random.Shared.Next(100000000, 999999999)}";
        var busyWorker = Worker.CreateFreelancer(phone3, nid3, "Busy Worker");
        busyWorker.SetKycResult(92.00m, "APPROVED");
        busyWorker.TransitionTo(WorkStatus.Busy);
        busyWorker.WorkLat = 10.7760m;
        busyWorker.WorkLng = 106.7000m;
        busyWorker.WorkRadiusKm = 15;
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

    /// <summary>Decisions D24: the search matches on the registered work area, and a worker is found only inside the search step and their own radius.</summary>
    [Fact]
    public async Task FindAvailableFreelancersAsync_UsesTheRegisteredWorkAreaAndTheWorkersOwnRadius()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var availabilityQuery = new WorkerAvailabilityQuery(db, new HaversineGeoService());
        var slotDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        const string shiftCode = "SHIFT_MORNING";
        var center = new GeoPoint(10.7760, 106.7000);

        async Task<Worker> AddWorkerAsync(string name, decimal? workLat, decimal? workLng, byte? radiusKm, decimal? currentLat)
        {
            var worker = Worker.CreateFreelancer(
                $"098{Random.Shared.Next(1000000, 9999999)}", $"012{Random.Shared.Next(100000000, 999999999)}", name);
            worker.SetKycResult(92.00m, "APPROVED");
            worker.WorkLat = workLat;
            worker.WorkLng = workLng;
            worker.WorkRadiusKm = radiusKm;
            worker.CurrentLat = currentLat;
            worker.CurrentLng = currentLat is null ? null : 106.7000m;
            db.Workers.Add(worker);
            await db.SaveChangesAsync();
            db.BookingSlots.Add(new BookingSlot
            {
                WorkerId = worker.WorkerId,
                SlotDate = slotDate,
                ShiftCode = shiftCode,
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(12, 0),
                SlotSource = "FREELANCER",
                SlotStatus = "AVAILABLE",
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            return worker;
        }

        // About 8 km north of the order (0.072 degree of latitude).
        var farLat = 10.8480m;
        var okWorker = await AddWorkerAsync("Radius 10 km", farLat, 106.7000m, 10, currentLat: null);
        var tooSmall = await AddWorkerAsync("Radius 5 km", farLat, 106.7000m, 5, currentLat: null);
        // Live GPS right next to the order but no registered work area: never found.
        var noArea = await AddWorkerAsync("No work area", null, null, null, currentLat: 10.7761m);

        var found = await availabilityQuery.FindAvailableFreelancersAsync(
            new AvailabilityQuery(slotDate, shiftCode, center, RadiusKm: 10.0, Limit: 10),
            CancellationToken.None);

        // Other freelancers may already be in the database: look only at the three workers of this test.
        Assert.Contains(found, x => x.WorkerId == okWorker.WorkerId);
        Assert.DoesNotContain(found, x => x.WorkerId == tooSmall.WorkerId);
        Assert.DoesNotContain(found, x => x.WorkerId == noArea.WorkerId);

        // The 5 km step does not find the worker with a 10 km radius: they are 8 km away.
        var step5 = await availabilityQuery.FindAvailableFreelancersAsync(
            new AvailabilityQuery(slotDate, shiftCode, center, RadiusKm: 5.0, Limit: 10),
            CancellationToken.None);
        Assert.DoesNotContain(step5, x => x.WorkerId == okWorker.WorkerId);

        await transaction.RollbackAsync();
    }
}
