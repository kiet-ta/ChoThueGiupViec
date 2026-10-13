using CommonService.Application.Common.Options;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;
using Xunit;

namespace CommonService.Tests.Dispatch;

/// <summary>
/// End-to-end and cross-cutting integration tests verifying all core dispatch business rules (BE-M3-01..10):
/// 1. Double-assign guard: race condition prevention when 2 workers accept same slot simultaneously.
/// 2. 30s timeout engine: clock advancing causes offer timeout and advances to next ranked candidate.
/// 3. Stepped radius scanner: 5 -> 7 -> 10 km expansion.
/// 4. Economy pool isolation: Agency staff strictly excluded from Economy jobs.
/// 5. Force majeure incident: penalty waiver and customer 100% refund.
/// </summary>
public class ComprehensiveDispatchIntegrationTests
{
    private sealed class MockClock : IClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 10, 15, 6, 0, 0, DateTimeKind.Utc);
        public DateTime LocalNow => UtcNow.AddHours(7);
        public DateTime ToUtc(DateTime local) => local.AddHours(-7);
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateOnly LocalToday => DateOnly.FromDateTime(LocalNow);
    }

    private sealed class MockMediator : IMediator
    {
        public List<INotification> PublishedEvents { get; } = new();

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            if (notification is INotification n) PublishedEvents.Add(n);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
        {
            PublishedEvents.Add(notification);
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotImplementedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class MockWorkerAvailabilityQuery : IWorkerAvailabilityQuery
    {
        public List<AvailableWorker> Workers { get; } = new();

        public Task<IReadOnlyList<AvailableWorker>> FindAvailableFreelancersAsync(AvailabilityQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AvailableWorker>>(Workers.Where(w => w.DistanceKm <= query.RadiusKm).OrderBy(w => w.DistanceKm).Take(query.Limit).ToList());
    }

    [Fact]
    public async Task Scenario1_DoubleAssignGuard_TwoConcurrentAcceptsOfTheSameOffer_ExactlyOneWins()
    {
        var clock = new MockClock();
        var mediator = new MockMediator();
        var rules = MicrosoftOptions.Create(new BusinessRules());
        var query = new MockWorkerAvailabilityQuery();
        query.Workers.Add(new AvailableWorker(WorkerId: 10, SlotId: 555, DistanceKm: 1.0));
        var scanner = new SteppedRadiusDispatchScanner(query, rules);
        var dispatchRepo = new InMemoryDispatchRepository();
        dispatchRepo.AddSlot(555, workerId: 10);
        var offerEngine = new DispatchOfferEngine(scanner, dispatchRepo, clock, mediator, rules);

        var offer = await offerEngine.StartDispatchForOrderAsync(1, 1, ServiceTier.Economy, new DateOnly(2026, 10, 15), "SANG",
            new GeoPoint(10.762622, 106.660172), 260000m);
        Assert.NotNull(offer);

        // A second order cannot be offered the same slot while it is held.
        var otherOrder = await offerEngine.StartDispatchForOrderAsync(2, 1, ServiceTier.Economy, new DateOnly(2026, 10, 15), "SANG",
            new GeoPoint(10.762622, 106.660172), 260000m);
        Assert.Null(otherOrder);

        // The worker double-taps "accept": the database decides, exactly one request wins.
        var results = await Task.WhenAll(
            Task.Run(() => offerEngine.AcceptOfferAsync(offer.AssignmentId, workerId: 10)),
            Task.Run(() => offerEngine.AcceptOfferAsync(offer.AssignmentId, workerId: 10)));

        Assert.Single(results, r => r.Success);
        Assert.Equal(JobAssignmentStatus.Assigned, dispatchRepo.Assignments[offer.AssignmentId].AssignmentStatus);
        Assert.Equal(DispatchSlotStatuses.Booked, dispatchRepo.SlotStatus(555));
    }

    [Fact]
    public async Task Scenario2_OfferTimeout_30SecondsClockAdvancement_RejectsExpiredOfferAndAdvances()
    {
        var clock = new MockClock();
        var mediator = new MockMediator();
        var rules = MicrosoftOptions.Create(new BusinessRules());
        var query = new MockWorkerAvailabilityQuery();
        query.Workers.Add(new AvailableWorker(WorkerId: 10, SlotId: 888, DistanceKm: 1.0));
        query.Workers.Add(new AvailableWorker(WorkerId: 20, SlotId: 889, DistanceKm: 2.0));
        var scanner = new SteppedRadiusDispatchScanner(query, rules);
        var dispatchRepo = new InMemoryDispatchRepository();
        dispatchRepo.Orders[2] = new DispatchOrderContext(2, 1, ServiceTier.Economy, new DateOnly(2026, 10, 15), "SANG", 1,
            new GeoPoint(10.762622, 106.660172), 260000m);
        var offerEngine = new DispatchOfferEngine(scanner, dispatchRepo, clock, mediator, rules);

        var first = await offerEngine.StartDispatchForOrderAsync(2, 1, ServiceTier.Economy, new DateOnly(2026, 10, 15), "SANG",
            new GeoPoint(10.762622, 106.660172), 260000m);
        Assert.Equal(10, first!.WorkerId);

        // Advance clock by 31 seconds -> Timeout
        clock.UtcNow = clock.UtcNow.AddSeconds(31);

        var acceptResult = await offerEngine.AcceptOfferAsync(first.AssignmentId, workerId: 10);
        Assert.False(acceptResult.Success);

        // BR-03: the sweep closes the expired offer and the seat goes to the next ranked worker.
        Assert.Equal(1, await offerEngine.ExpirePendingOffersAsync());
        Assert.Equal(JobAssignmentStatus.Cancelled, dispatchRepo.Assignments[first.AssignmentId].AssignmentStatus);
        Assert.Equal(DispatchSlotStatuses.Available, dispatchRepo.SlotStatus(888));
        var next = Assert.Single(dispatchRepo.Assignments.Values, a => a.AssignmentStatus == JobAssignmentStatus.Offered);
        Assert.Equal(20, next.WorkerId);
        Assert.Equal(889, next.SlotId);
    }

    [Fact]
    public async Task Scenario3_SteppedRadiusScanner_Expands5to7to10KmAndExcludesLowRating()
    {
        var query = new MockWorkerAvailabilityQuery();
        // Candidate at 6.5 km (found in step 2: 7km)
        query.Workers.Add(new AvailableWorker(WorkerId: 301, SlotId: 1, DistanceKm: 6.5));

        var scanner = new SteppedRadiusDispatchScanner(query, MicrosoftOptions.Create(new BusinessRules()));

        var result = await scanner.ScanCandidatesAsync(
            ServiceTier.Economy,
            new DateOnly(2026, 10, 15),
            "SANG",
            new GeoPoint(10.762622, 106.660172)
        );

        Assert.NotNull(result.BestCandidate);
        Assert.Equal(7.0, result.MatchedRadiusKm);
        Assert.Equal(301, result.BestCandidate.Value.Candidate.WorkerId);
        Assert.False(result.ExhaustedAllSteps);
    }

    [Fact]
    public void Scenario4_EconomyPoolIsolation_AgencyStaffStrictlyExcludedFromEconomy()
    {
        var candidates = new List<DispatchCandidate>
        {
            new() { WorkerId = 401, WorkerType = "AGENCY_STAFF", AgencyId = 5, DistanceKm = 1.0, RatingAvg = 4.95m, CompletedJobs = 100 },
            new() { WorkerId = 402, WorkerType = "FREELANCER", AgencyId = null, DistanceKm = 1.0, RatingAvg = 4.95m, CompletedJobs = 100 }
        };

        var filtered = SteppedRadiusDispatchScanner.FilterCandidatesByServiceTier(candidates, ServiceTier.Economy).ToList();
        Assert.Single(filtered);
        Assert.Equal(402, filtered[0].WorkerId);
        Assert.Equal("FREELANCER", filtered[0].WorkerType);
    }

    [Fact]
    public async Task Scenario5_ForceMajeureIncident_WaivesWorkerPenaltyAndRefunds100Percent()
    {
        var clock = new MockClock();
        var mediator = new MockMediator();
        var rules = MicrosoftOptions.Create(new BusinessRules());
        var query = new MockWorkerAvailabilityQuery();
        var scanner = new SteppedRadiusDispatchScanner(query, rules);
        var dispatchRepo = new InMemoryDispatchRepository();
        var offerEngine = new DispatchOfferEngine(scanner, dispatchRepo, clock, mediator, rules);

        var inMemIncidentRepo = new InMemoryIncidentRepositoryHelper();
        var assignment = new JobAssignment
        {
            AssignmentId = 501,
            OrderId = 50,
            WorkerId = 42,
            ServiceTier = ServiceTier.Economy,
            GrossAmount = 260000m
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        inMemIncidentRepo.Assignments[501] = assignment;

        var incidentService = new IncidentService(inMemIncidentRepo, offerEngine, clock, mediator, rules, NullLogger<IncidentService>.Instance);

        // 1. Worker reports force majeure accident
        var reportResult = await incidentService.ReportIncidentAsync(new ReportIncidentRequest(
            AssignmentId: 501,
            WorkerId: 42,
            IncidentType: "ACCIDENT",
            Description: "Xe máy bị đâm trên đường đến nhà khách",
            PhotoUrl: "https://example.com/accident.jpg",
            Latitude: 10.762622m,
            Longitude: 106.660172m
        ));

        Assert.True(reportResult.Success);
        Assert.True(reportResult.IsPenaltyWaived);
        Assert.Equal(JobAssignmentStatus.Incident, assignment.AssignmentStatus);

        // 2. Customer declines replacement worker -> Cancelled with 100% refund
        var declineResult = await incidentService.HandleRedispatchFailedOrDeclinedAsync(
            incidentId: reportResult.IncidentId!.Value,
            customerDeclined: true
        );

        Assert.True(declineResult.Success);
        Assert.True(declineResult.IsPenaltyWaived);
        Assert.Equal(260000m, declineResult.RefundAmount);
        Assert.Equal(JobAssignmentStatus.Cancelled, assignment.AssignmentStatus);

        // Verify AssignmentFailed event triggered for 100% refund
        var failedEvt = Assert.IsType<AssignmentFailed>(mediator.PublishedEvents.Last());
        Assert.Equal(50, failedEvt.OrderId);
        Assert.Contains("Hoàn tiền 100%", failedEvt.Reason);
    }

    private sealed class InMemoryIncidentRepositoryHelper : IIncidentRepository
    {
        public Dictionary<long, JobAssignment> Assignments { get; } = new();
        public Dictionary<long, IncidentLog> Incidents { get; } = new();

        public Task<JobAssignment?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default)
        {
            Assignments.TryGetValue(assignmentId, out var a);
            return Task.FromResult(a);
        }

        public Task<JobOrder?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default) => Task.FromResult<JobOrder?>(null);

        public Task<IncidentLog> CreateIncidentLogAsync(IncidentLog log, CancellationToken cancellationToken = default)
        {
            log.IncidentId = Incidents.Count + 1000;
            Incidents[log.IncidentId] = log;
            return Task.FromResult(log);
        }

        public Task<IncidentLog?> GetIncidentLogAsync(long incidentId, CancellationToken cancellationToken = default)
        {
            Incidents.TryGetValue(incidentId, out var log);
            return Task.FromResult(log);
        }

        public Task<bool> UpdateIncidentStatusAsync(long incidentId, string redispatchStatus, CancellationToken cancellationToken = default)
        {
            if (Incidents.TryGetValue(incidentId, out var log))
            {
                log.RedispatchStatus = redispatchStatus;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> UpdateAssignmentStatusAsync(long assignmentId, JobAssignmentStatus newStatus, CancellationToken cancellationToken = default)
        {
            if (Assignments.TryGetValue(assignmentId, out var a))
            {
                a.TransitionTo(newStatus);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
    }
}
