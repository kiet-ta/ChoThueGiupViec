using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using MediatR;

namespace CommonService.Tests.Agencies;

/// <summary>BE-M5-09: the 30-minute self-swap window of an assigned Premium job (decisions Q09, spec 2.5 phase 2).</summary>
public class AgencySwapTests
{
    // The shift is 2026-10-15 08:00 Vietnam time = 2026-10-15 01:00 UTC; "now" is the day before, plenty of notice.
    private static readonly DateOnly ShiftDate = new(2026, 10, 15);
    private static readonly DateTime ShiftStartUtc = new(2026, 10, 15, 1, 0, 0, DateTimeKind.Utc);
    private const string Morning = "SHIFT_MORNING";

    private sealed class InMemorySwap : IAgencySwapRepository
    {
        public SwapAssignmentView Assignment { get; set; } = new(
            1001, 501, AgencyId: 7, WorkerId: 1, SlotId: 10, JobAssignmentStatus.Assigned, ShiftDate, Morning, new TimeOnly(8, 0), ServiceTier.Premium);

        public Dictionary<long, AgencySwapWindow> Windows { get; } = new();
        public Dictionary<int, (int WorkerId, string Status)> Slots { get; } = new()
        {
            [10] = (1, "BOOKED"),
            [20] = (2, "AVAILABLE"),
            [30] = (3, "AVAILABLE"),
        };

        public Dictionary<int, (int AgencyId, WorkStatus Status)> Workers { get; } = new()
        {
            [1] = (7, WorkStatus.Idle),
            [2] = (7, WorkStatus.Idle),
            [3] = (7, WorkStatus.Locked),
            [4] = (8, WorkStatus.Idle),
        };

        public Task<SwapAssignmentView?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SwapAssignmentView?>(assignmentId == Assignment.AssignmentId ? Assignment : null);

        public Task<AgencySwapWindow?> GetWindowAsync(long assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Windows.TryGetValue(assignmentId, out var w) ? w : null);

        public void AddWindow(AgencySwapWindow window) => Windows[window.AssignmentId] = window;

