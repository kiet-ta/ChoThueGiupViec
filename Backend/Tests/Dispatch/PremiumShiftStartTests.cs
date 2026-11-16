using CommonService.Application.Common.Options;
using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using MediatR;
using Xunit;

namespace CommonService.Tests.Dispatch;

/// <summary>
/// Decision D38 (PREM-START-01, #951): a Premium order whose seats are not all present at the shift start + 5 minutes is cancelled, refunded
/// and the agencies of the absent seats are penalised. No database: the repository, the penalty service, the publisher and the notices are stubs.
/// </summary>
public class PremiumShiftStartTests
{
    private static readonly DateOnly Day = new(2026, 11, 20);
    private static readonly DateTime Start = new(2026, 11, 20, 1, 0, 0, DateTimeKind.Utc); // 08:00 local, the morning shift
    private static readonly PremiumRules Rules = new();

    private static PremiumSeat Seat(
        byte seq, JobAssignmentStatus status, int? agency = 7, bool replacement = false, DateTime? created = null, DateTime? pending = null) =>
        new(seq, 100 + seq, 10 + seq, agency, status, 390000m, created ?? Start.AddHours(-20), replacement, pending);

    // ---------------------------------------------------------------- the pure rule

    [Fact]
    public void Everyone_checked_in_is_nobody_absent()
    {
        var (absent, unfilled) = PremiumShiftStartRules.Evaluate(
            [Seat(1, JobAssignmentStatus.CheckedIn), Seat(2, JobAssignmentStatus.InProgress)], 2, Start, Start.AddMinutes(30), Rules);

        Assert.Empty(absent);
        Assert.Equal(0, unfilled);
    }

    [Theory]
    [InlineData(4, 59, false)]
    [InlineData(5, 0, true)]
    [InlineData(40, 0, true)]
    public void A_seat_that_is_not_checked_in_is_absent_from_T_plus_5(int minutes, int seconds, bool isAbsent)
    {
        var now = Start.AddMinutes(minutes).AddSeconds(seconds);

        var (absent, _) = PremiumShiftStartRules.Evaluate(
            [Seat(1, JobAssignmentStatus.CheckedIn), Seat(2, JobAssignmentStatus.Assigned)], 2, Start, now, Rules);

        Assert.Equal(isAbsent, absent.Count == 1 && absent[0].Seq == 2);
    }

    [Fact]
    public void A_worker_waiting_for_the_customers_confirmation_is_present_for_ten_minutes_after_the_attempt()
    {
        var attempt = Start.AddMinutes(3);
        var seats = new[] { Seat(1, JobAssignmentStatus.CheckedIn), Seat(2, JobAssignmentStatus.Assigned, pending: attempt) };

        Assert.Empty(PremiumShiftStartRules.Evaluate(seats, 2, Start, attempt.AddMinutes(9), Rules).Absent);
        Assert.Single(PremiumShiftStartRules.Evaluate(seats, 2, Start, attempt.AddMinutes(10), Rules).Absent);
    }

    [Fact]
    public void A_replacement_seat_has_thirty_minutes_from_its_assignment_but_never_less_than_the_usual_grace()
    {
        var assigned = Start.AddMinutes(-10);
        var late = new[] { Seat(1, JobAssignmentStatus.Assigned, replacement: true, created: assigned) };

        Assert.Equal(Start.AddMinutes(20), PremiumShiftStartRules.Deadline(late[0], Start, Rules));
        Assert.Empty(PremiumShiftStartRules.Evaluate(late, 1, Start, Start.AddMinutes(19), Rules).Absent);
        Assert.Single(PremiumShiftStartRules.Evaluate(late, 1, Start, Start.AddMinutes(20), Rules).Absent);

        var early = Seat(1, JobAssignmentStatus.Assigned, replacement: true, created: Start.AddHours(-3));
        Assert.Equal(Start.AddMinutes(5), PremiumShiftStartRules.Deadline(early, Start, Rules));
    }

