using System.Reflection;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Workers;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using MediatR;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>
/// Decision D35 (LARGE-1W-01, #900): the 2nd job of a large ECONOMY house. No database: the repositories are stubs.
/// Owner rules: the worker has 15 minutes after the shift end to submit the completion, else the 2nd job opens by itself; the first
/// "not clean" of the customer opens it (instead of a redo); finishing in the first job releases the held next-shift slot.
/// </summary>
public class ContinuationTests
{
    private const int CustomerId = 5;
    private const int WorkerId = 7;
    private const long OrderId = 900;
    private const long AssignmentId = 55;
    private static readonly DateOnly Day = new(2026, 11, 20);

    private class StubContinuations : IContinuationRepository
    {
        public ContinuationState? State { get; set; } = new(true, null, Day, BookingShifts.Morning);
        public bool OpenResult { get; set; } = true;
        public List<long> Opened { get; } = [];
        public List<DueContinuation> Candidates { get; } = [];

        public Task<ContinuationState?> GetStateAsync(long orderId, CancellationToken cancellationToken = default) => Task.FromResult(State);

        public Task<bool> TryOpenAsync(long orderId, DateTime atUtc, CancellationToken cancellationToken = default)
        {
            if (OpenResult)
            {
                Opened.Add(orderId);
            }

            return Task.FromResult(OpenResult);
        }

        public Task<IReadOnlyList<DueContinuation>> GetOpenCandidatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DueContinuation>>(Candidates);
    }

