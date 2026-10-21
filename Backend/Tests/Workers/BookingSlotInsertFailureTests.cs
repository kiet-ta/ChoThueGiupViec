using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers;
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
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>
/// WORKERS-SLOT-01: when the insert of a new slot fails and no concurrent row explains it, the toggle must not answer success for a slot
/// that was never saved; and a failed insert must not stay tracked.
/// </summary>
public class BookingSlotInsertFailureTests
{
    private const int WorkerId = 7;
    private static readonly DateOnly SlotDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));

    private sealed class User : ICurrentUser
    {
        public int? UserId => WorkerId;
        public UserRole? Role => UserRole.Worker;
        public bool IsAuthenticated => true;
    }

    /// <summary>
    /// The slot is not there at first (the toggle takes the insert path); the first save fails; the slot a concurrent request created,
    /// if any, is there when the toggle reads again.
    /// </summary>
    private sealed class FailingInsertRepository(DbUpdateException failure, BookingSlot? concurrentRow) : IWorkerRepository
    {
        private bool _insertFailed;
        public int Saves { get; private set; }

        public Task<BookingSlot?> GetWorkerSlotByShiftAsync(int workerId, DateOnly slotDate, string shiftCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(_insertFailed ? concurrentRow : null);

        public Task AddSlotAsync(BookingSlot slot, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void UpdateSlot(BookingSlot slot)
        {
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            if (!_insertFailed)
            {
                _insertFailed = true;
                throw failure;
            }

            return Task.CompletedTask;
        }

        public Task<Worker?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker?> GetByNationalIdAsync(string nationalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByPhoneOrNationalIdAsync(string phoneNumber, string nationalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(IReadOnlyList<Worker> Items, int TotalCount)> GetEkycQueueAsync(int page, int pageSize, string status = EkycQueueStatus.Pending, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BookingSlot>> GetWorkerSlotsAsync(int workerId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<JobAssignment?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobPhoto>> GetJobPhotosAsync(long assignmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobPhoto>> GetAcceptedJobPhotosByPhaseAsync(long assignmentId, string photoPhase, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddJobPhotoAsync(JobPhoto photo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCompleteAssignmentAsync(long assignmentId, int workerId, decimal commissionRate, decimal payoutAmount, DateTime completedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Worker>> ListAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker> AddAsync(Worker entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Update(Worker entity) => throw new NotSupportedException();
        public void Delete(Worker entity) => throw new NotSupportedException();
    }

    private static BookingSlot ExistingRow(string status) => new()
    {
        SlotId = 5,
        WorkerId = WorkerId,
        SlotDate = SlotDate,
        ShiftCode = "SHIFT_MORNING",
        StartTime = new TimeOnly(8, 0),
        EndTime = new TimeOnly(12, 0),
        SlotSource = "FREELANCER",
        SlotStatus = status,
    };

    private static WorkerBookingSlotService Service(FailingInsertRepository repository) => new(repository, new User(), new FakeClock());

    private static ToggleBookingSlotRequest Request(bool isActive) => new(SlotDate, "SHIFT_MORNING", isActive);

    [Fact]
    public async Task ToggleAsync_InsertFailsAndNoRowExists_RethrowsTheOriginalException_InsteadOfAnsweringSuccess()
    {
        var failure = new DbUpdateException("insert failed");
        var repository = new FailingInsertRepository(failure, concurrentRow: null);

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() => Service(repository).ToggleAsync(Request(true)));

        Assert.Same(failure, thrown);
        Assert.Equal(1, repository.Saves);
    }

    [Fact]
    public async Task ToggleAsync_InsertFailsBecauseAConcurrentRequestCreatedTheRow_UpdatesThatRow()
    {
        var repository = new FailingInsertRepository(new DbUpdateException("unique index"), ExistingRow("BLOCKED"));

        var response = await Service(repository).ToggleAsync(Request(true));

        Assert.True(response.Success);
        Assert.Equal(5, response.Data!.SlotId);
        Assert.True(response.Data.IsActive);
        Assert.Equal(2, repository.Saves); // the failed insert, then the update of the concurrent row
    }

    [Fact]
    public async Task ToggleAsync_InsertFailsBecauseAConcurrentRequestCreatedAHeldRow_IsRefusedAsSlotInUse()
    {
        var repository = new FailingInsertRepository(new DbUpdateException("unique index"), ExistingRow("HELD"));

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => Service(repository).ToggleAsync(Request(false)));

        Assert.Equal(WorkerBookingSlotService.SlotInUseCode, ex.Code);
    }

    // ---- the real repository --------------------------------------------------------------------------------------------------

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

    [Fact]
    public async Task SaveChangesAsync_AFailedInsert_LeavesNothingTrackedAsAdded_SoALaterSaveDoesNotRetryIt()
    {
        if (!IsSqlServerAvailable()) return;

        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);
        var repository = new EfWorkerRepository(db);

        // A slot of a worker that does not exist: the foreign key refuses the insert.
        var slot = new BookingSlot
        {
            WorkerId = int.MaxValue,
            SlotDate = SlotDate,
            ShiftCode = "SHIFT_MORNING",
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(12, 0),
            SlotSource = "FREELANCER",
            SlotStatus = "AVAILABLE",
            UpdatedAt = DateTime.UtcNow,
        };
        await repository.AddSlotAsync(slot);

        await Assert.ThrowsAsync<DbUpdateException>(() => repository.SaveChangesAsync());

        Assert.DoesNotContain(db.ChangeTracker.Entries(), e => e.State == EntityState.Added);
        await repository.SaveChangesAsync(); // would throw again if the failed slot were still tracked
    }
}
