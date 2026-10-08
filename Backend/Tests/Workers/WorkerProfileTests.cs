using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Identity.Services;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace CommonService.Tests.Workers;

public class WorkerProfileTests
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

    private readonly IConfiguration _config;
    private readonly TokenService _tokenService;

    public WorkerProfileTests()
    {
        var configValues = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "Test_Hmac_Secret_Key_At_Least_32_Chars_Long!",
            ["Otp:HmacSecret"] = "Test_Hmac_Secret_Key_At_Least_32_Chars_Long!"
        };

        _config = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        var rulesOptions = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        _tokenService = new TokenService(rulesOptions, _config);
    }

    [Fact]
    public async Task RegisterWorker_ValidToken_CreatesWorkerProfile()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var repository = new EfWorkerRepository(db);

        string phone = $"091{Random.Shared.Next(1000000, 9999999)}";
        string nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";
        var (regToken, _) = _tokenService.GenerateWorkerRegistrationToken(phone);

        var handler = new RegisterWorkerCommandHandler(repository, _config);
        var request = new RegisterWorkerRequest(
            RegistrationToken: regToken,
            FullName: "Nguyen Van A",
            NationalId: nationalId
        );

        // Act
        var response = await handler.Handle(new RegisterWorkerCommand(request), CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal("Nguyen Van A", response.Data.FullName);
        Assert.Equal(nationalId, response.Data.NationalId);
        Assert.Equal(phone, response.Data.PhoneNumber);
        Assert.Equal("FREELANCER", response.Data.WorkerType);
        Assert.Equal("PENDING", response.Data.WorkStatus);
        Assert.Equal("PENDING", response.Data.KycStatus);

        var savedInDb = await db.Workers.FirstOrDefaultAsync(w => w.PhoneNumber == phone);
        Assert.NotNull(savedInDb);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task RegisterWorker_InvalidToken_ThrowsValidationException()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        var repository = new EfWorkerRepository(db);

        var handler = new RegisterWorkerCommandHandler(repository, _config);
        var request = new RegisterWorkerRequest(
            RegistrationToken: "invalid-token-string",
            FullName: "Nguyen Van B",
            NationalId: "012345678902"
        );

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new RegisterWorkerCommand(request), CancellationToken.None));
    }

    [Fact]
    public async Task RegisterWorker_DuplicateNationalId_ThrowsBusinessRuleViolation()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var repository = new EfWorkerRepository(db);

        string phone1 = $"098{Random.Shared.Next(1000000, 9999999)}";
        string phone2 = $"091{Random.Shared.Next(1000000, 9999999)}";
        string nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";

        var existing = Worker.CreateFreelancer(phone1, nationalId, "Existing Worker");
        db.Workers.Add(existing);
        await db.SaveChangesAsync();

        var (regToken, _) = _tokenService.GenerateWorkerRegistrationToken(phone2);

        var handler = new RegisterWorkerCommandHandler(repository, _config);
        var request = new RegisterWorkerRequest(
            RegistrationToken: regToken,
            FullName: "Nguyen Van C",
            NationalId: nationalId
        );

        // Act & Assert
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            handler.Handle(new RegisterWorkerCommand(request), CancellationToken.None));

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task GetWorkerProfileQuery_ReturnsProfileOfCurrentUser()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var repository = new EfWorkerRepository(db);

        string phone = $"092{Random.Shared.Next(1000000, 9999999)}";
        string nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";

        var worker = Worker.CreateFreelancer(phone, nationalId, "Worker Two");
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var fakeCurrentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };
        var handler = new GetWorkerProfileQueryHandler(repository, fakeCurrentUser);

        // Act
        var result = await handler.Handle(new GetWorkerProfileQuery(), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(worker.WorkerId, result.Data.WorkerId);
        Assert.Equal("Worker Two", result.Data.FullName);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task UpdateWorkerProfileCommand_UpdatesFullName()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var repository = new EfWorkerRepository(db);

        string phone = $"093{Random.Shared.Next(1000000, 9999999)}";
        string nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";

        var worker = Worker.CreateFreelancer(phone, nationalId, "Old Name");
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var fakeCurrentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };
        var handler = new UpdateWorkerProfileCommandHandler(repository, fakeCurrentUser);
        var updateReq = new UpdateWorkerProfileRequest(FullName: "New Updated Name");

        // Act
        var result = await handler.Handle(new UpdateWorkerProfileCommand(updateReq), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("New Updated Name", result.Data!.FullName);

        var updatedDb = await db.Workers.FindAsync(worker.WorkerId);
        Assert.Equal("New Updated Name", updatedDb!.FullName);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task WorkerProfileQueryPort_GetAndExists_WorkCorrectly()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        string phone = $"094{Random.Shared.Next(1000000, 9999999)}";
        string nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";

        var w1 = Worker.CreateFreelancer(phone, nationalId, "Worker Four");
        db.Workers.Add(w1);
        await db.SaveChangesAsync();

        var port = new WorkerProfileQuery(db);

        // Act
        var summary = await port.GetAsync(w1.WorkerId);
        var exists = await port.ExistsAsync(w1.WorkerId);
        var notExists = await port.ExistsAsync(999999);
        var many = await port.GetManyAsync([w1.WorkerId, 999999]);

        // Assert
        Assert.NotNull(summary);
        Assert.Equal("Worker Four", summary.FullName);
        Assert.True(exists);
        Assert.False(notExists);
        Assert.Single(many);
        Assert.Equal("Worker Four", many[w1.WorkerId].FullName);

        await transaction.RollbackAsync();
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId { get; set; }
        public UserRole? Role { get; set; } = UserRole.Worker;
        public bool IsAuthenticated => UserId.HasValue;
    }
}
