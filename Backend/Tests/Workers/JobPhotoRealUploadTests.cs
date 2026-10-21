using System.Reflection;
using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Workers;
using CommonService.WebAPI.Controllers.Workers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>
/// WORKERS-PHOTO-01: a worker uploads a real photo file, its sharpness is measured on the decoded pixels, the file is stored, and the
/// stored file is given back only to the worker, the customer of the assignment or an Admin. The service is the real one, with the real
/// <see cref="ImageQualityService"/> and <see cref="FakeFileStorage"/>; only the repository is a stub. No database needed.
/// </summary>
public class JobPhotoRealUploadTests
{
    private const long AssignmentId = 55;
    private const int WorkerId = 7;
    private const int CustomerId = 21;

    private sealed class User(int id, UserRole role) : ICurrentUser
    {
        public int? UserId { get; } = id;
        public UserRole? Role { get; } = role;
        public bool IsAuthenticated => true;
    }

    /// <summary>Holds one assignment and the photos added to it; each added photo gets an id, as the database would.</summary>
    private sealed class PhotoRepository : IWorkerRepository
    {
        private long _nextPhotoId = 801;

        public PhotoRepository(JobAssignmentStatus status = JobAssignmentStatus.CheckedIn)
        {
            Assignment = new JobAssignment { AssignmentId = AssignmentId, WorkerId = WorkerId, CustomerId = CustomerId };
            foreach (var step in new[] { JobAssignmentStatus.Assigned, JobAssignmentStatus.CheckedIn, JobAssignmentStatus.InProgress })
            {
                Assignment.TransitionTo(step);
                if (step == status)
                {
                    break;
                }
            }
        }

        public JobAssignment Assignment { get; }
        public List<JobPhoto> Photos { get; } = [];
        public bool FailSave { get; set; }
        public int Saves { get; private set; }

        public Task<JobAssignment?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<JobAssignment?>(assignmentId == AssignmentId ? Assignment : null);

