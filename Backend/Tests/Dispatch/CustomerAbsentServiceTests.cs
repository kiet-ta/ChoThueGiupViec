using CommonService.Application.Common.Options;
using CommonService.Application.Features.Dispatch;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using MediatR;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace CommonService.Tests.Dispatch;

public class CustomerAbsentServiceTests
{
    private sealed class InMemoryCustomerAbsentRepository : ICustomerAbsentRepository
    {
        private readonly Dictionary<long, JobAssignment> _assignments = new();
        private readonly Dictionary<long, CheckInLog> _logs = new();

        public void AddAssignment(JobAssignment assignment) => _assignments[assignment.AssignmentId] = assignment;
        public void AddCheckInLog(CheckInLog log) => _logs[log.AssignmentId] = log;

        public Task<JobAssignment?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default)
        {
            _assignments.TryGetValue(assignmentId, out var a);
            return Task.FromResult(a);
        }

        public Task<CheckInLog?> GetCheckInLogAsync(long assignmentId, CancellationToken cancellationToken = default)
        {
            _logs.TryGetValue(assignmentId, out var l);
            return Task.FromResult(l);
        }

        public Task<CheckInLog> SaveCheckInLogAsync(CheckInLog log, CancellationToken cancellationToken = default)
        {
            _logs[log.AssignmentId] = log;
            return Task.FromResult(log);
        }

