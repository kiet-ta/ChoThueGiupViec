using CommonService.Application.Common.Options;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using MediatR;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace CommonService.Tests.Dispatch;

public class DispatchOfferEngineTests
{
    private static readonly DateTime StartTime = new(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly ShiftDate = new(2026, 10, 15);
    private static readonly GeoPoint OrderLocation = new(10.762622, 106.660172);

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

    /// <summary>Records the location of every availability query (to prove the order's own address is used).</summary>
    private sealed class RecordingAvailabilityQuery : IWorkerAvailabilityQuery
    {
        public List<AvailableWorker> Workers { get; } = [];
        public List<GeoPoint> Locations { get; } = [];

        public Task<IReadOnlyList<AvailableWorker>> FindAvailableFreelancersAsync(AvailabilityQuery query, CancellationToken cancellationToken = default)
        {
            Locations.Add(query.Location);
            return Task.FromResult<IReadOnlyList<AvailableWorker>>(
                Workers.Where(w => w.DistanceKm <= query.RadiusKm).OrderBy(w => w.DistanceKm).Take(query.Limit).ToList());
        }
    }

    private sealed class Harness
    {
        public FakeClock Clock { get; } = new();
        public RecordingAvailabilityQuery Query { get; } = new();
        public InMemoryDispatchRepository Repo { get; } = new();
        public TestMediator Mediator { get; } = new();
        public FakeAgencyCapacityService Capacity { get; } = new();
        public DispatchOfferEngine Engine { get; }

        public Harness()
        {
            Clock.Set(StartTime);
            var rules = MicrosoftOptions.Create(new BusinessRules());
            var scanner = new SteppedRadiusDispatchScanner(Query, rules, MicrosoftOptions.Create(new MatchingScoreOptions()));
            Engine = new DispatchOfferEngine(scanner, Repo, Clock, Mediator, rules, Capacity);
        }

        public void AddWorker(int workerId, int slotId, double distanceKm)
        {
            Query.Workers.Add(new AvailableWorker(workerId, slotId, distanceKm));
            Repo.AddSlot(slotId, workerId);
        }

        public void AddOrder(long orderId, ServiceTier tier = ServiceTier.Economy, int requiredWorkers = 1, decimal total = 260000m, GeoPoint? at = null) =>
            Repo.Orders[orderId] = new DispatchOrderContext(orderId, 201, tier, ShiftDate, "SANG", requiredWorkers, at ?? OrderLocation, total);

        public Task<DispatchOffer?> OfferAsync(long orderId)
        {
            if (!Repo.Orders.ContainsKey(orderId))
            {
                AddOrder(orderId);
            }

            return Engine.StartDispatchForOrderAsync(orderId, 201, ServiceTier.Economy, ShiftDate, "SANG", OrderLocation, 260000m);
        }
    }

    [Fact]
    public async Task StartDispatchForOrder_creates_a_persisted_offer_with_30s_timeout_on_the_workers_real_slot()
    {
        var h = new Harness();
        h.AddWorker(101, slotId: 10, distanceKm: 2.5);

        var offer = await h.OfferAsync(501);

        Assert.NotNull(offer);
        Assert.Equal(101, offer.WorkerId);
        Assert.Equal(501, offer.OrderId);
        Assert.Equal(StartTime, offer.OfferedAtUtc);
        Assert.Equal(StartTime.AddSeconds(30), offer.ExpiresAtUtc);
        Assert.Equal(208000m, offer.PayoutAmount); // 260000 - 20% (Commission.Freelancer, Q11)
        var row = h.Repo.Assignments[offer.AssignmentId];
        Assert.Equal(JobAssignmentStatus.Offered, row.AssignmentStatus);
        Assert.Equal(10, row.SlotId); // the AvailableWorker slot, not 0 (NOT NULL FK to BOOKING_SLOT)
        Assert.Equal(DispatchSlotStatuses.Held, h.Repo.SlotStatus(10));
        Assert.Equal(offer.AssignmentId, (await h.Engine.GetCurrentOfferAsync(101))!.AssignmentId);
    }

    [Fact]
    public async Task AcceptOffer_within_30s_transitions_to_ASSIGNED_and_books_slot()
    {
        var h = new Harness();
        h.AddWorker(102, slotId: 20, distanceKm: 2.0);
        var offer = await h.OfferAsync(502);

        // Advance clock by 15 seconds (still valid)
        h.Clock.Advance(TimeSpan.FromSeconds(15));
        var acceptResult = await h.Engine.AcceptOfferAsync(offer!.AssignmentId, 102);

        Assert.True(acceptResult.Success);
        Assert.Equal(JobAssignmentStatus.Assigned, acceptResult.Assignment!.AssignmentStatus);
        Assert.Equal(h.Clock.UtcNow, acceptResult.Assignment.AcceptedAt);
        Assert.Equal(DispatchSlotStatuses.Booked, h.Repo.SlotStatus(20));
        var evt = Assert.IsType<JobAssigned>(Assert.Single(h.Mediator.PublishedEvents));
        Assert.Equal(offer.AssignmentId, evt.AssignmentId);
        Assert.Equal(102, evt.WorkerId);
        Assert.Equal(20, evt.SlotId);
        Assert.Null(await h.Engine.GetCurrentOfferAsync(102));
    }

    [Fact]
    public async Task AcceptOffer_after_30s_timeout_is_rejected()
    {
        var h = new Harness();
        h.AddWorker(103, slotId: 30, distanceKm: 1.5);
        var offer = await h.OfferAsync(503);

        // Advance clock by 31 seconds (expired!)
        h.Clock.Advance(TimeSpan.FromSeconds(31));
        var acceptResult = await h.Engine.AcceptOfferAsync(offer!.AssignmentId, 103);

        Assert.False(acceptResult.Success);
        Assert.Contains("expired", acceptResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(h.Mediator.PublishedEvents);
        Assert.Null(await h.Engine.GetCurrentOfferAsync(103));
    }

    [Fact]
    public async Task AcceptOffer_by_another_worker_is_rejected()
    {
        var h = new Harness();
        h.AddWorker(105, slotId: 50, distanceKm: 1.0);
        var offer = await h.OfferAsync(505);

        var result = await h.Engine.AcceptOfferAsync(offer!.AssignmentId, workerId: 999);

        Assert.False(result.Success);
        Assert.Equal(JobAssignmentStatus.Offered, h.Repo.Assignments[offer.AssignmentId].AssignmentStatus);
    }

    [Fact]
    public async Task RaceCondition_double_accept_is_safely_prevented()
    {
        var h = new Harness();
        h.AddWorker(104, slotId: 40, distanceKm: 1.0);
        var offer = await h.OfferAsync(504);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Task.Run(() => h.Engine.AcceptOfferAsync(offer!.AssignmentId, 104))));

        Assert.Single(results, r => r.Success);
        Assert.Single(h.Mediator.PublishedEvents.OfType<JobAssigned>());
        Assert.Equal(DispatchSlotStatuses.Booked, h.Repo.SlotStatus(40));
    }

