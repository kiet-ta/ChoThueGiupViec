using CommonService.Application.Common.Options;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using MediatR;
using Xunit;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace CommonService.Tests.Dispatch;

/// <summary>
/// DISPATCH-AUTO-01 (#890): a freelancer who turned on "Tự động nhận đơn" gets the Economy offer accepted for them when dispatch makes it;
/// the BR-02 second-shift offer still waits for the worker.
/// </summary>
public class DispatchAutoAcceptTests
{
    private const long OrderId = 900;
    private const int CustomerId = 201;
    private const string Morning = "SHIFT_MORNING";
    private const string Afternoon = "SHIFT_AFTERNOON";
    private static readonly DateTime Now = new(2026, 11, 15, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly ShiftDate = new(2026, 11, 16);
    private static readonly GeoPoint Location = new(10.762622, 106.660172);

    private class RecordingMediator : IMediator
    {
        public List<INotification> Published { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            if (notification is INotification n) Published.Add(n);
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

    private class Availability : IWorkerAvailabilityQuery
    {
        public List<AvailableWorker> Workers { get; } = [];

        public Task<IReadOnlyList<AvailableWorker>> FindAvailableFreelancersAsync(AvailabilityQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AvailableWorker>>(Workers.Where(w => w.DistanceKm <= query.RadiusKm).OrderBy(w => w.DistanceKm).ToList());
    }

    private class Harness
    {
        public FakeClock Clock { get; } = new();
        public Availability Query { get; } = new();
        public InMemoryDispatchRepository Repo { get; } = new();
        public RecordingMediator Mediator { get; } = new();
        public FakeNotificationService Notifications { get; } = new();
        public FakeCurrentUser User { get; } = new();
        public DispatchOfferEngine Engine { get; }
        public ConsecutiveShiftFallbackService Fallback { get; }

        public Harness(int requiredWorkers = 1)
        {
            Clock.Set(Now);
            var rules = MicrosoftOptions.Create(new BusinessRules());
            Engine = new DispatchOfferEngine(
                new SteppedRadiusDispatchScanner(Query, rules, MicrosoftOptions.Create(new MatchingScoreOptions())),
                Repo, Clock, Mediator, rules, notifications: Notifications);
            Fallback = new ConsecutiveShiftFallbackService(Repo, Clock, Mediator, User, rules);
            Repo.Orders[OrderId] = new DispatchOrderContext(
                OrderId, CustomerId, ServiceTier.Economy, ShiftDate, Morning, requiredWorkers, Location, 400000m);
            User.SignIn(CustomerId, UserRole.Customer);
        }

        public void AddWorker(int workerId, int slotId, double distanceKm, bool autoAccept)
        {
            Query.Workers.Add(new AvailableWorker(workerId, slotId, distanceKm));
            Repo.AddSlotOnShift(slotId, workerId, ShiftDate, Morning);
            if (autoAccept) Repo.AutoAcceptWorkerIds.Add(workerId);
        }
    }

    [Fact]
    public async Task A_worker_with_auto_accept_on_gets_the_job_without_confirming()
    {
        var h = new Harness();
        h.AddWorker(101, slotId: 10, distanceKm: 2.0, autoAccept: true);

        var offers = await h.Engine.DispatchPaidOrderAsync(OrderId);

        var offer = Assert.Single(offers);
        Assert.True(offer.IsAccepted);
        Assert.True(offer.IsAutoAccepted);
        var row = h.Repo.Assignments[offer.AssignmentId];
        Assert.Equal(JobAssignmentStatus.Assigned, row.AssignmentStatus); // the OFFERED row is kept and moved on (D1)
        Assert.Equal(Now, row.AcceptedAt);
        Assert.Equal(DispatchSlotStatuses.Booked, h.Repo.SlotStatus(10));
        Assert.Equal(JobOrderStatus.Assigned, h.Repo.Orders[OrderId].OrderStatus);
        var assigned = Assert.IsType<JobAssigned>(Assert.Single(h.Mediator.Published));
        Assert.Equal(101, assigned.WorkerId);
        Assert.Null(await h.Engine.GetCurrentOfferAsync(101)); // nothing left to confirm

        var message = Assert.Single(h.Notifications.Sent);
        Assert.Equal(UserRole.Worker, message.RecipientRole);
        Assert.Equal(101, message.RecipientId);
        Assert.Equal(DispatchNotificationTopics.AutoAccepted, message.Topic);
        Assert.Contains("ca sáng ngày 16/11/2026", message.Body);
        Assert.Equal(offer.AssignmentId.ToString(), message.Data!["assignmentId"]);
    }

    [Fact]
    public async Task A_worker_with_auto_accept_off_gets_a_normal_30_second_offer()
    {
        var h = new Harness();
        h.AddWorker(101, slotId: 10, distanceKm: 2.0, autoAccept: false);

        var offer = Assert.Single(await h.Engine.DispatchPaidOrderAsync(OrderId));

        Assert.False(offer.IsAccepted);
        Assert.False(offer.IsAutoAccepted);
        Assert.Equal(JobAssignmentStatus.Offered, h.Repo.Assignments[offer.AssignmentId].AssignmentStatus);
        Assert.Equal(DispatchSlotStatuses.Held, h.Repo.SlotStatus(10));
        Assert.Empty(h.Mediator.Published);
        Assert.Empty(h.Notifications.Sent);
        Assert.NotNull(await h.Engine.GetCurrentOfferAsync(101));
    }

    [Fact]
    public async Task Ranking_is_unchanged_the_nearest_worker_is_asked_first_even_without_auto_accept()
    {
        var h = new Harness();
        h.AddWorker(101, slotId: 10, distanceKm: 1.0, autoAccept: false);
        h.AddWorker(102, slotId: 20, distanceKm: 4.0, autoAccept: true);

        var offer = Assert.Single(await h.Engine.DispatchPaidOrderAsync(OrderId));

        Assert.Equal(101, offer.WorkerId);
        Assert.False(offer.IsAutoAccepted);
        Assert.Equal(DispatchSlotStatuses.Available, h.Repo.SlotStatus(20));
    }

    [Fact]
    public async Task After_a_decline_the_next_candidate_with_auto_accept_gets_the_job()
    {
        var h = new Harness();
        h.AddWorker(101, slotId: 10, distanceKm: 1.0, autoAccept: false);
        h.AddWorker(102, slotId: 20, distanceKm: 4.0, autoAccept: true);
        var first = Assert.Single(await h.Engine.DispatchPaidOrderAsync(OrderId));

        Assert.True(await h.Engine.DeclineOfferAsync(first.AssignmentId, 101));

        var next = Assert.Single(h.Repo.Assignments.Values, a => a.WorkerId == 102);
        Assert.Equal(JobAssignmentStatus.Assigned, next.AssignmentStatus);
        Assert.Equal(DispatchSlotStatuses.Booked, h.Repo.SlotStatus(20));
        Assert.Equal(JobOrderStatus.Assigned, h.Repo.Orders[OrderId].OrderStatus);
    }

    [Fact]
    public async Task Two_seats_with_two_auto_accept_workers_assign_the_order_at_once()
    {
        var h = new Harness(requiredWorkers: 2);
        h.AddWorker(101, slotId: 10, distanceKm: 1.0, autoAccept: true);
        h.AddWorker(102, slotId: 20, distanceKm: 2.0, autoAccept: true);

        var offers = await h.Engine.DispatchPaidOrderAsync(OrderId);

        Assert.Equal(2, offers.Count);
        Assert.All(offers, o => Assert.True(o.IsAutoAccepted));
        Assert.Equal([101, 102], offers.Select(o => o.WorkerId).Order());
        Assert.Equal(JobOrderStatus.Assigned, h.Repo.Orders[OrderId].OrderStatus);
        Assert.Equal(2, h.Notifications.Sent.Count);
    }

    [Fact]
    public async Task A_failed_auto_accept_leaves_the_offer_open_for_the_worker()
    {
        var h = new Harness();
        h.AddWorker(101, slotId: 10, distanceKm: 2.0, autoAccept: true);
        h.Repo.SetOrderStatus(OrderId, JobOrderStatus.Cancelled); // the accept path refuses an order that is no longer dispatching

        var offer = await h.Engine.StartDispatchForOrderAsync(OrderId, CustomerId, ServiceTier.Economy, ShiftDate, Morning, Location, 400000m);

        Assert.NotNull(offer);
        Assert.False(offer.IsAutoAccepted);
        Assert.Equal(JobAssignmentStatus.Offered, h.Repo.Assignments[offer.AssignmentId].AssignmentStatus);
        Assert.Equal(DispatchSlotStatuses.Held, h.Repo.SlotStatus(10));
        Assert.Empty(h.Notifications.Sent);
    }

    [Fact]
    public async Task The_BR02_second_shift_offer_is_never_auto_accepted()
    {
        var h = new Harness(requiredWorkers: 2);
        h.AddWorker(101, slotId: 10, distanceKm: 2.0, autoAccept: true);
        h.Repo.AddSlotOnShift(11, 101, ShiftDate, Afternoon);

        var first = Assert.Single(await h.Engine.DispatchPaidOrderAsync(OrderId)); // seat 2 finds nobody else
        Assert.True(first.IsAutoAccepted);
        Assert.Equal(JobOrderStatus.AwaitingFallback, h.Repo.Orders[OrderId].OrderStatus);

        var answer = await h.Fallback.RespondAsync(OrderId, accept: true);

        Assert.Equal("WORKER_ASKED", answer.Outcome);
        var second = Assert.Single(h.Repo.Assignments.Values, a => a.AssignmentSeq == 2);
        Assert.Equal(JobAssignmentStatus.Offered, second.AssignmentStatus); // option b of D7: never a second shift without asking
        Assert.Equal(DispatchSlotStatuses.Held, h.Repo.SlotStatus(11));
        Assert.Single(h.Notifications.Sent); // only the first seat
    }
}