    /// <summary>The bits of <see cref="IWorkerRepository"/> the service touches; every other member fails loudly.</summary>
    private class RepositoryProxy : DispatchProxy
    {
        public JobAssignment? Assignment { get; set; }
        public bool CompleteResult { get; set; } = true;
        public int Saves { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case "GetAssignmentByIdAsync":
                    return Task.FromResult(Assignment);
                case "SaveChangesAsync":
                    Saves++;
                    return Task.CompletedTask;
                case "TryCompleteAssignmentAsync":
                    return Task.FromResult(CompleteResult);
                default:
                    throw new NotSupportedException(targetMethod.Name);
            }
        }
    }

    private class Publisher : IPublisher
    {
        public List<object> Published { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            Published.Add(notification);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            Published.Add(notification!);
            return Task.CompletedTask;
        }
    }

    private class Caller(UserRole role, int id) : ICurrentUser
    {
        public int? UserId { get; } = id;
        public UserRole? Role { get; } = role;
        public bool IsAuthenticated => true;
    }

    private static JobAssignment AwaitingAcceptance(DateTime submittedAt)
    {
        var assignment = new JobAssignment
        {
            AssignmentId = AssignmentId,
            OrderId = OrderId,
            CustomerId = CustomerId,
            WorkerId = WorkerId,
            ServiceTier = ServiceTier.Economy,
            GrossAmount = 520000m,
            CommissionRate = 0.20m,
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        assignment.TransitionTo(JobAssignmentStatus.CheckedIn);
        assignment.TransitionTo(JobAssignmentStatus.InProgress);
        assignment.TransitionTo(JobAssignmentStatus.AwaitingAcceptance);
        assignment.SubmittedAt = submittedAt;
        return assignment;
    }

    private static (JobCompletionService Service, RepositoryProxy Repo, StubContinuations Continuations, FakeClock Clock) Customer(JobAssignment assignment)
    {
        var repo = DispatchProxy.Create<IWorkerRepository, RepositoryProxy>();
        ((RepositoryProxy)(object)repo).Assignment = assignment;
        var clock = new FakeClock();
        clock.Set(new DateTime(2026, 11, 20, 5, 10, 0, DateTimeKind.Utc));
        var continuations = new StubContinuations();
        var service = new JobCompletionService(repo, new Publisher(), new Caller(UserRole.Customer, CustomerId), clock, null, continuations);
        return (service, (RepositoryProxy)(object)repo, continuations, clock);
    }

    [Fact]
    public async Task TheFirstNotClean_OpensTheSecondJob_InsteadOfARedo()
    {
        var clock = new DateTime(2026, 11, 20, 5, 10, 0, DateTimeKind.Utc);
        var assignment = AwaitingAcceptance(clock.AddMinutes(-5));
        var (service, repo, continuations, _) = Customer(assignment);

        var result = await service.RequestRedoAsync(AssignmentId, new RequestRedoRequest("Dust under the sofa", null));

        Assert.Equal("IN_PROGRESS", result.Data!.Status);
        Assert.Equal(JobAssignmentStatus.InProgress, assignment.AssignmentStatus);
        Assert.Null(assignment.SubmittedAt);
        Assert.Equal(0, assignment.RedoCount); // not a redo
        Assert.Equal([OrderId], continuations.Opened);
        Assert.Equal(1, repo.Saves);
    }

    [Fact]
    public async Task TheSecondJobOpensOnce_ThenTheOldRedoApplies()
    {
        var now = new DateTime(2026, 11, 20, 5, 10, 0, DateTimeKind.Utc);
        var assignment = AwaitingAcceptance(now.AddMinutes(-5));
        var (service, _, continuations, _) = Customer(assignment);
        continuations.OpenResult = false; // already opened (or not a large house)

        var result = await service.RequestRedoAsync(AssignmentId, new RequestRedoRequest("Still dusty", null));

        Assert.Equal("IN_PROGRESS", result.Data!.Status);
        Assert.Equal(1, assignment.RedoCount); // the D32 redo
        Assert.Empty(continuations.Opened);
    }

    [Fact]
    public async Task FinishingInTheFirstJob_PaysTheWorkerTheWholeLargeHousePrice()
    {
        var now = new DateTime(2026, 11, 20, 5, 10, 0, DateTimeKind.Utc);
        var assignment = AwaitingAcceptance(now.AddMinutes(-5));
        var (service, _, continuations, _) = Customer(assignment);

        var result = await service.AcceptAsync(AssignmentId, null);

        Assert.Equal("COMPLETED", result.Data!.Status);
        Assert.Equal(520000m, result.Data.GrossAmount); // the worker is paid the whole large-house price
        Assert.Equal(416000m, result.Data.PayoutAmount); // 80 % of 520000
        Assert.Empty(continuations.Opened);
    }

    [Fact]
    public async Task ARedoOfAnOrdinaryOrder_StillWorks_WhenTheOrderIsNotContinuous()
    {
        var now = new DateTime(2026, 11, 20, 5, 10, 0, DateTimeKind.Utc);
        var assignment = AwaitingAcceptance(now.AddMinutes(-5));
        var (service, _, continuations, _) = Customer(assignment);
        continuations.OpenResult = false;

        await Assert.ThrowsAsync<ValidationException>(() => service.RequestRedoAsync(AssignmentId, new RequestRedoRequest(" ", null)));
        Assert.Empty(continuations.Opened);
    }

    // ---------------------------------------------------------------- the 15-minute sweep

    private static (ContinuationService Service, StubContinuations Continuations, FakeClock Clock) Sweep(DateTime nowUtc)
    {
        var clock = new FakeClock();
        clock.Set(nowUtc);
        var continuations = new StubContinuations();
        continuations.Candidates.Add(new DueContinuation(OrderId, Day, BookingShifts.Morning)); // shift ends 12:00 local = 05:00 UTC
        return (new ContinuationService(continuations, clock), continuations, clock);
    }

    [Fact]
    public async Task TheSweep_DoesNothing_UntilFifteenMinutesAfterTheShiftEnd()
    {
        var (service, continuations, _) = Sweep(new DateTime(2026, 11, 20, 5, 14, 59, DateTimeKind.Utc));

        Assert.Equal(0, await service.OpenDueAsync());
        Assert.Empty(continuations.Opened);
    }

    [Fact]
    public async Task TheSweep_OpensTheSecondJob_FifteenMinutesAfterTheShiftEnd()
    {
        var (service, continuations, _) = Sweep(new DateTime(2026, 11, 20, 5, 15, 0, DateTimeKind.Utc));

        Assert.Equal(1, await service.OpenDueAsync());
        Assert.Equal([OrderId], continuations.Opened);
    }

    [Fact]
    public async Task TheSweep_CountsOnlyWhatItOpened()
    {
        var (service, continuations, _) = Sweep(new DateTime(2026, 11, 20, 6, 0, 0, DateTimeKind.Utc));
        continuations.OpenResult = false; // another writer opened it first

        Assert.Equal(0, await service.OpenDueAsync());
    }

    [Fact]
    public async Task TheSweep_SkipsAnUnknownShift()
    {
        var (service, continuations, _) = Sweep(new DateTime(2026, 11, 20, 23, 0, 0, DateTimeKind.Utc));
        continuations.Candidates.Clear();
        continuations.Candidates.Add(new DueContinuation(OrderId, Day, "SHIFT_NOPE"));

        Assert.Equal(0, await service.OpenDueAsync());
    }

    // ---------------------------------------------------------------- the wire shape

    [Fact]
    public void TheWire_ShowsStandbyOpenAndReleased_AndNothingForAnOrdinaryOrder()
    {
        var clock = new FakeClock();

        Assert.Null(ContinuationDto.Describe(false, Day, BookingShifts.Morning, null, false, 4, clock));

        var standby = ContinuationDto.Describe(true, Day, BookingShifts.Morning, null, false, 4, clock)!;
        Assert.Equal(ContinuationDto.Standby, standby.Status);
        Assert.Equal(BookingShifts.Morning, standby.ShiftCode);
        Assert.Equal(new DateTime(2026, 11, 20, 5, 0, 0, DateTimeKind.Utc), standby.WindowStartAt); // 12:00 local, the shift end
        Assert.Equal(new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc), standby.WindowEndAt); // 4 hours later

        var opened = ContinuationDto.Describe(true, Day, BookingShifts.Morning, new DateTime(2026, 11, 20, 5, 20, 0, DateTimeKind.Utc), false, 4, clock)!;
        Assert.Equal(ContinuationDto.Open, opened.Status);

        var released = ContinuationDto.Describe(true, Day, BookingShifts.Morning, null, true, 4, clock)!;
        Assert.Equal(ContinuationDto.Released, released.Status);
    }

    [Fact]
    public void TheWindowFollowsTheConfiguredShiftMaxHours()
    {
        var wire = ContinuationDto.Describe(true, Day, BookingShifts.Evening, null, false, 3, new FakeClock())!;

        Assert.Equal(new DateTime(2026, 11, 20, 13, 30, 0, DateTimeKind.Utc), wire.WindowStartAt); // 20:30 local
        Assert.Equal(new DateTime(2026, 11, 20, 16, 30, 0, DateTimeKind.Utc), wire.WindowEndAt);
    }
}
