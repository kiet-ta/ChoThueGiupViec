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

public class IncidentServiceTests
{
    private sealed class InMemoryIncidentRepository : IIncidentRepository
    {
        public Dictionary<long, JobAssignment> Assignments { get; } = new();
        public Dictionary<long, JobOrder> Orders { get; } = new();
        public Dictionary<long, IncidentLog> Incidents { get; } = new();

        public Task<JobAssignment?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default)
        {
            Assignments.TryGetValue(assignmentId, out var a);
            return Task.FromResult(a);
        }

        public Task<JobOrder?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default)
        {
            Orders.TryGetValue(orderId, out var o);
            return Task.FromResult(o);
        }

        public Task<IncidentLog> CreateIncidentLogAsync(IncidentLog log, CancellationToken cancellationToken = default)
        {
            if (log.IncidentId == 0)
            {
                log.IncidentId = Incidents.Count + 1000;
            }
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

    private sealed class MockDispatchRepository : IDispatchRepository
    {
        public Dictionary<long, JobAssignment> Assignments { get; } = new();

        public Task<JobAssignment?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default)
        {
            Assignments.TryGetValue(assignmentId, out var a);
            return Task.FromResult(a);
        }

        public Task<JobAssignment> CreateAssignmentAsync(JobAssignment assignment, CancellationToken cancellationToken = default)
        {
            if (assignment.AssignmentId == 0)
            {
                assignment.AssignmentId = Assignments.Count + 100;
            }
            Assignments[assignment.AssignmentId] = assignment;
            return Task.FromResult(assignment);
        }

        public Task<bool> TryLockSlotAndAssignAsync(long assignmentId, int slotId, DateTime assignedAtUtc, CancellationToken cancellationToken = default)
        {
            if (Assignments.TryGetValue(assignmentId, out var a))
            {
                a.SlotId = slotId;
                a.TransitionTo(JobAssignmentStatus.Assigned);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> CancelAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default)
        {
            if (Assignments.TryGetValue(assignmentId, out var a))
            {
                a.TransitionTo(JobAssignmentStatus.Cancelled);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
    }

    private sealed class MockWorkerAvailabilityQuery : IWorkerAvailabilityQuery
    {
        public List<AvailableWorker> Workers { get; } = new();

        public Task<IReadOnlyList<AvailableWorker>> FindAvailableFreelancersAsync(AvailabilityQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AvailableWorker>>(Workers.Where(w => w.DistanceKm <= query.RadiusKm).Take(query.Limit).ToList());
    }

    [Fact]
    public async Task ReportIncidentAsync_RecordsIncidentWithPenaltyWaived_AndTransitionsToIncidentStatus()
    {
        var clock = new MockClock();
        var mediator = new MockMediator();
        var rules = MicrosoftOptions.Create(new BusinessRules());
        var query = new MockWorkerAvailabilityQuery();
        var scanner = new SteppedRadiusDispatchScanner(query, rules);
        var dispatchRepo = new MockDispatchRepository();
        var offerStore = new InMemoryOfferStore();
        var offerEngine = new DispatchOfferEngine(scanner, dispatchRepo, offerStore, clock, mediator, rules);

        var repo = new InMemoryIncidentRepository();
        var assignment = new JobAssignment
        {
            AssignmentId = 1,
            OrderId = 101,
            WorkerId = 42,
            ServiceTier = ServiceTier.Economy,
            GrossAmount = 260000m
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        repo.Assignments[1] = assignment;

        var service = new IncidentService(repo, offerEngine, clock, mediator, rules, NullLogger<IncidentService>.Instance);

        var req = new ReportIncidentRequest(
            AssignmentId: 1,
            WorkerId: 42,
            IncidentType: "ACCIDENT",
            Description: "Thủng xăm xe máy giữa cầu",
            PhotoUrl: "https://example.com/flat_tire.jpg",
            Latitude: 10.762622m,
            Longitude: 106.660172m
        );

        var result = await service.ReportIncidentAsync(req);

        Assert.True(result.Success);
        Assert.True(result.IsPenaltyWaived);
        Assert.Equal(IncidentResolutionStatus.IncidentLoggedRedispatching, result.Status);
        Assert.Equal(JobAssignmentStatus.Incident, repo.Assignments[1].AssignmentStatus);
        Assert.NotNull(result.IncidentId);

        var loggedIncident = repo.Incidents[result.IncidentId.Value];
        Assert.True(loggedIncident.PenaltyWaived);
        Assert.Equal("PENDING_REDISPATCH", loggedIncident.RedispatchStatus);

        // Verify IncidentReported domain event published
        var evt = Assert.IsType<IncidentReported>(mediator.PublishedEvents[0]);
        Assert.Equal(1, evt.AssignmentId);
        Assert.Equal(42, evt.WorkerId);
    }

    [Fact]
    public async Task ExecuteAutoRedispatchAsync_WhenReplacementFound_TransitionsOriginalToReassigned()
    {
        var clock = new MockClock();
        var mediator = new MockMediator();
        var rules = MicrosoftOptions.Create(new BusinessRules());
        var query = new MockWorkerAvailabilityQuery();
        query.Workers.Add(new AvailableWorker(WorkerId: 99, SlotId: 909, DistanceKm: 1.5));

        var scanner = new SteppedRadiusDispatchScanner(query, rules);
        var dispatchRepo = new MockDispatchRepository();
        var offerStore = new InMemoryOfferStore();
        var offerEngine = new DispatchOfferEngine(scanner, dispatchRepo, offerStore, clock, mediator, rules);

        var repo = new InMemoryIncidentRepository();
        var assignment = new JobAssignment
        {
            AssignmentId = 2,
            OrderId = 202,
            WorkerId = 42,
            ServiceTier = ServiceTier.Economy,
            GrossAmount = 260000m
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        assignment.TransitionTo(JobAssignmentStatus.Incident);
        repo.Assignments[2] = assignment;

        repo.Incidents[1002] = new IncidentLog
        {
            IncidentId = 1002,
            AssignmentId = 2,
            PenaltyWaived = true,
            RedispatchStatus = "PENDING_REDISPATCH"
        };

        var service = new IncidentService(repo, offerEngine, clock, mediator, rules, NullLogger<IncidentService>.Instance);

        var result = await service.ExecuteAutoRedispatchAsync(
            incidentId: 1002,
            orderLocation: new GeoPoint(10.762622, 106.660172),
            date: new DateOnly(2026, 10, 15),
            shiftCode: "SANG"
        );

        Assert.True(result.Success);
        Assert.Equal(IncidentResolutionStatus.ReplacementWorkerFound, result.Status);
        Assert.Equal(99, result.ReplacementWorkerId);
        Assert.Equal(JobAssignmentStatus.Reassigned, repo.Assignments[2].AssignmentStatus);
        Assert.Equal("REPLACED", repo.Incidents[1002].RedispatchStatus);
    }

    [Fact]
    public async Task HandleRedispatchFailedOrDeclinedAsync_WhenCustomerDeclines_CancelsAndPublishesAssignmentFailedFor100PercentRefund()
    {
        var clock = new MockClock();
        var mediator = new MockMediator();
        var rules = MicrosoftOptions.Create(new BusinessRules());
        var query = new MockWorkerAvailabilityQuery();
        var scanner = new SteppedRadiusDispatchScanner(query, rules);
        var dispatchRepo = new MockDispatchRepository();
        var offerStore = new InMemoryOfferStore();
        var offerEngine = new DispatchOfferEngine(scanner, dispatchRepo, offerStore, clock, mediator, rules);

        var repo = new InMemoryIncidentRepository();
        var assignment = new JobAssignment
        {
            AssignmentId = 3,
            OrderId = 303,
            WorkerId = 42,
            ServiceTier = ServiceTier.Economy,
            GrossAmount = 260000m
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        assignment.TransitionTo(JobAssignmentStatus.Incident);
        repo.Assignments[3] = assignment;

        repo.Incidents[1003] = new IncidentLog
        {
            IncidentId = 1003,
            AssignmentId = 3,
            PenaltyWaived = true,
            RedispatchStatus = "PENDING_REDISPATCH"
        };

        var service = new IncidentService(repo, offerEngine, clock, mediator, rules, NullLogger<IncidentService>.Instance);

        var result = await service.HandleRedispatchFailedOrDeclinedAsync(
            incidentId: 1003,
            customerDeclined: true
        );

        Assert.True(result.Success);
        Assert.Equal(IncidentResolutionStatus.CustomerDeclinedReplacementCancelled, result.Status);
        Assert.Equal(260000m, result.RefundAmount);
        Assert.True(result.IsPenaltyWaived);
        Assert.Equal(JobAssignmentStatus.Cancelled, repo.Assignments[3].AssignmentStatus);

        // Verify AssignmentFailed event published for 100% refund
        var failedEvt = Assert.IsType<AssignmentFailed>(mediator.PublishedEvents[0]);
        Assert.Equal(303, failedEvt.OrderId);
        Assert.Contains("Khách hàng từ chối đổi thợ", failedEvt.Reason);
    }
}
