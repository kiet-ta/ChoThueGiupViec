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

public class MilestoneFailoverServiceTests
{
    private sealed class InMemoryMilestoneFailoverRepository : IMilestoneFailoverRepository
    {
        public Dictionary<long, JobAssignment> Assignments { get; } = new();
        public Dictionary<long, JobOrder> Orders { get; } = new();
        public Dictionary<int, BookingSlot> Slots { get; } = new();
        public Dictionary<int, Worker> Workers { get; } = new();
        public List<Worker> SuperFreelancers { get; } = new();

        public Task<JobAssignment?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default)
        {
            Assignments.TryGetValue(assignmentId, out var assignment);
            return Task.FromResult(assignment);
        }

        public Task<JobOrder?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default)
        {
            Orders.TryGetValue(orderId, out var order);
            return Task.FromResult(order);
        }

        public Task<BookingSlot?> GetSlotAsync(int slotId, CancellationToken cancellationToken = default)
        {
            Slots.TryGetValue(slotId, out var slot);
            return Task.FromResult(slot);
        }

        public Task<Worker?> GetWorkerAsync(int workerId, CancellationToken cancellationToken = default)
        {
            Workers.TryGetValue(workerId, out var worker);
            return Task.FromResult(worker);
        }

        public Task<IReadOnlyList<Worker>> GetEligibleSuperFreelancersAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Worker>>(SuperFreelancers);
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

        public Task<bool> UpdateAssignmentStatusAsync(long assignmentId, JobAssignmentStatus newStatus, CancellationToken cancellationToken = default)
        {
            if (Assignments.TryGetValue(assignmentId, out var assignment))
            {
                assignment.TransitionTo(newStatus);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> SwapWorkerInAssignmentAsync(long assignmentId, int newWorkerId, CancellationToken cancellationToken = default)
        {
            if (Assignments.TryGetValue(assignmentId, out var assignment))
            {
                assignment.WorkerId = newWorkerId;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> ReleaseSlotAsync(int slotId, CancellationToken cancellationToken = default)
        {
            if (Slots.TryGetValue(slotId, out var slot))
            {
                slot.SlotStatus = "AVAILABLE";
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> LockSlotAsync(int slotId, CancellationToken cancellationToken = default)
        {
            if (Slots.TryGetValue(slotId, out var slot))
            {
                slot.SlotStatus = "LOCKED";
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
            if (notification is INotification n)
            {
                PublishedEvents.Add(n);
            }
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

    private sealed class MockAgencyCapacityService : IAgencyCapacityService
    {
        public bool HasCap { get; set; } = true;
        public CapacityReservation? NextReservation { get; set; }

        public Task<bool> HasCapacityAsync(CapacityRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(HasCap);

        public Task<CapacityReservation?> TryReserveAsync(CapacityRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(NextReservation);

        public Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ReleaseByOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class MockSlaPenaltyService : ISlaPenaltyService
    {
        public List<SlaPenaltyRequest> AppliedRequests { get; } = new();
        public decimal Score { get; set; } = 100.00m;

        public Task<SlaPenaltyResult> ApplyAsync(SlaPenaltyRequest request, CancellationToken cancellationToken = default)
        {
            AppliedRequests.Add(request);
            decimal delta = request.Violation == SlaViolation.Shortage ? -10m : -20m;
            Score += delta;
            return Task.FromResult(new SlaPenaltyResult(
                SlaPointsDelta: delta,
                NewSlaScore: Score,
                EscrowDeducted: request.RescueCost + request.CustomerRefundAmount,
                Shortfall: 0m,
                AgencySuspended: false
            ));
        }
    }

    private static (MilestoneFailoverService, InMemoryMilestoneFailoverRepository, MockClock, MockMediator, MockAgencyCapacityService, MockSlaPenaltyService) CreateTestRig()
    {
        var repo = new InMemoryMilestoneFailoverRepository();
        var clock = new MockClock();
        var mediator = new MockMediator();
        var agencyService = new MockAgencyCapacityService();
        var slaService = new MockSlaPenaltyService();
        var rules = MicrosoftOptions.Create(new BusinessRules());

        var service = new MilestoneFailoverService(
            repo,
            agencyService,
            slaService,
            clock,
            mediator,
            rules,
            NullLogger<MilestoneFailoverService>.Instance
        );

        return (service, repo, clock, mediator, agencyService, slaService);
    }

    [Fact]
    public async Task ReportUnavailability_MoreThan2HoursNotice_Opens30MinSelfSwapWindow()
    {
        var (service, repo, clock, mediator, agencyService, slaService) = CreateTestRig();

        // Shift is at 10:00 UTC (4 hours from now 06:00 UTC)
        var assignment = new JobAssignment
        {
            AssignmentId = 1,
            OrderId = 10,
            AgencyId = 5,
            WorkerId = 50,
            SlotId = 100,
            ServiceTier = ServiceTier.Premium,
            GrossAmount = 400000m,
            PayoutAmount = 320000m
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        repo.Assignments[1] = assignment;

        repo.Slots[100] = new BookingSlot
        {
            SlotId = 100,
            SlotDate = DateOnly.FromDateTime(clock.UtcNow),
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(14, 0),
            SlotStatus = "LOCKED"
        };

        var result = await service.ReportUnavailabilityAsync(1, "Thợ bị ốm");

        Assert.True(result.Success);
        Assert.Equal(FailoverStatus.SelfSwapWindowOpened, result.Status);
        Assert.Equal(4.0, result.NoticeHoursBeforeShift, precision: 1);
        Assert.NotNull(result.SelfSwapDeadlineUtc);
        Assert.Equal(clock.UtcNow.AddMinutes(30), result.SelfSwapDeadlineUtc);
        Assert.Empty(slaService.AppliedRequests);
    }

    [Fact]
    public async Task ExecuteAgencySelfSwap_Within30MinWindow_SwapsWorkerSuccessfully()
    {
        var (service, repo, clock, mediator, agencyService, slaService) = CreateTestRig();

        // Setup assignment and open window (>2h)
        var assignment = new JobAssignment
        {
            AssignmentId = 2,
            OrderId = 20,
            AgencyId = 5,
            WorkerId = 50,
            SlotId = 200,
            ServiceTier = ServiceTier.Premium,
            GrossAmount = 400000m,
            PayoutAmount = 320000m
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        repo.Assignments[2] = assignment;

        repo.Slots[200] = new BookingSlot
        {
            SlotId = 200,
            SlotDate = DateOnly.FromDateTime(clock.UtcNow),
            StartTime = new TimeOnly(10, 0),
            SlotStatus = "LOCKED"
        };

        // Thợ mới cùng Agency 5
        repo.Workers[55] = new Worker
        {
            WorkerId = 55,
            FullName = "Worker Replacement"
        };
        // Reflection to set AgencyId since setter is private
        typeof(Worker).GetProperty(nameof(Worker.AgencyId))!.SetValue(repo.Workers[55], 5);

        await service.ReportUnavailabilityAsync(2, "Thợ bận");

        // Swap within 30 min (advance 10 minutes)
        clock.UtcNow = clock.UtcNow.AddMinutes(10);
        var swapResult = await service.ExecuteAgencySelfSwapAsync(2, 55);

        Assert.True(swapResult.Success);
        Assert.Equal(FailoverStatus.AgencySelfSwapped, swapResult.Status);
        Assert.Equal(55, swapResult.NewWorkerId);
        Assert.Equal(55, repo.Assignments[2].WorkerId);
        Assert.Empty(slaService.AppliedRequests);
    }

    [Fact]
    public async Task ReportUnavailability_LessThan2HoursNotice_TriggersImmediateEmergencyRescueToAnotherAgency()
    {
        var (service, repo, clock, mediator, agencyService, slaService) = CreateTestRig();

        // Shift is at 07:30 UTC (only 1.5 hours notice from 06:00 UTC)
        var assignment = new JobAssignment
        {
            AssignmentId = 3,
            OrderId = 30,
            AgencyId = 5,
            WorkerId = 50,
            SlotId = 300,
            ServiceTier = ServiceTier.Premium,
            GrossAmount = 400000m,
            PayoutAmount = 320000m
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        repo.Assignments[3] = assignment;

        repo.Slots[300] = new BookingSlot
        {
            SlotId = 300,
            SlotDate = DateOnly.FromDateTime(clock.UtcNow),
            StartTime = new TimeOnly(7, 30),
            SlotStatus = "LOCKED"
        };

        // Another Agency (Agency 7) has capacity
        agencyService.NextReservation = new CapacityReservation(Guid.NewGuid(), 7, [701]);

        var result = await service.ReportUnavailabilityAsync(3, "Thợ không liên lạc được sát giờ");

        Assert.True(result.Success);
        Assert.Equal(FailoverStatus.EmergencyRescuedToAgency, result.Status);
        Assert.Equal(JobAssignmentStatus.Reassigned, repo.Assignments[3].AssignmentStatus);
        Assert.Equal(7, result.NewAgencyId);

        // Verify SLA penalty applied to Agency 5
        Assert.Single(slaService.AppliedRequests);
        var penalty = slaService.AppliedRequests[0];
        Assert.Equal(5, penalty.AgencyId);
        Assert.Equal(SlaViolation.Shortage, penalty.Violation);
        Assert.Equal(320000m, penalty.RescueCost);

        // Verify JobAssigned event published for the new agency assignment
        var evt = Assert.IsType<JobAssigned>(mediator.PublishedEvents[0]);
        Assert.Equal(7, evt.AgencyId);
    }

    [Fact]
    public async Task EmergencyRescue_WhenNoAgencyAvailable_RescuesToSuperFreelancer()
    {
        var (service, repo, clock, mediator, agencyService, slaService) = CreateTestRig();

        // Shift is at 07:00 UTC (1 hour notice)
        var assignment = new JobAssignment
        {
            AssignmentId = 4,
            OrderId = 40,
            AgencyId = 5,
            WorkerId = 50,
            SlotId = 400,
            ServiceTier = ServiceTier.Premium,
            GrossAmount = 400000m,
            PayoutAmount = 320000m
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        repo.Assignments[4] = assignment;

        repo.Slots[400] = new BookingSlot
        {
            SlotId = 400,
            SlotDate = DateOnly.FromDateTime(clock.UtcNow),
            StartTime = new TimeOnly(7, 0),
            SlotStatus = "LOCKED"
        };

        // No other agency has capacity
        agencyService.NextReservation = null;

        // Super-Freelancer available (Rating >= 4.80, CompletedJobs >= 50)
        repo.SuperFreelancers.Add(new Worker
        {
            WorkerId = 99,
            FullName = "Nguyễn Siêu Thợ",
            IsSuperFreelancer = true,
            RatingAvg = 4.95m,
            CompletedJobs = 80
        });

        var result = await service.ReportUnavailabilityAsync(4, "Sát giờ báo bận");

        Assert.True(result.Success);
        Assert.Equal(FailoverStatus.EmergencyRescuedToSuperFreelancer, result.Status);
        Assert.Equal(99, result.NewWorkerId);
        Assert.Equal(JobAssignmentStatus.Reassigned, repo.Assignments[4].AssignmentStatus);

        // Verify SLA penalty deducted from failing agency
        Assert.Single(slaService.AppliedRequests);
        Assert.Equal(5, slaService.AppliedRequests[0].AgencyId);
        Assert.Equal(320000m, slaService.AppliedRequests[0].RescueCost);

        // Verify JobAssigned published with Super-Freelancer workerId
        var evt = Assert.IsType<JobAssigned>(mediator.PublishedEvents[0]);
        Assert.Equal(99, evt.WorkerId);
        Assert.Null(evt.AgencyId);
    }

    [Fact]
    public async Task HandleSelfSwapTimeout_After30MinWindow_TriggersEmergencyRescue()
    {
        var (service, repo, clock, mediator, agencyService, slaService) = CreateTestRig();

        var assignment = new JobAssignment
        {
            AssignmentId = 5,
            OrderId = 50,
            AgencyId = 5,
            WorkerId = 50,
            SlotId = 500,
            ServiceTier = ServiceTier.Premium,
            GrossAmount = 400000m,
            PayoutAmount = 320000m
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        repo.Assignments[5] = assignment;

        repo.Slots[500] = new BookingSlot
        {
            SlotId = 500,
            SlotDate = DateOnly.FromDateTime(clock.UtcNow),
            StartTime = new TimeOnly(10, 0),
            SlotStatus = "LOCKED"
        };

        await service.ReportUnavailabilityAsync(5, "Agency báo tìm thợ");

        // Another Agency 8 ready to rescue
        agencyService.NextReservation = new CapacityReservation(Guid.NewGuid(), 8, [801]);

        // Advance 31 minutes
        clock.UtcNow = clock.UtcNow.AddMinutes(31);

        var result = await service.HandleSelfSwapTimeoutAsync(5);

        Assert.True(result.Success);
        Assert.Equal(FailoverStatus.EmergencyRescuedToAgency, result.Status);
        Assert.Equal(8, result.NewAgencyId);
        Assert.Single(slaService.AppliedRequests);
    }
}
