using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Workers;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace CommonService.Tests.Workers;

public class JobPhotoTests
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

    private static string Digits(int n) => string.Concat(Enumerable.Range(0, n).Select(_ => Random.Shared.Next(0, 10)));

    /// <summary>A freelancer with an assignment of a real order on a real slot (the foreign keys of the schema hold), <c>CHECKED_IN</c> unless asked otherwise.</summary>
    private static async Task<(Worker worker, JobAssignment assignment)> SeedWorkerAndAssignmentAsync(AppDbContext db, bool checkedIn = true)
    {
        var now = DateTime.UtcNow;
        var worker = Worker.CreateFreelancer("09" + Digits(8), Digits(12), "Photo Worker");
        worker.CreatedAt = now;
        worker.UpdatedAt = now;
        db.Workers.Add(worker);
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

        var day = DateOnly.FromDateTime(now).AddDays(1);
        var order = new JobOrder
        {
            OrderCode = "T" + Digits(12),
            CustomerId = customer.CustomerId,
            AddressId = address.AddressId,
            ServiceTier = ServiceTier.Economy,
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
            WorkerId = worker.WorkerId,
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

        var assignment = new JobAssignment
        {
            OrderId = order.OrderId,
            CustomerId = customer.CustomerId,
            WorkerId = worker.WorkerId,
            SlotId = slot.SlotId,
            ServiceTier = ServiceTier.Economy,
            AssignmentSeq = 1,
            DispatchRadiusKm = 5,
            GrossAmount = 260000m,
            CommissionRate = 0.20m,
            PayoutAmount = 208000m,
            CreatedAt = now,
            UpdatedAt = now,
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        if (checkedIn)
        {
            assignment.TransitionTo(JobAssignmentStatus.CheckedIn);
        }

        db.JobAssignments.Add(assignment);
        await db.SaveChangesAsync();

        return (worker, assignment);
    }

    [Fact]
    public async Task UploadAsync_SharpBeforePhoto_AcceptedAndSavedInDb()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var (worker, assignment) = await SeedWorkerAndAssignmentAsync(db);

        var repository = new EfWorkerRepository(db);
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        var imageQuality = new ImageQualityService(rules);
        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };

        var handler = new JobPhotoService(repository, imageQuality, currentUser, rules, new FakeClock(), new FakeFileStorage(), new JobPhotoUploadPolicy(true));
        var req = new UploadJobPhotoRequest(PhotoPhase: "BEFORE", AngleNo: 1, PhotoUrl: "https://storage.local/jobs/angle1_sharp.jpg");

        // Act
        var result = await handler.UploadAsync(assignment.AssignmentId, req, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("BEFORE", result.Data.PhotoPhase);
        Assert.Equal((byte)1, result.Data.AngleNo);
        Assert.True(result.Data.IsAccepted);

        var photoInDb = await db.JobPhotos.FirstOrDefaultAsync(p => p.AssignmentId == assignment.AssignmentId && p.AngleNo == 1);
        Assert.NotNull(photoInDb);
        Assert.True(photoInDb.IsAccepted);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task UploadAsync_FirstAcceptedBeforePhoto_StartsTheWork()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();
        var (worker, assignment) = await SeedWorkerAndAssignmentAsync(db);
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        var handler = new JobPhotoService(
            new EfWorkerRepository(db), new ImageQualityService(rules), new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker }, rules, new FakeClock(), new FakeFileStorage(), new JobPhotoUploadPolicy(true));

        // A blurry BEFORE photo is refused and does not start anything.
        await Assert.ThrowsAsync<ValidationException>(() => handler.UploadAsync(assignment.AssignmentId, new UploadJobPhotoRequest("BEFORE", 1, "https://storage.local/jobs/a1_blur.jpg"), CancellationToken.None));
        Assert.Equal(JobAssignmentStatus.CheckedIn, (await db.JobAssignments.AsNoTracking().SingleAsync(a => a.AssignmentId == assignment.AssignmentId)).AssignmentStatus);

        // The first accepted BEFORE photo starts it (any normal URL is a sharp photo).
        await handler.UploadAsync(assignment.AssignmentId, new UploadJobPhotoRequest("BEFORE", 1, "https://storage.local/jobs/a1.jpg"), CancellationToken.None);
        Assert.Equal(JobAssignmentStatus.InProgress, (await db.JobAssignments.AsNoTracking().SingleAsync(a => a.AssignmentId == assignment.AssignmentId)).AssignmentStatus);

        // Further photos keep it running.
        await handler.UploadAsync(assignment.AssignmentId, new UploadJobPhotoRequest("BEFORE", 2, "https://storage.local/jobs/a2.jpg"), CancellationToken.None);
        Assert.Equal(JobAssignmentStatus.InProgress, (await db.JobAssignments.AsNoTracking().SingleAsync(a => a.AssignmentId == assignment.AssignmentId)).AssignmentStatus);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task UploadAsync_BeforeCheckIn_IsRefused()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();
        var (worker, assignment) = await SeedWorkerAndAssignmentAsync(db, checkedIn: false);
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        var handler = new JobPhotoService(
            new EfWorkerRepository(db), new ImageQualityService(rules), new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker }, rules, new FakeClock(), new FakeFileStorage(), new JobPhotoUploadPolicy(true));

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => handler.UploadAsync(assignment.AssignmentId, new UploadJobPhotoRequest("BEFORE", 1, "https://storage.local/jobs/a1.jpg"), CancellationToken.None));
        Assert.Equal(0, await db.JobPhotos.CountAsync(p => p.AssignmentId == assignment.AssignmentId));

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task UploadAsync_BlurryPhoto_SavedAsNotAcceptedAndThrowsValidationException()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var (worker, assignment) = await SeedWorkerAndAssignmentAsync(db);

        var repository = new EfWorkerRepository(db);
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        var imageQuality = new ImageQualityService(rules);
        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };

        var handler = new JobPhotoService(repository, imageQuality, currentUser, rules, new FakeClock(), new FakeFileStorage(), new JobPhotoUploadPolicy(true));
        var req = new UploadJobPhotoRequest(PhotoPhase: "BEFORE", AngleNo: 1, PhotoUrl: "https://storage.local/jobs/angle1_blur.jpg");

        // Act & Assert
        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            handler.UploadAsync(assignment.AssignmentId, req, CancellationToken.None));
        Assert.StartsWith("Ảnh bị mờ", error.Errors["volScore"].Single());

        // Rejected photo is saved in DB for audit trail
        var photoInDb = await db.JobPhotos.FirstOrDefaultAsync(p => p.AssignmentId == assignment.AssignmentId && p.AngleNo == 1);
        Assert.NotNull(photoInDb);
        Assert.False(photoInDb.IsAccepted);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task UploadAsync_AfterPhoto_UnmatchedBeforeAngle_ThrowsValidationException()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var (worker, assignment) = await SeedWorkerAndAssignmentAsync(db);

        var repository = new EfWorkerRepository(db);
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        var imageQuality = new ImageQualityService(rules);
        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };

        var handler = new JobPhotoService(repository, imageQuality, currentUser, rules, new FakeClock(), new FakeFileStorage(), new JobPhotoUploadPolicy(true));

        // Upload BEFORE angle 1
        await handler.UploadAsync(assignment.AssignmentId, new UploadJobPhotoRequest("BEFORE", 1, "https://storage.local/jobs/angle1.jpg"), CancellationToken.None);

        // Upload AFTER angle 2 (does not match BEFORE angle 1)
        var afterReq = new UploadJobPhotoRequest(PhotoPhase: "AFTER", AngleNo: 2, PhotoUrl: "https://storage.local/jobs/angle2_after.jpg");

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.UploadAsync(assignment.AssignmentId, afterReq, CancellationToken.None));

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task UploadAsync_AfterPhoto_MatchingBeforeAngle_Succeeds()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var (worker, assignment) = await SeedWorkerAndAssignmentAsync(db);

        var repository = new EfWorkerRepository(db);
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        var imageQuality = new ImageQualityService(rules);
        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };

        var handler = new JobPhotoService(repository, imageQuality, currentUser, rules, new FakeClock(), new FakeFileStorage(), new JobPhotoUploadPolicy(true));

        // Upload BEFORE angle 1
        await handler.UploadAsync(assignment.AssignmentId, new UploadJobPhotoRequest("BEFORE", 1, "https://storage.local/jobs/angle1.jpg"), CancellationToken.None);

        // Upload AFTER angle 1
        var afterReq = new UploadJobPhotoRequest(PhotoPhase: "AFTER", AngleNo: 1, PhotoUrl: "https://storage.local/jobs/angle1_after.jpg");
        var result = await handler.UploadAsync(assignment.AssignmentId, afterReq, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("AFTER", result.Data.PhotoPhase);
        Assert.Equal((byte)1, result.Data.AngleNo);
        Assert.True(result.Data.IsAccepted);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task UploadAsync_ExceedsMaxPhotosInPhase_ThrowsBusinessRuleViolationException()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var (worker, assignment) = await SeedWorkerAndAssignmentAsync(db);

        var repository = new EfWorkerRepository(db);
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        var imageQuality = new ImageQualityService(rules);
        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };

        var handler = new JobPhotoService(repository, imageQuality, currentUser, rules, new FakeClock(), new FakeFileStorage(), new JobPhotoUploadPolicy(true));

        // Upload 5 accepted BEFORE photos
        for (byte angle = 1; angle <= 5; angle++)
        {
            await handler.UploadAsync(assignment.AssignmentId, new UploadJobPhotoRequest("BEFORE", angle, $"https://storage.local/jobs/angle{angle}.jpg"), CancellationToken.None);
        }

        // Try 6th photo in BEFORE phase (using angle 1)
        var req6 = new UploadJobPhotoRequest("BEFORE", 1, "https://storage.local/jobs/angle1_extra.jpg");

        // Act & Assert
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.UploadAsync(assignment.AssignmentId, req6, CancellationToken.None));

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task GetPhotosAsync_ReturnsAllUploadedPhotos()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var (worker, assignment) = await SeedWorkerAndAssignmentAsync(db);

        var repository = new EfWorkerRepository(db);
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        var imageQuality = new ImageQualityService(rules);
        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };

        var uploadHandler = new JobPhotoService(repository, imageQuality, currentUser, rules, new FakeClock(), new FakeFileStorage(), new JobPhotoUploadPolicy(true));
        var queryHandler = new JobPhotoService(repository, imageQuality, currentUser, rules, new FakeClock(), new FakeFileStorage(), new JobPhotoUploadPolicy(true));

        await uploadHandler.UploadAsync(assignment.AssignmentId, new UploadJobPhotoRequest("BEFORE", 1, "https://storage.local/jobs/before1.jpg"), CancellationToken.None);
        await uploadHandler.UploadAsync(assignment.AssignmentId, new UploadJobPhotoRequest("BEFORE", 2, "https://storage.local/jobs/before2.jpg"), CancellationToken.None);

        // Act
        var result = await queryHandler.GetPhotosAsync(assignment.AssignmentId, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.Count);

        await transaction.RollbackAsync();
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId { get; set; }
        public UserRole? Role { get; set; } = UserRole.Worker;
        public bool IsAuthenticated => UserId.HasValue;
    }
}

