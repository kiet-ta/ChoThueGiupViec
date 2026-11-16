using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace CommonService.Tests.Dispatch;

/// <summary>
/// DISPATCH-CANCEL-01 (#902), decisions Q15 + D35: a freelancer cancels an accepted job. The seat is released, the order is re-dispatched
/// without that worker until job start - 30 min, the customer is told, and a seat still open at the cutoff fails the order (automatic
/// 100 % refund through AssignmentFailed). Every cancel counts 1; the 3rd inside 30 days locks the freelancer. No money penalty.
/// </summary>
public class WorkerCancelServiceTests
{
    private static readonly DateOnly JobDate = new(2026, 11, 20);

    // SHIFT_MORNING 08:00 local (UTC+7) = 01:00 UTC.
    private static readonly DateTime JobStartUtc = new(2026, 11, 20, 1, 0, 0, DateTimeKind.Utc);
    private static readonly GeoPoint Home = new(21.028511, 105.804817);
    private const long OrderId = 601;
    private const int CustomerId = 5;

    private class RecordingMediator : IMediator
    {
        public List<object> Published { get; } = [];
        public Task Publish(object notification, CancellationToken cancellationToken = default) { Published.Add(notification); return Task.CompletedTask; }
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification { Published.Add(notification!); return Task.CompletedTask; }
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotImplementedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private class TestUser : ICurrentUser
    {
        public int? UserId { get; set; }
        public UserRole? Role { get; set; } = UserRole.Worker;
        public bool IsAuthenticated => UserId != null;
    }

    /// <summary>The cancel transaction over <see cref="InMemoryDispatchRepository"/>, with the same conditional semantics as the EF one.</summary>
    private class InMemoryCancelRepository(InMemoryDispatchRepository dispatch) : IWorkerCancelRepository
    {
        public Dictionary<long, JobOrder> Orders { get; } = new();

        /// <summary>Earlier cancels of a worker on other orders (their moments), counted like CANCELLED_BY_WORKER rows.</summary>
        public Dictionary<int, List<DateTime>> EarlierCancels { get; } = new();

        public HashSet<int> Locked { get; } = new();

        public Task<JobOrder?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Orders.TryGetValue(orderId, out var order) ? order : null);

        public Task<int> CountWorkerCancelsSinceAsync(int workerId, DateTime sinceUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(Count(workerId, sinceUtc));

        public Task<bool> IsAutoAcceptOnAsync(int workerId, CancellationToken cancellationToken = default) =>
            dispatch.IsAutoAcceptOnAsync(workerId, cancellationToken);

        public async Task<WorkerCancelCommit?> TryCancelAsync(
            long assignmentId, int workerId, DateTime cancelledAtUtc, DateTime windowStartUtc, int lockCount, CancellationToken cancellationToken = default)
        {
            var a = dispatch.Assignments[assignmentId];
            if (a.WorkerId != workerId || a.AssignmentStatus != JobAssignmentStatus.Assigned)
            {
                return null;
            }

            a.TransitionTo(JobAssignmentStatus.CancelledByWorker);
            a.UpdatedAt = cancelledAtUtc;
            if (dispatch.Slots[a.SlotId].Status == DispatchSlotStatuses.Booked)
            {
                dispatch.Slots[a.SlotId] = (workerId, DispatchSlotStatuses.Available);
            }

            if (!await dispatch.TryMoveOrderStatusAsync(a.OrderId, JobOrderStatus.Assigned, JobOrderStatus.Dispatching, cancelledAtUtc, cancellationToken))
            {
                await dispatch.TryMoveOrderStatusAsync(a.OrderId, JobOrderStatus.AwaitingFallback, JobOrderStatus.Dispatching, cancelledAtUtc, cancellationToken);
            }

            var autoAcceptWasOn = dispatch.AutoAcceptWorkerIds.Remove(workerId);
            var count = Count(workerId, windowStartUtc);
            var locked = count >= lockCount && Locked.Add(workerId);
            return new WorkerCancelCommit(count, locked, autoAcceptWasOn);
        }

        public Task<IReadOnlyList<JobOrder>> GetRedispatchingAfterWorkerCancelAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JobOrder>>(Orders.Values
                .Where(o => dispatch.Orders.TryGetValue(o.OrderId, out var ctx) && ctx.OrderStatus == JobOrderStatus.Dispatching)
                .Where(o => o.ScheduledDate >= fromDate && o.ScheduledDate <= toDate)
                .Where(o => dispatch.Assignments.Values.Any(a => a.OrderId == o.OrderId && a.AssignmentStatus == JobAssignmentStatus.CancelledByWorker))
                .ToList());

        public Task<bool> HasWorkerCancelledSeatAsync(long orderId, byte? assignmentSeq, CancellationToken cancellationToken = default) =>
            Task.FromResult(dispatch.Assignments.Values.Any(a => a.OrderId == orderId
                && a.AssignmentStatus == JobAssignmentStatus.CancelledByWorker
                && (assignmentSeq == null || a.AssignmentSeq == assignmentSeq)));

        private int Count(int workerId, DateTime sinceUtc) =>
            dispatch.Assignments.Values.Count(x => x.WorkerId == workerId && x.AssignmentStatus == JobAssignmentStatus.CancelledByWorker && x.UpdatedAt >= sinceUtc)
            + (EarlierCancels.TryGetValue(workerId, out var times) ? times.Count(t => t >= sinceUtc) : 0);
    }

