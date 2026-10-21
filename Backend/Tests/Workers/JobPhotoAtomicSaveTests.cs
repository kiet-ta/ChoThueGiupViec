using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>
/// WORKERS-DATA-02: an accepted photo and the CHECKED_IN to IN_PROGRESS change it causes are saved by one SaveChangesAsync. No database
/// needed: the repository records what had been done to the entities at each save.
/// </summary>
public class JobPhotoAtomicSaveTests
{
    private const long AssignmentId = 55;
    private const int WorkerId = 7;

    private sealed class WorkerUser : ICurrentUser
    {
        public int? UserId => WorkerId;
        public UserRole? Role => UserRole.Worker;
        public bool IsAuthenticated => true;
    }

    private sealed class FixedQuality(bool accepted) : IImageQualityService
    {
        public Task<ImageQualityResult> AssessAsync(Stream image, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImageQualityResult(accepted ? 250.0 : 12.5, accepted));
    }

    /// <summary>Holds one assignment; each save is recorded as (photos added so far, status of the assignment at that moment).</summary>
    private sealed class PhotoRepository : IWorkerRepository
    {
        private readonly List<JobPhoto> _acceptedBefore;

        public PhotoRepository(JobAssignmentStatus status, params byte[] acceptedBeforeAngles)
        {
            Assignment = new JobAssignment { AssignmentId = AssignmentId, WorkerId = WorkerId, CustomerId = 21 };
            foreach (var step in new[] { JobAssignmentStatus.Assigned, JobAssignmentStatus.CheckedIn, JobAssignmentStatus.InProgress })
            {
                Assignment.TransitionTo(step);
                if (step == status)
                {
                    break;
                }
            }

            _acceptedBefore = acceptedBeforeAngles
                .Select(a => new JobPhoto { AssignmentId = AssignmentId, PhotoPhase = "BEFORE", AngleNo = a, IsAccepted = true })
                .ToList();
        }

        public JobAssignment Assignment { get; }
        public List<JobPhoto> Added { get; } = [];
        public List<(int PhotosAdded, JobAssignmentStatus Status)> Saves { get; } = [];

        public Task<JobAssignment?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<JobAssignment?>(Assignment);

        public Task<IReadOnlyList<JobPhoto>> GetAcceptedJobPhotosByPhaseAsync(long assignmentId, string photoPhase, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JobPhoto>>(photoPhase == "BEFORE" ? _acceptedBefore : []);

        public Task AddJobPhotoAsync(JobPhoto photo, CancellationToken cancellationToken = default)
        {
            Added.Add(photo);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves.Add((Added.Count, Assignment.AssignmentStatus));
            return Task.CompletedTask;
        }

        public Task<Worker?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker?> GetByNationalIdAsync(string nationalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByPhoneOrNationalIdAsync(string phoneNumber, string nationalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(IReadOnlyList<Worker> Items, int TotalCount)> GetEkycQueueAsync(int page, int pageSize, string status = EkycQueueStatus.Pending, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BookingSlot>> GetWorkerSlotsAsync(int workerId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BookingSlot?> GetWorkerSlotByShiftAsync(int workerId, DateOnly slotDate, string shiftCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddSlotAsync(BookingSlot slot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void UpdateSlot(BookingSlot slot) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobPhoto>> GetJobPhotosAsync(long assignmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCompleteAssignmentAsync(long assignmentId, int workerId, decimal commissionRate, decimal payoutAmount, DateTime completedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Worker>> ListAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker> AddAsync(Worker entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Update(Worker entity) => throw new NotSupportedException();
        public void Delete(Worker entity) => throw new NotSupportedException();
    }

    private static JobPhotoService Service(PhotoRepository repository, bool sharp = true) =>
        new(repository, new FixedQuality(sharp), new WorkerUser(), Microsoft.Extensions.Options.Options.Create(new BusinessRules()), new FakeClock());

    private static UploadJobPhotoRequest Request(string phase, byte angle) =>
        new(phase, angle, "https://storage.local/jobs/photo.jpg");

    [Fact]
    public async Task UploadAsync_FirstAcceptedBeforePhoto_IsSavedTogetherWithTheStartOfTheWork()
    {
        var repository = new PhotoRepository(JobAssignmentStatus.CheckedIn);

        await Service(repository).UploadAsync(AssignmentId, Request("BEFORE", 1));

        // One save, and when it ran the photo was added and the assignment was already IN_PROGRESS.
        var save = Assert.Single(repository.Saves);
        Assert.Equal(1, save.PhotosAdded);
        Assert.Equal(JobAssignmentStatus.InProgress, save.Status);
        Assert.Equal(JobAssignmentStatus.InProgress, repository.Assignment.AssignmentStatus);
        Assert.True(Assert.Single(repository.Added).IsAccepted);
    }

    [Fact]
    public async Task UploadAsync_BlurryBeforePhoto_IsStoredAsRejected_RefusedWithTheVolScoreError_AndTheStatusIsUnchanged()
    {
        var repository = new PhotoRepository(JobAssignmentStatus.CheckedIn);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Service(repository, sharp: false).UploadAsync(AssignmentId, Request("BEFORE", 1)));

        Assert.Contains("volScore", ex.Errors.Keys);
        var save = Assert.Single(repository.Saves);
        Assert.Equal(1, save.PhotosAdded);
        Assert.Equal(JobAssignmentStatus.CheckedIn, save.Status);
        Assert.False(Assert.Single(repository.Added).IsAccepted);
        Assert.Equal(JobAssignmentStatus.CheckedIn, repository.Assignment.AssignmentStatus);
    }

    [Fact]
    public async Task UploadAsync_BeforePhotoOnAnAssignmentAlreadyInProgress_IsStoredWithNoStatusChange()
    {
        var repository = new PhotoRepository(JobAssignmentStatus.InProgress, acceptedBeforeAngles: 1);

        await Service(repository).UploadAsync(AssignmentId, Request("BEFORE", 2));

        var save = Assert.Single(repository.Saves);
        Assert.Equal(JobAssignmentStatus.InProgress, save.Status);
        Assert.Single(repository.Added);
    }

    [Fact]
    public async Task UploadAsync_AfterPhoto_IsStoredWithNoStatusChange()
    {
        var repository = new PhotoRepository(JobAssignmentStatus.InProgress, acceptedBeforeAngles: 1);

        await Service(repository).UploadAsync(AssignmentId, Request("AFTER", 1));

        var save = Assert.Single(repository.Saves);
        Assert.Equal(JobAssignmentStatus.InProgress, save.Status);
        Assert.Equal("AFTER", Assert.Single(repository.Added).PhotoPhase);
    }
}