    [Fact]
    public async Task Decline_moves_the_seat_to_the_next_ranked_worker_and_frees_the_slot()
    {
        var h = new Harness();
        h.AddOrder(506);
        h.AddWorker(201, slotId: 61, distanceKm: 1.0);
        h.AddWorker(202, slotId: 62, distanceKm: 2.0);
        var first = await h.OfferAsync(506);
        Assert.Equal(201, first!.WorkerId);

        Assert.True(await h.Engine.DeclineOfferAsync(first.AssignmentId, 201, "busy"));

        // BR-03: next ranked candidate, the decliner is not offered the order again.
        Assert.Equal(JobAssignmentStatus.Cancelled, h.Repo.Assignments[first.AssignmentId].AssignmentStatus);
        Assert.Equal(DispatchSlotStatuses.Available, h.Repo.SlotStatus(61));
        var next = await h.Engine.GetCurrentOfferAsync(202);
        Assert.NotNull(next);
        Assert.Equal(506, next.OrderId);
        Assert.Equal(62, next.SlotId);
        Assert.Null(await h.Engine.GetCurrentOfferAsync(201));
    }

    [Fact]
    public async Task Decline_by_the_last_candidate_publishes_AssignmentFailed()
    {
        var h = new Harness();
        h.AddOrder(507);
        h.AddWorker(301, slotId: 71, distanceKm: 1.0);
        var only = await h.OfferAsync(507);

        await h.Engine.DeclineOfferAsync(only!.AssignmentId, 301);

        var failed = Assert.IsType<AssignmentFailed>(Assert.Single(h.Mediator.PublishedEvents));
        Assert.Equal(507, failed.OrderId);
    }

