using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using MediatR;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace CommonService.Tests.Dispatch;

/// <summary>BR-02 fallback (#310): one worker, two consecutive shifts, after the customer agrees and the worker confirms.</summary>
public class ConsecutiveShiftFallbackTests
{
    private const long OrderId = 700;
    private const int CustomerId = 201;
    private const string Morning = "SHIFT_MORNING";
    private const string Afternoon = "SHIFT_AFTERNOON";

    // 10:00 Vietnam time on the day before the shift (08:00 local = 01:00 UTC on the 15th).
    private static readonly DateTime Now = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly ShiftDate = new(2026, 10, 15);
    private static readonly GeoPoint Location = new(10.762622, 106.660172);

    private sealed class RecordingMediator : IMediator
    {
        public readonly List<INotification> Published = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            if (notification is INotification n)
            {
                Published.Add(n);
            }

            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
        {
            Published.Add(notification);
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotImplementedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class Availability : IWorkerAvailabilityQuery
    {
        public List<AvailableWorker> Workers { get; } = [];

        public Task<IReadOnlyList<AvailableWorker>> FindAvailableFreelancersAsync(AvailabilityQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AvailableWorker>>(Workers.Where(w => w.DistanceKm <= query.RadiusKm).OrderBy(w => w.DistanceKm).ToList());
    }

    private sealed class Harness
    {
        public FakeClock Clock { get; } = new();
        public Availability Query { get; } = new();
        public InMemoryDispatchRepository Repo { get; } = new();
        public RecordingMediator Mediator { get; } = new();
        public FakeCurrentUser User { get; } = new();
        public DispatchOfferEngine Engine { get; }
        public ConsecutiveShiftFallbackService Fallback { get; }

        public Harness(string shiftCode = Morning)
        {
            Clock.Set(Now);
            var rules = MicrosoftOptions.Create(new BusinessRules());
            Engine = new DispatchOfferEngine(
                new SteppedRadiusDispatchScanner(Query, rules, MicrosoftOptions.Create(new MatchingScoreOptions())),
                Repo, Clock, Mediator, rules);
            Fallback = new ConsecutiveShiftFallbackService(Repo, Clock, Mediator, User, rules);
            Repo.Orders[OrderId] = new DispatchOrderContext(OrderId, CustomerId, ServiceTier.Economy, ShiftDate, shiftCode, 2, Location, 400000m);
            User.SignIn(CustomerId, UserRole.Customer);
        }

        public void AddWorker(int workerId, int slotId, double distanceKm)
        {
            Query.Workers.Add(new AvailableWorker(workerId, slotId, distanceKm));
            Repo.AddSlotOnShift(slotId, workerId, ShiftDate, Morning);
        }

        public void AddConsecutiveSlot(int slotId, int workerId, string shiftCode = Afternoon, string status = DispatchSlotStatuses.Available) =>
            Repo.AddSlotOnShift(slotId, workerId, ShiftDate, shiftCode, status);

        /// <summary>Seat 1 is offered to worker 101 and accepted; seat 2 finds nobody (the sweep's second attempt).</summary>
        public async Task<long> SeatOneAcceptedSeatTwoExhaustedAsync()
        {
            AddWorker(101, slotId: 10, distanceKm: 2.0);
            var offer = await Engine.DispatchPaidOrderAsync(OrderId);
            Assert.Single(offer);
            Assert.True((await Engine.AcceptOfferAsync(offer[0].AssignmentId, 101)).Success);
            Mediator.Published.Clear();
            await Engine.StartDispatchForOrderAsync(OrderId, CustomerId, ServiceTier.Economy, ShiftDate, Morning, Location, 200000m, assignmentSeq: 2);
            return offer[0].AssignmentId;
        }
    }

    [Fact]
    public async Task A_missing_second_seat_with_an_accepted_first_seat_waits_for_the_customer_instead_of_failing()
    {
        var h = new Harness();
        h.AddConsecutiveSlot(11, workerId: 101);

        await h.SeatOneAcceptedSeatTwoExhaustedAsync();

        Assert.DoesNotContain(h.Mediator.Published, e => e is AssignmentFailed);
        Assert.Equal(JobOrderStatus.AwaitingFallback, h.Repo.Orders[OrderId].OrderStatus);
        var status = await h.Fallback.GetAsync(OrderId);
        Assert.Equal("AWAITING_CUSTOMER", status.State);
        Assert.Equal(101, status.WorkerId);
        Assert.Equal(Afternoon, status.SecondShiftCode);
        Assert.Equal(new DateTime(2026, 10, 15, 1, 0, 0, DateTimeKind.Utc), status.DeadlineUtc); // the shift start (08:00 local)
    }

    [Fact]
    public async Task Without_a_free_consecutive_slot_the_order_fails_as_before()
    {
        var h = new Harness();
        h.AddConsecutiveSlot(11, workerId: 101, status: DispatchSlotStatuses.Booked);

        await h.SeatOneAcceptedSeatTwoExhaustedAsync();

        Assert.Contains(h.Mediator.Published, e => e is AssignmentFailed);
        Assert.Equal(JobOrderStatus.Dispatching, h.Repo.Orders[OrderId].OrderStatus);
    }

    [Fact]
    public async Task An_evening_shift_has_no_consecutive_shift_so_the_order_fails_as_before()
    {
        var h = new Harness(shiftCode: "SHIFT_EVENING");
        h.AddConsecutiveSlot(11, workerId: 101);

        h.AddWorker(101, slotId: 10, distanceKm: 2.0);
        var offer = await h.Engine.DispatchPaidOrderAsync(OrderId);
        await h.Engine.AcceptOfferAsync(offer[0].AssignmentId, 101);
        h.Mediator.Published.Clear();
        await h.Engine.StartDispatchForOrderAsync(OrderId, CustomerId, ServiceTier.Economy, ShiftDate, "SHIFT_EVENING", Location, 200000m, 2);

        Assert.Contains(h.Mediator.Published, e => e is AssignmentFailed);
    }

    [Fact]
    public async Task While_the_first_seat_is_only_offered_nothing_is_decided_and_the_order_is_not_cancelled()
    {
        var h = new Harness();
        h.AddConsecutiveSlot(11, workerId: 101);
        h.AddWorker(101, slotId: 10, distanceKm: 2.0);

        var offers = await h.Engine.DispatchPaidOrderAsync(OrderId); // seat 1 offered, seat 2 has no candidate

        Assert.Single(offers);
        Assert.DoesNotContain(h.Mediator.Published, e => e is AssignmentFailed);
        Assert.Equal(JobOrderStatus.Dispatching, h.Repo.Orders[OrderId].OrderStatus);
    }

    [Fact]
    public async Task Both_seats_without_any_live_assignment_still_fail()
    {
        var h = new Harness();

        await h.Engine.StartDispatchForOrderAsync(OrderId, CustomerId, ServiceTier.Economy, ShiftDate, Morning, Location, 200000m, 2);

        Assert.Contains(h.Mediator.Published, e => e is AssignmentFailed);
    }

    [Fact]
    public async Task Customer_agrees_then_the_worker_has_two_minutes_to_confirm_and_the_order_becomes_assigned()
    {
        var h = new Harness();
        h.AddConsecutiveSlot(11, workerId: 101);
        await h.SeatOneAcceptedSeatTwoExhaustedAsync();

        var answer = await h.Fallback.RespondAsync(OrderId, accept: true);

        Assert.Equal("WORKER_ASKED", answer.Outcome);
        Assert.Equal(Now.AddSeconds(120), answer.WorkerDeadlineUtc);
        var offer = Assert.Single(h.Repo.Assignments.Values, a => a.AssignmentSeq == 2);
        Assert.Equal(101, offer.WorkerId);
        Assert.Equal(11, offer.SlotId);
        Assert.Equal(JobAssignmentStatus.Offered, offer.AssignmentStatus);
        Assert.Equal(DispatchSlotStatuses.Held, h.Repo.SlotStatus(11));
        Assert.Equal("AWAITING_WORKER", (await h.Fallback.GetAsync(OrderId)).State);

        h.Clock.Advance(TimeSpan.FromSeconds(90)); // past the normal 30 s window, inside the 120 s one
        var current = await h.Engine.GetCurrentOfferAsync(101);
        Assert.NotNull(current);
        Assert.True(current.IsConsecutiveShift);
        Assert.Equal(Now.AddSeconds(120), current.ExpiresAtUtc);

        var accepted = await h.Engine.AcceptOfferAsync(offer.AssignmentId, 101);

        Assert.True(accepted.Success);
        Assert.Equal(DispatchSlotStatuses.Booked, h.Repo.SlotStatus(11));
        Assert.Equal(JobOrderStatus.Assigned, h.Repo.Orders[OrderId].OrderStatus);
        Assert.DoesNotContain(h.Mediator.Published, e => e is AssignmentFailed);
    }

    [Fact]
    public async Task The_sweep_does_not_close_a_consecutive_offer_at_30_seconds()
    {
        var h = new Harness();
        h.AddConsecutiveSlot(11, workerId: 101);
        await h.SeatOneAcceptedSeatTwoExhaustedAsync();
        await h.Fallback.RespondAsync(OrderId, accept: true);

        h.Clock.Advance(TimeSpan.FromSeconds(31));
        var expired = await h.Engine.ExpirePendingOffersAsync();

        Assert.Equal(0, expired);
        Assert.Contains(h.Repo.Assignments.Values, a => a.AssignmentSeq == 2 && a.AssignmentStatus == JobAssignmentStatus.Offered);
    }

    [Fact]
    public async Task A_worker_who_does_not_confirm_in_two_minutes_fails_the_order_and_nobody_else_is_offered()
    {
        var h = new Harness();
        h.AddConsecutiveSlot(11, workerId: 101);
        await h.SeatOneAcceptedSeatTwoExhaustedAsync();
        h.Query.Workers.Add(new AvailableWorker(102, 12, 3.0)); // another candidate shows up; the fallback must not use it
        h.Repo.AddSlotOnShift(12, 102, ShiftDate, Morning);
        await h.Fallback.RespondAsync(OrderId, accept: true);

        h.Clock.Advance(TimeSpan.FromSeconds(121));
        await h.Engine.ExpirePendingOffersAsync();

        var failed = Assert.IsType<AssignmentFailed>(Assert.Single(h.Mediator.Published, e => e is AssignmentFailed));
        Assert.Equal(OrderId, failed.OrderId);
        Assert.DoesNotContain(h.Repo.Assignments.Values, a => a.WorkerId == 102);
        Assert.Equal(DispatchSlotStatuses.Available, h.Repo.SlotStatus(11));
    }

    [Fact]
    public async Task Confirming_after_two_minutes_is_expired()
    {
        var h = new Harness();
        h.AddConsecutiveSlot(11, workerId: 101);
        await h.SeatOneAcceptedSeatTwoExhaustedAsync();
        await h.Fallback.RespondAsync(OrderId, accept: true);
        var offer = h.Repo.Assignments.Values.Single(a => a.AssignmentSeq == 2);

        h.Clock.Advance(TimeSpan.FromSeconds(121));
        var result = await h.Engine.AcceptOfferAsync(offer.AssignmentId, 101);

        Assert.False(result.Success);
        Assert.Equal(OfferAcceptError.Expired, result.Error);
    }

    [Fact]
    public async Task A_worker_who_declines_the_second_shift_fails_the_order()
    {
        var h = new Harness();
        h.AddConsecutiveSlot(11, workerId: 101);
        await h.SeatOneAcceptedSeatTwoExhaustedAsync();
        h.Query.Workers.Add(new AvailableWorker(102, 12, 3.0));
        h.Repo.AddSlotOnShift(12, 102, ShiftDate, Morning);
        await h.Fallback.RespondAsync(OrderId, accept: true);
        var offer = h.Repo.Assignments.Values.Single(a => a.AssignmentSeq == 2);

        var declined = await h.Engine.DeclineOfferAsync(offer.AssignmentId, 101, "bận");

        Assert.True(declined);
        Assert.Single(h.Mediator.Published, e => e is AssignmentFailed);
        Assert.DoesNotContain(h.Repo.Assignments.Values, a => a.WorkerId == 102);
    }

    [Fact]
    public async Task Customer_declines_and_the_order_is_failed_for_cancel_and_refund()
    {
        var h = new Harness();
        h.AddConsecutiveSlot(11, workerId: 101);
        await h.SeatOneAcceptedSeatTwoExhaustedAsync();

        var answer = await h.Fallback.RespondAsync(OrderId, accept: false);

        Assert.Equal("CANCELLED", answer.Outcome);
        var failed = Assert.IsType<AssignmentFailed>(Assert.Single(h.Mediator.Published, e => e is AssignmentFailed));
        Assert.Equal(OrderId, failed.OrderId);
    }

    [Fact]
    public async Task Agreeing_after_the_shift_started_counts_as_a_decline()
    {
        var h = new Harness();
        h.AddConsecutiveSlot(11, workerId: 101);
        await h.SeatOneAcceptedSeatTwoExhaustedAsync();
        h.Clock.Set(new DateTime(2026, 10, 15, 1, 0, 1, DateTimeKind.Utc));

        var answer = await h.Fallback.RespondAsync(OrderId, accept: true);

        Assert.Equal("CANCELLED", answer.Outcome);
        Assert.Single(h.Mediator.Published, e => e is AssignmentFailed);
    }

    [Fact]
    public async Task Silence_until_the_shift_starts_is_a_decline_and_only_then()
    {
        var h = new Harness();
        h.AddConsecutiveSlot(11, workerId: 101);
        await h.SeatOneAcceptedSeatTwoExhaustedAsync();

        Assert.Equal(0, await h.Fallback.ExpireUnansweredAsync()); // the day before: still time to answer

        h.Clock.Set(new DateTime(2026, 10, 15, 1, 0, 0, DateTimeKind.Utc));
        Assert.Equal(1, await h.Fallback.ExpireUnansweredAsync());
        Assert.Single(h.Mediator.Published, e => e is AssignmentFailed);
    }

    [Fact]
    public async Task Someone_elses_order_is_not_found_and_an_order_that_is_not_waiting_is_a_conflict()
    {
        var h = new Harness();
        h.User.SignIn(999, UserRole.Customer);
        await Assert.ThrowsAsync<NotFoundException>(() => h.Fallback.GetAsync(OrderId));
        await Assert.ThrowsAsync<NotFoundException>(() => h.Fallback.RespondAsync(OrderId, true));

        h.User.SignIn(CustomerId, UserRole.Customer);
        Assert.Equal("NOT_WAITING", (await h.Fallback.GetAsync(OrderId)).State);
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Fallback.RespondAsync(OrderId, true));
        Assert.Equal(DispatchErrorCodes.FallbackNotWaiting, ex.Code);
    }

    [Fact]
    public async Task A_second_answer_while_the_worker_is_asked_is_a_conflict()
    {
        var h = new Harness();
        h.AddConsecutiveSlot(11, workerId: 101);
        await h.SeatOneAcceptedSeatTwoExhaustedAsync();
        await h.Fallback.RespondAsync(OrderId, accept: true);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Fallback.RespondAsync(OrderId, false));

        Assert.Equal(DispatchErrorCodes.FallbackAlreadyAnswered, ex.Code);
    }
}