/// <summary>The deterministic stand-in of the photo quality check (decisions D18): no database needed.</summary>
public class JobPhotoStandInTests
{
    private static async Task<ImageQualityResult> AssessAsync(string url)
    {
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        using var stream = new MemoryStream(JobPhotoStandIn.BytesFor(url));
        return await new ImageQualityService(rules).AssessAsync(stream);
    }

    [Theory]
    [InlineData("https://x/1.jpg")]
    [InlineData("https://storage.giupviec.local/jobs/0123456789abcdef0123456789abcdef.jpg")]
    [InlineData("https://storage.local/jobs/angle1_sharp.jpg")]
    public async Task A_normal_url_is_always_a_sharp_photo(string url)
    {
        var result = await AssessAsync(url);

        Assert.True(result.IsAccepted);
        Assert.True(result.VolScore >= 100.0);
    }

    [Theory]
    [InlineData("https://storage.local/jobs/angle1_blur.jpg")]
    [InlineData("https://storage.local/jobs/LOW_QUALITY_1.jpg")]
    [InlineData("https://storage.local/jobs/vol_45.jpg")]
    public async Task A_url_with_a_blur_marker_is_a_blurry_photo(string url)
    {
        var result = await AssessAsync(url);

        Assert.False(result.IsAccepted);
        Assert.True(result.VolScore < 100.0);
    }
}
