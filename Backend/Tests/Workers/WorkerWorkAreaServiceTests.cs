using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Workers;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>
/// GET / PUT /api/v1/workers/me/work-area (contract workers.md 2.3B, decisions D24, D40). Wards are the post-merger list of
/// provinces.open-api.vn v2, bundled as <c>vn-wards.json</c>: 26740 = Phường Sài Gòn (HCM; merged Bến Nghé, Đa Kao, Nguyễn Thái Bình),
/// 26743 = Phường Bến Thành (HCM), 27031 = Phường Tân Phú (HCM).
/// </summary>
public class WorkerWorkAreaServiceTests
{
    private const int SaiGon = 26740;
    private const int BenThanh = 26743;
    private const int TanPhu = 27031;

    private class TestCurrentUser : ICurrentUser
    {
        public int? UserId { get; set; }
        public UserRole? Role { get; set; } = UserRole.Worker;
        public bool IsAuthenticated => UserId.HasValue;
    }

    /// <summary>Two workers, like the table. Only what the service uses is implemented.</summary>
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
        public Task RetireAcceptedJobPhotosAsync(long assignmentId, string photoPhase, byte angleNo, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
        return (new WorkerWorkAreaService(repo, new TestCurrentUser { UserId = signedIn }, clock, new EmbeddedWardCatalog()), repo, mine, other);
    }

    private static UpdateWorkerWorkAreaRequest Valid(
        double? lat = 10.7769, double? lng = 106.7009, int? radius = 10, IReadOnlyList<int>? wardCodes = null) =>
        new(lat, lng, radius, null, wardCodes ?? [SaiGon, BenThanh]);

    private static UpdateWorkerWorkAreaRequest ValidNames(params string[] names) =>
        new(10.7769, 106.7009, 10, names, null);

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

        var saved = await service.SaveAsync(Valid(10.77691234, 106.70091234, 15, [BenThanh, SaiGon]));

        Assert.True(saved.Data!.IsSet);
        Assert.Equal(15, saved.Data.RadiusKm);
        // The order the worker chose, with the province of each ward.
        Assert.Equal([BenThanh, SaiGon], saved.Data.Wards!.Select(w => w.Code));
        Assert.Equal(["Phường Bến Thành", "Phường Sài Gòn"], saved.Data.Districts);
        Assert.All(saved.Data.Wards!, w => Assert.Equal((79, "Thành phố Hồ Chí Minh"), (w.ProvinceCode, w.ProvinceName)));
        Assert.Empty(saved.Data.UnresolvedNames!);
        Assert.Equal(new DateTime(2026, 10, 31, 3, 0, 0, DateTimeKind.Utc), saved.Data.SetAt);
        // Six decimals, like PUT /me/location (the columns are DECIMAL(9,6)).
        Assert.Equal(10.776912m, mine.WorkLat);
        Assert.Equal(106.700912m, mine.WorkLng);
        Assert.Equal((byte)15, mine.WorkRadiusKm);
        Assert.Equal("26743;26740", mine.WorkDistricts); // codes, not names
        Assert.Equal(1, repo.Saves);

        var read = await service.GetAsync();
        Assert.True(read.Data!.IsSet);
        Assert.Equal(saved.Data.Latitude, read.Data.Latitude);
        Assert.Equal([BenThanh, SaiGon], read.Data.Wards!.Select(w => w.Code));

