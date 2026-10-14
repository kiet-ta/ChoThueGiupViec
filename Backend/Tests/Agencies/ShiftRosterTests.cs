using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Dispatch;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace CommonService.Tests.Agencies;

/// <summary>BE-M5-05: the agency's weekly shift roster (PRD 2.6/4.4): UNIQUE(worker, date, shift), no double booking, all-or-nothing writes.</summary>
public class ShiftRosterTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc); // Wednesday 2026-10-14, 10:00 Vietnam time
    private static readonly DateOnly Today = new(2026, 10, 14);
    private const string Morning = "SHIFT_MORNING";
    private const string Afternoon = "SHIFT_AFTERNOON";

    private sealed class InMemoryRoster : IRosterRepository
    {
        public Dictionary<int, (string Name, WorkStatus Status, int AgencyId)> Workers { get; } = new()
        {
            [1] = ("An", WorkStatus.Idle, 7),
            [2] = ("Binh", WorkStatus.Idle, 7),
            [3] = ("Chi", WorkStatus.Locked, 7),
            [9] = ("Other agency", WorkStatus.Idle, 8),
        };

        public List<BookingSlot> Slots { get; } = [];
        private int _next = 500;

        public Task<IReadOnlyDictionary<int, (string FullName, WorkStatus Status)>> GetAgencyWorkersAsync(
            int agencyId, IReadOnlyCollection<int> workerIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<int, (string, WorkStatus)>>(Workers
                .Where(w => w.Value.AgencyId == agencyId && workerIds.Contains(w.Key))
                .ToDictionary(w => w.Key, w => (w.Value.Name, w.Value.Status)));

        public Task<IReadOnlyList<RosterEntryDto>> ListAsync(int agencyId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RosterEntryDto>>(Slots
                .Where(s => s.AgencyId == agencyId && s.SlotDate >= fromDate && s.SlotDate <= toDate)
                .OrderBy(s => s.SlotDate).ThenBy(s => s.ShiftCode).ThenBy(s => s.WorkerId)
                .Select(s => new RosterEntryDto(s.SlotId, s.WorkerId, Workers[s.WorkerId].Name, s.SlotDate, s.ShiftCode,
                    s.StartTime.ToString("HH':'mm"), s.EndTime.ToString("HH':'mm"), s.SlotStatus))
                .ToList());

        public Task<IReadOnlyList<BookingSlot>> FindSlotsAsync(
            IReadOnlyCollection<(int WorkerId, DateOnly Date, string ShiftCode)> keys, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BookingSlot>>(Slots.Where(s => keys.Contains((s.WorkerId, s.SlotDate, s.ShiftCode))).ToList());

        /// <summary>Set to make the next write lose a race: the slot is "added by someone else" first and the write reports false.</summary>
        public bool LoseNextWrite { get; set; }

        public Task<bool> TryAddSlotsAsync(IReadOnlyCollection<BookingSlot> slots, CancellationToken cancellationToken = default)
        {
            if (LoseNextWrite)
            {
                LoseNextWrite = false;
                var first = slots.First();
                first.SlotId = _next++;
                Slots.Add(first);
                return Task.FromResult(false);
            }

            foreach (var slot in slots)
            {
                slot.SlotId = _next++;
                Slots.Add(slot);
            }

            return Task.FromResult(true);
        }

        public Task<BookingSlot?> GetOwnedSlotAsync(int agencyId, int slotId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Slots.FirstOrDefault(s => s.SlotId == slotId && s.AgencyId == agencyId));

        public void RemoveSlot(BookingSlot slot) => Slots.Remove(slot);
    }

    private sealed class PassThroughUnitOfWork : CommonService.Application.Interfaces.IRepositories.IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default) => action();
    }

    private static (ShiftRosterService Service, InMemoryRoster Repo) Create()
    {
        var repo = new InMemoryRoster();
        var user = new FakeCurrentUser();
        user.SignIn(7, UserRole.Partner);
        var clock = new FakeClock();
        clock.Set(Now);
        return (new ShiftRosterService(repo, new PassThroughUnitOfWork(), user, clock, MicrosoftOptions.Create(new BusinessRules())), repo);
    }

    private static RosterRequest Request(params (int Worker, DateOnly Date, string Shift)[] entries) =>
        new(entries.Select(e => new RosterRequestEntry(e.Worker, e.Date, e.Shift)).ToList());

    [Fact]
    public async Task Put_creates_available_agency_slots_with_the_shift_times()
    {
        var (service, repo) = Create();

        var result = await service.PutAsync(Request((1, Today.AddDays(1), Morning), (2, Today.AddDays(1), Afternoon)));

        Assert.Equal(2, result.Created);
        Assert.Equal(0, result.Unchanged);
        var slot = repo.Slots.Single(s => s.WorkerId == 1);
        Assert.Equal(7, slot.AgencyId);
        Assert.Equal("AGENCY", slot.SlotSource);
        Assert.Equal(DispatchSlotStatuses.Available, slot.SlotStatus);
        Assert.Equal(new TimeOnly(8, 0), slot.StartTime);
        Assert.Equal(new TimeOnly(12, 0), slot.EndTime);
        Assert.Equal(new TimeOnly(13, 0), repo.Slots.Single(s => s.WorkerId == 2).StartTime);
    }

    [Fact]
    public async Task A_slot_added_by_a_concurrent_request_is_a_slot_not_editable_conflict()
    {
        var (service, repo) = Create();
        repo.LoseNextWrite = true;

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => service.PutAsync(Request((1, Today.AddDays(1), Morning))));

        Assert.Equal(RosterErrorCodes.SlotNotEditable, ex.Code);
        Assert.Single(repo.Slots);
    }

    [Fact]
    public async Task Put_is_idempotent_for_an_existing_available_slot()
    {
        var (service, repo) = Create();
        await service.PutAsync(Request((1, Today.AddDays(1), Morning)));

        var again = await service.PutAsync(Request((1, Today.AddDays(1), Morning), (1, Today.AddDays(2), Morning)));

        Assert.Equal(1, again.Created);
        Assert.Equal(1, again.Unchanged);
        Assert.Equal(2, repo.Slots.Count);
    }

    [Fact]
    public async Task One_invalid_entry_rejects_the_whole_request_and_changes_nothing()
    {
        var (service, repo) = Create();

        var ex = await Assert.ThrowsAsync<RosterRejectedException>(() => service.PutAsync(Request(
            (1, Today.AddDays(1), Morning), // fine
            (9, Today.AddDays(1), Morning), // a worker of another agency
            (3, Today.AddDays(1), Morning), // locked
            (1, Today.AddDays(-1), Morning), // past
            (1, Today.AddDays(15), Morning), // beyond the 14-day horizon
            (2, Today.AddDays(1), "SHIFT_NIGHT")))); // unknown shift

        Assert.Equal(RosterErrorCodes.RosterInvalid, ex.Code);
        Assert.Equal(
            [1, 2, 3, 4, 5],
            ex.Errors.Select(e => e.Index).ToArray());
        Assert.Contains(ex.Errors, e => e.Index == 1 && e.Code == RosterErrorCodes.WorkerNotOfAgency);
        Assert.Contains(ex.Errors, e => e.Index == 2 && e.Code == RosterErrorCodes.WorkerLocked);
        Assert.Contains(ex.Errors, e => e.Index == 3 && e.Code == RosterErrorCodes.PastDate);
        Assert.Contains(ex.Errors, e => e.Index == 4 && e.Code == RosterErrorCodes.BeyondHorizon);
        Assert.Contains(ex.Errors, e => e.Index == 5 && e.Code == RosterErrorCodes.InvalidEntry);
        Assert.Empty(repo.Slots);
    }

    [Fact]
    public async Task The_same_worker_date_and_shift_twice_in_one_request_is_flagged_on_the_second()
    {
        var (service, _) = Create();

        var ex = await Assert.ThrowsAsync<RosterRejectedException>(() => service.PutAsync(Request(
            (1, Today.AddDays(1), Morning), (1, Today.AddDays(1), Morning))));

        var error = Assert.Single(ex.Errors);
        Assert.Equal(1, error.Index);
        Assert.Equal(RosterErrorCodes.DuplicateEntry, error.Code);
    }

    [Theory]
    [InlineData("HELD")]
    [InlineData("BOOKED")]
    [InlineData("BLOCKED")]
    public async Task A_slot_that_is_held_booked_or_blocked_cannot_be_rostered_again(string status)
    {
        var (service, repo) = Create();
        repo.Slots.Add(new BookingSlot
        {
            SlotId = 1,
            WorkerId = 1,
            AgencyId = 7,
            SlotDate = Today.AddDays(1),
            ShiftCode = Morning,
            SlotStatus = status,
            SlotSource = "AGENCY",
        });

        var ex = await Assert.ThrowsAsync<RosterRejectedException>(() => service.PutAsync(Request((1, Today.AddDays(1), Morning))));

        Assert.Equal(RosterErrorCodes.SlotNotEditable, ex.Errors.Single().Code);
    }

    [Fact]
    public async Task A_freelance_slot_toggle_of_the_same_worker_shift_blocks_the_roster_entry()
    {
        // UNIQUE(worker_id, slot_date, shift_code): a slot the worker made (no agency) is the same row, so no second slot is possible.
        var (service, repo) = Create();
        repo.Slots.Add(new BookingSlot
        {
            SlotId = 1,
            WorkerId = 1,
            AgencyId = null,
            SlotDate = Today.AddDays(1),
            ShiftCode = Morning,
            SlotStatus = "AVAILABLE",
            SlotSource = "FREELANCER",
        });

        await Assert.ThrowsAsync<RosterRejectedException>(() => service.PutAsync(Request((1, Today.AddDays(1), Morning))));
    }

    [Fact]
    public async Task Empty_request_is_a_validation_error()
    {
        var (service, _) = Create();

        await Assert.ThrowsAsync<ValidationException>(() => service.PutAsync(new RosterRequest([])));
        await Assert.ThrowsAsync<ValidationException>(() => service.PutAsync(new RosterRequest(null)));
    }

    [Fact]
    public async Task Week_view_covers_monday_to_sunday_of_the_requested_date()
    {
        var (service, _) = Create();
        await service.PutAsync(Request((1, new DateOnly(2026, 10, 15), Morning), (2, new DateOnly(2026, 10, 19), Morning)));

        var week = await service.GetWeekAsync(new DateOnly(2026, 10, 14)); // Wednesday: week of Monday 12 to Sunday 18 October

        var entry = Assert.Single(week);
        Assert.Equal(new DateOnly(2026, 10, 15), entry.SlotDate);
        Assert.Equal("An", entry.WorkerName);
        Assert.Equal("08:00", entry.StartTime);
        Assert.Single(await service.GetWeekAsync(new DateOnly(2026, 10, 19))); // next Monday starts a new week
    }

    [Fact]
    public async Task Only_an_available_slot_of_the_agency_can_be_removed()
    {
        var (service, repo) = Create();
        await service.PutAsync(Request((1, Today.AddDays(1), Morning)));
        var slotId = repo.Slots.Single().SlotId;

        await Assert.ThrowsAsync<NotFoundException>(() => service.RemoveAsync(slotId + 100));
        repo.Slots[0].SlotStatus = DispatchSlotStatuses.Held;
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => service.RemoveAsync(slotId));
        Assert.Equal(RosterErrorCodes.SlotNotEditable, ex.Code);

        repo.Slots[0].SlotStatus = DispatchSlotStatuses.Available;
        await service.RemoveAsync(slotId);
        Assert.Empty(repo.Slots);
    }

    [Fact]
    public async Task A_customer_cannot_use_the_roster()
    {
        var (_, repo) = Create();
        var user = new FakeCurrentUser();
        user.SignIn(1, UserRole.Customer);
        var service = new ShiftRosterService(repo, new PassThroughUnitOfWork(), user, new FakeClock(), MicrosoftOptions.Create(new BusinessRules()));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.GetWeekAsync(null));
    }
}