        public Task<bool> UpdateAssignmentAbsentFeeAsync(long assignmentId, decimal absenceFeeAmount, CancellationToken cancellationToken = default)
        {
            if (_assignments.TryGetValue(assignmentId, out var assignment))
            {
                assignment.AbsenceFeeAmount = absenceFeeAmount;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> RecordCallAttemptAsync(long assignmentId, CancellationToken cancellationToken = default)
        {
            if (_logs.TryGetValue(assignmentId, out var log))
            {
                log.CallAttempts++;
                return Task.FromResult(true);
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
    public async Task ReportCustomerAbsent_fails_if_wait_time_less_than_15_minutes()
    {
        var checkedInTime = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock();
        clock.Set(checkedInTime.AddMinutes(10)); // Only 10 mins elapsed (< 15m)

        var repo = new InMemoryCustomerAbsentRepository();
        var assignment = new JobAssignment
        {
            AssignmentId = 201,
            OrderId = 11,
            CustomerId = 1,
            WorkerId = 5,
            GrossAmount = 260000m
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        assignment.TransitionTo(JobAssignmentStatus.CheckedIn);
        repo.AddAssignment(assignment);

        repo.AddCheckInLog(new CheckInLog
        {
            AssignmentId = 201,
            CheckedInAt = checkedInTime,
            CallAttempts = 2,
            GpsVerified = true
        });

        var mediator = new TestMediator();
        var service = new CustomerAbsentService(repo, clock, mediator, MicrosoftOptions.Create(new BusinessRules()));

        var result = await service.ReportCustomerAbsentAsync(201, 5);

        Assert.False(result.Success);
        Assert.Contains("Chưa đủ thời gian chờ tối thiểu 15 phút", result.ErrorMessage);
        Assert.Empty(mediator.PublishedEvents);
    }

    [Fact]
    public async Task ReportCustomerAbsent_fails_if_call_attempts_less_than_2()
    {
        var checkedInTime = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock();
        clock.Set(checkedInTime.AddMinutes(20)); // 20 mins elapsed (>= 15m)

        var repo = new InMemoryCustomerAbsentRepository();
        var assignment = new JobAssignment
        {
            AssignmentId = 202,
            OrderId = 12,
            CustomerId = 2,
            WorkerId = 6,
            GrossAmount = 260000m
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        assignment.TransitionTo(JobAssignmentStatus.CheckedIn);
        repo.AddAssignment(assignment);

        repo.AddCheckInLog(new CheckInLog
        {
            AssignmentId = 202,
            CheckedInAt = checkedInTime,
            CallAttempts = 1, // Only 1 call (< 2 calls required)
            GpsVerified = true
        });

        var mediator = new TestMediator();
        var service = new CustomerAbsentService(repo, clock, mediator, MicrosoftOptions.Create(new BusinessRules()));

        var result = await service.ReportCustomerAbsentAsync(202, 6);

        Assert.False(result.Success);
        Assert.Contains("Cần thực hiện tối thiểu 2 cuộc gọi", result.ErrorMessage);
        Assert.Empty(mediator.PublishedEvents);
    }

    [Fact]
    public async Task ReportCustomerAbsent_succeeds_when_elapsed_15m_and_calls_gte_2()
    {
        var checkedInTime = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        var reportTime = checkedInTime.AddMinutes(18); // 18 mins elapsed (>= 15m)
        var clock = new FakeClock();
        clock.Set(reportTime);

        var repo = new InMemoryCustomerAbsentRepository();
        var assignment = new JobAssignment
        {
            AssignmentId = 203,
            OrderId = 13,
            CustomerId = 3,
            WorkerId = 7,
            GrossAmount = 260000m
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        assignment.TransitionTo(JobAssignmentStatus.CheckedIn);
        repo.AddAssignment(assignment);

        repo.AddCheckInLog(new CheckInLog
        {
            AssignmentId = 203,
            CheckedInAt = checkedInTime,
            CallAttempts = 2, // Exactly 2 calls (>= 2)
            GpsVerified = true
        });

        var mediator = new TestMediator();
        var service = new CustomerAbsentService(repo, clock, mediator, MicrosoftOptions.Create(new BusinessRules()));

        var result = await service.ReportCustomerAbsentAsync(203, 7);

        Assert.True(result.Success);
        Assert.Equal(203, result.AssignmentId);
        Assert.Equal(reportTime, result.ReportedAtUtc);
        Assert.Equal(0.40m, result.WorkerFeeRate);
        Assert.Equal(104000.00m, result.AbsenceFeeAmount); // 40% of 260,000
        Assert.Equal(156000.00m, result.CustomerRefundAmount); // 60% of 260,000

        // Verify CheckInLog customer_absent_at was set
        var log = await repo.GetCheckInLogAsync(203);
        Assert.Equal(reportTime, log!.CustomerAbsentAt);

        // Verify CustomerAbsentReported event published
        Assert.Single(mediator.PublishedEvents);
        var evt = Assert.IsType<CustomerAbsentReported>(mediator.PublishedEvents[0]);
        Assert.Equal(203, evt.AssignmentId);
        Assert.Equal(13, evt.OrderId);
        Assert.Equal(7, evt.WorkerId);
        Assert.Equal(reportTime, evt.ReportedAtUtc);
    }

    [Fact]
    public async Task RecordCallAttempt_increments_call_count_when_checked_in()
    {
        var checkedInTime = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock();
        clock.Set(checkedInTime);

        var repo = new InMemoryCustomerAbsentRepository();
        var assignment = new JobAssignment
        {
            AssignmentId = 204,
            OrderId = 14,
            CustomerId = 4,
            WorkerId = 8,
            GrossAmount = 260000m
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        assignment.TransitionTo(JobAssignmentStatus.CheckedIn);
        repo.AddAssignment(assignment);

        repo.AddCheckInLog(new CheckInLog
        {
            AssignmentId = 204,
            CheckedInAt = checkedInTime,
            CallAttempts = 0,
            GpsVerified = true
        });

        var mediator = new TestMediator();
        var service = new CustomerAbsentService(repo, clock, mediator, MicrosoftOptions.Create(new BusinessRules()));

        var call1 = await service.RecordCallAttemptAsync(204, 8);
        var call2 = await service.RecordCallAttemptAsync(204, 8);

        Assert.True(call1);
        Assert.True(call2);

        var log = await repo.GetCheckInLogAsync(204);
        Assert.Equal(2, log!.CallAttempts);
    }
}
