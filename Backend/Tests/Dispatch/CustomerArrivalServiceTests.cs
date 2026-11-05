using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace CommonService.Tests.Dispatch;

/// <summary>CUST-ARRIVAL-01 (#657): the customer confirms the worker's arrival when the GPS is more than 100 m off (BR-04).</summary>
public class CustomerArrivalServiceTests
{
    private const int CustomerId = 11;
    private const int WorkerId = 21;
    private static readonly GeoPoint Target = new(10.762622, 106.660172);
    private static readonly GeoPoint Far = new(10.765500, 106.662000); // about 350 m away
    private static readonly GeoPoint Near = new(10.762630, 106.660180);

    private sealed class Repo : IFieldCheckInRepository
    {
        public Dictionary<long, JobAssignment> Assignments { get; } = new();
        public Dictionary<long, CheckInLog> Logs { get; } = new();
        private long _next = 1;

        public Task<JobAssignment?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Assignments.GetValueOrDefault(assignmentId));

        public Task<CheckInLog?> GetCheckInLogAsync(long assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Logs.GetValueOrDefault(assignmentId));

        public Task<CheckInLog> SaveCheckInLogAsync(CheckInLog log, CancellationToken cancellationToken = default)
        {
            if (log.CheckinId == 0) log.CheckinId = _next++;
            Logs[log.AssignmentId] = log;
            return Task.FromResult(log);
        }

        public Task<bool> TransitionToCheckedInAsync(long assignmentId, DateTime checkedInAtUtc, CancellationToken cancellationToken = default)
        {
            if (!Assignments.TryGetValue(assignmentId, out var a) || a.AssignmentStatus != JobAssignmentStatus.Assigned)
                return Task.FromResult(false);
            a.TransitionTo(JobAssignmentStatus.CheckedIn);
            return Task.FromResult(true);
        }
    }

    private sealed class User(int? id, UserRole role) : ICurrentUser
    {
        public bool IsAuthenticated => id != null;
        public int? UserId => id;
        public UserRole? Role => role;
    }

    private sealed class Fixture
    {
        public Repo Repo { get; } = new();
        public FieldCheckInService CheckIn { get; }

        public Fixture()
        {
            var clock = new FakeClock();
            clock.Set(new DateTime(2026, 11, 7, 1, 0, 0, DateTimeKind.Utc));
            CheckIn = new FieldCheckInService(Repo, new FakeGeoService(), clock, MicrosoftOptions.Create(new BusinessRules()));
            var assignment = new JobAssignment
            {
                AssignmentId = 500,
                OrderId = 50,
                CustomerId = CustomerId,
                WorkerId = WorkerId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            assignment.TransitionTo(JobAssignmentStatus.Assigned);
            Repo.Assignments[500] = assignment;
        }

        public CustomerArrivalService Service(int customerId = CustomerId) =>
            new(Repo, CheckIn, new User(customerId, UserRole.Customer));
    }

    [Fact]
    public async Task No_attempt_yet_is_not_awaiting()
    {
        var f = new Fixture();

        var status = await f.Service().GetAsync(500);

        Assert.False(status.AwaitingCustomerConfirmation);
        Assert.Equal("ASSIGNED", status.Status);
        Assert.Null(status.DistanceMeters);
    }

    [Fact]
    public async Task Failed_gps_attempt_is_awaiting_with_the_distance()
    {
        var f = new Fixture();
        await f.CheckIn.CheckInAsync(500, WorkerId, Far, Target);

        var status = await f.Service().GetAsync(500);

        Assert.True(status.AwaitingCustomerConfirmation);
        Assert.True(status.DistanceMeters > 100m);
        Assert.NotNull(status.AttemptedAt);
    }

    [Fact]
    public async Task Verified_gps_check_in_is_not_awaiting()
    {
        var f = new Fixture();
        await f.CheckIn.CheckInAsync(500, WorkerId, Near, Target);

        var status = await f.Service().GetAsync(500);

        Assert.False(status.AwaitingCustomerConfirmation);
        Assert.Equal("CHECKED_IN", status.Status);
    }

    [Fact]
    public async Task Confirm_moves_the_assignment_to_checked_in_and_stores_the_method()
    {
        var f = new Fixture();
        await f.CheckIn.CheckInAsync(500, WorkerId, Far, Target);

        var confirmed = await f.Service().ConfirmAsync(500);

        Assert.True(confirmed.IsCustomerConfirmed);
        Assert.Equal("CHECKED_IN", confirmed.Status);
        Assert.Equal(JobAssignmentStatus.CheckedIn, f.Repo.Assignments[500].AssignmentStatus);
        Assert.Equal(CustomerArrivalService.CustomerConfirmMethod, f.Repo.Logs[500].FallbackMethod);
        Assert.True(CustomerArrivalService.CustomerConfirmMethod.Length <= 20); // CHECKIN_LOG.fallback_method is varchar(20)
        Assert.False((await f.Service().GetAsync(500)).AwaitingCustomerConfirmation);
    }

    [Fact]
    public async Task Confirm_without_a_failed_attempt_is_a_conflict()
    {
        var f = new Fixture();

        var error = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => f.Service().ConfirmAsync(500));

        Assert.Equal(CustomerArrivalService.ArrivalNotPending, error.Code);
        Assert.Equal(JobAssignmentStatus.Assigned, f.Repo.Assignments[500].AssignmentStatus);
    }

    [Fact]
    public async Task Confirm_twice_is_a_conflict()
    {
        var f = new Fixture();
        await f.CheckIn.CheckInAsync(500, WorkerId, Far, Target);
        await f.Service().ConfirmAsync(500);

        var error = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => f.Service().ConfirmAsync(500));

        Assert.Equal(CustomerArrivalService.ArrivalNotPending, error.Code);
    }

    [Fact]
    public async Task Another_customer_gets_not_found_for_read_and_confirm()
    {
        var f = new Fixture();
        await f.CheckIn.CheckInAsync(500, WorkerId, Far, Target);

        await Assert.ThrowsAsync<NotFoundException>(() => f.Service(customerId: 99).GetAsync(500));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service(customerId: 99).ConfirmAsync(500));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service().GetAsync(12345));
        Assert.Equal(JobAssignmentStatus.Assigned, f.Repo.Assignments[500].AssignmentStatus);
    }

    [Fact]
    public async Task A_worker_token_is_refused()
    {
        var f = new Fixture();
        var service = new CustomerArrivalService(f.Repo, f.CheckIn, new User(CustomerId, UserRole.Worker));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.GetAsync(500));
    }
}