    [Fact]
    public async Task ExpirePendingOffersAsync_closes_offers_older_than_30s_and_offers_the_next_candidate()
    {
        var h = new Harness();
        h.AddOrder(508);
        h.AddWorker(401, slotId: 81, distanceKm: 1.0);
        h.AddWorker(402, slotId: 82, distanceKm: 3.0);
        var first = await h.OfferAsync(508);

        h.Clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(0, await h.Engine.ExpirePendingOffersAsync());

        h.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, await h.Engine.ExpirePendingOffersAsync());

        Assert.Equal(JobAssignmentStatus.Cancelled, h.Repo.Assignments[first!.AssignmentId].AssignmentStatus);
        var next = await h.Engine.GetCurrentOfferAsync(402);
        Assert.NotNull(next);
        Assert.Equal(h.Clock.UtcNow.AddSeconds(30), next.ExpiresAtUtc);
    }

    [Fact]
    public async Task StartDispatchForOrder_exhausts_over_10km_and_publishes_AssignmentFailed()
    {
        var h = new Harness();
        h.AddWorker(105, slotId: 50, distanceKm: 12.0); // beyond 10 km

        var offer = await h.OfferAsync(509);

        Assert.Null(offer);
        var evt = Assert.IsType<AssignmentFailed>(Assert.Single(h.Mediator.PublishedEvents));
        Assert.Equal(509, evt.OrderId);
        Assert.Contains("10 km", evt.Reason);
        Assert.Empty(h.Repo.Assignments);
    }

    [Fact]
    public async Task DispatchPaidOrder_reads_location_and_seats_from_the_order_and_offers_two_distinct_workers()
    {
        var h = new Harness();
        var orderAddress = new GeoPoint(21.028511, 105.804817); // not the old hard-coded point
        h.AddOrder(510, requiredWorkers: 2, total: 520000m, at: orderAddress);
        h.AddWorker(501, slotId: 91, distanceKm: 1.0);
        h.AddWorker(502, slotId: 92, distanceKm: 2.0);

        var offers = await h.Engine.DispatchPaidOrderAsync(510);

        Assert.Equal(2, offers.Count);
        Assert.All(h.Query.Locations, l => Assert.Equal(orderAddress, l));
        var rows = h.Repo.Assignments.Values.OrderBy(a => a.AssignmentSeq).ToList();
        Assert.Equal([(byte)1, (byte)2], rows.Select(a => a.AssignmentSeq));
        Assert.Equal([501, 502], rows.Select(a => a.WorkerId)); // BR-02: two different workers
        Assert.All(rows, a => Assert.Equal(260000m, a.GrossAmount));
    }

    [Fact]
    public async Task OrderPaidDispatchHandler_dispatches_the_paid_order_read_by_id()
    {
        var h = new Harness();
        h.AddOrder(511);
        h.AddWorker(601, slotId: 101, distanceKm: 1.0);
        var handler = new OrderPaidDispatchHandler(h.Engine);

        await handler.Handle(new OrderPaid(511, 201, 260000m, "SANG", new DateTime(2026, 10, 15), 1), CancellationToken.None);

        var offer = await h.Engine.GetCurrentOfferAsync(601);
        Assert.NotNull(offer);
        Assert.Equal(511, offer.OrderId);
    }

    [Fact]
    public async Task Premium_uses_the_agency_hold_made_at_order_creation_without_reserving_again()
    {
        var h = new Harness();
        h.AddOrder(512, tier: ServiceTier.Premium);
        h.Capacity.RemainingSlots = 5;
        // Booking reserved when the order was created (decisions Q23 B8).
        var hold = await h.Capacity.TryReserveAsync(new CapacityRequest(ShiftDate, "SANG", 1, OrderId: 512));
        h.Repo.AddSlot(hold!.SlotIds[0], workerId: 77, status: DispatchSlotStatuses.Held);

        var assigned = await h.Engine.DispatchPaidOrderAsync(512);

        Assert.Single(assigned);
        Assert.Equal(4, h.Capacity.RemainingSlots); // no second reservation
        var row = Assert.Single(h.Repo.Assignments.Values);
        Assert.Equal(JobAssignmentStatus.Assigned, row.AssignmentStatus);
        Assert.Equal(77, row.WorkerId); // the roster slot's worker, not 0 (NOT NULL FK to WORKER)
        Assert.Equal(hold.AgencyId, row.AgencyId);
        Assert.Equal(hold.SlotIds[0], row.SlotId);
        Assert.Equal(0.200m, row.CommissionRate); // the agency's package rate carried by the hold (Q11; fake agency = FREE)
        Assert.Equal(208000m, row.PayoutAmount);
        Assert.Equal(DispatchSlotStatuses.Booked, h.Repo.SlotStatus(hold.SlotIds[0]));
        var evt = Assert.IsType<JobAssigned>(Assert.Single(h.Mediator.PublishedEvents));
        Assert.Equal(77, evt.WorkerId);
    }

    [Fact]
    public async Task Premium_without_a_hold_publishes_AssignmentFailed()
    {
        var h = new Harness();
        h.AddOrder(513, tier: ServiceTier.Premium);

        var assigned = await h.Engine.DispatchPaidOrderAsync(513);

        Assert.Empty(assigned);
        Assert.Empty(h.Repo.Assignments);
        var evt = Assert.IsType<AssignmentFailed>(Assert.Single(h.Mediator.PublishedEvents));
        Assert.Equal(513, evt.OrderId);
        Assert.Contains("Agency", evt.Reason);
    }

    [Fact]
    public async Task DispatchPaidOrder_of_an_unknown_order_does_nothing()
    {
        var h = new Harness();

        Assert.Empty(await h.Engine.DispatchPaidOrderAsync(999));
        Assert.Empty(h.Mediator.PublishedEvents);
    }

    [Fact]
    public async Task Premium_hold_with_fewer_slots_than_seats_publishes_AssignmentFailed_and_assigns_nobody()
    {
        var h = new Harness();
        h.AddOrder(514, tier: ServiceTier.Premium, requiredWorkers: 2, total: 520000m);
        var hold = await h.Capacity.TryReserveAsync(new CapacityRequest(ShiftDate, "SANG", 1, OrderId: 514));
        h.Repo.AddSlot(hold!.SlotIds[0], workerId: 78, status: DispatchSlotStatuses.Held);

        Assert.Empty(await h.Engine.DispatchPaidOrderAsync(514));

        Assert.Empty(h.Repo.Assignments);
        Assert.Equal(514, Assert.IsType<AssignmentFailed>(Assert.Single(h.Mediator.PublishedEvents)).OrderId);
    }

    [Fact]
    public async Task DispatchPaidOrder_is_idempotent_a_repeated_OrderPaid_offers_no_second_set()
    {
        var h = new Harness();
        h.AddOrder(515);
        h.AddWorker(701, slotId: 111, distanceKm: 1.0);
        h.AddWorker(702, slotId: 112, distanceKm: 2.0);

        await h.Engine.DispatchPaidOrderAsync(515);
        await h.Engine.DispatchPaidOrderAsync(515);

        var row = Assert.Single(h.Repo.Assignments.Values);
        Assert.Equal(701, row.WorkerId);
    }

    [Fact]
    public async Task Two_seat_order_stops_after_the_seat_that_finds_nobody()
    {
        var h = new Harness();
        h.AddOrder(516, requiredWorkers: 2, total: 520000m);
        h.AddWorker(801, slotId: 121, distanceKm: 1.0); // only one worker for two seats

        var offers = await h.Engine.DispatchPaidOrderAsync(516);

        Assert.Single(offers);
        Assert.Equal(516, Assert.IsType<AssignmentFailed>(Assert.Single(h.Mediator.PublishedEvents)).OrderId);
    }

    [Fact]
    public async Task Cancelled_order_releases_its_offer_and_the_offer_can_no_longer_be_accepted()
    {
        var h = new Harness();
        h.AddOrder(517);
        h.AddWorker(901, slotId: 131, distanceKm: 1.0);
        var offer = await h.OfferAsync(517);

        h.Repo.SetOrderStatus(517, JobOrderStatus.Cancelled);
        var handler = new OrderCancelledDispatchHandler(h.Engine, Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderCancelledDispatchHandler>.Instance);
        await handler.Handle(new OrderCancelled(517, 201, "customer", StartTime), CancellationToken.None);

        Assert.Equal(JobAssignmentStatus.Cancelled, h.Repo.Assignments[offer!.AssignmentId].AssignmentStatus);
        Assert.Equal(DispatchSlotStatuses.Available, h.Repo.SlotStatus(131));
        Assert.False((await h.Engine.AcceptOfferAsync(offer.AssignmentId, 901)).Success);
    }

    [Fact]
    public async Task Accept_is_refused_once_the_order_is_cancelled_even_if_the_offer_row_is_still_open()
    {
        var h = new Harness();
        h.AddOrder(518);
        h.AddWorker(902, slotId: 132, distanceKm: 1.0);
        var offer = await h.OfferAsync(518);
        h.Repo.SetOrderStatus(518, JobOrderStatus.Cancelled);

        var result = await h.Engine.AcceptOfferAsync(offer!.AssignmentId, 902);

        Assert.False(result.Success);
        Assert.Equal(DispatchSlotStatuses.Held, h.Repo.SlotStatus(132));
    }

    [Fact]
    public async Task Decline_on_a_cancelled_order_does_not_offer_it_to_anyone_else()
    {
        var h = new Harness();
        h.AddOrder(519);
        h.AddWorker(903, slotId: 141, distanceKm: 1.0);
        h.AddWorker(904, slotId: 142, distanceKm: 2.0);
        var offer = await h.OfferAsync(519);
        h.Repo.SetOrderStatus(519, JobOrderStatus.Cancelled);

        Assert.True(await h.Engine.DeclineOfferAsync(offer!.AssignmentId, 903));

        Assert.Null(await h.Engine.GetCurrentOfferAsync(904));
        Assert.Empty(h.Mediator.PublishedEvents);
    }

    [Fact]
    public async Task Sweep_recovers_a_seat_lost_after_one_quiet_offer_window()
    {
        var h = new Harness();
        h.AddOrder(520);
        h.AddWorker(905, slotId: 151, distanceKm: 1.0);
        // OrderPaid was handled but dispatch failed: the order is DISPATCHING with no row at all.

        Assert.Equal(1, await h.Engine.RecoverMissingSeatsAsync());

        var offer = await h.Engine.GetCurrentOfferAsync(905);
        Assert.NotNull(offer);
        Assert.Equal(520, offer.OrderId);
        Assert.Equal(0, await h.Engine.RecoverMissingSeatsAsync()); // the seat is live again: nothing to recover
    }

    [Fact]
    public async Task Sweep_waits_one_offer_window_after_the_last_change_before_re_offering()
    {
        var h = new Harness();
        h.AddOrder(521);
        h.AddWorker(906, slotId: 161, distanceKm: 1.0);
        var offer = await h.OfferAsync(521);
        await h.Repo.TryCloseOfferAsync(offer!.AssignmentId, h.Clock.UtcNow); // the seat was lost just now

        h.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(0, await h.Engine.RecoverMissingSeatsAsync()); // a decline/next-candidate may still be in flight

        h.Clock.Advance(TimeSpan.FromSeconds(21));
        Assert.Equal(1, await h.Engine.RecoverMissingSeatsAsync());
    }

    [Fact]
    public async Task Sweep_ignores_a_recently_updated_order_and_a_past_shift()
    {
        var h = new Harness();
        h.AddOrder(522);
        h.Repo.OrderUpdatedAt[522] = h.Clock.UtcNow; // just moved to DISPATCHING: OrderPaid is being handled
        h.Repo.Orders[523] = new DispatchOrderContext(523, 201, ServiceTier.Economy, ShiftDate.AddDays(-1), "SANG", 1, OrderLocation, 260000m);
        h.AddWorker(907, slotId: 171, distanceKm: 1.0);

        Assert.Equal(0, await h.Engine.RecoverMissingSeatsAsync());
        Assert.Empty(h.Repo.Assignments);
    }

    [Fact]
    public async Task Premium_partly_assigned_order_is_completed_without_assigning_a_seat_twice()
    {
        var h = new Harness();
        h.AddOrder(524, tier: ServiceTier.Premium, requiredWorkers: 2, total: 520000m);
        var hold = await h.Capacity.TryReserveAsync(new CapacityRequest(ShiftDate, "SANG", 2, OrderId: 524));
        h.Repo.AddSlot(hold!.SlotIds[0], workerId: 81, status: DispatchSlotStatuses.Held);
        h.Repo.AddSlot(hold.SlotIds[1], workerId: 82, status: DispatchSlotStatuses.Held);
        await h.Repo.CreateAssignedAsync(new CommonService.Domain.Entities.JobAssignment
        {
            OrderId = 524,
            CustomerId = 201,
            WorkerId = 81,
            AgencyId = hold.AgencyId,
            SlotId = hold.SlotIds[0],
            ServiceTier = ServiceTier.Premium,
            AssignmentSeq = 1,
            GrossAmount = 260000m,
            CommissionRate = 0.200m,
            PayoutAmount = 208000m,
            CreatedAt = StartTime,
            UpdatedAt = StartTime,
        }); // seat 1 was assigned, then the dispatch failed before seat 2

        var assigned = await h.Engine.DispatchPaidOrderAsync(524);

        var seat2 = Assert.Single(assigned);
        Assert.Equal(82, seat2.WorkerId);
        Assert.Equal(2, h.Repo.Assignments.Count);
        Assert.Equal([(byte)1, (byte)2], h.Repo.Assignments.Values.Select(a => a.AssignmentSeq).OrderBy(x => x));
    }

    private sealed class ThrowingDispatchRepository : IDispatchRepository
    {
        private static Exception Down() => new InvalidOperationException("database down");
        public Task<DispatchOrderContext?> GetOrderContextAsync(long orderId, CancellationToken cancellationToken = default) => throw Down();
        public Task<IReadOnlyCollection<long>> GetWorkerIdsAlreadyOfferedAsync(long orderId, CancellationToken cancellationToken = default) => throw Down();
        public Task<CommonService.Domain.Entities.JobAssignment?> TryCreateOfferAsync(CommonService.Domain.Entities.JobAssignment offer, CancellationToken cancellationToken = default) => throw Down();
        public Task<CommonService.Domain.Entities.JobAssignment?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default) => throw Down();
        public Task<CommonService.Domain.Entities.JobAssignment?> GetOpenOfferForWorkerAsync(int workerId, DateTime offeredAfterUtc, CancellationToken cancellationToken = default) => throw Down();
        public Task<bool> TryAcceptOfferAsync(long assignmentId, int workerId, DateTime offeredAfterUtc, DateTime acceptedAtUtc, CancellationToken cancellationToken = default) => throw Down();
        public Task<bool> TryCloseOfferAsync(long assignmentId, DateTime closedAtUtc, CancellationToken cancellationToken = default) => throw Down();
        public Task<IReadOnlyList<CommonService.Domain.Entities.JobAssignment>> GetOffersCreatedAtOrBeforeAsync(DateTime createdAtOrBeforeUtc, CancellationToken cancellationToken = default) => throw Down();
        public Task<int?> GetSlotWorkerIdAsync(int slotId, CancellationToken cancellationToken = default) => throw Down();
        public Task<CommonService.Domain.Entities.JobAssignment> CreateAssignedAsync(CommonService.Domain.Entities.JobAssignment assignment, CancellationToken cancellationToken = default) => throw Down();
        public Task<IReadOnlyCollection<byte>> GetTakenSeatsAsync(long orderId, CancellationToken cancellationToken = default) => throw Down();
        public Task<IReadOnlyList<long>> GetOrdersMissingSeatsAsync(DateTime quietSinceUtc, DateOnly scheduledFrom, CancellationToken cancellationToken = default) => throw Down();
        public Task<int> CancelOpenAssignmentsOfOrderAsync(long orderId, DateTime cancelledAtUtc, CancellationToken cancellationToken = default) => throw Down();
    }

    [Fact]
    public async Task Dispatch_failures_in_the_event_handlers_do_not_escape_so_payment_and_refund_flows_continue()
    {
        var rules = MicrosoftOptions.Create(new BusinessRules());
        var clock = new FakeClock();
        var engine = new DispatchOfferEngine(new SteppedRadiusDispatchScanner(new FakeWorkerAvailabilityQuery(), rules),
            new ThrowingDispatchRepository(), clock, new TestMediator(), rules);
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderCancelledDispatchHandler>.Instance;

        await new OrderPaidDispatchHandler(engine).Handle(new OrderPaid(1, 1, 1m, "SANG", new DateTime(2026, 10, 15), 1), CancellationToken.None);
        await new OrderCancelledDispatchHandler(engine, logger).Handle(new OrderCancelled(1, 1, "x", StartTime), CancellationToken.None);
    }
}
