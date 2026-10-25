using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Agencies;
using MediatR;

namespace CommonService.Tests.Agencies;

/// <summary>
/// AGENCY-FIX-01: an Agency retires a worker (AGENCY-UAT-02, fixed). In-memory world whose "database" restores itself when the
/// unit of work fails, and whose read-only worker copy models <c>AsNoTracking</c> (a change to it is lost), the two things the first version got wrong.
/// </summary>
public class WorkerRetirementTests
{
    private const int AgencyId = 7;
    private const string Morning = "SHIFT_MORNING";
    private const string Afternoon = "SHIFT_AFTERNOON";
    private static readonly DateTime Now = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 10, 14); // Vietnam date of Now (10:00 local)
    private static readonly DateOnly Tomorrow = Today.AddDays(1);
    private static readonly DateOnly DayAfter = Today.AddDays(2);

    private sealed class World : IAgencyRepository, IAgencySwapRepository, IUnitOfWork, IWorkerQuotaGuard, IPublisher
    {
        public List<Worker> Workers { get; private set; } = [];
        public List<BookingSlot> Slots { get; private set; } = [];
        public List<JobAssignment> Assignments { get; private set; } = [];
        public List<(object Event, bool InsideTransaction)> Published { get; } = [];
        public bool InTransaction { get; private set; }
        public int QuotaLimit { get; set; } = 100;
        public bool FailWhenAssignmentsMove { get; set; }
        public long? SwapFailsFor { get; set; }
        public Exception? PublishFails { get; set; }
        private int _nextWorker = 100;

        // ---- seeding --------------------------------------------------------------------------------------------
        public Worker AddWorker(int id, WorkStatus status = WorkStatus.Idle, int agencyId = AgencyId, string? phone = null, string? nid = null)
        {
            var w = Worker.CreateAgencyStaff(agencyId, phone ?? $"09000{id:D5}", nid ?? $"0123456{id:D5}", $"Worker {id}");
            w.WorkerId = id;
            ForceStatus(w, status);
            Workers.Add(w);
            return w;
        }

        public BookingSlot AddSlot(int id, int workerId, DateOnly date, string shift, string status)
        {
            var s = new BookingSlot { SlotId = id, WorkerId = workerId, SlotDate = date, ShiftCode = shift, SlotStatus = status };
            Slots.Add(s);
            return s;
        }

        public JobAssignment AddJob(long id, long orderId, int workerId, int slotId, JobAssignmentStatus status = JobAssignmentStatus.Assigned)
        {
            var a = new JobAssignment { AssignmentId = id, OrderId = orderId, WorkerId = workerId, SlotId = slotId, AgencyId = AgencyId };
            typeof(JobAssignment).GetProperty(nameof(JobAssignment.AssignmentStatus))!.SetValue(a, status);
            Assignments.Add(a);
            return a;
        }

        public static void ForceStatus(Worker w, WorkStatus status) =>
            typeof(Worker).GetProperty(nameof(Worker.WorkStatus))!.SetValue(w, status);

        public Worker WorkerOf(int id) => Workers.Single(w => w.WorkerId == id);
        public JobAssignment Job(long id) => Assignments.Single(a => a.AssignmentId == id);
        public BookingSlot Slot(int id) => Slots.Single(s => s.SlotId == id);

        // ---- IUnitOfWork: a failing action puts everything back ----------------------------------------------------
        public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default)
        {
            var workers = Workers.Select(w => (w, w.WorkStatus)).ToList();
            var slots = Slots.Select(s => (s, s.WorkerId, s.SlotStatus)).ToList();
            var jobs = Assignments.Select(a => (a, a.WorkerId, a.SlotId)).ToList();
            InTransaction = true;
            try
            {
                return await action();
            }
            catch
            {
                Workers = workers.Select(x => { ForceStatus(x.w, x.WorkStatus); return x.w; }).ToList();
                Slots = slots.Select(x => { x.s.WorkerId = x.WorkerId; x.s.SlotStatus = x.SlotStatus; return x.s; }).ToList();
                Assignments = jobs.Select(x => { x.a.WorkerId = x.WorkerId; x.a.SlotId = x.SlotId; return x.a; }).ToList();
                throw;
            }
            finally
            {
                InTransaction = false;
            }
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        // ---- IPublisher ---------------------------------------------------------------------------------------------
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Record(notification);

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification =>
            Record(notification!);

        private Task Record(object notification)
        {
            if (PublishFails is not null)
            {
                throw PublishFails;
            }

            Published.Add((notification, InTransaction));
            return Task.CompletedTask;
        }

        // ---- IWorkerQuotaGuard -----------------------------------------------------------------------------------------
        public Task EnsureCanAddWorkersAsync(int agencyId, int additionalWorkers, CancellationToken cancellationToken = default)
        {
            var counted = Workers.Count(w => w.AgencyId == agencyId && w.WorkStatus is not (WorkStatus.Locked or WorkStatus.Retiring or WorkStatus.Retired));
            return counted + additionalWorkers > QuotaLimit
                ? throw new BusinessRuleViolationException("Quota exceeded.", SubscriptionErrorCodes.QuotaExceeded)
                : Task.CompletedTask;
        }

        // ---- IAgencyRepository ---------------------------------------------------------------------------------------
        public Task<bool> TaxCodeExistsAsync(string taxCode, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task AddAsync(PartnerAgency agency, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<(IReadOnlyList<AgencyWorkerItem> Items, int TotalCount)> GetAgencyWorkersAsync(
            int agencyId, int page, int pageSize, string? statusFilter, CancellationToken cancellationToken = default) =>
            Task.FromResult(((IReadOnlyList<AgencyWorkerItem>)[], 0));

        /// <summary>A copy, like <c>AsNoTracking</c>: changing it changes nothing in the "database".</summary>
        public Task<Worker?> GetAgencyWorkerAsync(int agencyId, int workerId, CancellationToken cancellationToken = default)
        {
            var w = Workers.FirstOrDefault(x => x.WorkerId == workerId && x.AgencyId == agencyId);
            if (w is null)
            {
                return Task.FromResult<Worker?>(null);
            }

            var copy = Worker.CreateAgencyStaff(agencyId, w.PhoneNumber, w.NationalId, w.FullName);
            copy.WorkerId = w.WorkerId;
            ForceStatus(copy, w.WorkStatus);
            return Task.FromResult<Worker?>(copy);
        }

        public Task<Worker?> GetAgencyWorkerForUpdateAsync(int agencyId, int workerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Workers.FirstOrDefault(x => x.WorkerId == workerId && x.AgencyId == agencyId));

        public Task<IReadOnlyList<(int WorkerId, int AgencyId)>> ListRetiringWorkersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<(int WorkerId, int AgencyId)>>(
                Workers.Where(w => w.WorkStatus == WorkStatus.Retiring && w.AgencyId != null).Select(w => (w.WorkerId, w.AgencyId!.Value)).ToList());

        public Task<int> GetFutureAssignedJobCountAsync(int workerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Assignments.Count(a => a.WorkerId == workerId && a.AssignmentStatus == JobAssignmentStatus.Assigned));

        public Task<IReadOnlyList<FutureJob>> GetFutureAssignedJobsAsync(int workerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FutureJob>>(
                Assignments.Where(a => a.WorkerId == workerId && a.AssignmentStatus == JobAssignmentStatus.Assigned)
                    .Select(a => (a, s: Slot(a.SlotId)))
                    .OrderBy(x => x.s.SlotDate).ThenBy(x => x.s.ShiftCode)
                    .Select(x => new FutureJob(x.a.AssignmentId, x.a.OrderId, x.a.SlotId, x.s.SlotDate, x.s.ShiftCode))
                    .ToList());

        public Task<bool> HasActiveJobAsync(int workerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Assignments.Any(a => a.WorkerId == workerId
                && a.AssignmentStatus is JobAssignmentStatus.CheckedIn or JobAssignmentStatus.InProgress or JobAssignmentStatus.AwaitingAcceptance));

        public Task<int> CountFutureHeldSlotsAsync(int workerId, DateOnly fromDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Slots.Count(s => s.WorkerId == workerId && s.SlotStatus == DispatchSlotStatuses.Held && s.SlotDate >= fromDate));

        public Task<int> RemoveFutureAvailableSlotsAsync(int workerId, DateOnly fromDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Slots.RemoveAll(s => s.WorkerId == workerId && s.SlotStatus == DispatchSlotStatuses.Available && s.SlotDate >= fromDate
                && Assignments.All(a => a.SlotId != s.SlotId)));

        public Task<Worker> CreateAgencyStaffAsync(int agencyId, NewWorkerDetails details, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var w = Worker.CreateAgencyStaff(agencyId, details.PhoneNumber!, details.NationalId!, details.FullName!);
            w.WorkerId = ++_nextWorker;
            Workers.Add(w);
            return Task.FromResult(w);
        }

        public Task<bool> WorkerPhoneOrNationalIdExistsAsync(string phoneNumber, string nationalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Workers.Any(w => w.PhoneNumber == phoneNumber || w.NationalId == nationalId));

        public Task<int> UpdateWorkerSlotsAsync(int oldWorkerId, int newWorkerId, DateOnly fromDate, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var assigned = Assignments.Where(a => a.WorkerId == oldWorkerId && a.AssignmentStatus == JobAssignmentStatus.Assigned).Select(a => a.SlotId).ToHashSet();
            var moving = Slots.Where(s => s.WorkerId == oldWorkerId
                && ((s.SlotStatus == DispatchSlotStatuses.Held && s.SlotDate >= fromDate)
                    || (s.SlotStatus == DispatchSlotStatuses.Booked && assigned.Contains(s.SlotId)))).ToList();
            moving.ForEach(s => s.WorkerId = newWorkerId);
            return Task.FromResult(moving.Count);
        }

        public Task<int> UpdateWorkerAssignmentsAsync(int oldWorkerId, int newWorkerId, CancellationToken cancellationToken = default)
        {
            var moving = Assignments.Where(a => a.WorkerId == oldWorkerId && a.AssignmentStatus == JobAssignmentStatus.Assigned).ToList();
            moving.ForEach(a => a.WorkerId = newWorkerId);
            if (FailWhenAssignmentsMove)
            {
                throw new InvalidOperationException("the last step failed");
            }

            return Task.FromResult(moving.Count);
        }

        // ---- IAgencySwapRepository (only the two members the retirement uses) ---------------------------------------------
        public Task<BookingSlot?> GetAvailableSlotAsync(int workerId, DateOnly date, string shiftCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Slots.FirstOrDefault(s => s.WorkerId == workerId && s.SlotDate == date && s.ShiftCode == shiftCode && s.SlotStatus == DispatchSlotStatuses.Available));

        public Task<bool> TrySwapAsync(long assignmentId, int oldSlotId, int newWorkerId, int newSlotId, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var job = Assignments.Single(a => a.AssignmentId == assignmentId);
            var newSlot = Slots.Single(s => s.SlotId == newSlotId);
            if (job.SlotId != oldSlotId || newSlot.SlotStatus != DispatchSlotStatuses.Available || assignmentId == SwapFailsFor)
            {
                return Task.FromResult(false);
            }

            newSlot.SlotStatus = DispatchSlotStatuses.Booked;
            Slots.Single(s => s.SlotId == oldSlotId).SlotStatus = DispatchSlotStatuses.Available;
            job.WorkerId = newWorkerId;
            job.SlotId = newSlotId;
            return Task.FromResult(true);
        }

        public Task<SwapAssignmentView?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgencySwapWindow?> GetWindowAsync(long assignmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void AddWindow(AgencySwapWindow window) => throw new NotSupportedException();
        public Task<bool> TryCloseWindowAsync(long assignmentId, string to, int? newWorkerId, DateTime nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgencySwapWindow>> ListOpenAsync(int? agencyId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgencySwapWindow>> ListOpenDueAsync(DateTime nowUtc, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkStatus?> GetAgencyWorkerStatusAsync(int agencyId, int workerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static (World World, WorkerRetirementService Service, WorkerRetirementSweepService Sweep) Setup(Action<World>? seed = null)
    {
        var world = new World();
        seed?.Invoke(world);
        var clock = new FakeClock();
        clock.Set(Now);
        var user = new FakeCurrentUser();
        user.SignIn(AgencyId, UserRole.Partner);
        return (world,
            new WorkerRetirementService(world, world, world, world, world, user, clock),
            new WorkerRetirementSweepService(world, world, world, clock));
    }

    /// <summary>Worker 1 with one future ASSIGNED job (assignment 1001 of order 501, slot 10 on tomorrow's morning).</summary>
    private static void OneFutureJob(World w)
    {
        w.AddWorker(1);
        w.AddSlot(10, 1, Tomorrow, Morning, DispatchSlotStatuses.Booked);
        w.AddJob(1001, 501, 1, 10);
    }

    private static RetireWorkerRequest Existing(int id) => new() { Replacement = new RetireWorkerReplacement { ExistingWorkerId = id } };

    private static RetireWorkerRequest NewWorker(string phone = "0912345678", string nid = "012345678901", string name = "New Worker") => new()
    {
        Replacement = new RetireWorkerReplacement { NewWorker = new NewWorkerDetails { FullName = name, PhoneNumber = phone, NationalId = nid } }
    };

    private static async Task<string?> CodeOf(Task task)
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => task);
        return ex.Code;
    }

    // ---- ownership and idempotence (criterion 9) ------------------------------------------------------------------------

    [Fact]
    public async Task Unknown_or_foreign_worker_is_404()
    {
        var (w, service, _) = Setup(x => { x.AddWorker(1); x.AddWorker(2, agencyId: 8); });

        await Assert.ThrowsAsync<NotFoundException>(() => service.RetireWorkerAsync(2, new RetireWorkerRequest()));
        await Assert.ThrowsAsync<NotFoundException>(() => service.RetireWorkerAsync(999, new RetireWorkerRequest()));
        Assert.Equal(WorkStatus.Idle, w.WorkerOf(2).WorkStatus);
    }

    [Fact]
    public async Task Repeating_the_call_on_a_retiring_or_retired_worker_answers_its_status_and_does_nothing()
    {
        var (w, service, _) = Setup(x => { OneFutureJob(x); x.AddWorker(3, WorkStatus.Retired); });
        await service.RetireWorkerAsync(1, new RetireWorkerRequest { ConfirmRescue = true });
        w.Published.Clear();

        var again = await service.RetireWorkerAsync(1, new RetireWorkerRequest { ConfirmRescue = true });
        var retired = await service.RetireWorkerAsync(3, new RetireWorkerRequest());

        Assert.Equal("RETIRING", again.WorkStatus);
        Assert.False(again.RequiresConfirmation);
        Assert.Equal("RETIRED", retired.WorkStatus);
        Assert.Empty(w.Published);
    }

    [Fact]
    public async Task A_pending_worker_cannot_be_retired()
    {
        var (_, service, _) = Setup(x => x.AddWorker(1, WorkStatus.Pending));

        Assert.Equal(RetireErrorCodes.WorkerNotRetirable, await CodeOf(service.RetireWorkerAsync(1, new RetireWorkerRequest())));
    }

    // ---- preview (criterion 1) --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Without_replacement_and_confirmation_a_worker_with_future_jobs_gets_a_preview_and_nothing_changes()
    {
        var (w, service, _) = Setup(OneFutureJob);

        var r = await service.RetireWorkerAsync(1, new RetireWorkerRequest());

        Assert.True(r.RequiresConfirmation);
        var job = Assert.Single(r.FutureJobs!);
        Assert.Equal((1001L, 501L, Tomorrow, Morning), (job.AssignmentId, job.OrderId, job.SlotDate, job.ShiftCode));
        Assert.Equal(WorkStatus.Idle, w.WorkerOf(1).WorkStatus);
        Assert.Empty(w.Published);
    }

    // ---- status (criterion 4) -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(WorkStatus.Idle)]
    [InlineData(WorkStatus.Busy)]
    [InlineData(WorkStatus.Locked)]
    public async Task A_worker_without_any_job_goes_Retiring_then_Retired_in_one_call_and_it_is_saved(WorkStatus from)
    {
        var (w, service, _) = Setup(x => x.AddWorker(1, from));

        var r = await service.RetireWorkerAsync(1, new RetireWorkerRequest());

        Assert.Equal("RETIRED", r.WorkStatus);
        Assert.False(r.RequiresConfirmation);
        Assert.Equal(WorkStatus.Retired, w.WorkerOf(1).WorkStatus); // read-only copies are lost: this proves the tracked entity was changed
    }

    [Theory]
    [InlineData(JobAssignmentStatus.CheckedIn)]
    [InlineData(JobAssignmentStatus.InProgress)]
    [InlineData(JobAssignmentStatus.AwaitingAcceptance)]
    public async Task A_worker_with_a_job_in_progress_stays_Retiring(JobAssignmentStatus active)
    {
        var (w, service, _) = Setup(x =>
        {
            x.AddWorker(1);
            x.AddSlot(10, 1, Today, Morning, DispatchSlotStatuses.Booked);
            x.AddJob(1001, 501, 1, 10, active);
        });

        var r = await service.RetireWorkerAsync(1, new RetireWorkerRequest());

        Assert.Equal("RETIRING", r.WorkStatus);
        Assert.Equal(WorkStatus.Retiring, w.WorkerOf(1).WorkStatus);
    }

    [Fact]
    public async Task Future_AVAILABLE_slots_are_removed_but_a_slot_a_finished_job_points_to_stays()
    {
        var (w, service, _) = Setup(x =>
        {
            x.AddWorker(1);
            x.AddSlot(10, 1, Tomorrow, Morning, DispatchSlotStatuses.Available);
            x.AddSlot(11, 1, DayAfter, Morning, DispatchSlotStatuses.Available);
            x.AddSlot(12, 1, Tomorrow, Afternoon, DispatchSlotStatuses.Available);
            x.AddJob(900, 400, 1, 12, JobAssignmentStatus.Reassigned);
        });

        await service.RetireWorkerAsync(1, new RetireWorkerRequest());

        Assert.Equal([12], w.Slots.Select(s => s.SlotId).ToArray());
    }

    // ---- rescue hand-over (criterion 5) -------------------------------------------------------------------------------------

    [Fact]
    public async Task Confirmed_rescue_sets_Retiring_and_publishes_after_the_commit_with_the_real_order_id()
    {
        var (w, service, _) = Setup(x =>
        {
            OneFutureJob(x);
            x.AddSlot(11, 1, DayAfter, Morning, DispatchSlotStatuses.Booked);
            x.AddJob(1002, 502, 1, 11);
        });

        var r = await service.RetireWorkerAsync(1, new RetireWorkerRequest { ConfirmRescue = true });

        Assert.Equal("RETIRING", r.WorkStatus);
        Assert.Equal(WorkStatus.Retiring, w.WorkerOf(1).WorkStatus);
        var events = w.Published.Select(p => (AgencySwapWindowExpired: (AgencySwapWindowExpired)p.Event, p.InsideTransaction)).ToList();
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.False(e.InsideTransaction));
        Assert.Equal([(1001L, 501L), (1002L, 502L)], events.Select(e => (e.AgencySwapWindowExpired.AssignmentId, e.AgencySwapWindowExpired.OrderId)).ToArray());
        Assert.All(events, e => Assert.Equal((AgencyId, RetireErrorCodes.RescueReason), (e.AgencySwapWindowExpired.AgencyId, e.AgencySwapWindowExpired.Reason)));
    }

    [Fact]
    public async Task Nothing_is_published_when_the_retirement_fails()
    {
        var (w, service, _) = Setup(x => { OneFutureJob(x); x.AddSlot(11, 1, DayAfter, Morning, DispatchSlotStatuses.Held); });

        await CodeOf(service.RetireWorkerAsync(1, new RetireWorkerRequest { ConfirmRescue = true }));

        Assert.Empty(w.Published);
        Assert.Equal(WorkStatus.Idle, w.WorkerOf(1).WorkStatus);
    }

    [Fact]
    public async Task A_publish_that_fails_after_the_commit_does_not_undo_the_retirement()
    {
        var (w, service, _) = Setup(OneFutureJob);
        w.PublishFails = new InvalidOperationException("bus down");

        var r = await service.RetireWorkerAsync(1, new RetireWorkerRequest { ConfirmRescue = true });

        Assert.Equal("RETIRING", r.WorkStatus);
        Assert.Equal(WorkStatus.Retiring, w.WorkerOf(1).WorkStatus);
    }

    // ---- SLOT_HELD (criterion 6) ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_HELD_slot_without_a_new_worker_is_409_SLOT_HELD_and_nothing_changes()
    {
        var (w, service, _) = Setup(x => { OneFutureJob(x); x.AddSlot(11, 1, DayAfter, Morning, DispatchSlotStatuses.Held); x.AddWorker(2); });

        Assert.Equal(RetireErrorCodes.SlotHeld, await CodeOf(service.RetireWorkerAsync(1, new RetireWorkerRequest())));
        Assert.Equal(RetireErrorCodes.SlotHeld, await CodeOf(service.RetireWorkerAsync(1, new RetireWorkerRequest { ConfirmRescue = true })));
        Assert.Equal(RetireErrorCodes.SlotHeld, await CodeOf(service.RetireWorkerAsync(1, Existing(2))));
        Assert.Equal(WorkStatus.Idle, w.WorkerOf(1).WorkStatus);
        Assert.Equal(DispatchSlotStatuses.Held, w.Slot(11).SlotStatus);
    }

    // ---- existing worker as replacement (criteria 2, 3) -----------------------------------------------------------------------------

    [Fact]
    public async Task An_existing_worker_takes_every_future_job_with_the_real_slot_and_nobody_is_penalised()
    {
        var (w, service, _) = Setup(x =>
        {
            OneFutureJob(x);
            x.AddSlot(11, 1, DayAfter, Afternoon, DispatchSlotStatuses.Booked);
            x.AddJob(1002, 502, 1, 11);
            x.AddWorker(2);
            x.AddSlot(20, 2, Tomorrow, Morning, DispatchSlotStatuses.Available);
            x.AddSlot(21, 2, DayAfter, Afternoon, DispatchSlotStatuses.Available);
        });

        var r = await service.RetireWorkerAsync(1, Existing(2));

        Assert.Equal((2, 20, 2, 21), (w.Job(1001).WorkerId, w.Job(1001).SlotId, w.Job(1002).WorkerId, w.Job(1002).SlotId));
        Assert.Equal((DispatchSlotStatuses.Booked, DispatchSlotStatuses.Booked), (w.Slot(20).SlotStatus, w.Slot(21).SlotStatus));
        Assert.DoesNotContain(w.Slots, s => s.SlotId is 10 or 11); // the freed AVAILABLE slots of the retiring worker are removed
        Assert.Equal("RETIRED", r.WorkStatus); // every future job moved and none is in progress: nothing is left to wait for
        Assert.Equal(2, r.ReplacementWorkerId);
        Assert.Empty(w.Published);
    }

    [Fact]
    public async Task One_job_the_replacement_cannot_take_rejects_the_whole_call_and_lists_the_conflict()
    {
        var (w, service, _) = Setup(x =>
        {
            OneFutureJob(x);
            x.AddSlot(11, 1, DayAfter, Afternoon, DispatchSlotStatuses.Booked);
            x.AddJob(1002, 502, 1, 11);
            x.AddWorker(2);
            x.AddSlot(20, 2, Tomorrow, Morning, DispatchSlotStatuses.Available); // no slot for the day after
        });

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => service.RetireWorkerAsync(1, Existing(2)));

        Assert.Equal(RetireErrorCodes.ReplacementConflict, ex.Code);
        Assert.Contains($"{DayAfter:yyyy-MM-dd} {Afternoon}", ex.Message);
        Assert.Equal((1, 10, 1, 11), (w.Job(1001).WorkerId, w.Job(1001).SlotId, w.Job(1002).WorkerId, w.Job(1002).SlotId));
        Assert.Equal(DispatchSlotStatuses.Available, w.Slot(20).SlotStatus);
        Assert.Equal(WorkStatus.Idle, w.WorkerOf(1).WorkStatus);
    }

    [Fact]
    public async Task A_job_that_fails_to_move_after_another_moved_puts_the_first_back()
    {
        var (w, service, _) = Setup(x =>
        {
            OneFutureJob(x);
            x.AddSlot(11, 1, DayAfter, Afternoon, DispatchSlotStatuses.Booked);
            x.AddJob(1002, 502, 1, 11);
            x.AddWorker(2);
            x.AddSlot(20, 2, Tomorrow, Morning, DispatchSlotStatuses.Available);
            x.AddSlot(21, 2, DayAfter, Afternoon, DispatchSlotStatuses.Available);
        });
        w.SwapFailsFor = 1002; // the second job changed under us: TrySwap answers false for it

        Assert.Equal(RetireErrorCodes.ReplacementConflict, await CodeOf(service.RetireWorkerAsync(1, Existing(2))));

        Assert.Equal((1, 10), (w.Job(1001).WorkerId, w.Job(1001).SlotId));
        Assert.Equal(DispatchSlotStatuses.Available, w.Slot(20).SlotStatus);
    }

    [Theory]
    [InlineData(8, WorkStatus.Idle, RetireErrorCodes.ReplacementNotOfAgency)]
    [InlineData(2, WorkStatus.Locked, RetireErrorCodes.ReplacementNotAvailable)]
    [InlineData(2, WorkStatus.Retiring, RetireErrorCodes.ReplacementNotAvailable)]
    [InlineData(2, WorkStatus.Retired, RetireErrorCodes.ReplacementNotAvailable)]
    public async Task A_replacement_of_another_agency_or_unable_to_work_is_refused(int agencyOfReplacement, WorkStatus status, string code)
    {
        var (_, service, _) = Setup(x => { OneFutureJob(x); x.AddWorker(2, status, agencyId: agencyOfReplacement == 8 ? 8 : AgencyId); });

        Assert.Equal(code, await CodeOf(service.RetireWorkerAsync(1, Existing(2))));
    }

    [Fact]
    public async Task Both_or_neither_replacement_form_is_a_validation_error()
    {
        var (_, service, _) = Setup(OneFutureJob);
        var both = new RetireWorkerRequest { Replacement = new RetireWorkerReplacement { ExistingWorkerId = 2, NewWorker = new NewWorkerDetails() } };
        var neither = new RetireWorkerRequest { Replacement = new RetireWorkerReplacement() };

        await Assert.ThrowsAsync<ValidationException>(() => service.RetireWorkerAsync(1, both));
        await Assert.ThrowsAsync<ValidationException>(() => service.RetireWorkerAsync(1, neither));
    }

    // ---- new worker as replacement (criteria 2, 7) ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_new_worker_takes_over_the_future_slots_and_jobs_but_not_the_slot_of_a_job_in_progress()
    {
        var (w, service, _) = Setup(x =>
        {
            OneFutureJob(x);
            x.AddSlot(12, 1, Today, Afternoon, DispatchSlotStatuses.Booked);
            x.AddJob(1003, 503, 1, 12, JobAssignmentStatus.InProgress);
            x.AddSlot(13, 1, DayAfter, Morning, DispatchSlotStatuses.Held);
        });

        var r = await service.RetireWorkerAsync(1, NewWorker());

        var created = w.WorkerOf(r.ReplacementWorkerId!.Value);
        Assert.Equal(("0912345678", WorkStatus.Idle, AgencyId), (created.PhoneNumber, created.WorkStatus, created.AgencyId!.Value));
        Assert.Equal((created.WorkerId, created.WorkerId), (w.Job(1001).WorkerId, w.Slot(10).WorkerId));
        Assert.Equal(created.WorkerId, w.Slot(13).WorkerId); // a HELD slot (customer in checkout) goes with the new worker
        Assert.Equal((1, 1), (w.Job(1003).WorkerId, w.Slot(12).WorkerId)); // the job in progress stays with the worker who does it
        Assert.Equal("RETIRING", r.WorkStatus);
    }

    [Fact]
    public async Task A_failure_in_the_last_step_leaves_no_new_worker_no_moved_slot_no_moved_job()
    {
        var (w, service, _) = Setup(OneFutureJob);
        w.FailWhenAssignmentsMove = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RetireWorkerAsync(1, NewWorker()));

        Assert.Single(w.Workers);
        Assert.Equal((1, 1), (w.Slot(10).WorkerId, w.Job(1001).WorkerId));
        Assert.Equal(WorkStatus.Idle, w.WorkerOf(1).WorkStatus);
    }

    [Fact]
    public async Task The_new_worker_is_validated_like_an_import_row_and_a_taken_phone_or_national_id_is_ALREADY_EXISTS()
    {
        var (w, service, _) = Setup(x => { OneFutureJob(x); x.AddWorker(3, WorkStatus.Retired, phone: "0988888888", nid: "999999999999"); });

        var invalid = await Assert.ThrowsAsync<ValidationException>(() => service.RetireWorkerAsync(1, NewWorker(phone: "12", nid: "abc", name: "")));
        Assert.Contains("newWorker.phoneNumber", invalid.Errors.Keys);
        Assert.Contains("newWorker.nationalId", invalid.Errors.Keys);
        Assert.Contains("newWorker.fullName", invalid.Errors.Keys);

        Assert.Equal(RetireErrorCodes.DuplicatePhoneOrId, await CodeOf(service.RetireWorkerAsync(1, NewWorker(phone: "0988888888"))));
        Assert.Equal(RetireErrorCodes.DuplicatePhoneOrId, await CodeOf(service.RetireWorkerAsync(1, NewWorker(nid: "999999999999"))));
        Assert.Equal(2, w.Workers.Count);
    }

    // ---- quota (criterion 7) ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_one_for_one_replacement_of_a_counted_worker_runs_no_quota_check()
    {
        var (w, service, _) = Setup(x => { x.AddWorker(1); x.AddWorker(2); });
        w.QuotaLimit = 2; // full: a quota check would fail

        var r = await service.RetireWorkerAsync(1, NewWorker());

        Assert.Equal("RETIRED", r.WorkStatus);
    }

    [Fact]
    public async Task Replacing_a_Locked_worker_that_was_not_counted_runs_the_quota_check_and_a_failure_rolls_everything_back()
    {
        var (w, service, _) = Setup(x => { x.AddWorker(1, WorkStatus.Locked); x.AddWorker(2); });
        w.QuotaLimit = 1;

        Assert.Equal(SubscriptionErrorCodes.QuotaExceeded, await CodeOf(service.RetireWorkerAsync(1, NewWorker())));

        Assert.Equal(2, w.Workers.Count);
        Assert.Equal(WorkStatus.Locked, w.WorkerOf(1).WorkStatus);
    }

    // ---- sweep (criterion 8) ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_sweep_turns_a_Retiring_worker_without_any_job_into_Retired_and_saves_it()
    {
        var (w, _, sweep) = Setup(x => x.AddWorker(1, WorkStatus.Retiring));

        var (retired, republished) = await sweep.SweepAsync();

        Assert.Equal((1, 0), (retired, republished));
        Assert.Equal(WorkStatus.Retired, w.WorkerOf(1).WorkStatus);
    }

    [Fact]
    public async Task The_sweep_leaves_a_Retiring_worker_with_a_job_in_progress_alone()
    {
        var (w, _, sweep) = Setup(x =>
        {
            x.AddWorker(1, WorkStatus.Retiring);
            x.AddSlot(10, 1, Today, Morning, DispatchSlotStatuses.Booked);
            x.AddJob(1001, 501, 1, 10, JobAssignmentStatus.InProgress);
        });

        var (retired, republished) = await sweep.SweepAsync();

        Assert.Equal((0, 0), (retired, republished));
        Assert.Equal(WorkStatus.Retiring, w.WorkerOf(1).WorkStatus);
    }

    [Fact]
    public async Task The_sweep_hands_the_future_jobs_of_a_Retiring_worker_to_the_rescue_again_with_the_real_order_id()
    {
        var (w, _, sweep) = Setup(x => { OneFutureJob(x); SetRetiring(x.WorkerOf(1)); });

        var (retired, republished) = await sweep.SweepAsync();

        Assert.Equal((0, 1), (retired, republished));
        Assert.Equal(WorkStatus.Retiring, w.WorkerOf(1).WorkStatus);
        var e = Assert.IsType<AgencySwapWindowExpired>(Assert.Single(w.Published).Event);
        Assert.Equal((1001L, 501L, AgencyId), (e.AssignmentId, e.OrderId, e.AgencyId));
    }

    [Fact]
    public async Task One_worker_failing_in_the_sweep_does_not_stop_the_others()
    {
        var (w, _, sweep) = Setup(x => { OneFutureJob(x); SetRetiring(x.WorkerOf(1)); x.AddWorker(2, WorkStatus.Retiring); });
        w.PublishFails = new InvalidOperationException("bus down");

        var (retired, _) = await sweep.SweepAsync();

        Assert.Equal(1, retired);
        Assert.Equal(WorkStatus.Retired, w.WorkerOf(2).WorkStatus);
    }

    [Fact]
    public void The_sweep_runs_every_five_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), WorkerRetirementSweepWorker.SweepInterval);
    }

    private static void SetRetiring(Worker w) => w.TransitionTo(WorkStatus.Retiring);
}