    [Fact]
    public void A_seat_without_a_live_assignment_counts_after_the_grace()
    {
        Assert.Equal(0, PremiumShiftStartRules.Evaluate([Seat(1, JobAssignmentStatus.CheckedIn)], 2, Start, Start.AddMinutes(4), Rules).Unfilled);
        Assert.Equal(1, PremiumShiftStartRules.Evaluate([Seat(1, JobAssignmentStatus.CheckedIn)], 2, Start, Start.AddMinutes(5), Rules).Unfilled);
    }

    [Fact]
    public void The_new_violation_costs_thirty_points_and_the_other_ones_keep_theirs()
    {
        var points = new SlaPenaltyRules();

        Assert.Equal(30m, EscrowRules.Points(SlaViolation.NoShowAtStart, points));
        Assert.Equal(20m, EscrowRules.Points(SlaViolation.NoShow, points));
        Assert.Equal(10m, EscrowRules.Points(SlaViolation.Shortage, points));
        Assert.Equal(5m, EscrowRules.Points(SlaViolation.QualityComplaint, points));
    }

    // ---------------------------------------------------------------- the service

    private class StubRepository : IPremiumStartRepository
    {
        public List<PremiumOrderSnapshot> Orders { get; } = [];
        public JobOrderStatus StatusAfterCancel { get; set; } = JobOrderStatus.Cancelled;
        public HashSet<long> Cancelled { get; } = [];

        public Task<IReadOnlyList<PremiumOrderSnapshot>> GetLivePremiumOrdersAsync(DateOnly latestDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PremiumOrderSnapshot>>(Orders.Where(o => !Cancelled.Contains(o.OrderId) && o.ScheduledDate <= latestDate).ToList());

        public List<long> RescueCancels { get; } = [];

        public Task<PremiumOrderInfo?> GetOrderInfoAsync(long orderId, CancellationToken cancellationToken = default)
        {
            var order = Orders.FirstOrDefault(o => o.OrderId == orderId);
            return Task.FromResult<PremiumOrderInfo?>(order is null ? null : new PremiumOrderInfo(
                order.CustomerId, order.TotalAmount, Cancelled.Contains(orderId) ? JobOrderStatus.Cancelled : JobOrderStatus.Assigned));
        }

        public Task<bool> TryCancelRescueAsync(long assignmentId, int workerId, DateTime atUtc, CancellationToken cancellationToken = default)
        {
            RescueCancels.Add(assignmentId);
            return Task.FromResult(true);
        }
    }

    private class RecordingWorkerCancels : IWorkerCancelRepository
    {
        public List<(long AssignmentId, int WorkerId)> Cancels { get; } = [];

        public Task<WorkerCancelCommit?> TryCancelAsync(
            long assignmentId, int workerId, DateTime cancelledAtUtc, DateTime windowStartUtc, int lockCount, CancellationToken cancellationToken = default)
        {
            Cancels.Add((assignmentId, workerId));
            return Task.FromResult<WorkerCancelCommit?>(new WorkerCancelCommit(1, false, true));
        }

