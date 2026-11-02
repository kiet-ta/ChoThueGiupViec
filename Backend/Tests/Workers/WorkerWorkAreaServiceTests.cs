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

/// <summary>GET / PUT /api/workers/me/work-area (contract workers.md 2.3B, decisions D24).</summary>
public class WorkerWorkAreaServiceTests
{
    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId { get; set; }
        public UserRole? Role { get; set; } = UserRole.Worker;
        public bool IsAuthenticated => UserId.HasValue;
    }

    /// <summary>Two workers, like the table. Only what the service uses is implemented.</summary>
    private sealed class WorkersOnly : IWorkerRepository
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
        public Task AddJobPhotoAsync(JobPhoto photo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCompleteAssignmentAsync(long assignmentId, int workerId, decimal commissionRate, decimal payoutAmount, DateTime completedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Worker>> ListAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker> AddAsync(Worker entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Delete(Worker entity) => throw new NotSupportedException();
    }

    private static (WorkerWorkAreaService Service, WorkersOnly Repo, Worker Mine, Worker Other) Create(int? signedIn = 1)
    {
        var repo = new WorkersOnly();
        var mine = Worker.CreateFreelancer("0901000001", "012345678901", "Mine");
        typeof(Worker).GetProperty(nameof(Worker.WorkerId))!.SetValue(mine, 1);
        var other = Worker.CreateFreelancer("0901000002", "012345678902", "Other");
        typeof(Worker).GetProperty(nameof(Worker.WorkerId))!.SetValue(other, 2);
        repo.Workers[1] = mine;
        repo.Workers[2] = other;
        var clock = new FakeClock();
        clock.Set(new DateTime(2026, 10, 31, 3, 0, 0, DateTimeKind.Utc));
        return (new WorkerWorkAreaService(repo, new TestCurrentUser { UserId = signedIn }, clock), repo, mine, other);
    }

    private static UpdateWorkerWorkAreaRequest Valid(
        double? lat = 10.7769, double? lng = 106.7009, int? radius = 10, IReadOnlyList<string>? districts = null) =>
        new(lat, lng, radius, districts ?? ["Quận 1", "Quận 3"]);

    private static async Task<IDictionary<string, string[]>> ErrorsOf(Task<object> call) =>
        (await Assert.ThrowsAsync<ValidationException>(async () => await call)).Errors;

    [Fact]
    public async Task Get_before_anything_is_saved_says_not_set_and_never_fails()
    {
        var (service, _, _, _) = Create();

        var result = await service.GetAsync();

        Assert.True(result.Success);
        Assert.False(result.Data!.IsSet);
        Assert.Null(result.Data.Latitude);
        Assert.Null(result.Data.Longitude);
        Assert.Null(result.Data.RadiusKm);
        Assert.Empty(result.Data.Districts);
        Assert.Null(result.Data.SetAt);
    }

    [Fact]
    public async Task Save_stores_the_area_and_get_returns_it()
    {
        var (service, repo, mine, other) = Create();

        var saved = await service.SaveAsync(Valid(10.77691234, 106.70091234, 15, ["Quận 1", "Quận 3"]));

        Assert.True(saved.Data!.IsSet);
        Assert.Equal(15, saved.Data.RadiusKm);
        Assert.Equal(["Quận 1", "Quận 3"], saved.Data.Districts);
        Assert.Equal(new DateTime(2026, 10, 31, 3, 0, 0, DateTimeKind.Utc), saved.Data.SetAt);
        // Six decimals, like PUT /me/location (the columns are DECIMAL(9,6)).
        Assert.Equal(10.776912m, mine.WorkLat);
        Assert.Equal(106.700912m, mine.WorkLng);
        Assert.Equal((byte)15, mine.WorkRadiusKm);
        Assert.Equal("Quận 1;Quận 3", mine.WorkDistricts);
        Assert.Equal(1, repo.Saves);

        var read = await service.GetAsync();
        Assert.True(read.Data!.IsSet);
        Assert.Equal(saved.Data.Latitude, read.Data.Latitude);
        Assert.Equal(["Quận 1", "Quận 3"], read.Data.Districts);

        // Only the signed-in worker's row changed.
        Assert.Null(other.WorkLat);
        Assert.Null(other.WorkRadiusKm);
    }

    [Fact]
    public async Task Save_without_districts_is_fine_and_a_second_save_replaces_the_first()
    {
        var (service, _, mine, _) = Create();

        await service.SaveAsync(Valid(districts: ["Quận 1"]));
        var second = await service.SaveAsync(Valid(10.80, 106.71, 5, []));

        Assert.Equal(5, second.Data!.RadiusKm);
        Assert.Empty(second.Data.Districts);
        Assert.Null(mine.WorkDistricts);
        Assert.Equal(10.8m, mine.WorkLat);
    }

    [Fact]
    public async Task Save_drops_duplicate_districts_and_trims_the_names()
    {
        var (service, _, _, _) = Create();

        var saved = await service.SaveAsync(Valid(districts: ["  Quận 1 ", "quận 1", "Quận 3"]));

        Assert.Equal(["Quận 1", "Quận 3"], saved.Data!.Districts);
    }

    [Theory]
    [InlineData(null, 106.0, 10, "latitude")]
    [InlineData(91.0, 106.0, 10, "latitude")]
    [InlineData(-91.0, 106.0, 10, "latitude")]
    [InlineData(double.NaN, 106.0, 10, "latitude")]
    [InlineData(10.0, null, 10, "longitude")]
    [InlineData(10.0, 181.0, 10, "longitude")]
    [InlineData(10.0, -181.0, 10, "longitude")]
    [InlineData(10.0, 106.0, null, "radiusKm")]
    [InlineData(10.0, 106.0, 0, "radiusKm")]
    [InlineData(10.0, 106.0, 7, "radiusKm")]
    [InlineData(10.0, 106.0, 20, "radiusKm")]
    public async Task Save_rejects_a_bad_field_with_a_field_error_and_changes_nothing(double? lat, double? lng, int? radius, string field)
    {
        var (service, repo, mine, _) = Create();

        var errors = await ErrorsOf(Task.Run<object>(async () => await service.SaveAsync(Valid(lat, lng, radius))));

        Assert.Contains(field, errors.Keys);
        Assert.Null(mine.WorkLat);
        Assert.Equal(0, repo.Saves);
    }

    [Fact]
    public async Task Save_rejects_too_many_blank_long_or_separated_districts()
    {
        var (service, repo, mine, _) = Create();
        var eleven = Enumerable.Range(1, 11).Select(i => $"Quận {i}").ToList();

        foreach (var bad in new string[][]
        {
            eleven.ToArray(),
            ["Quận 1", "  "],
            [new string('x', 101)],
            ["Quận 1;Quận 3"],
        })
        {
            var errors = await ErrorsOf(Task.Run<object>(async () => await service.SaveAsync(Valid(districts: bad))));
            Assert.Contains("districts", errors.Keys);
        }

        // 10 districts of 100 characters are the largest accepted.
        var largest = await service.SaveAsync(Valid(districts: Enumerable.Range(1, 10).Select(i => new string((char)('a' + i), 100)).ToList()));
        Assert.Equal(10, largest.Data!.Districts.Count);
        Assert.Equal(1, repo.Saves);
        Assert.NotNull(mine.WorkDistricts);
    }

    [Fact]
    public async Task A_missing_body_is_a_validation_error()
    {
        var (service, _, _, _) = Create();

        await Assert.ThrowsAsync<ValidationException>(() => service.SaveAsync(null));
    }

    [Fact]
    public async Task Without_a_signed_in_worker_both_calls_are_refused()
    {
        var (service, _, _, _) = Create(signedIn: null);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.GetAsync());
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.SaveAsync(Valid()));
    }

    [Fact]
    public async Task A_signed_in_id_without_a_profile_is_not_found()
    {
        var (service, _, _, _) = Create(signedIn: 99);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync());
    }
}
