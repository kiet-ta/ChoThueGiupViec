using CommonService.Application.Common.Options;
using CommonService.Application.Features.Workers.Commands;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Queries;
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

public class EkycTests
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
    public async Task SubmitEkycCommand_ConfidenceAbove85_AutoApprovesWorker()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var repository = new EfWorkerRepository(db);

        string phone = $"096{Random.Shared.Next(1000000, 9999999)}";
        string nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";

        var worker = Worker.CreateFreelancer(phone, nationalId, "Worker Ekyc Test");
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var fakeEkyc = new FakeEkycProvider { Confidence = 92.00m, FraudFlag = false };
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };

        var handler = new SubmitEkycCommandHandler(repository, fakeEkyc, currentUser, rules);
        var req = new SubmitEkycRequest(
            FrontCccdUrl: "https://storage.local/ekyc/front.jpg",
            BackCccdUrl: "https://storage.local/ekyc/back.jpg",
            SelfieUrl: "https://storage.local/ekyc/selfie.jpg"
        );

        // Act
        var result = await handler.Handle(new SubmitEkycCommand(req), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("APPROVED", result.Data.KycStatus);
        Assert.True(result.Data.AutoApproved);

        var updatedWorker = await db.Workers.FindAsync(worker.WorkerId);
        Assert.Equal("APPROVED", updatedWorker!.KycStatus);
        Assert.Equal(WorkStatus.Idle, updatedWorker.WorkStatus); // Transitioned to IDLE

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task SubmitEkycCommand_ConfidenceBelow85_RoutesToManualReview()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var repository = new EfWorkerRepository(db);

        string phone = $"097{Random.Shared.Next(1000000, 9999999)}";
        string nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";

        var worker = Worker.CreateFreelancer(phone, nationalId, "Worker Ekyc Low Score");
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        // Fake provider returning low confidence score (78.50%)
        var fakeEkyc = new FakeEkycProvider { Confidence = 78.50m, FraudFlag = false };
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };

        var handler = new SubmitEkycCommandHandler(repository, fakeEkyc, currentUser, rules);
        var req = new SubmitEkycRequest(
            FrontCccdUrl: "https://storage.local/ekyc/front.jpg",
            BackCccdUrl: "https://storage.local/ekyc/back.jpg",
            SelfieUrl: "https://storage.local/ekyc/selfie.jpg"
        );

        // Act
        var result = await handler.Handle(new SubmitEkycCommand(req), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("MANUAL_REVIEW", result.Data.KycStatus);
        Assert.False(result.Data.AutoApproved);

        var updatedWorker = await db.Workers.FindAsync(worker.WorkerId);
        Assert.Equal("MANUAL_REVIEW", updatedWorker!.KycStatus);
        Assert.Equal(WorkStatus.Pending, updatedWorker.WorkStatus); // Remains PENDING

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task GetEkycStatusQuery_ReturnsCurrentEkycStatus()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var repository = new EfWorkerRepository(db);

        string phone = $"099{Random.Shared.Next(1000000, 9999999)}";
        string nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";

        var worker = Worker.CreateFreelancer(phone, nationalId, "Worker Status Query Test");
        worker.SetKycResult(92.00m, "APPROVED");
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };
        var handler = new GetEkycStatusQueryHandler(repository, currentUser);

        // Act
        var result = await handler.Handle(new GetEkycStatusQuery(), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("APPROVED", result.Data.KycStatus);
        Assert.Equal(92.00m, result.Data.ConfidenceScore);

        await transaction.RollbackAsync();
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId { get; set; }
        public UserRole? Role { get; set; } = UserRole.Worker;
        public bool IsAuthenticated => UserId.HasValue;
    }
}
