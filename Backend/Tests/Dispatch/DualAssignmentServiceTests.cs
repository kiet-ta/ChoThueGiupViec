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

public class DualAssignmentServiceTests
{
    private sealed class InMemoryDualAssignmentRepository : IDualAssignmentRepository
    {
        public Dictionary<long, JobOrder> Orders { get; } = new();
        public Dictionary<long, JobAssignment> Assignments { get; } = new();

        public Task<JobOrder?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default)
        {
            Orders.TryGetValue(orderId, out var order);
            return Task.FromResult(order);
        }

        public Task<IReadOnlyList<JobAssignment>> GetAssignmentsByOrderIdAsync(long orderId, CancellationToken cancellationToken = default)
        {
            var list = Assignments.Values.Where(a => a.OrderId == orderId).ToList();
            return Task.FromResult<IReadOnlyList<JobAssignment>>(list);
        }

        public Task<JobAssignment> CreateAssignmentAsync(JobAssignment assignment, CancellationToken cancellationToken = default)
        {
            if (assignment.AssignmentId == 0)
            {
                assignment.AssignmentId = Assignments.Count + 1000;
            }
            Assignments[assignment.AssignmentId] = assignment;
            return Task.FromResult(assignment);
        }

        public Task<bool> UpdateAssignmentWorkerAndSlotAsync(long assignmentId, int workerId, int slotId, CancellationToken cancellationToken = default)
        {
            if (Assignments.TryGetValue(assignmentId, out var a))
            {
                a.WorkerId = workerId;
                a.SlotId = slotId;
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
    public void RequiresDualWorkers_ReturnsTrue_ForAreaOver80m2_OrRequiredWorkers2()
    {
        Assert.True(DualAssignmentService.RequiresDualWorkers(85.0m, 1));
        Assert.True(DualAssignmentService.RequiresDualWorkers(120.0m, 2));
        Assert.True(DualAssignmentService.RequiresDualWorkers(70.0m, 2));
        Assert.False(DualAssignmentService.RequiresDualWorkers(60.0m, 1));
        Assert.False(DualAssignmentService.RequiresDualWorkers(80.0m, 1));
    }

    [Fact]
    public async Task DispatchDualWorkersAsync_CreatesTwoParallelAssignments_ForHouseOver80m2()
    {
        var clock = new MockClock();
        var mediator = new MockMediator();
        var rules = MicrosoftOptions.Create(new BusinessRules());
        var query = new MockWorkerAvailabilityQuery();
        query.Workers.Add(new AvailableWorker(WorkerId: 10, SlotId: 101, DistanceKm: 2.0));
        query.Workers.Add(new AvailableWorker(WorkerId: 20, SlotId: 202, DistanceKm: 3.5));

        var scanner = new SteppedRadiusDispatchScanner(query, rules);
        var dispatchRepo = new MockDispatchRepository();
        var offerStore = new InMemoryOfferStore();
        var offerEngine = new DispatchOfferEngine(scanner, dispatchRepo, offerStore, clock, mediator, rules);

        var repo = new InMemoryDualAssignmentRepository();
        var service = new DualAssignmentService(repo, offerEngine, clock, mediator, rules, NullLogger<DualAssignmentService>.Instance);

        var result = await service.DispatchDualWorkersAsync(
            orderId: 501,
            customerId: 1,
            serviceTier: ServiceTier.Economy,
            date: new DateOnly(2026, 10, 15),
            shiftCode: "SANG",
            location: new GeoPoint(10.762622, 106.660172),
            totalAmount: 520000m
        );

        Assert.True(result.Success);
        Assert.Equal(2, result.RequiredWorkers);
        Assert.False(result.IsFallbackConsecutiveShifts);
        Assert.NotNull(result.FirstAssignmentId);
        Assert.NotNull(result.SecondAssignmentId);
        Assert.NotEqual(result.FirstAssignmentId, result.SecondAssignmentId);
        Assert.Equal(2, dispatchRepo.Assignments.Count);

        var seq1 = dispatchRepo.Assignments[result.FirstAssignmentId.Value];
        var seq2 = dispatchRepo.Assignments[result.SecondAssignmentId.Value];

        Assert.Equal(1, seq1.AssignmentSeq);
        Assert.Equal(2, seq2.AssignmentSeq);
        Assert.Equal(260000m, seq1.GrossAmount);
        Assert.Equal(260000m, seq2.GrossAmount);
    }

    [Fact]
    public async Task ActivateConsecutiveShiftsFallbackAsync_WhenCustomerConfirms_AssignsSameWorkerToTwoConsecutiveShifts()
    {
        var clock = new MockClock();
        var mediator = new MockMediator();
        var rules = MicrosoftOptions.Create(new BusinessRules());
        var query = new MockWorkerAvailabilityQuery();
        var scanner = new SteppedRadiusDispatchScanner(query, rules);
        var dispatchRepo = new MockDispatchRepository();
        var offerStore = new InMemoryOfferStore();
        var offerEngine = new DispatchOfferEngine(scanner, dispatchRepo, offerStore, clock, mediator, rules);

        var repo = new InMemoryDualAssignmentRepository();
        var service = new DualAssignmentService(repo, offerEngine, clock, mediator, rules, NullLogger<DualAssignmentService>.Instance);

        // Setup order
        repo.Orders[502] = new JobOrder
        {
            OrderId = 502,
            CustomerId = 10,
            AreaSnapshotM2 = 100m
        };

        // Worker 10 assigned to assignment 1 (Morning shift)
        var assignment1 = new JobAssignment
        {
            AssignmentId = 1001,
            OrderId = 502,
            CustomerId = 10,
            WorkerId = 10,
            SlotId = 101,
            ServiceTier = ServiceTier.Economy,
            AssignmentSeq = 1,
            GrossAmount = 260000m,
            CommissionRate = 0.20m,
            PayoutAmount = 208000m
        };
        assignment1.TransitionTo(JobAssignmentStatus.Assigned);
        repo.Assignments[1001] = assignment1;

        // Fallback: Missing worker 2 -> Customer confirms worker 10 doing consecutive shift (slot 102 - Afternoon shift)
        var fallbackResult = await service.ActivateConsecutiveShiftsFallbackAsync(
            orderId: 502,
            existingAssignmentId: 1001,
            consecutiveSlotId: 102,
            customerConfirmed: true
        );

        Assert.True(fallbackResult.Success);
        Assert.True(fallbackResult.IsFallbackConsecutiveShifts);
        Assert.Equal(10, fallbackResult.FirstWorkerId);
        Assert.Equal(10, fallbackResult.SecondWorkerId);
        Assert.Equal(1001, fallbackResult.FirstAssignmentId);
        Assert.NotNull(fallbackResult.SecondAssignmentId);

        // Verify JobAssigned published for second shift
        var assignedEvt = Assert.IsType<JobAssigned>(mediator.PublishedEvents[0]);
        Assert.Equal(10, assignedEvt.WorkerId);
        Assert.Equal(102, assignedEvt.SlotId);
    }

    [Fact]
    public async Task ActivateConsecutiveShiftsFallbackAsync_Rejects_IfCustomerNotConfirmed()
    {
        var clock = new MockClock();
        var mediator = new MockMediator();
        var rules = MicrosoftOptions.Create(new BusinessRules());
        var query = new MockWorkerAvailabilityQuery();
        var scanner = new SteppedRadiusDispatchScanner(query, rules);
        var dispatchRepo = new MockDispatchRepository();
        var offerStore = new InMemoryOfferStore();
        var offerEngine = new DispatchOfferEngine(scanner, dispatchRepo, offerStore, clock, mediator, rules);

        var repo = new InMemoryDualAssignmentRepository();
        var service = new DualAssignmentService(repo, offerEngine, clock, mediator, rules, NullLogger<DualAssignmentService>.Instance);

        var result = await service.ActivateConsecutiveShiftsFallbackAsync(
            orderId: 503,
            existingAssignmentId: 1001,
            consecutiveSlotId: 102,
            customerConfirmed: false
        );

        Assert.False(result.Success);
        Assert.Contains("chưa xác nhận đồng ý", result.Message!);
        Assert.Empty(mediator.PublishedEvents);
    }
}
