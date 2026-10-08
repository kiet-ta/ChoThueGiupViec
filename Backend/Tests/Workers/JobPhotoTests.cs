using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Commands;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Queries;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
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

    private static async Task<(Worker worker, JobAssignment assignment)> SeedWorkerAndAssignmentAsync(AppDbContext db)
    {
        string phone = $"099{Random.Shared.Next(1000000, 9999999)}";
        string nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";

        var worker = Worker.CreateFreelancer(phone, nationalId, "Photo Worker");
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var assignment = new JobAssignment
        {
            OrderId = 10001,
            CustomerId = 2001,
            WorkerId = worker.WorkerId,
            SlotId = 501,
            ServiceTier = ServiceTier.Economy,
            AssignmentSeq = 1,
            GrossAmount = 260000m,
            CommissionRate = 0.20m,
            PayoutAmount = 208000m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.JobAssignments.Add(assignment);
        await db.SaveChangesAsync();

        return (worker, assignment);
    }

    [Fact]
    public async Task UploadJobPhotoCommand_SharpBeforePhoto_AcceptedAndSavedInDb()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var (worker, assignment) = await SeedWorkerAndAssignmentAsync(db);

        var repository = new EfWorkerRepository(db);
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        var imageQuality = new ImageQualityService(rules);
        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };

        var handler = new UploadJobPhotoCommandHandler(repository, imageQuality, currentUser, rules);
        var req = new UploadJobPhotoRequest(PhotoPhase: "BEFORE", AngleNo: 1, PhotoUrl: "https://storage.local/jobs/angle1_sharp.jpg");

        // Act
        var result = await handler.Handle(new UploadJobPhotoCommand(assignment.AssignmentId, req), CancellationToken.None);

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
    public async Task UploadJobPhotoCommand_BlurryPhoto_SavedAsNotAcceptedAndThrowsValidationException()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var (worker, assignment) = await SeedWorkerAndAssignmentAsync(db);

        var repository = new EfWorkerRepository(db);
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        var imageQuality = new ImageQualityService(rules);
        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };

        var handler = new UploadJobPhotoCommandHandler(repository, imageQuality, currentUser, rules);
        var req = new UploadJobPhotoRequest(PhotoPhase: "BEFORE", AngleNo: 1, PhotoUrl: "https://storage.local/jobs/angle1_blur.jpg");

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new UploadJobPhotoCommand(assignment.AssignmentId, req), CancellationToken.None));

        // Rejected photo is saved in DB for audit trail
        var photoInDb = await db.JobPhotos.FirstOrDefaultAsync(p => p.AssignmentId == assignment.AssignmentId && p.AngleNo == 1);
        Assert.NotNull(photoInDb);
        Assert.False(photoInDb.IsAccepted);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task UploadJobPhotoCommand_AfterPhoto_UnmatchedBeforeAngle_ThrowsValidationException()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var (worker, assignment) = await SeedWorkerAndAssignmentAsync(db);

        var repository = new EfWorkerRepository(db);
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        var imageQuality = new ImageQualityService(rules);
        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };

        var handler = new UploadJobPhotoCommandHandler(repository, imageQuality, currentUser, rules);

        // Upload BEFORE angle 1
        await handler.Handle(new UploadJobPhotoCommand(assignment.AssignmentId, new UploadJobPhotoRequest("BEFORE", 1, "https://storage.local/jobs/angle1.jpg")), CancellationToken.None);

        // Upload AFTER angle 2 (does not match BEFORE angle 1)
        var afterReq = new UploadJobPhotoRequest(PhotoPhase: "AFTER", AngleNo: 2, PhotoUrl: "https://storage.local/jobs/angle2_after.jpg");

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new UploadJobPhotoCommand(assignment.AssignmentId, afterReq), CancellationToken.None));

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task UploadJobPhotoCommand_AfterPhoto_MatchingBeforeAngle_Succeeds()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var (worker, assignment) = await SeedWorkerAndAssignmentAsync(db);

        var repository = new EfWorkerRepository(db);
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        var imageQuality = new ImageQualityService(rules);
        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };

        var handler = new UploadJobPhotoCommandHandler(repository, imageQuality, currentUser, rules);

        // Upload BEFORE angle 1
        await handler.Handle(new UploadJobPhotoCommand(assignment.AssignmentId, new UploadJobPhotoRequest("BEFORE", 1, "https://storage.local/jobs/angle1.jpg")), CancellationToken.None);

        // Upload AFTER angle 1
        var afterReq = new UploadJobPhotoRequest(PhotoPhase: "AFTER", AngleNo: 1, PhotoUrl: "https://storage.local/jobs/angle1_after.jpg");
        var result = await handler.Handle(new UploadJobPhotoCommand(assignment.AssignmentId, afterReq), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("AFTER", result.Data.PhotoPhase);
        Assert.Equal((byte)1, result.Data.AngleNo);
        Assert.True(result.Data.IsAccepted);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task UploadJobPhotoCommand_ExceedsMaxPhotosInPhase_ThrowsBusinessRuleViolationException()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var (worker, assignment) = await SeedWorkerAndAssignmentAsync(db);

        var repository = new EfWorkerRepository(db);
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        var imageQuality = new ImageQualityService(rules);
        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };

        var handler = new UploadJobPhotoCommandHandler(repository, imageQuality, currentUser, rules);

        // Upload 5 accepted BEFORE photos
        for (byte angle = 1; angle <= 5; angle++)
        {
            await handler.Handle(new UploadJobPhotoCommand(assignment.AssignmentId, new UploadJobPhotoRequest("BEFORE", angle, $"https://storage.local/jobs/angle{angle}.jpg")), CancellationToken.None);
        }

        // Try 6th photo in BEFORE phase (using angle 1)
        var req6 = new UploadJobPhotoRequest("BEFORE", 1, "https://storage.local/jobs/angle1_extra.jpg");

        // Act & Assert
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.Handle(new UploadJobPhotoCommand(assignment.AssignmentId, req6), CancellationToken.None));

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task GetJobPhotosQuery_ReturnsAllUploadedPhotos()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var (worker, assignment) = await SeedWorkerAndAssignmentAsync(db);

        var repository = new EfWorkerRepository(db);
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        var imageQuality = new ImageQualityService(rules);
        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };

        var uploadHandler = new UploadJobPhotoCommandHandler(repository, imageQuality, currentUser, rules);
        var queryHandler = new GetJobPhotosQueryHandler(repository);

        await uploadHandler.Handle(new UploadJobPhotoCommand(assignment.AssignmentId, new UploadJobPhotoRequest("BEFORE", 1, "https://storage.local/jobs/before1.jpg")), CancellationToken.None);
        await uploadHandler.Handle(new UploadJobPhotoCommand(assignment.AssignmentId, new UploadJobPhotoRequest("BEFORE", 2, "https://storage.local/jobs/before2.jpg")), CancellationToken.None);

        // Act
        var result = await queryHandler.Handle(new GetJobPhotosQuery(assignment.AssignmentId), CancellationToken.None);

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
