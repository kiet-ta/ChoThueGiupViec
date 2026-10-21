using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Identity.Services;
using CommonService.Application.Features.Workers;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using MediatR;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>
/// WORKERS-TIME-01: the Workers services read time through <see cref="IClock"/> (decisions G-3), and a slot date is a Vietnam local date.
/// The clock is pinned to 02:00 Vietnam time of 2026-10-22, which is 19:00 UTC of 2026-10-21: the two dates differ, so a service that
/// still used the UTC date or <c>DateTime.UtcNow</c> shows up. No database needed.
/// </summary>
public class WorkersClockTests
{
    private const int WorkerId = 7;
    private const int CustomerId = 21;
    private const long AssignmentId = 55;

    private static readonly DateTime PinnedUtc = new(2026, 10, 21, 19, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly VietnamToday = new(2026, 10, 22);
    private static readonly DateOnly UtcToday = new(2026, 10, 21);

    private static FakeClock PinnedClock()
    {
        var clock = new FakeClock();
        clock.Set(PinnedUtc);
        return clock;
    }

    private sealed class User(int id, UserRole role) : ICurrentUser
    {
        public int? UserId { get; } = id;
        public UserRole? Role { get; } = role;
        public bool IsAuthenticated => true;
    }

    private sealed class FixedQuality : IImageQualityService
    {
        public Task<ImageQualityResult> AssessAsync(Stream image, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImageQualityResult(250.0, true));
    }

    private sealed class RecordingPublisher : IPublisher
    {
        public List<object> Events { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            Events.Add(notification);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
        {
            Events.Add(notification);
            return Task.CompletedTask;
        }
    }

    /// <summary>Records what the services write; the assignment and the accepted photos are given by the test.</summary>
    private sealed class ClockRepository(JobAssignment? assignment = null) : IWorkerRepository
    {
        public List<BookingSlot> AddedSlots { get; } = [];
        public List<JobPhoto> AddedPhotos { get; } = [];
        public List<Worker> AddedWorkers { get; } = [];
        public (DateOnly Start, DateOnly End)? SlotRange { get; private set; }
        public DateTime? CompletedAtUtc { get; private set; }

        public Task<JobAssignment?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(assignment);

        public Task<IReadOnlyList<JobPhoto>> GetAcceptedJobPhotosByPhaseAsync(long assignmentId, string photoPhase, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JobPhoto>>(Enumerable.Range(1, 3)
                .Select(a => new JobPhoto { AssignmentId = AssignmentId, PhotoPhase = photoPhase, AngleNo = (byte)a, IsAccepted = true })
                .ToList());

        public Task AddJobPhotoAsync(JobPhoto photo, CancellationToken cancellationToken = default)
        {
            AddedPhotos.Add(photo);
            return Task.CompletedTask;
        }

        public Task<BookingSlot?> GetWorkerSlotByShiftAsync(int workerId, DateOnly slotDate, string shiftCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<BookingSlot?>(null);

        public Task AddSlotAsync(BookingSlot slot, CancellationToken cancellationToken = default)
        {
            AddedSlots.Add(slot);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BookingSlot>> GetWorkerSlotsAsync(int workerId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
        {
            SlotRange = (startDate, endDate);
            return Task.FromResult<IReadOnlyList<BookingSlot>>([]);
        }

        public Task<bool> ExistsByPhoneOrNationalIdAsync(string phoneNumber, string nationalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<Worker> AddAsync(Worker entity, CancellationToken cancellationToken = default)
        {
            AddedWorkers.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<bool> TryCompleteAssignmentAsync(long assignmentId, int workerId, decimal commissionRate, decimal payoutAmount, DateTime completedAtUtc, CancellationToken cancellationToken = default)
        {
            CompletedAtUtc = completedAtUtc;
            return Task.FromResult(true);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void UpdateSlot(BookingSlot slot)
        {
        }

        public Task<Worker?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker?> GetByNationalIdAsync(string nationalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(IReadOnlyList<Worker> Items, int TotalCount)> GetEkycQueueAsync(int page, int pageSize, string status = EkycQueueStatus.Pending, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobPhoto>> GetJobPhotosAsync(long assignmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Worker>> ListAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Update(Worker entity) => throw new NotSupportedException();
        public void Delete(Worker entity) => throw new NotSupportedException();
    }

    private static JobAssignment AssignmentIn(JobAssignmentStatus target)
    {
        var assignment = new JobAssignment
        {
            AssignmentId = AssignmentId,
            WorkerId = WorkerId,
            CustomerId = CustomerId,
            OrderId = 9,
            GrossAmount = 260000m,
            CommissionRate = 0m,
            UpdatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        foreach (var step in new[]
        {
            JobAssignmentStatus.Assigned, JobAssignmentStatus.CheckedIn, JobAssignmentStatus.InProgress, JobAssignmentStatus.AwaitingAcceptance,
        })
        {
            assignment.TransitionTo(step);
            if (step == target)
            {
                break;
            }
        }

        return assignment;
    }

    // ---- slot dates: the Vietnam date, not the UTC date -----------------------------------------------------------------------

    private static WorkerBookingSlotService SlotService(ClockRepository repository) =>
        new(repository, new User(WorkerId, UserRole.Worker), PinnedClock());

    [Fact]
    public async Task ToggleAsync_ASlotOfTheVietnamToday_IsNotInThePast_EvenThoughTheUtcDateIsEarlier()
    {
        var repository = new ClockRepository();

        var response = await SlotService(repository).ToggleAsync(new ToggleBookingSlotRequest(VietnamToday, "SHIFT_MORNING", true));

        Assert.True(response.Success);
        Assert.Equal(VietnamToday, Assert.Single(repository.AddedSlots).SlotDate);
    }

    [Fact]
    public async Task ToggleAsync_ASlotOfTheVietnamYesterday_IsRefusedAsInThePast()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            SlotService(new ClockRepository()).ToggleAsync(new ToggleBookingSlotRequest(UtcToday, "SHIFT_MORNING", true)));

        Assert.Contains("slotDate", ex.Errors.Keys);
    }

    [Fact]
    public async Task GetSlotsAsync_WithoutDates_StartsAtTheVietnamTodayAndEndsSevenDaysLater()
    {
        var repository = new ClockRepository();

        await SlotService(repository).GetSlotsAsync(null, null);

        Assert.Equal((VietnamToday, VietnamToday.AddDays(7)), repository.SlotRange);
    }

    // ---- every timestamp written is the clock's ---------------------------------------------------------------------------

    [Fact]
    public async Task ToggleAsync_WritesThePinnedInstant()
    {
        var repository = new ClockRepository();

        await SlotService(repository).ToggleAsync(new ToggleBookingSlotRequest(VietnamToday, "SHIFT_MORNING", true));

        Assert.Equal(PinnedUtc, Assert.Single(repository.AddedSlots).UpdatedAt);
    }

    [Fact]
    public async Task RegisterAsync_WritesThePinnedInstant()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "Test_Hmac_Secret_Key_At_Least_32_Chars_Long!",
            ["Otp:HmacSecret"] = "Test_Hmac_Secret_Key_At_Least_32_Chars_Long!",
        }).Build();
        var (token, _) = new TokenService(Microsoft.Extensions.Options.Options.Create(new BusinessRules()), config).GenerateWorkerRegistrationToken("0912345678");
        var repository = new ClockRepository();
        var service = new WorkerProfileService(repository, new User(WorkerId, UserRole.Worker), PinnedClock(), config);

        await service.RegisterAsync(new RegisterWorkerRequest(token, "Nguyen Van A", "012345678901"));

        var worker = Assert.Single(repository.AddedWorkers);
        Assert.Equal(PinnedUtc, worker.CreatedAt);
        Assert.Equal(PinnedUtc, worker.UpdatedAt);
    }

    [Fact]
    public async Task SubmitAsync_WritesThePinnedInstant()
    {
        var assignment = AssignmentIn(JobAssignmentStatus.InProgress);
        var service = new JobCompletionService(new ClockRepository(assignment), new RecordingPublisher(), new User(WorkerId, UserRole.Worker), PinnedClock());

        var response = await service.SubmitAsync(AssignmentId, "done");

        Assert.Equal(PinnedUtc, assignment.UpdatedAt);
        Assert.Equal(PinnedUtc, response.Data!.SubmittedAt);
    }

    [Fact]
    public async Task RequestRedoAsync_WritesThePinnedInstant()
    {
        var assignment = AssignmentIn(JobAssignmentStatus.AwaitingAcceptance);
        var service = new JobCompletionService(new ClockRepository(assignment), new RecordingPublisher(), new User(CustomerId, UserRole.Customer), PinnedClock());

        var response = await service.RequestRedoAsync(AssignmentId, new RequestRedoRequest("Chua sach"));

        Assert.Equal(PinnedUtc, assignment.UpdatedAt);
        Assert.Equal(PinnedUtc, response.Data!.SubmittedAt);
    }

    [Fact]
    public async Task AcceptAsync_WritesThePinnedInstant()
    {
        var repository = new ClockRepository(AssignmentIn(JobAssignmentStatus.AwaitingAcceptance));
        var publisher = new RecordingPublisher();
        var service = new JobCompletionService(repository, publisher, new User(CustomerId, UserRole.Customer), PinnedClock());

        var response = await service.AcceptAsync(AssignmentId, null);

        Assert.Equal(PinnedUtc, repository.CompletedAtUtc);
        Assert.Equal(PinnedUtc, response.Data!.CompletedAt);
        Assert.Equal(PinnedUtc, Assert.IsType<JobCompleted>(Assert.Single(publisher.Events)).CompletedAtUtc);
    }

    [Fact]
    public async Task UploadAsync_WritesThePinnedInstant()
    {
        var assignment = AssignmentIn(JobAssignmentStatus.CheckedIn);
        var repository = new ClockRepository(assignment);
        var service = new JobPhotoService(
            repository, new FixedQuality(), new User(WorkerId, UserRole.Worker),
            Microsoft.Extensions.Options.Options.Create(new BusinessRules()), PinnedClock(), new FakeFileStorage(), new JobPhotoUploadPolicy(true));

        // The first accepted BEFORE photo also starts the work, so both timestamps of the upload are checked.
        await service.UploadAsync(AssignmentId, new UploadJobPhotoRequest("BEFORE", 4, "https://storage.local/jobs/photo.jpg"));

        Assert.Equal(PinnedUtc, Assert.Single(repository.AddedPhotos).CapturedAt);
        Assert.Equal(PinnedUtc, assignment.UpdatedAt);
    }
}
