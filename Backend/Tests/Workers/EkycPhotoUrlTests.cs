using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>ADMIN-UAT-15 (#873): the three eKYC photo addresses are validated, kept on the worker and returned to the Admin queue. No SQL Server needed.</summary>
public class EkycPhotoUrlTests
{
    private const int WorkerId = 42;

    private static Worker NewWorker()
    {
        var worker = Worker.CreateFreelancer("0900000042", "012000000042", "Photo Worker");
        worker.WorkerId = WorkerId;
        return worker;
    }

    private static WorkerEkycService Service(SubmitRepository repository, RecordingProvider provider, ICurrentUser? user = null) =>
        new(repository, provider, user ?? new WorkerUser(), Microsoft.Extensions.Options.Options.Create(new BusinessRules()), null!);

    private static SubmitEkycRequest Request(string front = "https://storage.local/f.jpg", string back = "https://storage.local/b.jpg", string selfie = "https://storage.local/s.jpg") =>
        new(front, back, selfie);

    // ---- what is accepted ----------------------------------------------------------------------

    [Theory]
    [InlineData("https://storage.local/ekyc/42_front.jpg")]
    [InlineData("https://storage.giupviec.local/ekyc/42_front.jpg")]
    [InlineData("http://localhost:5004/files/ekyc/42_front.jpg")]
    [InlineData("HTTPS://Example.com/a.png?x=1&y=2")]
    [InlineData("/files/ekyc/42_front.jpg")]
    [InlineData("https://s/f.jpg")] // the address the e2e script sends
    public void A_valid_address_is_accepted(string url) => Assert.True(WorkerEkycService.IsSafePhotoUrl(url));

    [Fact]
    public async Task A_valid_submit_keeps_the_three_trimmed_addresses_on_the_worker_and_gives_the_provider_the_trimmed_front_and_selfie()
    {
        var worker = NewWorker();
        var repository = new SubmitRepository(worker);
        var provider = new RecordingProvider();

        var result = await Service(repository, provider).SubmitAsync(
            Request("  https://storage.local/ekyc/f.jpg ", "/files/ekyc/b.jpg", "\thttps://storage.local/ekyc/s.jpg\n"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(("https://storage.local/ekyc/f.jpg", "/files/ekyc/b.jpg", "https://storage.local/ekyc/s.jpg"),
            (worker.EkycFrontCccdUrl, worker.EkycBackCccdUrl, worker.EkycSelfieUrl));
        Assert.Equal(("https://storage.local/ekyc/f.jpg", "https://storage.local/ekyc/s.jpg"), (provider.Received!.IdCardImagePath, provider.Received.SelfieImagePath));
        Assert.Equal(1, repository.Saves);
        Assert.Equal("APPROVED", worker.KycStatus);
    }

    [Fact]
    public async Task A_low_confidence_submit_goes_to_manual_review_and_still_keeps_the_addresses()
    {
        var worker = NewWorker();

        await Service(new SubmitRepository(worker), new RecordingProvider { Confidence = 60m }).SubmitAsync(Request(), CancellationToken.None);

        Assert.Equal("MANUAL_REVIEW", worker.KycStatus);
        Assert.Equal("https://storage.local/f.jpg", worker.EkycFrontCccdUrl);
        Assert.Equal("https://storage.local/b.jpg", worker.EkycBackCccdUrl);
        Assert.Equal("https://storage.local/s.jpg", worker.EkycSelfieUrl);
    }

    [Fact]
    public async Task A_second_submit_replaces_the_addresses_of_the_first()
    {
        var worker = NewWorker();
        var service = Service(new SubmitRepository(worker), new RecordingProvider());

        await service.SubmitAsync(Request(front: "https://storage.local/old.jpg"), CancellationToken.None);
        await service.SubmitAsync(Request(front: "https://storage.local/new.jpg"), CancellationToken.None);

        Assert.Equal("https://storage.local/new.jpg", worker.EkycFrontCccdUrl);
    }

    // ---- what is refused -----------------------------------------------------------------------

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("ftp://storage.local/f.jpg")]
    [InlineData("file:///etc/passwd")]
    [InlineData("//evil.example/f.jpg")]
    [InlineData("/a\\b.jpg")]
    [InlineData("https://")]
    [InlineData("https://storage.local/a b.jpg")]
    [InlineData("https://storage.local/a\tb.jpg")]
    [InlineData("storage.local/f.jpg")]
    [InlineData("f.jpg")]
    public void An_unsafe_or_malformed_address_is_refused(string url) => Assert.False(WorkerEkycService.IsSafePhotoUrl(url));

    [Theory]
    [InlineData("frontCccdUrl")]
    [InlineData("backCccdUrl")]
    [InlineData("selfieUrl")]
    public async Task A_bad_address_is_a_400_naming_its_field_and_nothing_is_called_or_saved(string field)
    {
        var worker = NewWorker();
        var repository = new SubmitRepository(worker);
        var provider = new RecordingProvider();
        var request = Request(
            field == "frontCccdUrl" ? "javascript:alert(1)" : "https://storage.local/f.jpg",
            field == "backCccdUrl" ? "data:image/png;base64,AAAA" : "https://storage.local/b.jpg",
            field == "selfieUrl" ? "https://storage.local/" + new string('a', 500) : "https://storage.local/s.jpg");

        var error = await Assert.ThrowsAsync<ValidationException>(() => Service(repository, provider).SubmitAsync(request, CancellationToken.None));

        Assert.Equal([field], error.Errors.Keys);
        Assert.Null(provider.Received);
        Assert.Equal(0, repository.Saves);
        Assert.Null(worker.EkycFrontCccdUrl);
        Assert.Equal(string.Empty, worker.KycStatus);
    }

    [Fact]
    public async Task Every_bad_address_is_reported_at_once()
    {
        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            Service(new SubmitRepository(NewWorker()), new RecordingProvider()).SubmitAsync(Request("javascript:x", "data:y", "ftp://z/q"), CancellationToken.None));

        Assert.Equal(["backCccdUrl", "frontCccdUrl", "selfieUrl"], error.Errors.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task An_address_of_exactly_500_characters_is_accepted_and_501_is_not()
    {
        var at500 = "https://storage.local/" + new string('a', 500 - "https://storage.local/".Length);
        Assert.Equal(500, at500.Length);

        var worker = NewWorker();
        await Service(new SubmitRepository(worker), new RecordingProvider()).SubmitAsync(Request(front: at500), CancellationToken.None);
        Assert.Equal(at500, worker.EkycFrontCccdUrl);

        await Assert.ThrowsAsync<ValidationException>(() =>
            Service(new SubmitRepository(NewWorker()), new RecordingProvider()).SubmitAsync(Request(front: at500 + "a"), CancellationToken.None));
    }

    [Theory]
    [InlineData("", "https://storage.local/b.jpg", "https://storage.local/s.jpg")]
    [InlineData("https://storage.local/f.jpg", "   ", "https://storage.local/s.jpg")]
    [InlineData("https://storage.local/f.jpg", "https://storage.local/b.jpg", "")]
    public async Task A_missing_address_is_still_the_400_of_ekycPhotos(string front, string back, string selfie)
    {
        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            Service(new SubmitRepository(NewWorker()), new RecordingProvider()).SubmitAsync(new SubmitEkycRequest(front, back, selfie), CancellationToken.None));

        Assert.Contains("ekycPhotos", error.Errors.Keys);
    }

    // ---- the Admin queue -----------------------------------------------------------------------

    [Fact]
    public async Task The_queue_returns_the_stored_addresses_and_nulls_for_a_row_older_than_the_columns()
    {
        var withPhotos = NewWorker();
        withPhotos.SetKycResult(70m, "MANUAL_REVIEW", "https://storage.local/f.jpg", "https://storage.local/b.jpg", "/files/ekyc/s.jpg");
        var legacy = Worker.CreateFreelancer("0900000043", "012000000043", "Legacy Worker");
        legacy.WorkerId = 43;
        legacy.SetKycResult(70m, "MANUAL_REVIEW");
        var admin = new AdminUser();

        var page = await Service(new SubmitRepository(withPhotos, legacy), new RecordingProvider(), admin).GetQueueAsync(1, 20, "PENDING", CancellationToken.None);

        var items = page.Data!.Items.ToDictionary(i => i.WorkerId);
        Assert.Equal(("https://storage.local/f.jpg", "https://storage.local/b.jpg", "/files/ekyc/s.jpg"),
            (items[WorkerId].FrontCccdUrl, items[WorkerId].BackCccdUrl, items[WorkerId].SelfieUrl));
        Assert.Equal((null, null, null), (items[43].FrontCccdUrl, items[43].BackCccdUrl, items[43].SelfieUrl));
    }

    // ---- doubles --------------------------------------------------------------------------------

    private sealed class RecordingProvider : IEkycProvider
    {
        public decimal Confidence { get; init; } = 92m;
        public EkycRequest? Received { get; private set; }

        public Task<EkycResult> VerifyAsync(EkycRequest request, CancellationToken cancellationToken = default)
        {
            Received = request;
            return Task.FromResult(new EkycResult(Confidence, false, "Fake Worker", request.NationalId));
        }
    }

    private sealed class WorkerUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public int? UserId => WorkerId;
        public UserRole? Role => UserRole.Worker;
    }

    private sealed class AdminUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public int? UserId => 1;
        public UserRole? Role => UserRole.Admin;
    }

    private sealed class SubmitRepository(params Worker[] workers) : IWorkerRepository
    {
        public int Saves { get; private set; }

        public Task<Worker?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(workers.FirstOrDefault(w => w.WorkerId == id));

        public void Update(Worker entity)
        {
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<Worker> Items, int TotalCount)> GetEkycQueueAsync(int page, int pageSize, string status = EkycQueueStatus.Pending, CancellationToken cancellationToken = default) =>
            Task.FromResult(((IReadOnlyList<Worker>)workers.ToList(), workers.Length));

        public Task<BookingSlot?> GetWorkerSlotByShiftAsync(int workerId, DateOnly slotDate, string shiftCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddSlotAsync(BookingSlot slot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void UpdateSlot(BookingSlot slot) => throw new NotSupportedException();
        public Task<bool> TryCompleteAssignmentAsync(long assignmentId, int workerId, decimal commissionRate, decimal payoutAmount, DateTime completedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker?> GetByNationalIdAsync(string nationalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByPhoneOrNationalIdAsync(string phoneNumber, string nationalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BookingSlot>> GetWorkerSlotsAsync(int workerId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<JobAssignment?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobPhoto>> GetJobPhotosAsync(long assignmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobPhoto>> GetAcceptedJobPhotosByPhaseAsync(long assignmentId, string photoPhase, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RetireAcceptedJobPhotosAsync(long assignmentId, string photoPhase, byte angleNo, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddJobPhotoAsync(JobPhoto photo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Worker>> ListAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker> AddAsync(Worker entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Delete(Worker entity) => throw new NotSupportedException();
    }
}
