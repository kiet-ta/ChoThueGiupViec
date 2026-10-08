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
using Xunit;

namespace CommonService.Tests.Workers;

public class BookingSlotTests
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
    public async Task ToggleBookingSlotCommand_CreatesAndTogglesSlotCorrectly()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var repository = new EfWorkerRepository(db);

        string phone = $"098{Random.Shared.Next(1000000, 9999999)}";
        string nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";

        var worker = Worker.CreateFreelancer(phone, nationalId, "Slot Test Worker");
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };
        var toggleHandler = new ToggleBookingSlotCommandHandler(repository, currentUser);

        var slotDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        // 1. Enable Morning Slot
        var req1 = new ToggleBookingSlotRequest(slotDate, "SHIFT_MORNING", IsActive: true);
        var res1 = await toggleHandler.Handle(new ToggleBookingSlotCommand(req1), CancellationToken.None);

        Assert.True(res1.Success);
        Assert.NotNull(res1.Data);
        Assert.Equal("SHIFT_MORNING", res1.Data.ShiftCode);
        Assert.Equal("08:00", res1.Data.StartTime);
        Assert.Equal("12:00", res1.Data.EndTime);
        Assert.True(res1.Data.IsActive);

        // 2. Disable Morning Slot (Toggle off)
        var req2 = new ToggleBookingSlotRequest(slotDate, "SHIFT_MORNING", IsActive: false);
        var res2 = await toggleHandler.Handle(new ToggleBookingSlotCommand(req2), CancellationToken.None);

        Assert.True(res2.Success);
        Assert.NotNull(res2.Data);
        Assert.False(res2.Data.IsActive);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task GetWorkerSlotsQuery_ReturnsSlotsInDateRange()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var repository = new EfWorkerRepository(db);

        string phone = $"097{Random.Shared.Next(1000000, 9999999)}";
        string nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";

        var worker = Worker.CreateFreelancer(phone, nationalId, "Slot Range Test Worker");
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };
        var toggleHandler = new ToggleBookingSlotCommandHandler(repository, currentUser);
        var queryHandler = new GetWorkerSlotsQueryHandler(repository, currentUser);

        var date1 = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var date2 = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));

        await toggleHandler.Handle(new ToggleBookingSlotCommand(new ToggleBookingSlotRequest(date1, "SHIFT_MORNING", true)), CancellationToken.None);
        await toggleHandler.Handle(new ToggleBookingSlotCommand(new ToggleBookingSlotRequest(date2, "SHIFT_AFTERNOON", true)), CancellationToken.None);

        // Act
        var result = await queryHandler.Handle(new GetWorkerSlotsQuery(date1, date2), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.Count);

        Assert.Contains(result.Data, s => s.SlotDate == date1 && s.ShiftCode == "SHIFT_MORNING" && s.IsActive);
        Assert.Contains(result.Data, s => s.SlotDate == date2 && s.ShiftCode == "SHIFT_AFTERNOON" && s.IsActive);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task ToggleBookingSlotCommand_InvalidShiftCode_ThrowsValidationException()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var repository = new EfWorkerRepository(db);

        string phone = $"096{Random.Shared.Next(1000000, 9999999)}";
        string nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";

        var worker = Worker.CreateFreelancer(phone, nationalId, "Invalid Shift Worker");
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };
        var toggleHandler = new ToggleBookingSlotCommandHandler(repository, currentUser);

        var slotDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var req = new ToggleBookingSlotRequest(slotDate, "INVALID_SHIFT", IsActive: true);

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            toggleHandler.Handle(new ToggleBookingSlotCommand(req), CancellationToken.None));

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task ToggleBookingSlotCommand_PastDate_ThrowsValidationException()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = CreateContext();
        using var transaction = await db.Database.BeginTransactionAsync();

        var repository = new EfWorkerRepository(db);

        string phone = $"095{Random.Shared.Next(1000000, 9999999)}";
        string nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";

        var worker = Worker.CreateFreelancer(phone, nationalId, "Past Date Worker");
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };
        var toggleHandler = new ToggleBookingSlotCommandHandler(repository, currentUser);

        var pastDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var req = new ToggleBookingSlotRequest(pastDate, "SHIFT_MORNING", IsActive: true);

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            toggleHandler.Handle(new ToggleBookingSlotCommand(req), CancellationToken.None));

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task ToggleBookingSlotCommand_ConcurrentToggle_IsIdempotent()
    {
        if (!IsSqlServerAvailable()) return;

        using var dbMaster = CreateContext();
        using var transaction = await dbMaster.Database.BeginTransactionAsync();

        string phone = $"094{Random.Shared.Next(1000000, 9999999)}";
        string nationalId = $"012{Random.Shared.Next(100000000, 999999999)}";

        var worker = Worker.CreateFreelancer(phone, nationalId, "Concurrent Worker");
        dbMaster.Workers.Add(worker);
        await dbMaster.SaveChangesAsync();

        var currentUser = new TestCurrentUser { UserId = worker.WorkerId, Role = UserRole.Worker };
        var slotDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));

        var req = new ToggleBookingSlotRequest(slotDate, "SHIFT_MORNING", IsActive: true);

        // Run 2 handlers concurrently on the same worker
        var task1 = Task.Run(async () =>
        {
            using var db1 = CreateContext();
            var repo1 = new EfWorkerRepository(db1);
            var handler1 = new ToggleBookingSlotCommandHandler(repo1, currentUser);
            return await handler1.Handle(new ToggleBookingSlotCommand(req), CancellationToken.None);
        });

        var task2 = Task.Run(async () =>
        {
            using var db2 = CreateContext();
            var repo2 = new EfWorkerRepository(db2);
            var handler2 = new ToggleBookingSlotCommandHandler(repo2, currentUser);
            return await handler2.Handle(new ToggleBookingSlotCommand(req), CancellationToken.None);
        });

        var results = await Task.WhenAll(task1, task2);

        Assert.All(results, r => Assert.True(r.Success));

        var slotsInDb = await dbMaster.BookingSlots.Where(s => s.WorkerId == worker.WorkerId && s.SlotDate == slotDate && s.ShiftCode == "SHIFT_MORNING").ToListAsync();
        Assert.Single(slotsInDb);

        await transaction.RollbackAsync();
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId { get; set; }
        public UserRole? Role { get; set; } = UserRole.Worker;
        public bool IsAuthenticated => UserId.HasValue;
    }
}
