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

/// <summary>GET / PUT /api/v1/workers/me/auto-accept (contract workers.md 2.3C, DISPATCH-AUTO-01 #890).</summary>
public class WorkerAutoAcceptServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 15, 3, 0, 0, DateTimeKind.Utc);

    private class TestCurrentUser : ICurrentUser
    {
        public int? UserId { get; set; }
        public UserRole? Role { get; set; } = UserRole.Worker;
        public bool IsAuthenticated => UserId.HasValue;
    }

    /// <summary>Only what the service uses is implemented.</summary>
    private class WorkersOnly : IWorkerRepository
    {
        public Dictionary<int, Worker> Workers { get; } = [];
        public int Saves { get; private set; }

        public Task<Worker?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Workers.GetValueOrDefault(id));

        public void Update(Worker entity)
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
        public Task<BookingSlot?> GetWorkerSlotByShiftAsync(int workerId, DateOnly slotDate, string shiftCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddSlotAsync(BookingSlot slot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void UpdateSlot(BookingSlot slot) => throw new NotSupportedException();
        public Task<JobAssignment?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobPhoto>> GetJobPhotosAsync(long assignmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobPhoto>> GetAcceptedJobPhotosByPhaseAsync(long assignmentId, string photoPhase, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RetireAcceptedJobPhotosAsync(long assignmentId, string photoPhase, byte angleNo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddJobPhotoAsync(JobPhoto photo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCompleteAssignmentAsync(long assignmentId, int workerId, decimal commissionRate, decimal payoutAmount, DateTime completedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Worker>> ListAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker> AddAsync(Worker entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Delete(Worker entity) => throw new NotSupportedException();
    }

    private static Worker WithId(Worker worker, int id)
    {
        typeof(Worker).GetProperty(nameof(Worker.WorkerId))!.SetValue(worker, id);
        return worker;
    }

    private static void GiveWorkArea(Worker worker)
    {
        worker.WorkLat = 10.7769m;
        worker.WorkLng = 106.7009m;
        worker.WorkRadiusKm = 10;
    }

    /// <summary>Worker 1 = freelancer with a work area, 2 = freelancer without one, 3 = agency staff with a work area.</summary>
    private static (WorkerAutoAcceptService Service, WorkersOnly Repo) Create(int? signedIn)
    {
        var repo = new WorkersOnly();
        var withArea = WithId(Worker.CreateFreelancer("0901000001", "012345678901", "Có khu vực"), 1);
        GiveWorkArea(withArea);
        repo.Workers[1] = withArea;
        repo.Workers[2] = WithId(Worker.CreateFreelancer("0901000002", "012345678902", "Chưa có khu vực"), 2);
        var staff = WithId(Worker.CreateAgencyStaff(9, "0901000003", "012345678903", "Nhân viên"), 3);
        GiveWorkArea(staff);
        repo.Workers[3] = staff;

        var clock = new FakeClock();
        clock.Set(Now);
        return (new WorkerAutoAcceptService(repo, new TestCurrentUser { UserId = signedIn }, clock), repo);
    }

    [Fact]
    public async Task A_new_worker_has_it_off()
    {
        var (service, _) = Create(signedIn: 1);

        var result = await service.GetAsync();

        Assert.True(result.Success);
        Assert.False(result.Data!.Enabled);
    }

    [Fact]
    public async Task A_freelancer_with_a_work_area_turns_it_on_then_off()
    {
        var (service, repo) = Create(signedIn: 1);

        var on = await service.SaveAsync(new UpdateWorkerAutoAcceptRequest(true));

        Assert.True(on.Data!.Enabled);
        Assert.True(repo.Workers[1].AutoAcceptOffers);
        Assert.Equal(Now, repo.Workers[1].UpdatedAt);
        Assert.True((await service.GetAsync()).Data!.Enabled);

        var off = await service.SaveAsync(new UpdateWorkerAutoAcceptRequest(false));

        Assert.False(off.Data!.Enabled);
        Assert.False(repo.Workers[1].AutoAcceptOffers);
        Assert.Equal(2, repo.Saves);
    }

    [Fact]
    public async Task Saving_the_same_value_again_writes_nothing()
    {
        var (service, repo) = Create(signedIn: 1);

        await service.SaveAsync(new UpdateWorkerAutoAcceptRequest(false));

        Assert.Equal(0, repo.Saves);
    }

    [Fact]
    public async Task Turning_it_on_without_a_work_area_is_a_conflict_and_nothing_is_stored()
    {
        var (service, repo) = Create(signedIn: 2);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => service.SaveAsync(new UpdateWorkerAutoAcceptRequest(true)));

        Assert.Equal(WorkerAutoAcceptErrorCodes.WorkAreaRequired, ex.Code);
        Assert.False(repo.Workers[2].AutoAcceptOffers);
        Assert.Equal(0, repo.Saves);
    }

    [Fact]
    public async Task Turning_it_off_without_a_work_area_is_allowed()
    {
        var (service, repo) = Create(signedIn: 2);
        repo.Workers[2].AutoAcceptOffers = true; // e.g. set before the work area rule

        var result = await service.SaveAsync(new UpdateWorkerAutoAcceptRequest(false));

        Assert.False(result.Data!.Enabled);
        Assert.False(repo.Workers[2].AutoAcceptOffers);
    }

    [Fact]
    public async Task Agency_staff_cannot_use_it_and_always_read_off()
    {
        var (service, repo) = Create(signedIn: 3);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => service.SaveAsync(new UpdateWorkerAutoAcceptRequest(true)));

        Assert.Equal(WorkerAutoAcceptErrorCodes.NotFreelancer, ex.Code);
        Assert.False(repo.Workers[3].AutoAcceptOffers);
        Assert.False((await service.GetAsync()).Data!.Enabled);
    }

    [Fact]
    public async Task A_missing_value_is_a_validation_error()
    {
        var (service, _) = Create(signedIn: 1);

        var empty = await Assert.ThrowsAsync<ValidationException>(() => service.SaveAsync(new UpdateWorkerAutoAcceptRequest(null)));
        var noBody = await Assert.ThrowsAsync<ValidationException>(() => service.SaveAsync(null));

        Assert.Contains("enabled", empty.Errors.Keys);
        Assert.Contains("enabled", noBody.Errors.Keys);
    }

    [Fact]
    public async Task Without_a_signed_in_worker_it_is_forbidden()
    {
        var (service, _) = Create(signedIn: null);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.GetAsync());
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.SaveAsync(new UpdateWorkerAutoAcceptRequest(true)));
    }
}