    private class PassThroughUnitOfWork : CommonService.Application.Interfaces.IRepositories.IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default) => action();
    }

    /// <summary>The swap window store, over the assignments of the dispatch fake (only what an agency worker's cancel touches).</summary>
    private class InMemorySwapRepository(InMemoryDispatchRepository dispatch) : IAgencySwapRepository
    {
        public Dictionary<long, AgencySwapWindow> Windows { get; } = new();

        public Task<SwapAssignmentView?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SwapAssignmentView?>(dispatch.Assignments.TryGetValue(assignmentId, out var a) && a.AgencyId is int agency
                ? new SwapAssignmentView(a.AssignmentId, a.OrderId, agency, a.WorkerId, a.SlotId, a.AssignmentStatus,
                    JobDate, BookingShifts.Morning, new TimeOnly(8, 0), ServiceTier.Premium)
                : null);

        public Task<AgencySwapWindow?> GetWindowAsync(long assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Windows.TryGetValue(assignmentId, out var w) ? w : null);

        public void AddWindow(AgencySwapWindow window) => Windows[window.AssignmentId] = window;

        public Task<bool> TryCloseWindowAsync(long assignmentId, string to, int? newWorkerId, DateTime nowUtc, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<AgencySwapWindow>> ListOpenAsync(int? agencyId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<AgencySwapWindow>> ListOpenDueAsync(DateTime nowUtc, int take, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<WorkStatus?> GetAgencyWorkerStatusAsync(int agencyId, int workerId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<BookingSlot?> GetAvailableSlotAsync(int workerId, DateOnly date, string shiftCode, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<bool> TrySwapAsync(long assignmentId, int oldSlotId, int newWorkerId, int newSlotId, DateTime nowUtc, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private class Harness
    {
        public FakeClock Clock { get; } = new();
        public InMemorySwapRepository SwapRepo { get; }
        public FakeWorkerAvailabilityQuery Query { get; } = new();
        public InMemoryDispatchRepository Repo { get; } = new();
        public InMemoryCancelRepository Cancels { get; }
        public TestUser User { get; } = new();
        public RecordingMediator Mediator { get; } = new();
        public FakeNotificationService Notifications { get; } = new();
        public DispatchOfferEngine Engine { get; }
        public WorkerCancelService Service { get; }

        public Harness(DateTime? nowUtc = null)
        {
            Clock.Set(nowUtc ?? JobStartUtc.AddDays(-1));
            Cancels = new InMemoryCancelRepository(Repo);
            var rules = MicrosoftOptions.Create(new BusinessRules());
            Engine = new DispatchOfferEngine(new SteppedRadiusDispatchScanner(Query, rules), Repo, Clock, Mediator, rules);
            SwapRepo = new InMemorySwapRepository(Repo);
            var swap = new AgencySwapService(SwapRepo, new PassThroughUnitOfWork(), Mediator, User, Clock);
            Service = new WorkerCancelService(Cancels, Repo, Engine, Clock, Mediator, rules, User, Notifications, agencySwap: swap);
        }

        public void AddWorker(int workerId, int slotId, double distanceKm = 1.0)
        {
            Query.Workers.Add(new AvailableWorker(workerId, slotId, distanceKm));
            Repo.AddSlot(slotId, workerId);
        }

        /// <summary>Worker <paramref name="workerId"/> accepted the only seat of the order (order ASSIGNED, slot BOOKED).</summary>
        public async Task<long> AssignedAsync(int workerId, int slotId, int? agencyId = null)
        {
            AddWorker(workerId, slotId);
            Repo.Orders[OrderId] = new DispatchOrderContext(OrderId, CustomerId, ServiceTier.Economy, JobDate, BookingShifts.Morning, 1, Home, 260000m);
            var order = new JobOrder
            {
                OrderId = OrderId,
                OrderCode = "GV601",
                CustomerId = CustomerId,
                ServiceTier = ServiceTier.Economy,
                ScheduledDate = JobDate,
                ShiftCode = BookingShifts.Morning,
                RequiredWorkers = 1,
                TotalAmount = 260000m,
            };
            Cancels.Orders[OrderId] = order;
            var offer = (await Engine.StartDispatchForOrderAsync(OrderId, CustomerId, ServiceTier.Economy, JobDate, BookingShifts.Morning, Home, 260000m))!;
            Assert.True((await Engine.AcceptOfferAsync(offer.AssignmentId, workerId)).Success);
            Repo.Assignments[offer.AssignmentId].AgencyId = agencyId;
            Query.Workers.RemoveAll(w => w.WorkerId == workerId); // busy now: only other workers are candidates
            User.UserId = workerId;
            Mediator.Published.Clear();
            return offer.AssignmentId;
        }
    }

    [Fact]
    public async Task Cancel_releases_the_slot_turns_auto_accept_off_and_offers_the_seat_to_another_worker_only()
    {
        var h = new Harness();
        var id = await h.AssignedAsync(42, slotId: 7);
        h.Repo.AutoAcceptWorkerIds.Add(42);
        h.AddWorker(43, slotId: 8);

        var result = await h.Service.CancelAsync(id, "  Xe hỏng, không đi được  ");

        Assert.Equal("CANCELLED_BY_WORKER", result.Status);
        Assert.Equal(WorkerCancelOutcomes.Redispatching, result.OrderOutcome);
        Assert.Equal(JobStartUtc.AddMinutes(-30), result.SubstituteSearchUntil);
        Assert.Equal(1, result.CancelCountInWindow);
        Assert.False(result.AccountLocked);
        Assert.Equal(JobAssignmentStatus.CancelledByWorker, h.Repo.Assignments[id].AssignmentStatus);
        Assert.Equal(DispatchSlotStatuses.Available, h.Repo.SlotStatus(7)); // D2: the worker gets the slot back
        Assert.DoesNotContain(42, h.Repo.AutoAcceptWorkerIds); // D33 open item 5

        var replacement = Assert.Single(h.Repo.Assignments.Values, a => a.AssignmentStatus == JobAssignmentStatus.Offered);
        Assert.Equal(43, replacement.WorkerId);
        Assert.Equal(1, replacement.AssignmentSeq); // D7: the same seat
        Assert.Equal(JobOrderStatus.Dispatching, h.Repo.Orders[OrderId].OrderStatus);

        var evt = Assert.Single(h.Mediator.Published.OfType<AssignmentCancelledByWorker>());
        Assert.Equal("Xe hỏng, không đi được", evt.Reason);
        Assert.Empty(h.Mediator.Published.OfType<AssignmentFailed>());
        var notice = Assert.Single(h.Notifications.Sent, m => m.RecipientRole == UserRole.Customer);
        Assert.Equal((CustomerId, WorkerCancelNotificationTopics.WorkerCancelled), (notice.RecipientId, notice.Topic));
        var workerNotice = Assert.Single(h.Notifications.Sent, m => m.RecipientRole == UserRole.Worker); // #891: the switch went off
        Assert.Equal((42, WorkerCancelNotificationTopics.AutoAcceptOff), (workerNotice.RecipientId, workerNotice.Topic));
    }

    [Fact]
    public async Task Worker_is_told_auto_accept_turned_off_only_when_it_was_on_even_inside_the_cutoff()
    {
        // #891 item 3: "Tự động nhận đơn đã tắt vì bạn vừa hủy ca".
        var on = new Harness();
        var id = await on.AssignedAsync(42, slotId: 7);
        on.Repo.AutoAcceptWorkerIds.Add(42);
        on.Clock.Set(JobStartUtc.AddMinutes(-10)); // no substitute search, the order fails at once

        await on.Service.CancelAsync(id, "Ốm");

        var notice = Assert.Single(on.Notifications.Sent);
        Assert.Equal((UserRole.Worker, 42, WorkerCancelNotificationTopics.AutoAcceptOff), (notice.RecipientRole, notice.RecipientId, notice.Topic));
        Assert.StartsWith("Tự động nhận đơn đã tắt vì bạn vừa hủy ca", notice.Body);
        Assert.Equal((OrderId.ToString(), id.ToString()), (notice.Data!["orderId"], notice.Data["assignmentId"]));

        var off = new Harness();
        var job = await off.AssignedAsync(42, slotId: 7);
        off.Clock.Set(JobStartUtc.AddMinutes(-10));
        await off.Service.CancelAsync(job, "Ốm");
        Assert.Empty(off.Notifications.Sent); // the switch was already off: nothing to tell
    }

    [Fact]
    public async Task With_nobody_else_available_the_order_fails_for_a_100_percent_refund()
    {
        var h = new Harness();
        var id = await h.AssignedAsync(42, slotId: 7);

        await h.Service.CancelAsync(id, "Bận việc gia đình");

        // The cancelling worker is never offered the order again; the engine found nobody and failed the order.
        Assert.DoesNotContain(h.Repo.Assignments.Values, a => a.AssignmentStatus == JobAssignmentStatus.Offered);
        Assert.Single(h.Mediator.Published.OfType<AssignmentFailed>());
    }

    [Theory]
    [InlineData(30)] // exactly the cutoff
    [InlineData(10)]
    public async Task Inside_the_cutoff_the_order_fails_at_once_without_searching(int minutesBeforeStart)
    {
        var h = new Harness();
        var id = await h.AssignedAsync(42, slotId: 7);
        h.AddWorker(43, slotId: 8);
        h.Clock.Set(JobStartUtc.AddMinutes(-minutesBeforeStart));

        var result = await h.Service.CancelAsync(id, "Ốm");

        Assert.Equal(WorkerCancelOutcomes.CancelledRefunded, result.OrderOutcome);
        Assert.Null(result.SubstituteSearchUntil);
        Assert.Equal(WorkerCancelReasons.NoSubstitute, Assert.Single(h.Mediator.Published.OfType<AssignmentFailed>()).Reason);
        Assert.DoesNotContain(h.Repo.Assignments.Values, a => a.AssignmentStatus == JobAssignmentStatus.Offered);
        Assert.Empty(h.Notifications.Sent); // the no-substitute notice comes from the AssignmentFailed handler
    }

    [Fact]
    public async Task Refusals_404_not_own_403_agency_409_not_assigned_409_started_400_reason()
    {
        var h = new Harness();
        var id = await h.AssignedAsync(42, slotId: 7);

        h.User.UserId = 99;
        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.CancelAsync(id, "x"));
        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.CancelAsync(9999, "x"));

        h.User.UserId = 42;
        await Assert.ThrowsAsync<ArgumentException>(() => h.Service.CancelAsync(id, "   "));
        await Assert.ThrowsAsync<ArgumentException>(() => h.Service.CancelAsync(id, new string('a', 256)));

        h.Clock.Set(JobStartUtc);
        var started = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Service.CancelAsync(id, "x"));
        Assert.Equal(DispatchErrorCodes.JobAlreadyStarted, started.Code); // after T it is the ABSENT flow (Q10)

        h.Clock.Set(JobStartUtc.AddDays(-1));
        h.Repo.Assignments[id].TransitionTo(JobAssignmentStatus.CheckedIn);
        var state = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Service.CancelAsync(id, "x"));
        Assert.Equal(DispatchErrorCodes.InvalidAssignmentState, state.Code);

        Assert.Empty(h.Mediator.Published);

        // D43: an agency worker is no longer refused, but the same refusals hold (404 not own, 409 started, 400 reason).
        var agency = new Harness();
        var agencyJob = await agency.AssignedAsync(50, slotId: 9, agencyId: 3);
        await Assert.ThrowsAsync<ArgumentException>(() => agency.Service.CancelAsync(agencyJob, "   "));
        agency.User.UserId = 99;
        await Assert.ThrowsAsync<NotFoundException>(() => agency.Service.CancelAsync(agencyJob, "x"));
        agency.User.UserId = 50;
        agency.Clock.Set(JobStartUtc);
        var agencyStarted = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => agency.Service.CancelAsync(agencyJob, "x"));
        Assert.Equal(DispatchErrorCodes.JobAlreadyStarted, agencyStarted.Code);
        Assert.Empty(agency.SwapRepo.Windows);
        Assert.Empty(agency.Mediator.Published);
        Assert.Empty(agency.Notifications.Sent);
    }

    [Fact]
    public async Task Agency_worker_cancel_opens_the_swap_window_and_tells_the_agency_without_counting_or_locking()
    {
        // D43 (AGENCY-CANCEL-01): the job stays ASSIGNED, the agency gets one notice and a 30-minute window.
        var h = new Harness();
        var id = await h.AssignedAsync(50, slotId: 9, agencyId: 3);
        h.Repo.AutoAcceptWorkerIds.Add(50);
        h.AddWorker(51, slotId: 10); // a freelancer who would take the seat if Dispatch re-dispatched it

        var preview = await h.Service.PreviewAsync(id);
        Assert.True(preview.AgencyWorker);
        Assert.False(preview.WillLockAccount);
        Assert.True(preview.SubstituteSearchPossible); // a day of notice: the window can open
        Assert.Equal(0, preview.CancelCountInWindow);

        var result = await h.Service.CancelAsync(id, "  Xe hỏng  ");

        Assert.Equal("ASSIGNED", result.Status);
        Assert.Equal(WorkerCancelOutcomes.AgencyNotified, result.OrderOutcome);
        Assert.Equal(h.Clock.UtcNow.AddMinutes(30), result.SubstituteSearchUntil);
        Assert.Equal((0, false), (result.CancelCountInWindow, result.AccountLocked));
        Assert.Equal("OPEN", h.SwapRepo.Windows[id].Status);
        Assert.Equal(3, h.SwapRepo.Windows[id].AgencyId);
        Assert.Equal("Xe hỏng", h.SwapRepo.Windows[id].Reason);

        // Nothing of the freelancer flow ran: the job, the slot, the order and the auto-accept switch are untouched.
        Assert.Equal(JobAssignmentStatus.Assigned, h.Repo.Assignments[id].AssignmentStatus);
        Assert.Equal(DispatchSlotStatuses.Booked, h.Repo.SlotStatus(9));
        Assert.Equal(JobOrderStatus.Assigned, h.Repo.Orders[OrderId].OrderStatus);
        Assert.Contains(50, h.Repo.AutoAcceptWorkerIds);
        Assert.DoesNotContain(h.Repo.Assignments.Values, a => a.AssignmentStatus == JobAssignmentStatus.Offered);
        Assert.Empty(h.Mediator.Published);
        Assert.Empty(h.Cancels.Locked);

        var notice = Assert.Single(h.Notifications.Sent);
        Assert.Equal((UserRole.Partner, 3, WorkerCancelNotificationTopics.AgencyWorkerCancelled), (notice.RecipientRole, notice.RecipientId, notice.Topic));
        Assert.Equal((OrderId.ToString(), id.ToString()), (notice.Data!["orderId"], notice.Data["assignmentId"]));

        // A second tap changes nothing and does not tell the agency again.
        var again = await h.Service.CancelAsync(id, "Xe hỏng");
        Assert.Equal(WorkerCancelOutcomes.AgencyNotified, again.OrderOutcome);
        Assert.Single(h.Notifications.Sent);
        Assert.Single(h.SwapRepo.Windows);
    }

    [Theory]
    [InlineData(120)] // exactly 2 h
    [InlineData(45)]
    public async Task Agency_worker_cancel_with_two_hours_or_less_tells_the_agency_and_hands_over_without_cancelling_the_order(int minutesBeforeStart)
    {
        var h = new Harness();
        var id = await h.AssignedAsync(50, slotId: 9, agencyId: 3);
        h.Clock.Set(JobStartUtc.AddMinutes(-minutesBeforeStart));

        var preview = await h.Service.PreviewAsync(id);
        Assert.False(preview.SubstituteSearchPossible); // no window any more

        var result = await h.Service.CancelAsync(id, "Ốm");

        Assert.Equal(WorkerCancelOutcomes.AgencyNotified, result.OrderOutcome);
        Assert.Null(result.SubstituteSearchUntil); // no swap window
        Assert.Equal("EXPIRED", h.SwapRepo.Windows[id].Status);
        // The existing failover/SLA flow takes over through AgencySwapWindowExpired; this action never cancels or refunds the order itself.
        Assert.Equal(id, Assert.Single(h.Mediator.Published.OfType<AgencySwapWindowExpired>()).AssignmentId);
        Assert.Empty(h.Mediator.Published.OfType<AssignmentFailed>());
        Assert.Empty(h.Mediator.Published.OfType<AssignmentCancelledByWorker>());
        Assert.Equal(JobOrderStatus.Assigned, h.Repo.Orders[OrderId].OrderStatus);
        var notice = Assert.Single(h.Notifications.Sent);
        Assert.Equal((UserRole.Partner, 3), (notice.RecipientRole, notice.RecipientId));
    }

    [Fact]
    public async Task A_freelancer_is_unchanged_by_the_agency_path()
    {
        var h = new Harness();
        var id = await h.AssignedAsync(42, slotId: 7);

        var preview = await h.Service.PreviewAsync(id);
        Assert.False(preview.AgencyWorker);

        var result = await h.Service.CancelAsync(id, "x");

        Assert.Equal("CANCELLED_BY_WORKER", result.Status);
        Assert.Empty(h.SwapRepo.Windows);
        Assert.DoesNotContain(h.Notifications.Sent, m => m.RecipientRole == UserRole.Partner);
    }

    [Fact]
    public async Task The_third_cancel_inside_30_days_locks_the_freelancer_and_older_ones_do_not_count()
    {
        var h = new Harness();
        var id = await h.AssignedAsync(42, slotId: 7);
        var now = h.Clock.UtcNow;
        h.Cancels.EarlierCancels[42] = [now.AddDays(-29), now.AddDays(-2)];

        var preview = await h.Service.PreviewAsync(id);
        Assert.Equal((2, 3, 30, true), (preview.CancelCountInWindow, preview.LockCount, preview.WindowDays, preview.WillLockAccount));

        var result = await h.Service.CancelAsync(id, "x");

        Assert.Equal(3, result.CancelCountInWindow);
        Assert.True(result.AccountLocked);
        Assert.True(Assert.Single(h.Mediator.Published.OfType<AssignmentCancelledByWorker>()).WorkerLocked);

        var older = new Harness();
        var job = await older.AssignedAsync(42, slotId: 7);
        older.Cancels.EarlierCancels[42] = [older.Clock.UtcNow.AddDays(-31), older.Clock.UtcNow.AddDays(-2)];
        var kept = await older.Service.CancelAsync(job, "x");
        Assert.Equal(2, kept.CancelCountInWindow);
        Assert.False(kept.AccountLocked);
    }

    [Fact]
    public async Task Preview_shows_the_count_the_auto_accept_switch_and_whether_a_search_is_still_possible()
    {
        var h = new Harness();
        var id = await h.AssignedAsync(42, slotId: 7);
        h.Repo.AutoAcceptWorkerIds.Add(42);

        var far = await h.Service.PreviewAsync(id);
        Assert.Equal((0, false, true, true, JobStartUtc), (far.CancelCountInWindow, far.WillLockAccount, far.AutoAcceptOn, far.SubstituteSearchPossible, far.JobStartAt));

        h.Clock.Set(JobStartUtc.AddMinutes(-20));
        Assert.False((await h.Service.PreviewAsync(id)).SubstituteSearchPossible);
        Assert.Equal(JobAssignmentStatus.Assigned, h.Repo.Assignments[id].AssignmentStatus); // a preview changes nothing
    }

    [Fact]
    public async Task Cutoff_sweep_fails_an_order_still_waiting_for_a_substitute_and_only_at_the_cutoff()
    {
        var h = new Harness();
        var id = await h.AssignedAsync(42, slotId: 7);
        h.AddWorker(43, slotId: 8);
        await h.Service.CancelAsync(id, "x"); // offered to 43, who does not answer
        h.Mediator.Published.Clear();

        h.Clock.Set(JobStartUtc.AddMinutes(-31));
        Assert.Equal(0, await h.Service.FailUnfilledAtCutoffAsync());

        h.Clock.Set(JobStartUtc.AddMinutes(-30));
        Assert.Equal(1, await h.Service.FailUnfilledAtCutoffAsync());
        var failed = Assert.Single(h.Mediator.Published.OfType<AssignmentFailed>());
        Assert.Equal((OrderId, WorkerCancelReasons.NoSubstitute), (failed.OrderId, failed.Reason));
    }

    [Fact]
    public async Task Cutoff_sweep_ignores_an_order_whose_seat_was_filled_again()
    {
        var h = new Harness();
        var id = await h.AssignedAsync(42, slotId: 7);
        h.AddWorker(43, slotId: 8);
        await h.Service.CancelAsync(id, "x");
        var replacement = h.Repo.Assignments.Values.Single(a => a.AssignmentStatus == JobAssignmentStatus.Offered);
        Assert.True((await h.Engine.AcceptOfferAsync(replacement.AssignmentId, 43)).Success);
        Assert.Equal(JobOrderStatus.Assigned, h.Repo.Orders[OrderId].OrderStatus);

        h.Clock.Set(JobStartUtc.AddMinutes(-5));
        Assert.Equal(0, await h.Service.FailUnfilledAtCutoffAsync());
    }

    [Fact]
    public async Task Customer_is_told_when_a_substitute_accepts_and_when_none_was_found_but_not_for_other_orders()
    {
        var h = new Harness();
        var id = await h.AssignedAsync(42, slotId: 7);
        h.AddWorker(43, slotId: 8);
        await h.Service.CancelAsync(id, "x");
        var replacement = h.Repo.Assignments.Values.Single(a => a.AssignmentStatus == JobAssignmentStatus.Offered);
        var notifier = new FakeNotificationService();
        var handler = new WorkerCancelNotificationHandler(h.Cancels, h.Repo, notifier, NullLogger<WorkerCancelNotificationHandler>.Instance);

        await handler.Handle(new JobAssigned(replacement.AssignmentId, OrderId, 43, null, 8, h.Clock.UtcNow), default);
        await handler.Handle(new AssignmentFailed(OrderId, WorkerCancelReasons.NoSubstitute, h.Clock.UtcNow), default);

        Assert.Equal(
            [WorkerCancelNotificationTopics.SubstituteFound, WorkerCancelNotificationTopics.SubstituteNotFound],
            notifier.Sent.Select(m => m.Topic));
        Assert.All(notifier.Sent, m => Assert.Equal((UserRole.Customer, CustomerId), (m.RecipientRole, m.RecipientId)));

        // An order without a worker cancel (normal dispatch) gets none of these notices.
        var plain = new Harness();
        var plainJob = await plain.AssignedAsync(44, slotId: 10);
        var quiet = new FakeNotificationService();
        var plainHandler = new WorkerCancelNotificationHandler(plain.Cancels, plain.Repo, quiet, NullLogger<WorkerCancelNotificationHandler>.Instance);
        await plainHandler.Handle(new JobAssigned(plainJob, OrderId, 44, null, 10, plain.Clock.UtcNow), default);
        await plainHandler.Handle(new AssignmentFailed(OrderId, "BR-03", plain.Clock.UtcNow), default);
        Assert.Empty(quiet.Sent);
    }
}