        public Task<bool> TryCloseWindowAsync(long assignmentId, string to, int? newWorkerId, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            if (!Windows.TryGetValue(assignmentId, out var w) || w.Status != "OPEN")
            {
                return Task.FromResult(false);
            }

            w.Status = to;
            w.NewWorkerId = newWorkerId;
            w.ClosedAt = nowUtc;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<AgencySwapWindow>> ListOpenAsync(int? agencyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgencySwapWindow>>(
                Windows.Values.Where(w => w.Status == "OPEN" && (agencyId == null || w.AgencyId == agencyId)).ToList());

        public Task<IReadOnlyList<AgencySwapWindow>> ListOpenDueAsync(DateTime nowUtc, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgencySwapWindow>>(Windows.Values.Where(w => w.Status == "OPEN" && w.DeadlineAt <= nowUtc).Take(take).ToList());

        public Task<WorkStatus?> GetAgencyWorkerStatusAsync(int agencyId, int workerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkStatus?>(Workers.TryGetValue(workerId, out var w) && w.AgencyId == agencyId ? w.Status : null);

        public Task<BookingSlot?> GetAvailableSlotAsync(int workerId, DateOnly date, string shiftCode, CancellationToken cancellationToken = default)
        {
            var slot = Slots.Where(s => s.Value.WorkerId == workerId && s.Value.Status == "AVAILABLE").Select(s => (int?)s.Key).FirstOrDefault();
            return Task.FromResult<BookingSlot?>(slot is null ? null : new BookingSlot { SlotId = slot.Value, WorkerId = workerId, SlotDate = date, ShiftCode = shiftCode });
        }

        public Task<bool> TrySwapAsync(long assignmentId, int oldSlotId, int newWorkerId, int newSlotId, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            if (Assignment.SlotId != oldSlotId || Slots[newSlotId].Status != "AVAILABLE")
            {
                return Task.FromResult(false);
            }

            Slots[newSlotId] = (newWorkerId, "BOOKED");
            Slots[oldSlotId] = (Slots[oldSlotId].WorkerId, "AVAILABLE");
            Assignment = Assignment with { WorkerId = newWorkerId, SlotId = newSlotId };
            return Task.FromResult(true);
        }
    }

    private sealed class PassThroughUnitOfWork : CommonService.Application.Interfaces.IRepositories.IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default) => action();
    }

    private sealed class RecordingPublisher : IPublisher
    {
        public List<object> Published { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            Published.Add(notification);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
        {
            Published.Add(notification!);
            return Task.CompletedTask;
        }
    }

    private sealed class Harness
    {
        public InMemorySwap Repo { get; } = new();
        public RecordingPublisher Publisher { get; } = new();
        public FakeClock Clock { get; } = new();
        public FakeCurrentUser User { get; } = new();
        public AgencySwapService Service { get; }

        public Harness(DateTime? now = null)
        {
            Clock.Set(now ?? new DateTime(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc));
            Service = new AgencySwapService(Repo, new PassThroughUnitOfWork(), Publisher, User, Clock);
            User.SignIn(7, UserRole.Partner);
        }
    }

    [Fact]
    public async Task Notice_over_two_hours_opens_a_30_minute_window()
    {
        var h = new Harness();

        var window = await h.Service.ReportUnavailableAsync(1001, new UnavailableRequest("worker is sick"));

        Assert.Equal("OPEN", window.State);
        Assert.Equal(h.Clock.UtcNow.AddMinutes(30), window.DeadlineAt);
        Assert.Equal(1800, window.RemainingSeconds);
        Assert.Equal(ShiftStartUtc, window.ShiftStartUtc);
        Assert.Empty(h.Publisher.Published);
    }

    [Fact]
    public async Task The_window_never_runs_past_two_hours_before_the_shift()
    {
        // 2 h 10 min before the shift: the 30 minutes would end after the 2 h line, so the window ends at the line (10 minutes).
        var h = new Harness(ShiftStartUtc.AddHours(-2).AddMinutes(-10));

        var window = await h.Service.ReportUnavailableAsync(1001, new UnavailableRequest("late"));

        Assert.Equal("OPEN", window.State);
        Assert.Equal(ShiftStartUtc.AddHours(-2), window.DeadlineAt);
        Assert.Equal(600, window.RemainingSeconds);
    }

    [Theory]
    [InlineData(120)] // exactly 2 h
    [InlineData(45)]
    public async Task Notice_of_two_hours_or_less_hands_over_to_the_rescue_at_once(int minutesBefore)
    {
        var h = new Harness(ShiftStartUtc.AddMinutes(-minutesBefore));

        var window = await h.Service.ReportUnavailableAsync(1001, new UnavailableRequest("no one"));

        Assert.Equal("RESCUE_STARTED", window.State);
        var evt = Assert.IsType<AgencySwapWindowExpired>(Assert.Single(h.Publisher.Published));
        Assert.Equal(1001, evt.AssignmentId);
        Assert.Equal(7, evt.AgencyId);
        Assert.Equal("EXPIRED", h.Repo.Windows[1001].Status);
    }

    [Fact]
    public async Task Swap_moves_the_job_and_the_slots_and_closes_the_window()
    {
        var h = new Harness();
        await h.Service.ReportUnavailableAsync(1001, new UnavailableRequest("sick"));

        var result = await h.Service.SwapWorkerAsync(1001, new SwapRequest(2));

        Assert.Equal("SWAPPED", result.State);
        Assert.Equal(2, result.NewWorkerId);
        Assert.Equal(2, h.Repo.Assignment.WorkerId);
        Assert.Equal(20, h.Repo.Assignment.SlotId);
        Assert.Equal("BOOKED", h.Repo.Slots[20].Status);
        Assert.Equal("AVAILABLE", h.Repo.Slots[10].Status);
        Assert.Equal("SWAPPED", h.Repo.Windows[1001].Status);
        Assert.Empty(h.Publisher.Published); // no rescue, no SLA penalty
    }

    [Fact]
    public async Task Swap_after_the_deadline_hands_over_and_changes_nothing()
    {
        var h = new Harness();
        await h.Service.ReportUnavailableAsync(1001, new UnavailableRequest("sick"));
        h.Clock.Advance(TimeSpan.FromMinutes(31));

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Service.SwapWorkerAsync(1001, new SwapRequest(2)));

        Assert.Equal(SwapErrorCodes.WindowClosed, ex.Code);
        Assert.Equal(1, h.Repo.Assignment.WorkerId);
        Assert.IsType<AgencySwapWindowExpired>(Assert.Single(h.Publisher.Published));
    }

    [Fact]
    public async Task The_sweep_hands_over_each_overdue_window_once()
    {
        var h = new Harness();
        await h.Service.ReportUnavailableAsync(1001, new UnavailableRequest("sick"));
        h.Clock.Advance(TimeSpan.FromMinutes(30));

        var first = await h.Service.ExpireDueAsync();
        var second = await h.Service.ExpireDueAsync();

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Single(h.Publisher.Published);
    }

    [Fact]
    public async Task A_swap_wins_against_the_sweep_and_the_sweep_then_does_nothing()
    {
        var h = new Harness();
        await h.Service.ReportUnavailableAsync(1001, new UnavailableRequest("sick"));
        await h.Service.SwapWorkerAsync(1001, new SwapRequest(2));
        h.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(0, await h.Service.ExpireDueAsync());
        Assert.Empty(h.Publisher.Published);
    }

    [Fact]
    public async Task The_replacement_must_be_an_available_worker_of_the_agency_with_a_free_slot_and_not_the_same_one()
    {
        var h = new Harness();
        await h.Service.ReportUnavailableAsync(1001, new UnavailableRequest("sick"));

        async Task<string?> CodeOf(int worker) =>
            (await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Service.SwapWorkerAsync(1001, new SwapRequest(worker)))).Code;

        Assert.Equal(SwapErrorCodes.SameWorker, await CodeOf(1));
        Assert.Equal(SwapErrorCodes.WorkerNotOfAgency, await CodeOf(4)); // another agency's worker
        Assert.Equal(SwapErrorCodes.WorkerNotAvailable, await CodeOf(3)); // locked
        h.Repo.Slots[20] = (2, "HELD");
        Assert.Equal(SwapErrorCodes.NoSlot, await CodeOf(2));
        Assert.Equal(1, h.Repo.Assignment.WorkerId);
    }