        public Task<IReadOnlyList<JobPhoto>> GetAcceptedJobPhotosByPhaseAsync(long assignmentId, string photoPhase, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JobPhoto>>(Photos.Where(p => p.IsAccepted && p.PhotoPhase == photoPhase).ToList());

        public Task<IReadOnlyList<JobPhoto>> GetJobPhotosAsync(long assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JobPhoto>>(assignmentId == AssignmentId ? Photos.ToList() : []);

        public Task AddJobPhotoAsync(JobPhoto photo, CancellationToken cancellationToken = default)
        {
            photo.PhotoId = _nextPhotoId++;
            Photos.Add(photo);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            if (FailSave)
            {
                Photos.Clear();
                throw new InvalidOperationException("the database is down");
            }

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
        public Task<bool> TryCompleteAssignmentAsync(long assignmentId, int workerId, decimal commissionRate, decimal payoutAmount, DateTime completedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Worker>> ListAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker> AddAsync(Worker entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Update(Worker entity) => throw new NotSupportedException();
        public void Delete(Worker entity) => throw new NotSupportedException();
    }

    private static JobPhotoService Service(PhotoRepository repository, FakeFileStorage storage, ICurrentUser user, bool allowUrlPhotos = false)
    {
        var rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        return new JobPhotoService(repository, new ImageQualityService(rules), user, rules, new FakeClock(), storage, new JobPhotoUploadPolicy(allowUrlPhotos));
    }

    private static JobPhotoService WorkerService(PhotoRepository repository, FakeFileStorage storage, bool allowUrlPhotos = false) =>
        Service(repository, storage, new User(WorkerId, UserRole.Worker), allowUrlPhotos);

    private static UploadedPhotoFile File(byte[] bytes, string contentType = "image/jpeg", long? length = null) =>
        new("photo.jpg", contentType, new MemoryStream(bytes), length ?? bytes.Length);

    // ---- upload -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task UploadFileAsync_ASharpJpeg_IsAccepted_Stored_AndStartsTheWork()
    {
        var repository = new PhotoRepository(JobAssignmentStatus.CheckedIn);
        var storage = new FakeFileStorage();

        var response = await WorkerService(repository, storage).UploadFileAsync(AssignmentId, "before", 1, File(TestPhotos.SharpJpeg()));

        var photo = Assert.Single(repository.Photos);
        Assert.True(photo.IsAccepted);
        Assert.True(photo.VolScore >= new BusinessRules().Vol.Threshold);
        Assert.StartsWith("job-photos/", photo.ImageUrl);
        Assert.Equal(1, storage.Count);
        Assert.Equal(JobAssignmentStatus.InProgress, repository.Assignment.AssignmentStatus);
        Assert.Equal(1, repository.Saves);

        // The client is given the authenticated file address, never the storage path.
        Assert.Equal($"/api/workers/assignments/{AssignmentId}/photos/801/file", response.Data!.PhotoUrl);
        Assert.True(response.Data.IsAccepted);
    }

    [Fact]
    public async Task UploadFileAsync_ABlurredJpeg_IsStoredAsRejected_AndRefusedWithTheVolScoreError()
    {
        var repository = new PhotoRepository(JobAssignmentStatus.CheckedIn);
        var storage = new FakeFileStorage();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            WorkerService(repository, storage).UploadFileAsync(AssignmentId, "BEFORE", 1, File(TestPhotos.BlurredJpeg())));

        Assert.Contains("volScore", ex.Errors.Keys);
        Assert.False(Assert.Single(repository.Photos).IsAccepted);
        Assert.Equal(1, storage.Count); // kept for audit
        Assert.Equal(JobAssignmentStatus.CheckedIn, repository.Assignment.AssignmentStatus);
    }

    [Fact]
    public async Task UploadFileAsync_AFlatImage_IsRefused()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            WorkerService(new PhotoRepository(), new FakeFileStorage()).UploadFileAsync(AssignmentId, "BEFORE", 1, File(TestPhotos.FlatJpeg())));

        Assert.Contains("volScore", ex.Errors.Keys);
    }

    [Fact]
    public async Task UploadFileAsync_APngAndAWebp_AreAccepted()
    {
        var repository = new PhotoRepository(JobAssignmentStatus.InProgress);
        var service = WorkerService(repository, new FakeFileStorage());

        var png = await service.UploadFileAsync(AssignmentId, "BEFORE", 1, File(TestPhotos.SharpPng(), "image/png"));
        var webp = await service.UploadFileAsync(AssignmentId, "BEFORE", 2, File(TestPhotos.SharpWebp(), "image/webp"));

        Assert.True(png.Data!.IsAccepted);
        Assert.True(webp.Data!.IsAccepted);
    }

    [Fact]
    public async Task UploadFileAsync_ABeforePhotoNeedsNoUrl_AndAnAfterPhotoStillNeedsAMatchingBeforeAngle()
    {
        var repository = new PhotoRepository(JobAssignmentStatus.InProgress);
        var service = WorkerService(repository, new FakeFileStorage());
        await service.UploadFileAsync(AssignmentId, "BEFORE", 1, File(TestPhotos.SharpJpeg()));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.UploadFileAsync(AssignmentId, "AFTER", 3, File(TestPhotos.SharpJpeg())));
        var matching = await service.UploadFileAsync(AssignmentId, "AFTER", 1, File(TestPhotos.SharpJpeg()));

        Assert.Contains("angleNo", ex.Errors.Keys);
        Assert.True(matching.Data!.IsAccepted);
    }

    public static TheoryData<string, string> RefusedFiles => new()
    {
        { "no file", "file" },
        { "empty file", "file" },
        { "claims more than 5 MB", "file" },
        { "text/plain", "contentType" },
        { "png bytes declared as jpeg", "contentType" },
        { "jpeg signature but no image", "file" },
        { "claims too many pixels", "file" },
    };

    [Theory]
    [MemberData(nameof(RefusedFiles))]
    public async Task UploadFileAsync_ARefusedFile_IsA400OnTheNamedField_AndNothingIsStoredOrSaved(string scenario, string field)
    {
        var repository = new PhotoRepository();
        var storage = new FakeFileStorage();
        UploadedPhotoFile? file = scenario switch
        {
            "no file" => null,
            "empty file" => File([], length: 0),
            "claims more than 5 MB" => File(TestPhotos.SharpJpeg(), length: JobPhotoFileRules.MaxBytes + 1),
            "text/plain" => File(TestPhotos.SharpJpeg(), "text/plain"),
            "png bytes declared as jpeg" => File(TestPhotos.SharpPng(), "image/jpeg"),
            "jpeg signature but no image" => File(TestPhotos.CorruptJpeg()),
            "claims too many pixels" => File(TestPhotos.PngClaiming(10000, 10000), "image/png"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => WorkerService(repository, storage).UploadFileAsync(AssignmentId, "BEFORE", 1, file));

        Assert.Contains(field, ex.Errors.Keys);
        Assert.Equal(0, storage.Count);
        Assert.Empty(repository.Photos);
        Assert.Equal(0, repository.Saves);
    }

    [Fact]
    public async Task UploadFileAsync_AStreamLongerThanItsDeclaredLength_IsStoppedAtTheLimit()
    {
        var oversized = new byte[JobPhotoFileRules.MaxBytes + 100];
        TestPhotos.SharpJpeg().CopyTo(oversized, 0); // starts as a JPEG, declared small

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            WorkerService(new PhotoRepository(), new FakeFileStorage()).UploadFileAsync(AssignmentId, "BEFORE", 1, File(oversized, length: 1000)));

        Assert.Contains("file", ex.Errors.Keys);
    }

