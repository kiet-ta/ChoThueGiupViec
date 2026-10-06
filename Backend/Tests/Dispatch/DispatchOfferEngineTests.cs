using CommonService.Application.Common.Options;
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

public class DispatchOfferEngineTests
{
    private sealed class InMemoryDispatchRepository : IDispatchRepository
    {
        private readonly Dictionary<long, JobAssignment> _assignments = new();
        private readonly Dictionary<int, BookingSlot> _slots = new();
        private long _idCounter = 1000;

        public void AddSlot(BookingSlot slot) => _slots[slot.SlotId] = slot;
        public BookingSlot? GetSlot(int slotId) => _slots.TryGetValue(slotId, out var s) ? s : null;

        public Task<JobAssignment?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default)
        {
            _assignments.TryGetValue(assignmentId, out var a);
            return Task.FromResult(a);
        }

        public Task<JobAssignment> CreateAssignmentAsync(JobAssignment assignment, CancellationToken cancellationToken = default)
        {
            assignment.AssignmentId = ++_idCounter;
            _assignments[assignment.AssignmentId] = assignment;
            return Task.FromResult(assignment);
        }

        public Task<bool> TryLockSlotAndAssignAsync(long assignmentId, int slotId, DateTime assignedAtUtc, CancellationToken cancellationToken = default)
        {
            if (!_assignments.TryGetValue(assignmentId, out var assignment))
                return Task.FromResult(false);

            if (assignment.AssignmentStatus != JobAssignmentStatus.Offered)
                return Task.FromResult(false);

            if (_slots.TryGetValue(slotId, out var slot))
            {
                if (slot.SlotStatus == "LOCKED")
                    return Task.FromResult(false); // Already locked

                slot.SlotStatus = "LOCKED";
                slot.UpdatedAt = assignedAtUtc;
            }

            assignment.SlotId = slotId;
            assignment.AcceptedAt = assignedAtUtc;
            assignment.TransitionTo(JobAssignmentStatus.Assigned);
            return Task.FromResult(true);
        }

