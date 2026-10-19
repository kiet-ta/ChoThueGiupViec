using CommonService.Application.Common.Options;
using CommonService.Application.Features.Workers.Commands;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Queries;
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

public class EkycAuditTests
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
    public async Task GetEkycQueueQuery_FiltersByStatus_PendingApprovedByAdminAndRejected_AndRefusesAnythingElse()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();
        var repository = new EfWorkerRepository(db);

        Worker NewWorker(string name)
        {
            var w = Worker.CreateFreelancer($"095{Random.Shared.Next(1000000, 9999999)}", $"012{Random.Shared.Next(100000000, 999999999)}", name);
            db.Workers.Add(w);
            return w;
        }

        var admin = new AdminAccount
        {
            Email = "ekyc-" + Guid.NewGuid().ToString("N")[..10] + "@example.test",
            FullName = "Ekyc Queue Admin",
            PasswordHash = "x",
            AdminRole = "SUPER_ADMIN",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.Admins.Add(admin);
        await db.SaveChangesAsync();

        var waiting = NewWorker("Queue Waiting");
        waiting.SetKycResult(72.0m, "MANUAL_REVIEW");
        var approved = NewWorker("Queue Approved By Admin");
        approved.SetKycResult(72.0m, "MANUAL_REVIEW");
        approved.ReviewKyc(approved: true, adminId: admin.AdminId);
        var autoApproved = NewWorker("Queue Auto Approved");
        autoApproved.SetKycResult(92.0m, "APPROVED"); // approved by the provider: nobody reviewed it
        var rejected = NewWorker("Queue Rejected");
        rejected.SetKycResult(40.0m, "MANUAL_REVIEW");
        rejected.ReviewKyc(approved: false, adminId: admin.AdminId);
        await db.SaveChangesAsync();

        var handler = new GetEkycQueueQueryHandler(repository, new TestCurrentUser { UserId = 1, Role = UserRole.Admin });
        async Task<List<int>> Ids(string? status) =>
            (await handler.Handle(new GetEkycQueueQuery(1, 200, status!), CancellationToken.None)).Data!.Items.Select(i => i.WorkerId).ToList();

        var pending = await Ids("PENDING");
        Assert.Contains(waiting.WorkerId, pending);
        Assert.DoesNotContain(approved.WorkerId, pending);
        Assert.DoesNotContain(rejected.WorkerId, pending);

        var approvedList = await Ids("approved"); // case-insensitive
        Assert.Contains(approved.WorkerId, approvedList);
        Assert.DoesNotContain(waiting.WorkerId, approvedList);
        Assert.DoesNotContain(autoApproved.WorkerId, approvedList);

        var rejectedList = await Ids("REJECTED");
        Assert.Contains(rejected.WorkerId, rejectedList);
        Assert.DoesNotContain(waiting.WorkerId, rejectedList);

        Assert.Contains(waiting.WorkerId, await Ids(null)); // no status = PENDING

        var error = await Assert.ThrowsAsync<CommonService.Application.Exceptions.ValidationException>(() => Ids("DONE"));
        Assert.Contains("status", error.Errors.Keys);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task GetEkycQueueQuery_ReturnsPagedManualReviewAndAuditItems()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var repository = new EfWorkerRepository(db);

        string phone = $"095{Random.Shared.Next(1000000, 9999999)}";
        string nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";

        var worker = Worker.CreateFreelancer(phone, nationalId, "Audit Queue Test Worker");
        worker.SetKycResult(78.50m, "MANUAL_REVIEW");
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var currentUser = new TestCurrentUser { UserId = 1, Role = UserRole.Admin };
        var queryHandler = new GetEkycQueueQueryHandler(repository, currentUser);

        // Act
        var result = await queryHandler.Handle(new GetEkycQueueQuery(1, 20), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.TotalCount >= 1);

        var item = result.Data.Items.FirstOrDefault(i => i.WorkerId == worker.WorkerId);
        Assert.NotNull(item);
        Assert.Equal("MANUAL_REVIEW", item.KycStatus);
        Assert.Equal(78.50m, item.ConfidenceScore);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task ReviewEkycCommand_Approved_UpdatesStatusAndActivatesWorker()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var repository = new EfWorkerRepository(db);

        string phone = $"094{Random.Shared.Next(1000000, 9999999)}";
        string nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";

        var worker = Worker.CreateFreelancer(phone, nationalId, "Pending Review Worker");
        worker.SetKycResult(80.00m, "MANUAL_REVIEW");
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        int adminId = await CommonService.Tests.Persistence.SqlSeed.AdminAsync(db);
        var currentUser = new TestCurrentUser { UserId = adminId, Role = UserRole.Admin };
        var handler = new ReviewEkycCommandHandler(repository, currentUser);
        var req = new ReviewEkycRequest(Approved: true);

        // Act
        var result = await handler.Handle(new ReviewEkycCommand(worker.WorkerId, req), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("APPROVED", result.Data.KycStatus);

        var updated = await db.Workers.FindAsync(worker.WorkerId);
        Assert.NotNull(updated);
        Assert.Equal("APPROVED", updated.KycStatus);
        Assert.Equal(adminId, updated.KycReviewedBy);
        Assert.Equal(WorkStatus.Idle, updated.WorkStatus);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task ReviewEkycCommand_Rejected_UpdatesStatusAndLocksWorker()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var repository = new EfWorkerRepository(db);

        string phone = $"093{Random.Shared.Next(1000000, 9999999)}";
        string nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";

        var worker = Worker.CreateFreelancer(phone, nationalId, "Fraud Suspect Worker");
        worker.SetKycResult(50.00m, "MANUAL_REVIEW");
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        int adminId = await CommonService.Tests.Persistence.SqlSeed.AdminAsync(db);
        var currentUser = new TestCurrentUser { UserId = adminId, Role = UserRole.Admin };
        var handler = new ReviewEkycCommandHandler(repository, currentUser);
        var req = new ReviewEkycRequest(Approved: false, RejectionReason: "ID photo does not match selfie.");

        // Act
        var result = await handler.Handle(new ReviewEkycCommand(worker.WorkerId, req), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("REJECTED", result.Data.KycStatus);
        Assert.Equal("ID photo does not match selfie.", result.Data.RejectionReason);

        var updated = await db.Workers.FindAsync(worker.WorkerId);
        Assert.NotNull(updated);
        Assert.Equal("REJECTED", updated.KycStatus);
        Assert.Equal(adminId, updated.KycReviewedBy);
        Assert.Equal(WorkStatus.Locked, updated.WorkStatus);

        await transaction.RollbackAsync();
    }

    [Fact]
    public void EkycAuditService_SampleAuditRules_EvaluatesCorrectly()
    {
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules
        {
            Ekyc = new EkycRules
            {
                FullAuditFirstJobs = 5,
                AuditRate = 0.20m
            }
        });

        var auditService = new EkycAuditService(rules);

        var agencyStaff = Worker.CreateAgencyStaff(1, "0911111111", "012345678911", "Agency Staff");
        Assert.False(auditService.ShouldAuditJob(agencyStaff));

        var newFreelancer = Worker.CreateFreelancer("0922222222", "012345678922", "New Freelancer");
        newFreelancer.CompletedJobs = 3;
        Assert.True(auditService.ShouldAuditJob(newFreelancer));

        var seasonedFreelancer = Worker.CreateFreelancer("0933333333", "012345678933", "Seasoned Freelancer");
        seasonedFreelancer.CompletedJobs = 10;
        Assert.True(auditService.ShouldAuditJob(seasonedFreelancer, randomRoll: 0.15));
        Assert.False(auditService.ShouldAuditJob(seasonedFreelancer, randomRoll: 0.25));
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId { get; set; }
        public UserRole? Role { get; set; } = UserRole.Worker;
        public bool IsAuthenticated => UserId.HasValue;
    }
}