    [Fact]
    public async Task Someone_elses_assignment_is_not_found_and_a_non_assigned_one_is_a_conflict()
    {
        var h = new Harness();
        h.User.SignIn(99, UserRole.Partner);
        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.ReportUnavailableAsync(1001, new UnavailableRequest("x")));
        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.SwapWorkerAsync(1001, new SwapRequest(2)));

        h.User.SignIn(7, UserRole.Partner);
        h.Repo.Assignment = h.Repo.Assignment with { Status = JobAssignmentStatus.CheckedIn };
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Service.ReportUnavailableAsync(1001, new UnavailableRequest("x")));
        Assert.Equal(SwapErrorCodes.InvalidState, ex.Code);
    }

    [Fact]
    public async Task Reporting_twice_is_idempotent_and_a_swap_needs_an_open_window()
    {
        var h = new Harness();
        var noWindow = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Service.SwapWorkerAsync(1001, new SwapRequest(2)));
        Assert.Equal(SwapErrorCodes.WindowNotOpen, noWindow.Code);

        var first = await h.Service.ReportUnavailableAsync(1001, new UnavailableRequest("sick"));
        var again = await h.Service.ReportUnavailableAsync(1001, new UnavailableRequest("sick again"));

        Assert.Equal(first.DeadlineAt, again.DeadlineAt);
        Assert.Single(await h.Service.ListOpenAsync());
        await Assert.ThrowsAsync<ValidationException>(() => h.Service.ReportUnavailableAsync(1001, new UnavailableRequest(" ")));
        await Assert.ThrowsAsync<ValidationException>(() => h.Service.SwapWorkerAsync(1001, new SwapRequest(null)));
    }
}