        public Task<bool> CancelAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default)
        {
            if (_assignments.TryGetValue(assignmentId, out var assignment))
            {
                if (assignment.AssignmentStatus == JobAssignmentStatus.Offered)
                {
                    assignment.TransitionTo(JobAssignmentStatus.Cancelled);
                    return Task.FromResult(true);
                }
            }
            return Task.FromResult(false);
        }
    }

    private sealed class TestMediator : IMediator
    {
        public readonly List<INotification> PublishedEvents = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            if (notification is INotification n)
                PublishedEvents.Add(n);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
        {
            PublishedEvents.Add(notification);
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotImplementedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    [Fact]
    public async Task StartDispatchForOrder_creates_offer_with_30s_timeout_and_saves_to_store()
    {
        var startTime = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock();
        clock.Set(startTime);
        var fakeQuery = new FakeWorkerAvailabilityQuery();
        fakeQuery.Workers.Add(new AvailableWorker(WorkerId: 101, SlotId: 10, DistanceKm: 2.5));

        var scanner = new SteppedRadiusDispatchScanner(
            fakeQuery,
            MicrosoftOptions.Create(new BusinessRules()),
            MicrosoftOptions.Create(new MatchingScoreOptions())
        );

        var repo = new InMemoryDispatchRepository();
        var store = new InMemoryOfferStore();
        var mediator = new TestMediator();

        var engine = new DispatchOfferEngine(
            scanner,
            repo,
            store,
            clock,
            mediator,
            MicrosoftOptions.Create(new BusinessRules())
        );

        var offer = await engine.StartDispatchForOrderAsync(
            orderId: 501,
            customerId: 201,
            serviceTier: ServiceTier.Economy,
            date: new DateOnly(2026, 10, 15),
            shiftCode: "SANG",
            location: new GeoPoint(10.762622, 106.660172),
            grossAmount: 260000m
        );

        Assert.NotNull(offer);
        Assert.Equal(101, offer.WorkerId);
        Assert.Equal(501, offer.OrderId);
        Assert.Equal(startTime, offer.OfferedAtUtc);
        Assert.Equal(startTime.AddSeconds(30), offer.ExpiresAtUtc);
        Assert.Equal(208000m, offer.PayoutAmount); // 260000 - 20%
        Assert.NotNull(store.GetOfferByAssignmentId(offer.AssignmentId));
    }

    [Fact]
    public async Task AcceptOffer_within_30s_transitions_to_ASSIGNED_and_locks_slot()
    {
        var startTime = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock();
        clock.Set(startTime);
        var fakeQuery = new FakeWorkerAvailabilityQuery();
        fakeQuery.Workers.Add(new AvailableWorker(WorkerId: 102, SlotId: 20, DistanceKm: 2.0));

        var scanner = new SteppedRadiusDispatchScanner(
            fakeQuery,
            MicrosoftOptions.Create(new BusinessRules())
        );

        var repo = new InMemoryDispatchRepository();
        repo.AddSlot(new BookingSlot
        {
            SlotId = 20,
            WorkerId = 102,
            SlotDate = new DateOnly(2026, 10, 15),
            ShiftCode = "SANG",
            SlotStatus = "AVAILABLE",
            UpdatedAt = startTime
        });

        var store = new InMemoryOfferStore();
        var mediator = new TestMediator();

        var engine = new DispatchOfferEngine(
            scanner,
            repo,
            store,
            clock,
            mediator,
            MicrosoftOptions.Create(new BusinessRules())
        );

        var offer = await engine.StartDispatchForOrderAsync(
            orderId: 502,
            customerId: 202,
            serviceTier: ServiceTier.Economy,
            date: new DateOnly(2026, 10, 15),
            shiftCode: "SANG",
            location: new GeoPoint(10.762622, 106.660172),
            grossAmount: 260000m
        );

        // Advance clock by 15 seconds (still valid)
        clock.Advance(TimeSpan.FromSeconds(15));

        var acceptResult = await engine.AcceptOfferAsync(offer!.AssignmentId, 102, slotId: 20);

        Assert.True(acceptResult.Success);
        Assert.NotNull(acceptResult.Assignment);
        Assert.Equal(JobAssignmentStatus.Assigned, acceptResult.Assignment.AssignmentStatus);
        Assert.Equal(clock.UtcNow, acceptResult.Assignment.AcceptedAt);

        // Verify slot was locked
        var lockedSlot = repo.GetSlot(20);
        Assert.Equal("LOCKED", lockedSlot!.SlotStatus);

        // Verify JobAssigned event was published
        Assert.Single(mediator.PublishedEvents);
        var evt = Assert.IsType<JobAssigned>(mediator.PublishedEvents[0]);
        Assert.Equal(offer.AssignmentId, evt.AssignmentId);
        Assert.Equal(102, evt.WorkerId);
    }

    [Fact]
    public async Task AcceptOffer_after_30s_timeout_is_rejected_and_offer_expires()
    {
        var startTime = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock();
        clock.Set(startTime);
        var fakeQuery = new FakeWorkerAvailabilityQuery();
        fakeQuery.Workers.Add(new AvailableWorker(WorkerId: 103, SlotId: 30, DistanceKm: 1.5));

        var scanner = new SteppedRadiusDispatchScanner(fakeQuery, MicrosoftOptions.Create(new BusinessRules()));
        var repo = new InMemoryDispatchRepository();
        var store = new InMemoryOfferStore();
        var mediator = new TestMediator();

        var engine = new DispatchOfferEngine(
            scanner,
            repo,
            store,
            clock,
            mediator,
            MicrosoftOptions.Create(new BusinessRules())
        );

        var offer = await engine.StartDispatchForOrderAsync(
            orderId: 503,
            customerId: 203,
            serviceTier: ServiceTier.Economy,
            date: new DateOnly(2026, 10, 15),
            shiftCode: "SANG",
            location: new GeoPoint(10.762622, 106.660172),
            grossAmount: 260000m
        );

        // Advance clock by 31 seconds (expired!)
        clock.Advance(TimeSpan.FromSeconds(31));

        var acceptResult = await engine.AcceptOfferAsync(offer!.AssignmentId, 103, slotId: 30);

        Assert.False(acceptResult.Success);
        Assert.Contains("expired", acceptResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(mediator.PublishedEvents);
    }

    [Fact]
    public async Task RaceCondition_double_accept_is_safely_prevented()
    {
        var startTime = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock();
        clock.Set(startTime);
        var fakeQuery = new FakeWorkerAvailabilityQuery();
        fakeQuery.Workers.Add(new AvailableWorker(WorkerId: 104, SlotId: 40, DistanceKm: 1.0));

        var scanner = new SteppedRadiusDispatchScanner(fakeQuery, MicrosoftOptions.Create(new BusinessRules()));
        var repo = new InMemoryDispatchRepository();
        repo.AddSlot(new BookingSlot { SlotId = 40, SlotStatus = "AVAILABLE" });

        var store = new InMemoryOfferStore();
        var mediator = new TestMediator();

        var engine = new DispatchOfferEngine(
            scanner,
            repo,
            store,
            clock,
            mediator,
            MicrosoftOptions.Create(new BusinessRules())
        );

        var offer = await engine.StartDispatchForOrderAsync(
            orderId: 504,
            customerId: 204,
            serviceTier: ServiceTier.Economy,
            date: new DateOnly(2026, 10, 15),
            shiftCode: "SANG",
            location: new GeoPoint(10.762622, 106.660172),
            grossAmount: 260000m
        );

        // First worker accepts successfully
        var res1 = await engine.AcceptOfferAsync(offer!.AssignmentId, 104, 40);
        Assert.True(res1.Success);

        // Second attempt to accept the same offer must fail
        var res2 = await engine.AcceptOfferAsync(offer.AssignmentId, 104, 40);
        Assert.False(res2.Success);
    }

    [Fact]
    public async Task ExpirePendingOffersAsync_cancels_all_offers_older_than_30s()
    {
        var startTime = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock();
        clock.Set(startTime);
        var fakeQuery = new FakeWorkerAvailabilityQuery();
        fakeQuery.Workers.Add(new AvailableWorker(WorkerId: 105, SlotId: 50, DistanceKm: 1.0));

        var scanner = new SteppedRadiusDispatchScanner(fakeQuery, MicrosoftOptions.Create(new BusinessRules()));
        var repo = new InMemoryDispatchRepository();
        var store = new InMemoryOfferStore();
        var mediator = new TestMediator();

        var engine = new DispatchOfferEngine(scanner, repo, store, clock, mediator, MicrosoftOptions.Create(new BusinessRules()));

        var offer = await engine.StartDispatchForOrderAsync(
            505, 205, ServiceTier.Economy, new DateOnly(2026, 10, 15), "SANG",
            new GeoPoint(10.762622, 106.660172), 260000m
        );

        clock.Advance(TimeSpan.FromSeconds(35));
        int expired = await engine.ExpirePendingOffersAsync();

        Assert.Equal(1, expired);
        Assert.Null(store.GetOfferByAssignmentId(offer!.AssignmentId));

        var assignment = await repo.GetAssignmentByIdAsync(offer.AssignmentId);
        Assert.Equal(JobAssignmentStatus.Cancelled, assignment!.AssignmentStatus);
    }

    [Fact]
    public async Task OrderPaidDispatchHandler_triggers_dispatch_on_OrderPaid_event()
    {
        var startTime = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock();
        clock.Set(startTime);
        var fakeQuery = new FakeWorkerAvailabilityQuery();
        fakeQuery.Workers.Add(new AvailableWorker(WorkerId: 106, SlotId: 60, DistanceKm: 1.2));

        var scanner = new SteppedRadiusDispatchScanner(fakeQuery, MicrosoftOptions.Create(new BusinessRules()));
        var repo = new InMemoryDispatchRepository();
        var store = new InMemoryOfferStore();
        var mediator = new TestMediator();

        var engine = new DispatchOfferEngine(scanner, repo, store, clock, mediator, MicrosoftOptions.Create(new BusinessRules()));
        var handler = new OrderPaidDispatchHandler(engine);

        var orderPaidEvent = new OrderPaid(
            OrderId: 601,
            CustomerId: 301,
            Amount: 260000m,
            ShiftCode: "SANG",
            ScheduledDate: new DateTime(2026, 10, 15),
            RequiredWorkers: 1
        );

        await handler.Handle(orderPaidEvent, CancellationToken.None);

        var activeOffer = store.GetCurrentOffer(106);
        Assert.NotNull(activeOffer);
        Assert.Equal(601, activeOffer.OrderId);
        Assert.Equal(106, activeOffer.WorkerId);
    }

    [Fact]
    public async Task StartDispatchForOrder_exhausts_over_10km_and_publishes_AssignmentFailed()
    {
        var startTime = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock();
        clock.Set(startTime);
        var fakeQuery = new FakeWorkerAvailabilityQuery();
        // Worker is at 15.0 km (outside 10 km maximum stepped radius)
        fakeQuery.Workers.Add(new AvailableWorker(WorkerId: 107, SlotId: 70, DistanceKm: 15.0));

        var scanner = new SteppedRadiusDispatchScanner(fakeQuery, MicrosoftOptions.Create(new BusinessRules()));
        var repo = new InMemoryDispatchRepository();
        var store = new InMemoryOfferStore();
        var mediator = new TestMediator();

        var engine = new DispatchOfferEngine(scanner, repo, store, clock, mediator, MicrosoftOptions.Create(new BusinessRules()));

        var offer = await engine.StartDispatchForOrderAsync(
            orderId: 701,
            customerId: 401,
            serviceTier: ServiceTier.Economy,
            date: new DateOnly(2026, 10, 15),
            shiftCode: "SANG",
            location: new GeoPoint(10.762622, 106.660172),
            grossAmount: 260000m
        );

        Assert.Null(offer);
        // Verify AssignmentFailed domain event was published to trigger 100% customer refund
        Assert.Single(mediator.PublishedEvents);
        var failedEvent = Assert.IsType<AssignmentFailed>(mediator.PublishedEvents[0]);
        Assert.Equal(701, failedEvent.OrderId);
        Assert.Contains("10 km", failedEvent.Reason);
        Assert.Equal(startTime, failedEvent.FailedAtUtc);
    }
}