    [Fact]
    public async Task UploadFileAsync_TheSameRulesAsBefore_PhaseAngleStatusAndOwnerAreCheckedBeforeAnythingIsStored()
    {
        var storage = new FakeFileStorage();
        var jpeg = TestPhotos.SharpJpeg();

        await Assert.ThrowsAsync<ValidationException>(() => WorkerService(new PhotoRepository(), storage).UploadFileAsync(AssignmentId, "DURING", 1, File(jpeg)));
        await Assert.ThrowsAsync<ValidationException>(() => WorkerService(new PhotoRepository(), storage).UploadFileAsync(AssignmentId, "BEFORE", 6, File(jpeg)));
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            WorkerService(new PhotoRepository(JobAssignmentStatus.Assigned), storage).UploadFileAsync(AssignmentId, "BEFORE", 1, File(jpeg)));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Service(new PhotoRepository(), storage, new User(WorkerId + 1, UserRole.Worker)).UploadFileAsync(AssignmentId, "BEFORE", 1, File(jpeg)));
        await Assert.ThrowsAsync<NotFoundException>(() => WorkerService(new PhotoRepository(), storage).UploadFileAsync(AssignmentId + 1, "BEFORE", 1, File(jpeg)));

        Assert.Equal(0, storage.Count);
    }

    [Fact]
    public async Task UploadFileAsync_WhenTheRowCannotBeSaved_TheStoredFileIsRemovedAgain()
    {
        var repository = new PhotoRepository { FailSave = true };
        var storage = new FakeFileStorage();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkerService(repository, storage).UploadFileAsync(AssignmentId, "BEFORE", 1, File(TestPhotos.SharpJpeg())));

        Assert.Equal(0, storage.Count);
    }

    // ---- the legacy JSON body: Development only -------------------------------------------------------------------------------------

    [Fact]
    public async Task UploadAsync_ThePhotoUrlBody_IsRefusedOutsideDevelopment()
    {
        var repository = new PhotoRepository();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            WorkerService(repository, new FakeFileStorage(), allowUrlPhotos: false)
                .UploadAsync(AssignmentId, new UploadJobPhotoRequest("BEFORE", 1, "https://storage.local/jobs/a1.jpg")));

        Assert.Contains("photoUrl", ex.Errors.Keys);
        Assert.Contains("file", ex.Errors["photoUrl"][0]);
        Assert.Empty(repository.Photos);
    }

    [Fact]
    public async Task UploadAsync_ThePhotoUrlBody_StillWorksInDevelopment_WithTheStandIn()
    {
        var repository = new PhotoRepository();

        var response = await WorkerService(repository, new FakeFileStorage(), allowUrlPhotos: true)
            .UploadAsync(AssignmentId, new UploadJobPhotoRequest("BEFORE", 1, "https://storage.local/jobs/a1.jpg"));

        Assert.True(response.Data!.IsAccepted);
        Assert.Equal("https://storage.local/jobs/a1.jpg", response.Data.PhotoUrl); // a client URL is returned as it came
    }

    // ---- the stored file ------------------------------------------------------------------------------------------------------------

    private static async Task<(PhotoRepository Repository, FakeFileStorage Storage, long PhotoId)> UploadedPhotoAsync()
    {
        var repository = new PhotoRepository(JobAssignmentStatus.InProgress);
        var storage = new FakeFileStorage();
        var response = await WorkerService(repository, storage).UploadFileAsync(AssignmentId, "BEFORE", 1, File(TestPhotos.SharpJpeg()));
        return (repository, storage, response.Data!.PhotoId);
    }

    [Theory]
    [InlineData(WorkerId, UserRole.Worker)]
    [InlineData(CustomerId, UserRole.Customer)]
    [InlineData(999, UserRole.Admin)]
    public async Task GetPhotoFileAsync_TheWorker_TheCustomerAndAnAdmin_GetTheBytes(int userId, UserRole role)
    {
        var (repository, storage, photoId) = await UploadedPhotoAsync();

        var file = await Service(repository, storage, new User(userId, role)).GetPhotoFileAsync(AssignmentId, photoId);

        Assert.Equal("image/jpeg", file.ContentType);
        using var buffer = new MemoryStream();
        await file.Content.CopyToAsync(buffer);
        Assert.Equal(TestPhotos.SharpJpeg(), buffer.ToArray());
    }

    [Theory]
    [InlineData(WorkerId + 1, UserRole.Worker)]
    [InlineData(CustomerId + 1, UserRole.Customer)]
    [InlineData(WorkerId, UserRole.Partner)]
    public async Task GetPhotoFileAsync_AnotherWorker_AnotherCustomer_OrAPartner_IsRefused(int userId, UserRole role)
    {
        var (repository, storage, photoId) = await UploadedPhotoAsync();

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Service(repository, storage, new User(userId, role)).GetPhotoFileAsync(AssignmentId, photoId));
    }

    [Fact]
    public async Task GetPhotoFileAsync_AnUnknownAssignmentOrPhoto_IsNotFound()
    {
        var (repository, storage, photoId) = await UploadedPhotoAsync();
        var worker = Service(repository, storage, new User(WorkerId, UserRole.Worker));

        await Assert.ThrowsAsync<NotFoundException>(() => worker.GetPhotoFileAsync(AssignmentId + 1, photoId));
        await Assert.ThrowsAsync<NotFoundException>(() => worker.GetPhotoFileAsync(AssignmentId, photoId + 100));
    }

    [Fact]
    public async Task GetPhotoFileAsync_APhotoThatHasNoStoredFile_IsNotFound()
    {
        // A row from the Development URL upload, or from before uploads existed: image_url is a client URL, not a storage path.
        var repository = new PhotoRepository(JobAssignmentStatus.InProgress);
        var storage = new FakeFileStorage();
        var legacy = await WorkerService(repository, storage, allowUrlPhotos: true)
            .UploadAsync(AssignmentId, new UploadJobPhotoRequest("BEFORE", 1, "https://storage.local/jobs/a1.jpg"));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service(repository, storage, new User(WorkerId, UserRole.Worker)).GetPhotoFileAsync(AssignmentId, legacy.Data!.PhotoId));
    }

    [Fact]
    public async Task GetPhotoFileAsync_WhenTheStoredFileIsGone_IsNotFound()
    {
        var (repository, _, photoId) = await UploadedPhotoAsync();
        var emptyStorage = new FakeFileStorage();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service(repository, emptyStorage, new User(WorkerId, UserRole.Worker)).GetPhotoFileAsync(AssignmentId, photoId));
    }

    [Fact]
    public async Task GetPhotosAsync_ListsAStoredPhotoWithTheFileAddress_AndALegacyPhotoWithItsUrl()
    {
        var repository = new PhotoRepository(JobAssignmentStatus.InProgress);
        var storage = new FakeFileStorage();
        var stored = await WorkerService(repository, storage).UploadFileAsync(AssignmentId, "BEFORE", 1, File(TestPhotos.SharpJpeg()));
        var legacy = await WorkerService(repository, storage, allowUrlPhotos: true)
            .UploadAsync(AssignmentId, new UploadJobPhotoRequest("BEFORE", 2, "https://storage.local/jobs/a2.jpg"));

        var list = await Service(repository, storage, new User(CustomerId, UserRole.Customer)).GetPhotosAsync(AssignmentId);

        Assert.Equal(stored.Data!.PhotoUrl, list.Data!.Single(p => p.PhotoId == stored.Data.PhotoId).PhotoUrl);
        Assert.Equal("https://storage.local/jobs/a2.jpg", list.Data!.Single(p => p.PhotoId == legacy.Data!.PhotoId).PhotoUrl);
    }

    // ---- the HTTP surface -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheFileEndpoint_IsNotAnonymous_AndIsLimitedToWorkerCustomerAndAdmin()
    {
        var action = typeof(WorkersController).GetMethod(nameof(WorkersController.GetPhotoFile))!;

        Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>());
        var roles = action.GetCustomAttributes<AuthorizeAttribute>()
            .SelectMany(a => (a.Roles ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Order()
            .ToArray();
        Assert.Equal(["Admin", "Customer", "Worker"], roles);
    }

    [Fact]
    public void ThePhotoUpload_IsAMultipartAction_AndTheJsonOneIsHiddenFromTheOpenApiDocument()
    {
        var multipart = typeof(WorkersController).GetMethod(nameof(WorkersController.UploadPhoto))!;
        var json = typeof(WorkersController).GetMethod(nameof(WorkersController.UploadPhotoByUrl))!;

        Assert.Contains("multipart/form-data", multipart.GetCustomAttribute<ConsumesAttribute>()!.ContentTypes);
        Assert.Contains("application/json", json.GetCustomAttribute<ConsumesAttribute>()!.ContentTypes);
        Assert.True(json.GetCustomAttribute<ApiExplorerSettingsAttribute>()!.IgnoreApi);
        Assert.Null(multipart.GetCustomAttribute<ApiExplorerSettingsAttribute>());
    }
}
