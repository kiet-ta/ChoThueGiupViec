using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>SLOT-TOGGLE-GUARD (#398): the worker's availability toggle never changes a slot that Dispatch owns (HELD or BOOKED).</summary>
public class BookingSlotToggleGuardTests
{
    private const int WorkerId = 7;
    private static readonly DateOnly SlotDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

    private sealed class User : ICurrentUser
    {
        public int? UserId => WorkerId;
        public UserRole? Role => UserRole.Worker;
        public bool IsAuthenticated => true;
    }

    /// <summary>Holds one worker's slots; only the members the toggle uses are implemented.</summary>
    private sealed class SlotRepository : IWorkerRepository
    {
        public List<BookingSlot> Slots { get; } = [];
        public int Saves { get; private set; }

        public Task<BookingSlot?> GetWorkerSlotByShiftAsync(int workerId, DateOnly slotDate, string shiftCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Slots.FirstOrDefault(s => s.WorkerId == workerId && s.SlotDate == slotDate && s.ShiftCode == shiftCode));

        public Task AddSlotAsync(BookingSlot slot, CancellationToken cancellationToken = default)
        {
            Slots.Add(slot);
            return Task.CompletedTask;
        }

        public void UpdateSlot(BookingSlot slot)
        {
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
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
        public Task<Worker?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Worker>> ListAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker> AddAsync(Worker entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Update(Worker entity) => throw new NotSupportedException();
        public void Delete(Worker entity) => throw new NotSupportedException();
    }

    private static (SlotRepository Repo, WorkerBookingSlotService Handler) Setup(string? existingStatus)
    {
        var repo = new SlotRepository();
        if (existingStatus != null)
        {
            repo.Slots.Add(new BookingSlot
            {
                SlotId = 1044,
                WorkerId = WorkerId,
                SlotDate = SlotDate,
                ShiftCode = "SHIFT_MORNING",
                SlotSource = "FREELANCER",
                SlotStatus = existingStatus,
            });
        }

        return (repo, new WorkerBookingSlotService(repo, new User()));
    }

    private static Task<CommonService.Application.Common.Models.ApiResponse<BookingSlotResponse>> Toggle(
        WorkerBookingSlotService handler, bool isActive) =>
        handler.ToggleAsync(new ToggleBookingSlotRequest(SlotDate, "SHIFT_MORNING", isActive), default);

    [Theory]
    [InlineData("BOOKED", false)]
    [InlineData("BOOKED", true)]
    [InlineData("HELD", false)]
    [InlineData("HELD", true)]
    public async Task A_slot_owned_by_dispatch_is_refused_and_keeps_its_status(string status, bool isActive)
    {
        var (repo, handler) = Setup(status);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => Toggle(handler, isActive));

        Assert.Equal(WorkerBookingSlotService.SlotInUseCode, ex.Code);
        Assert.Equal(status, Assert.Single(repo.Slots).SlotStatus);
        Assert.Equal(0, repo.Saves);
    }

    [Theory]
    [InlineData("AVAILABLE", false, "BLOCKED")]
    [InlineData("BLOCKED", true, "AVAILABLE")]
    public async Task The_worker_still_switches_a_free_slot_on_and_off(string status, bool isActive, string expected)
    {
        var (repo, handler) = Setup(status);

        var response = await Toggle(handler, isActive);

        Assert.True(response.Success);
        Assert.Equal(isActive, response.Data!.IsActive);
        Assert.Equal(expected, Assert.Single(repo.Slots).SlotStatus);
    }

    [Fact]
    public async Task A_new_slot_is_still_created()
    {
        var (repo, handler) = Setup(existingStatus: null);

        var response = await Toggle(handler, isActive: true);

        Assert.True(response.Success);
        Assert.Equal("AVAILABLE", Assert.Single(repo.Slots).SlotStatus);
    }
}
