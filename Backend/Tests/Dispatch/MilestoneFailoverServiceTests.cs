using CommonService.Application.Common.Options;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Domain.StateMachines;
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

        /// <summary>RESCUE-TEST-01: set by CreateTestRig; the repository records whether each write ran inside the unit of work.</summary>
        public MockUnitOfWork? Uow { get; set; }
        public List<(string Call, bool InTransaction)> WriteLog { get; } = new();
        public List<(JobAssignmentStatus From, JobAssignmentStatus To)> StatusUpdates { get; } = new();

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
            assignment.TransitionTo(JobAssignmentStatus.Assigned);
            Assignments[assignment.AssignmentId] = assignment;
            WriteLog.Add(("CreateAssignment", Uow?.InTransaction ?? false));
            return Task.FromResult(assignment);
        }

        public Task<bool> UpdateAssignmentStatusAsync(long assignmentId, JobAssignmentStatus expectedCurrentStatus, JobAssignmentStatus newStatus, CancellationToken cancellationToken = default)
        {
            WriteLog.Add(("UpdateAssignmentStatus", Uow?.InTransaction ?? false));
            StatusUpdates.Add((expectedCurrentStatus, newStatus));
            if (Assignments.TryGetValue(assignmentId, out var assignment))
            {
                if (assignment.AssignmentStatus == expectedCurrentStatus)
                {
                    assignment.TransitionTo(newStatus);
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
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
            WriteLog.Add(("ReleaseSlot", Uow?.InTransaction ?? false));
            if (Slots.TryGetValue(slotId, out var slot))
            {
                slot.SlotStatus = "AVAILABLE";
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> LockSlotAsync(int slotId, CancellationToken cancellationToken = default)
        {
            WriteLog.Add(("LockSlot", Uow?.InTransaction ?? false));
            if (Slots.TryGetValue(slotId, out var slot))
            {
                slot.SlotStatus = "LOCKED";
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<BookingSlot?> GetAvailableSlotAsync(int workerId, DateOnly date, string shiftCode, CancellationToken cancellationToken = default)
        {
            var slot = Slots.Values.FirstOrDefault(s =>
                s.WorkerId == workerId && s.SlotDate == date && s.ShiftCode == shiftCode && s.SlotStatus == "AVAILABLE");
            return Task.FromResult(slot);
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
        public MockUnitOfWork? Uow { get; set; }
        public List<bool> PublishedInTransaction { get; } = new();

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            if (notification is INotification n)
            {
                PublishedEvents.Add(n);
                PublishedInTransaction.Add(Uow?.InTransaction ?? false);
            }
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
        {
            PublishedEvents.Add(notification);
            PublishedInTransaction.Add(Uow?.InTransaction ?? false);
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
        public int TryReserveCalls { get; private set; }
        public List<CapacityRequest> Requests { get; } = new();
        public List<Guid> Released { get; } = new();

        public Task<bool> HasCapacityAsync(CapacityRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(HasCap);

        public Task<CapacityReservation?> TryReserveAsync(CapacityRequest request, CancellationToken cancellationToken = default)
        {
            TryReserveCalls++;
            Requests.Add(request);
            return Task.FromResult(NextReservation);
        }

        public Task<CapacityReservation?> GetReservationByOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<CapacityReservation?>(null);

        public Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken = default)
        {
            Released.Add(reservationId);
            return Task.CompletedTask;
        }

        public Task ReleaseByOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class MockSlaPenaltyService : ISlaPenaltyService
    {
        public List<SlaPenaltyRequest> AppliedRequests { get; } = new();
        public MockUnitOfWork? Uow { get; set; }
        public List<bool> AppliedInTransaction { get; } = new();
        public decimal Score { get; set; } = 100.00m;

        public Task<SlaPenaltyResult> ApplyAsync(SlaPenaltyRequest request, CancellationToken cancellationToken = default)
        {
            AppliedRequests.Add(request);
            AppliedInTransaction.Add(Uow?.InTransaction ?? false);
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

    private sealed class MockUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool InTransaction { get; private set; }
        public int Executions { get; private set; }

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default)
        {
            Executions++;
            InTransaction = true;
            try
            {
                return await action();
            }
            finally
            {
                InTransaction = false;
            }
        }
    }

    private static (MilestoneFailoverService, InMemoryMilestoneFailoverRepository, MockClock, MockMediator, MockAgencyCapacityService, MockSlaPenaltyService, MockUnitOfWork) CreateTestRig()
    {
        var repo = new InMemoryMilestoneFailoverRepository();
        var clock = new MockClock();
        var mediator = new MockMediator();
        var agencyService = new MockAgencyCapacityService();
        var slaService = new MockSlaPenaltyService();
        var unitOfWork = new MockUnitOfWork();
        var rules = MicrosoftOptions.Create(new BusinessRules());
        repo.Uow = unitOfWork;
        mediator.Uow = unitOfWork;
        slaService.Uow = unitOfWork;

        var service = new MilestoneFailoverService(
            repo,
            agencyService,
            slaService,
            clock,
            mediator,
            unitOfWork,
            rules,
            NullLogger<MilestoneFailoverService>.Instance
        );

        return (service, repo, clock, mediator, agencyService, slaService, unitOfWork);
    }

    [Fact]
    public async Task ReportUnavailability_MoreThan2HoursNotice_Opens30MinSelfSwapWindow()
    {
        var (service, repo, clock, mediator, agencyService, slaService, unitOfWork) = CreateTestRig();

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
        var (service, repo, clock, mediator, agencyService, slaService, unitOfWork) = CreateTestRig();

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
        var (service, repo, clock, mediator, agencyService, slaService, unitOfWork) = CreateTestRig();

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
            ShiftCode = "SHIFT_MORNING",
            SlotStatus = "LOCKED"
        };

        // Another Agency (Agency 7) has capacity - add the reserved slot with a worker
        repo.Slots[701] = new BookingSlot
        {
            SlotId = 701,
            WorkerId = 70,
            SlotDate = DateOnly.FromDateTime(clock.UtcNow),
            StartTime = new TimeOnly(7, 30),
            ShiftCode = "SHIFT_MORNING",
            SlotStatus = "AVAILABLE"
        };

        agencyService.NextReservation = new CapacityReservation(Guid.NewGuid(), 7, [701], 0.200m);

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
        var (service, repo, clock, mediator, agencyService, slaService, unitOfWork) = CreateTestRig();

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
            ShiftCode = "SHIFT_MORNING",
            SlotStatus = "LOCKED"
        };

        // Super-Freelancer available (Rating >= 4.80, CompletedJobs >= 50)
        // Add an available slot for the super freelancer on the same date/shift
        repo.Slots[401] = new BookingSlot
        {
            SlotId = 401,
            WorkerId = 99,
            SlotDate = DateOnly.FromDateTime(clock.UtcNow),
            StartTime = new TimeOnly(7, 0),
            ShiftCode = "SHIFT_MORNING",
            SlotStatus = "AVAILABLE"
        };

        repo.SuperFreelancers.Add(new Worker
        {
            WorkerId = 99,
            FullName = "Nguyễn Siêu Thợ",
            IsSuperFreelancer = true,
            RatingAvg = 4.95m,
            CompletedJobs = 80
        });

        // No other agency has capacity
        agencyService.NextReservation = null;

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
        var (service, repo, clock, mediator, agencyService, slaService, unitOfWork) = CreateTestRig();

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
            ShiftCode = "SHIFT_MORNING",
            SlotStatus = "LOCKED"
        };

        // Add the rescue slot for Agency 8
        repo.Slots[801] = new BookingSlot
        {
            SlotId = 801,
            WorkerId = 80,
            SlotDate = DateOnly.FromDateTime(clock.UtcNow),
            StartTime = new TimeOnly(10, 0),
            ShiftCode = "SHIFT_MORNING",
            SlotStatus = "AVAILABLE"
        };

        await service.ReportUnavailabilityAsync(5, "Agency báo tìm thợ");

        // Another Agency 8 ready to rescue
        agencyService.NextReservation = new CapacityReservation(Guid.NewGuid(), 8, [801], 0.200m);

        // Advance 31 minutes
        clock.UtcNow = clock.UtcNow.AddMinutes(31);

        var result = await service.HandleSelfSwapTimeoutAsync(5);

        Assert.True(result.Success);
        Assert.Equal(FailoverStatus.EmergencyRescuedToAgency, result.Status);
        Assert.Equal(8, result.NewAgencyId);
        Assert.Single(slaService.AppliedRequests);
    }

    // ---- RESCUE-TEST-01: the named tests for the criteria of #443 (production code unchanged) ---------------------------------

    private const string Morning = "SHIFT_MORNING";

    private static JobAssignment SeedAssigned(
        InMemoryMilestoneFailoverRepository repo, MockClock clock, long id = 3, long orderId = 30, int slotId = 300, int agencyId = 5, string shift = Morning)
    {
        var assignment = new JobAssignment
        {
            AssignmentId = id,
            OrderId = orderId,
            CustomerId = 1,
            AgencyId = agencyId,
            WorkerId = 50,
            SlotId = slotId,
            ServiceTier = ServiceTier.Premium,
            AssignmentSeq = 1,
            GrossAmount = 400000m,
            PayoutAmount = 320000m
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        repo.Assignments[id] = assignment;
        repo.Slots[slotId] = new BookingSlot
        {
            SlotId = slotId,
            WorkerId = 50,
            SlotDate = DateOnly.FromDateTime(clock.UtcNow),
            StartTime = new TimeOnly(7, 30),
            ShiftCode = shift,
            SlotStatus = "LOCKED"
        };
        return assignment;
    }

    private static BookingSlot AddSlot(InMemoryMilestoneFailoverRepository repo, MockClock clock, int slotId, int workerId, string status = "AVAILABLE", string shift = Morning) =>
        repo.Slots[slotId] = new BookingSlot
        {
            SlotId = slotId,
            WorkerId = workerId,
            SlotDate = DateOnly.FromDateTime(clock.UtcNow),
            StartTime = new TimeOnly(7, 30),
            ShiftCode = shift,
            SlotStatus = status
        };

    private static Worker AddSuperFreelancer(InMemoryMilestoneFailoverRepository repo, int workerId = 99)
    {
        var worker = new Worker { WorkerId = workerId, FullName = "Super", IsSuperFreelancer = true, RatingAvg = 4.95m, CompletedJobs = 80 };
        repo.SuperFreelancers.Add(worker);
        return worker;
    }

    // 3a ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task EmergencyRescue_ToAnotherAgency_CreatesTheNewAssignmentWithTheWorkerOfTheReservedSlot()
    {
        var (service, repo, clock, mediator, agencyService, _, _) = CreateTestRig();
        var original = SeedAssigned(repo, clock);
        AddSlot(repo, clock, 701, workerId: 70);
        agencyService.NextReservation = new CapacityReservation(Guid.NewGuid(), 7, [701], 0.200m);

        var result = await service.TriggerEmergencyRescueAsync(original, null, repo.Slots[300], "no-show");

        Assert.Equal(FailoverStatus.EmergencyRescuedToAgency, result.Status);
        var created = repo.Assignments[result.NewAssignmentId!.Value];
        Assert.Equal((70, 7, 701), (created.WorkerId, created.AgencyId, created.SlotId));
        Assert.DoesNotContain(mediator.PublishedEvents, e => e is AssignmentFailed);
    }

    [Fact]
    public async Task EmergencyRescue_WhenTheReservedSlotHasNoWorker_FailsTheStepNotTheProcess_AndContinuesToSuperFreelancer()
    {
        var (service, repo, clock, _, agencyService, _, _) = CreateTestRig();
        var original = SeedAssigned(repo, clock);
        AddSlot(repo, clock, 701, workerId: 0); // reserved slot of the other Agency has no worker
        var reservationId = Guid.NewGuid();
        agencyService.NextReservation = new CapacityReservation(reservationId, 7, [701], 0.200m);
        AddSuperFreelancer(repo);
        AddSlot(repo, clock, 401, workerId: 99);

        var result = await service.TriggerEmergencyRescueAsync(original, null, repo.Slots[300], "no-show");

        Assert.Equal(FailoverStatus.EmergencyRescuedToSuperFreelancer, result.Status);
        Assert.Contains(reservationId, agencyService.Released);
        Assert.DoesNotContain(repo.Assignments.Values, a => a.WorkerId == 0);
    }

    // 3b ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task EmergencyRescue_SuperFreelancerAssignment_UsesTheirOwnAvailableSlot_NotTheSlotOfTheOldAgencyWorker()
    {
        var (service, repo, clock, _, _, _, _) = CreateTestRig();
        var original = SeedAssigned(repo, clock);
        AddSuperFreelancer(repo);
        AddSlot(repo, clock, 401, workerId: 99);

        var result = await service.TriggerEmergencyRescueAsync(original, null, repo.Slots[300], "no-show");

        var created = repo.Assignments[result.NewAssignmentId!.Value];
        Assert.Equal((99, 401), (created.WorkerId, created.SlotId));
        Assert.NotEqual(original.SlotId, created.SlotId);
        Assert.Null(created.AgencyId);
        Assert.Equal("LOCKED", repo.Slots[401].SlotStatus);
        Assert.Equal("AVAILABLE", repo.Slots[300].SlotStatus); // the old worker's slot is released
    }

    [Fact]
    public async Task EmergencyRescue_SuperFreelancerWithoutAnAvailableSlot_IsNotEligible_AndTheRescueFails()
    {
        var (service, repo, clock, mediator, _, slaService, _) = CreateTestRig();
        var original = SeedAssigned(repo, clock);
        AddSuperFreelancer(repo);
        AddSlot(repo, clock, 401, workerId: 99, status: "BOOKED"); // not AVAILABLE

        var result = await service.TriggerEmergencyRescueAsync(original, null, repo.Slots[300], "no-show");

        Assert.Equal(FailoverStatus.RescueFailed, result.Status);
        Assert.DoesNotContain(repo.Assignments.Values, a => a.WorkerId == 99);
        Assert.Equal(SlaViolation.NoShow, Assert.Single(slaService.AppliedRequests).Violation);
        Assert.Single(mediator.PublishedEvents.OfType<AssignmentFailed>());
    }

    // 3c ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task EmergencyRescue_TakesTheShiftCodeFromTheSlot_ThenFromTheOrder_NeverAnInventedValue()
    {
        var (service, repo, clock, _, agencyService, _, _) = CreateTestRig();
        var original = SeedAssigned(repo, clock, shift: "SHIFT_AFTERNOON");
        var order = new JobOrder { OrderId = 30, ShiftCode = "SHIFT_EVENING", ScheduledDate = DateOnly.FromDateTime(clock.UtcNow) };
        agencyService.NextReservation = null;

        await service.TriggerEmergencyRescueAsync(original, order, repo.Slots[300], "no-show");
        await service.TriggerEmergencyRescueAsync(original, order, null, "no-show");

        Assert.Equal(["SHIFT_AFTERNOON", "SHIFT_EVENING"], agencyService.Requests.Select(r => r.ShiftCode).ToArray());
        Assert.DoesNotContain(agencyService.Requests, r => r.ShiftCode == "SANG");
    }

    [Fact(Skip = "Production defect in MilestoneFailoverService.cs:350-353 (criterion 3c of #443); fix needs Scope exception #455. Remove Skip with that fix.")]
    public async Task EmergencyRescue_WithoutAShiftCodeOnSlotOrOrder_DoesNotReserveCapacityForAnInventedShift()
    {
        var (service, repo, clock, mediator, agencyService, _, _) = CreateTestRig();
        var original = SeedAssigned(repo, clock);
        AddSlot(repo, clock, 701, workerId: 70);
        agencyService.NextReservation = new CapacityReservation(Guid.NewGuid(), 7, [701], 0.200m);

        var result = await service.TriggerEmergencyRescueAsync(original, null, null, "no-show");

        // Criterion 3c of #443: "if neither slot nor order has the code, the rescue fails with a logged reason".
        Assert.NotEqual(FailoverStatus.EmergencyRescuedToAgency, result.Status);
        Assert.DoesNotContain(agencyService.Requests, r => string.IsNullOrEmpty(r.ShiftCode));
        _ = mediator;
    }

    // 3d ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task EmergencyRescue_ReservationOfTheSameAgency_IsReleased_AndTheRescueContinuesToSuperFreelancer()
    {
        var (service, repo, clock, _, agencyService, _, _) = CreateTestRig();
        var original = SeedAssigned(repo, clock, agencyId: 5);
        AddSlot(repo, clock, 501, workerId: 51);
        var reservationId = Guid.NewGuid();
        agencyService.NextReservation = new CapacityReservation(reservationId, 5, [501], 0.200m); // the SAME agency
        AddSuperFreelancer(repo);
        AddSlot(repo, clock, 401, workerId: 99);

        var result = await service.TriggerEmergencyRescueAsync(original, null, repo.Slots[300], "no-show");

        Assert.Equal(FailoverStatus.EmergencyRescuedToSuperFreelancer, result.Status);
        Assert.Equal([reservationId], agencyService.Released);
        Assert.DoesNotContain(repo.Assignments.Values, a => a.AgencyId == 5 && a.AssignmentId != original.AssignmentId);
    }

    // 3e ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task EmergencyRescue_CommissionRate_AgencyBranchUsesTheReservation_SuperFreelancerBranchUsesBusinessRules()
    {
        var (service, repo, clock, _, agencyService, _, _) = CreateTestRig();
        var toAgency = SeedAssigned(repo, clock, id: 3, orderId: 30, slotId: 300);
        AddSlot(repo, clock, 701, workerId: 70);
        agencyService.NextReservation = new CapacityReservation(Guid.NewGuid(), 7, [701], 0.150m); // not the 0.20 literal
        var agencyResult = await service.TriggerEmergencyRescueAsync(toAgency, null, repo.Slots[300], "no-show");

        var toFreelancer = SeedAssigned(repo, clock, id: 4, orderId: 40, slotId: 400);
        agencyService.NextReservation = null;
        AddSuperFreelancer(repo);
        AddSlot(repo, clock, 401, workerId: 99);
        var freelancerResult = await service.TriggerEmergencyRescueAsync(toFreelancer, null, repo.Slots[400], "no-show");

        Assert.Equal(0.150m, repo.Assignments[agencyResult.NewAssignmentId!.Value].CommissionRate);
        Assert.Equal(new BusinessRules().Commission.Freelancer, repo.Assignments[freelancerResult.NewAssignmentId!.Value].CommissionRate);
    }

    // 3f ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task EmergencyRescue_RunsEveryDatabaseStepInOneUnitOfWork_AndPublishesAndPenalisesAfterItReturns()
    {
        var (service, repo, clock, mediator, agencyService, slaService, unitOfWork) = CreateTestRig();
        var original = SeedAssigned(repo, clock);
        AddSlot(repo, clock, 701, workerId: 70);
        agencyService.NextReservation = new CapacityReservation(Guid.NewGuid(), 7, [701], 0.200m);

        await service.TriggerEmergencyRescueAsync(original, null, repo.Slots[300], "no-show");

        Assert.Equal(1, unitOfWork.Executions);
        Assert.Equal(["UpdateAssignmentStatus", "ReleaseSlot", "CreateAssignment", "LockSlot"], repo.WriteLog.Select(w => w.Call).ToArray());
        Assert.All(repo.WriteLog, w => Assert.True(w.InTransaction, w.Call));
        Assert.Equal([false], mediator.PublishedInTransaction); // JobAssigned
        Assert.Equal([false], slaService.AppliedInTransaction);  // penalty
    }

    // 3g ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task EmergencyRescue_MovesTheOriginalOnlyThroughTheStateMachineEdgeAssignedToReassigned()
    {
        var (service, repo, clock, _, agencyService, _, _) = CreateTestRig();
        var original = SeedAssigned(repo, clock);
        AddSlot(repo, clock, 701, workerId: 70);
        agencyService.NextReservation = new CapacityReservation(Guid.NewGuid(), 7, [701], 0.200m);

        await service.TriggerEmergencyRescueAsync(original, null, repo.Slots[300], "no-show");

        Assert.True(JobAssignmentStateMachine.Instance.CanTransition(JobAssignmentStatus.Assigned, JobAssignmentStatus.Reassigned));
        Assert.Equal([(JobAssignmentStatus.Assigned, JobAssignmentStatus.Reassigned)], repo.StatusUpdates);
        Assert.Equal(JobAssignmentStatus.Reassigned, original.AssignmentStatus);
    }

    // idempotency (criterion 4) ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Handler_SameEventTwice_ReassignsOnce_AndPenalisesOnce()
    {
        var (service, repo, clock, mediator, agencyService, slaService, _) = CreateTestRig();
        SeedAssigned(repo, clock);
        AddSlot(repo, clock, 701, workerId: 70);
        agencyService.NextReservation = new CapacityReservation(Guid.NewGuid(), 7, [701], 0.200m);
        var handler = new AgencySwapWindowExpiredHandler(service, repo);
        var evt = new AgencySwapWindowExpired(3, 30, 5, "worker retired by agency", clock.UtcNow);

        await handler.Handle(evt, CancellationToken.None);
        await handler.Handle(evt, CancellationToken.None);

        Assert.Single(repo.StatusUpdates);
        Assert.Single(slaService.AppliedRequests);
        Assert.Single(mediator.PublishedEvents.OfType<JobAssigned>());
        Assert.Equal(2, repo.Assignments.Count); // the original and one replacement
        Assert.Equal(1, agencyService.TryReserveCalls);
    }

    [Theory]
    [InlineData(JobAssignmentStatus.Reassigned)]
    [InlineData(JobAssignmentStatus.CheckedIn)]
    [InlineData(JobAssignmentStatus.Cancelled)]
    public async Task Handler_OriginalNoLongerAssigned_DoesNothing(JobAssignmentStatus status)
    {
        var (service, repo, clock, mediator, agencyService, slaService, _) = CreateTestRig();
        var original = SeedAssigned(repo, clock);
        typeof(JobAssignment).GetProperty(nameof(JobAssignment.AssignmentStatus))!.SetValue(original, status);
        var handler = new AgencySwapWindowExpiredHandler(service, repo);

        await handler.Handle(new AgencySwapWindowExpired(3, 30, 5, "worker retired by agency", clock.UtcNow), CancellationToken.None);

        Assert.Equal(0, agencyService.TryReserveCalls);
        Assert.Empty(slaService.AppliedRequests);
        Assert.Empty(mediator.PublishedEvents);
        Assert.Empty(repo.WriteLog);
    }

    // refund path (criterion 5) --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task EmergencyRescue_WithNobodyToTakeIt_PublishesAssignmentFailedOnce_WithTheOrder_AndPenalisesNoShowWithAFullRefund()
    {
        var (service, repo, clock, mediator, agencyService, slaService, _) = CreateTestRig();
        var original = SeedAssigned(repo, clock);
        agencyService.NextReservation = null;

        var result = await service.TriggerEmergencyRescueAsync(original, null, repo.Slots[300], "no-show");

        Assert.Equal(FailoverStatus.RescueFailed, result.Status);
        var failed = Assert.Single(mediator.PublishedEvents.OfType<AssignmentFailed>());
        Assert.Equal(30, failed.OrderId);
        Assert.DoesNotContain(mediator.PublishedEvents, e => e is JobAssigned);
        var penalty = Assert.Single(slaService.AppliedRequests);
        Assert.Equal((SlaViolation.NoShow, 400000m, 0m), (penalty.Violation, penalty.CustomerRefundAmount, penalty.RescueCost));
        Assert.Empty(repo.WriteLog); // nothing was reassigned
    }

    // event input (criterion 10) ---------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0L)]
    [InlineData(999L)]
    public async Task Handler_TakesTheOrderFromTheAssignment_NotFromTheOrderIdOfTheEvent(long orderIdOfTheEvent)
    {
        // Current behaviour, recorded: retire sent OrderId = 0 until AGENCY-FIX-01; the rescue still uses the assignment's order.
        var (service, repo, clock, mediator, agencyService, _, _) = CreateTestRig();
        SeedAssigned(repo, clock, orderId: 30);
        AddSlot(repo, clock, 701, workerId: 70);
        agencyService.NextReservation = new CapacityReservation(Guid.NewGuid(), 7, [701], 0.200m);
        var handler = new AgencySwapWindowExpiredHandler(service, repo);

        await handler.Handle(new AgencySwapWindowExpired(3, orderIdOfTheEvent, 5, "worker retired by agency", clock.UtcNow), CancellationToken.None);

        var assigned = Assert.Single(mediator.PublishedEvents.OfType<JobAssigned>());
        Assert.Equal(30, assigned.OrderId);
        Assert.Equal(30, agencyService.Requests.Single().OrderId);
    }
}