        public Task<CommonService.Domain.Entities.JobOrder?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountWorkerCancelsSinceAsync(int workerId, DateTime sinceUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsAutoAcceptOnAsync(int workerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CommonService.Domain.Entities.JobOrder>> GetRedispatchingAfterWorkerCancelAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasWorkerCancelledSeatAsync(long orderId, byte? assignmentSeq, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private class RecordingSla : ISlaPenaltyService
    {
        public List<SlaPenaltyRequest> Requests { get; } = [];
        public Func<Task>? BeforeApply { get; set; }

        public async Task<SlaPenaltyResult> ApplyAsync(SlaPenaltyRequest request, CancellationToken cancellationToken = default)
        {
            if (BeforeApply is not null)
            {
                await BeforeApply();
            }

            Requests.Add(request);
            return new SlaPenaltyResult(-30m, 70m, request.CustomerRefundAmount, 0m, false);
        }
    }

    private class RecordingPublisher(StubRepository repository, bool cancels = true) : IPublisher
    {
        public List<AssignmentFailed> Failures { get; } = [];
        public List<string> Order { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Handle(notification);

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Handle(notification!);

        private Task Handle(object notification)
        {
            if (notification is AssignmentFailed failed)
            {
                Failures.Add(failed);
                Order.Add("cancel");
                if (cancels)
                {
                    repository.Cancelled.Add(failed.OrderId);
                }
            }

            return Task.CompletedTask;
        }
    }

    private class RecordingNotifications : INotificationService
    {
        public List<NotificationMessage> Sent { get; } = [];

        public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private static PremiumOrderSnapshot Order(long id, params PremiumSeat[] seats) =>
        new(id, 5, Day, BookingShiftsMorning, seats.Length == 0 ? 2 : 2, 780000m, false, false, seats);

    private const string BookingShiftsMorning = "SHIFT_MORNING";

    private static (PremiumShiftStartService Service, StubRepository Repo, RecordingSla Sla, RecordingPublisher Publisher, RecordingNotifications Notices, FakeClock Clock) World(
        DateTime nowUtc, bool cancels = true, IWorkerCancelRepository? workerCancels = null)
    {
        var repo = new StubRepository();
        var sla = new RecordingSla();
        var publisher = new RecordingPublisher(repo, cancels);
        var notices = new RecordingNotifications();
        var clock = new FakeClock();
        clock.Set(nowUtc);
        var canceller = new PremiumOrderCanceller(publisher, repo, notices);
        return (new PremiumShiftStartService(repo, sla, canceller, clock, Microsoft.Extensions.Options.Options.Create(new BusinessRules()), workerCancels), repo, sla, publisher, notices, clock);
    }

    [Fact]
    public async Task Both_present_changes_nothing()
    {
        var (service, repo, sla, publisher, notices, _) = World(Start.AddMinutes(30));
        repo.Orders.Add(Order(1, Seat(1, JobAssignmentStatus.CheckedIn), Seat(2, JobAssignmentStatus.CheckedIn)));

        Assert.Equal(0, await service.CancelUnstaffedOrdersAsync());
        Assert.Empty(sla.Requests);
        Assert.Empty(publisher.Failures);
        Assert.Empty(notices.Sent);
    }

    [Fact]
    public async Task Before_the_grace_nothing_happens()
    {
        var (service, repo, sla, publisher, _, _) = World(Start.AddMinutes(4));
        repo.Orders.Add(Order(1, Seat(1, JobAssignmentStatus.CheckedIn), Seat(2, JobAssignmentStatus.Assigned)));

        Assert.Equal(0, await service.CancelUnstaffedOrdersAsync());
        Assert.Empty(sla.Requests);
        Assert.Empty(publisher.Failures);
    }

    [Fact]
    public async Task One_absent_worker_cancels_the_order_penalises_the_agency_and_apologises()
    {
        var (service, repo, sla, publisher, notices, _) = World(Start.AddMinutes(5));
        repo.Orders.Add(Order(1, Seat(1, JobAssignmentStatus.CheckedIn), Seat(2, JobAssignmentStatus.Assigned)));

        Assert.Equal(1, await service.CancelUnstaffedOrdersAsync());

        var penalty = Assert.Single(sla.Requests);
        Assert.Equal(7, penalty.AgencyId);
        Assert.Equal(SlaViolation.NoShowAtStart, penalty.Violation);
        Assert.Equal(390000m, penalty.CustomerRefundAmount); // the seat of the absent worker only
        Assert.Equal(1, penalty.OrderId);
        Assert.Equal(1, Assert.Single(publisher.Failures).OrderId);
        var apology = Assert.Single(notices.Sent);
        Assert.Equal(UserRole.Customer, apology.RecipientRole);
        Assert.Equal(5, apology.RecipientId);
        Assert.Equal(PremiumShiftStartService.ApologyTopic, apology.Topic);
        Assert.Equal("1", apology.Data!["orderId"]);
        Assert.Contains("xin lỗi", apology.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("780.000", apology.Body);
    }

    [Fact]
    public async Task Two_absent_workers_of_one_agency_are_one_penalty_of_the_two_seats()
    {
        var (service, repo, sla, _, _, _) = World(Start.AddMinutes(6));
        repo.Orders.Add(Order(1, Seat(1, JobAssignmentStatus.Assigned), Seat(2, JobAssignmentStatus.Assigned)));

        await service.CancelUnstaffedOrdersAsync();

        var penalty = Assert.Single(sla.Requests);
        Assert.Equal(780000m, penalty.CustomerRefundAmount);
    }

    [Fact]
    public async Task The_penalty_is_applied_before_the_order_is_cancelled()
    {
        var (service, repo, sla, publisher, _, _) = World(Start.AddMinutes(6));
        sla.BeforeApply = () =>
        {
            publisher.Order.Add("penalty");
            return Task.CompletedTask;
        };
        repo.Orders.Add(Order(1, Seat(1, JobAssignmentStatus.CheckedIn), Seat(2, JobAssignmentStatus.Assigned)));

        await service.CancelUnstaffedOrdersAsync();

        Assert.Equal(["penalty", "cancel"], publisher.Order);
    }

    [Fact]
    public async Task The_sweep_twice_penalises_and_apologises_once()
    {
        var (service, repo, sla, publisher, notices, _) = World(Start.AddMinutes(6));
        repo.Orders.Add(Order(1, Seat(1, JobAssignmentStatus.CheckedIn), Seat(2, JobAssignmentStatus.Assigned)));

        Assert.Equal(1, await service.CancelUnstaffedOrdersAsync());
        Assert.Equal(0, await service.CancelUnstaffedOrdersAsync());

        Assert.Single(sla.Requests);
        Assert.Single(publisher.Failures);
        Assert.Single(notices.Sent);
    }

    [Fact]
    public async Task No_apology_is_sent_while_the_order_is_not_really_cancelled()
    {
        var (service, repo, _, publisher, notices, _) = World(Start.AddMinutes(6), cancels: false);
        repo.Orders.Add(Order(1, Seat(1, JobAssignmentStatus.CheckedIn), Seat(2, JobAssignmentStatus.Assigned)));

        Assert.Equal(0, await service.CancelUnstaffedOrdersAsync());

        Assert.Single(publisher.Failures);
        Assert.Empty(notices.Sent);
    }

    [Fact]
    public async Task An_order_with_an_open_swap_window_or_an_incident_is_left_to_its_own_handling()
    {
        var (service, repo, sla, publisher, _, _) = World(Start.AddMinutes(30));
        var seats = new[] { Seat(1, JobAssignmentStatus.CheckedIn), Seat(2, JobAssignmentStatus.Assigned) };
        repo.Orders.Add(Order(1, seats) with { HasOpenSwapWindow = true });
        repo.Orders.Add(Order(2, seats) with { HasIncident = true });

        Assert.Equal(0, await service.CancelUnstaffedOrdersAsync());
        Assert.Empty(sla.Requests);
        Assert.Empty(publisher.Failures);
    }

    [Fact]
    public async Task An_absent_rescue_freelancer_cancels_the_order_without_an_agency_penalty()
    {
        var workerCancels = new RecordingWorkerCancels();
        var (service, repo, sla, publisher, _, _) = World(Start.AddMinutes(40), workerCancels: workerCancels);
        repo.Orders.Add(Order(1, Seat(1, JobAssignmentStatus.CheckedIn), Seat(2, JobAssignmentStatus.Assigned, agency: null, replacement: true, created: Start.AddMinutes(-5))));

        Assert.Equal(1, await service.CancelUnstaffedOrdersAsync());

        Assert.Empty(sla.Requests); // a Super-Freelancer has no agency: no SLA points
        Assert.Single(publisher.Failures);
        // ... but the freelancer gets ONE violation (CANCELLED_BY_WORKER, the count of Q15) through the normal worker-cancel write.
        var cancel = Assert.Single(workerCancels.Cancels);
        Assert.Equal(102, cancel.AssignmentId);
        Assert.Equal(12, cancel.WorkerId);
    }

    [Fact]
    public async Task A_present_rescue_freelancer_gets_no_violation()
    {
        var workerCancels = new RecordingWorkerCancels();
        var (service, repo, _, _, _, _) = World(Start.AddMinutes(40), workerCancels: workerCancels);
        repo.Orders.Add(Order(1, Seat(1, JobAssignmentStatus.Assigned), Seat(2, JobAssignmentStatus.CheckedIn, agency: null, replacement: true, created: Start.AddMinutes(-5))));

        await service.CancelUnstaffedOrdersAsync();

        Assert.Empty(workerCancels.Cancels); // only the absent agency seat is penalised
    }

    [Fact]
    public async Task The_canceller_apologises_only_when_the_order_is_really_cancelled()
    {
        var repo = new StubRepository();
        repo.Orders.Add(Order(1, Seat(1, JobAssignmentStatus.Assigned)));
        var notices = new RecordingNotifications();

        Assert.False(await new PremiumOrderCanceller(new RecordingPublisher(repo, cancels: false), repo, notices).CancelAsync(1, "x", Start));
        Assert.Empty(notices.Sent);

        Assert.True(await new PremiumOrderCanceller(new RecordingPublisher(repo), repo, notices).CancelAsync(1, "x", Start));
        var apology = Assert.Single(notices.Sent);
        Assert.Equal(PremiumOrderCanceller.ApologyTopic, apology.Topic);
    }

    [Fact]
    public void Rescue_cancel_is_for_premium_seats_without_an_agency_and_its_deadline_follows_the_replacement_rule()
    {
        var rescue = new CommonService.Domain.Entities.JobAssignment { ServiceTier = ServiceTier.Premium, AgencyId = null, CreatedAt = Start.AddMinutes(-10) };
        var agencySeat = new CommonService.Domain.Entities.JobAssignment { ServiceTier = ServiceTier.Premium, AgencyId = 7, CreatedAt = Start.AddMinutes(-10) };
        var economy = new CommonService.Domain.Entities.JobAssignment { ServiceTier = ServiceTier.Economy, AgencyId = null, CreatedAt = Start.AddMinutes(-10) };

        Assert.True(RescueCancelService.IsRescue(rescue));
        Assert.False(RescueCancelService.IsRescue(agencySeat));
        Assert.False(RescueCancelService.IsRescue(economy));

        var clock = new FakeClock();
        var repo = new StubRepository();
        var service = new RescueCancelService(
            repo, new PremiumOrderCanceller(new RecordingPublisher(repo), repo, new RecordingNotifications()), clock,
            Microsoft.Extensions.Options.Options.Create(new BusinessRules()));
        Assert.Equal(Start.AddMinutes(20), service.Deadline(rescue, Start)); // assigned 10 min before + 30
        clock.Set(Start.AddMinutes(19));
        service.RequireBeforeDeadline(rescue, Start);
        clock.Set(Start.AddMinutes(20));
        Assert.Throws<CommonService.Application.Exceptions.BusinessRuleViolationException>(() => service.RequireBeforeDeadline(rescue, Start));
    }

    [Fact]
    public async Task A_replacement_inside_its_thirty_minutes_does_not_cancel_the_order_yet()
    {
        var (service, repo, sla, _, _, _) = World(Start.AddMinutes(20));
        repo.Orders.Add(Order(1, Seat(1, JobAssignmentStatus.CheckedIn), Seat(2, JobAssignmentStatus.Assigned, replacement: true, created: Start.AddMinutes(-9))));

        Assert.Equal(0, await service.CancelUnstaffedOrdersAsync());
        Assert.Empty(sla.Requests);
    }
}