        // Only the signed-in worker's row changed.
        Assert.Null(other.WorkLat);
        Assert.Null(other.WorkRadiusKm);
    }

    [Fact]
    public async Task Save_without_wards_is_fine_and_a_second_save_replaces_the_first()
    {
        var (service, _, mine, _) = Create();

        await service.SaveAsync(Valid(wardCodes: [TanPhu]));
        var second = await service.SaveAsync(Valid(10.80, 106.71, 5, []));

        Assert.Equal(5, second.Data!.RadiusKm);
        Assert.Empty(second.Data.Wards!);
        Assert.Empty(second.Data.Districts);
        Assert.Null(mine.WorkDistricts);
        Assert.Equal(10.8m, mine.WorkLat);
    }

    [Fact]
    public async Task The_old_districts_names_still_work_when_each_names_exactly_one_ward()
    {
        var (service, _, mine, _) = Create();

        // A current name, a pre-merger name (Bến Nghé was merged into Sài Gòn) and the same ward again with other spelling.
        var saved = await service.SaveAsync(ValidNames("  Phường Bến Thành ", "Phường Bến Nghé", "phường sài gòn"));

        Assert.Equal([BenThanh, SaiGon], saved.Data!.Wards!.Select(w => w.Code));
        Assert.Equal("26743;26740", mine.WorkDistricts);
    }

    [Fact]
    public async Task The_old_districts_names_that_are_unknown_or_ambiguous_are_refused()
    {
        var (service, repo, mine, _) = Create();

        foreach (var bad in new[]
        {
            "Quận 1",                    // the district level no longer exists
            "Phường Việt Hưng",          // a current name in Hà Nội and in Quảng Ninh
            "Phường Nguyễn Thái Bình",   // a pre-merger name that went into Sài Gòn and into Bến Thành
            "  ",
        })
        {
            var errors = await ErrorsOf(Task.Run<object>(async () => await service.SaveAsync(ValidNames("Phường Bến Thành", bad))));
            Assert.Contains("districts", errors.Keys);
        }

        Assert.Equal(0, repo.Saves);
        Assert.Null(mine.WorkDistricts);
    }

    [Fact]
    public async Task When_wardCodes_is_sent_the_old_districts_are_ignored()
    {
        var (service, _, _, _) = Create();

        var saved = await service.SaveAsync(new UpdateWorkerWorkAreaRequest(10.77, 106.70, 10, ["Quận 1"], [SaiGon]));

        Assert.Equal([SaiGon], saved.Data!.Wards!.Select(w => w.Code));
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
    public async Task Save_rejects_unknown_repeated_or_too_many_ward_codes()
    {
        var (service, repo, mine, _) = Create();
        var eleven = Enumerable.Range(0, 11).Select(i => 26740 + i).ToList();

        foreach (var bad in new int[][]
        {
            [1234567],            // not a ward code
            [SaiGon, SaiGon],     // repeated
            [0],
            [-5],
            eleven.ToArray(),     // more than 10
        })
        {
            var errors = await ErrorsOf(Task.Run<object>(async () => await service.SaveAsync(Valid(wardCodes: bad))));
            Assert.Contains("wardCodes", errors.Keys);
        }

        Assert.Equal(0, repo.Saves);
        Assert.Null(mine.WorkDistricts);
    }

    [Fact]
    public async Task Ten_ward_codes_are_the_most_and_fit_the_column()
    {
        var (service, _, mine, _) = Create();
        var catalog = new EmbeddedWardCatalog();
        // Ten different codes of the list (five digits at most, the column is NVARCHAR(500)).
        var ten = Enumerable.Range(26737, 400).Where(c => catalog.FindByCode(c) is not null).Take(10).ToList();
        Assert.Equal(10, ten.Count);

        var saved = await service.SaveAsync(Valid(wardCodes: ten));

        Assert.Equal(10, saved.Data!.Wards!.Count);
        Assert.True(mine.WorkDistricts!.Length <= 500);
    }

    [Fact]
    public async Task A_row_saved_with_names_before_reads_as_the_wards_they_became_and_a_read_writes_nothing()
    {
        var (service, repo, mine, _) = Create();
        await service.SaveAsync(Valid());
        var saves = repo.Saves;
        // How the row looked before ward codes: the names of the first picker, one of them since merged away, one no longer a place.
        mine.WorkDistricts = "Phường Bến Nghé;Phường Bến Thành;Quận 1;Phường Sài Gòn";

        var read = await service.GetAsync();

        // Bến Nghé -> Sài Gòn; Sài Gòn again collapses; Quận 1 cannot be placed.
        Assert.Equal([SaiGon, BenThanh], read.Data!.Wards!.Select(w => w.Code));
        Assert.Equal(["Phường Sài Gòn", "Phường Bến Thành"], read.Data.Districts);
        Assert.Equal(["Quận 1"], read.Data.UnresolvedNames);
        Assert.Equal("Phường Bến Nghé;Phường Bến Thành;Quận 1;Phường Sài Gòn", mine.WorkDistricts);
        Assert.Equal(saves, repo.Saves);

        // The next save stores codes.
        await service.SaveAsync(Valid(wardCodes: read.Data.Wards!.Select(w => w.Code).ToList()));
        Assert.Equal("26740;26743", mine.WorkDistricts);
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
