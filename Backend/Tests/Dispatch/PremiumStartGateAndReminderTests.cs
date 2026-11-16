using CommonService.Application.Features.Dispatch;
using CommonService.Application.Features.Workers;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using Xunit;

namespace CommonService.Tests.Dispatch;

/// <summary>
/// Decision D38 (PREM-START-02, #952): the work of a Premium order starts only when every seat is present, and each worker gets one reminder 30
/// minutes before the check-in time. No database: the seat read, the reminder store and the notices are stubs.
/// </summary>
public class PremiumStartGateAndReminderTests
{
    private static OrderSeatPresence Seats(bool premium, int required, params (byte, JobAssignmentStatus)[] live) => new(premium, required, live);

    // ---------------------------------------------------------------- the gate

    [Fact]
    public void Two_seats_one_present_holds_the_work()
    {
        var seats = Seats(true, 2, (1, JobAssignmentStatus.CheckedIn), (2, JobAssignmentStatus.Assigned));

        Assert.True(PremiumStartGate.MustWaitForPartner(seats));
    }

    [Fact]
    public void Two_seats_both_present_lets_the_work_start()
    {
        var seats = Seats(true, 2, (1, JobAssignmentStatus.CheckedIn), (2, JobAssignmentStatus.CheckedIn));

        Assert.False(PremiumStartGate.MustWaitForPartner(seats));
    }

    [Fact]
    public void A_colleague_already_working_counts_as_present()
    {
        var seats = Seats(true, 2, (1, JobAssignmentStatus.InProgress), (2, JobAssignmentStatus.CheckedIn));

        Assert.False(PremiumStartGate.MustWaitForPartner(seats));
    }

    [Fact]
    public void A_seat_without_a_live_assignment_holds_the_work()
    {
        var seats = Seats(true, 2, (1, JobAssignmentStatus.CheckedIn));

        Assert.True(PremiumStartGate.MustWaitForPartner(seats));
    }

    [Fact]
    public void A_cancelled_colleague_does_not_count_so_a_replacement_is_awaited()
    {
        // The live list never holds a CANCELLED / REASSIGNED row (the query drops them): the seat is simply missing until the replacement checks in.
        var seats = Seats(true, 2, (1, JobAssignmentStatus.CheckedIn), (2, JobAssignmentStatus.Assigned));

        Assert.True(PremiumStartGate.MustWaitForPartner(seats));
    }

    [Fact]
    public void An_Economy_order_and_a_one_seat_Premium_order_are_never_held()
    {
        Assert.False(PremiumStartGate.MustWaitForPartner(Seats(false, 1, (1, JobAssignmentStatus.CheckedIn))));
        Assert.False(PremiumStartGate.MustWaitForPartner(Seats(false, 2, (1, JobAssignmentStatus.CheckedIn))));
        Assert.False(PremiumStartGate.MustWaitForPartner(Seats(true, 1, (1, JobAssignmentStatus.CheckedIn))));
    }

    [Fact]
    public void The_error_code_is_the_one_of_the_contract()
    {
        Assert.Equal("PARTNER_NOT_CHECKED_IN", StartGateErrorCodes.PartnerNotCheckedIn);
    }

    // ---------------------------------------------------------------- the reminder

    private static readonly DateOnly Day = new(2026, 11, 20);
    private static readonly DateTime Start = new(2026, 11, 20, 1, 0, 0, DateTimeKind.Utc); // 08:00 local, the morning shift

    private class StubReminders : IPremiumReminderRepository
    {
        public List<ReminderSeat> Seats { get; } = [];
        public HashSet<long> Marked { get; } = [];

        public Task<IReadOnlyList<ReminderSeat>> GetSeatsToRemindAsync(DateOnly latestDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReminderSeat>>(Seats.Where(s => !Marked.Contains(s.AssignmentId)).ToList());

        public Task<bool> TryMarkRemindedAsync(long assignmentId, DateTime atUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(Marked.Add(assignmentId));
    }

    private class RecordingNotifications : INotificationService
    {
        public List<NotificationMessage> Sent { get; } = [];
        public bool Throws { get; set; }

        public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
        {
            if (Throws)
            {
                throw new InvalidOperationException("hub down");
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private static (PremiumCheckInReminderService Service, StubReminders Repo, RecordingNotifications Notices) World(DateTime nowUtc)
    {
        var repo = new StubReminders();
        var notices = new RecordingNotifications();
        var clock = new FakeClock();
        clock.Set(nowUtc);
        return (new PremiumCheckInReminderService(repo, notices, clock, Microsoft.Extensions.Options.Options.Create(new CommonService.Application.Common.Options.BusinessRules())), repo, notices);
    }

    private static ReminderSeat Seat(long assignmentId, int workerId) => new(assignmentId, 9, workerId, Day, "SHIFT_MORNING");

    [Fact]
    public async Task Both_workers_get_one_reminder_in_the_thirty_minutes_before_the_check_in_time()
    {
        var (service, repo, notices) = World(Start.AddMinutes(-29));
        repo.Seats.AddRange([Seat(1, 11), Seat(2, 12)]);

        Assert.Equal(2, await service.SendDueRemindersAsync());

        Assert.Equal([11, 12], notices.Sent.Select(n => n.RecipientId).Order().ToArray());
        Assert.All(notices.Sent, n =>
        {
            Assert.Equal(UserRole.Worker, n.RecipientRole);
            Assert.Equal(PremiumCheckInReminderService.ReminderTopic, n.Topic);
            Assert.Equal("9", n.Data!["orderId"]);
            Assert.Contains("di chuyển", n.Body);
        });
    }

    [Fact]
    public async Task A_second_run_sends_nothing_more_even_after_a_restart()
    {
        var (service, repo, notices) = World(Start.AddMinutes(-20));
        repo.Seats.Add(Seat(1, 11));

        Assert.Equal(1, await service.SendDueRemindersAsync());
        Assert.Equal(0, await service.SendDueRemindersAsync());
        // A restart: another service over the same store (the marker is persisted).
        var restarted = new PremiumCheckInReminderService(
            repo, notices, new FakeClock(), Microsoft.Extensions.Options.Options.Create(new CommonService.Application.Common.Options.BusinessRules()));
        Assert.Equal(0, await restarted.SendDueRemindersAsync());

        Assert.Single(notices.Sent);
    }

    [Theory]
    [InlineData(-31)]
    [InlineData(0)]
    [InlineData(3)]
    public async Task Nothing_is_sent_earlier_than_thirty_minutes_before_or_from_the_check_in_time_on(int minutesFromStart)
    {
        var (service, repo, notices) = World(Start.AddMinutes(minutesFromStart));
        repo.Seats.Add(Seat(1, 11));

        Assert.Equal(0, await service.SendDueRemindersAsync());
        Assert.Empty(notices.Sent);
    }

    [Fact]
    public async Task A_worker_who_already_checked_in_is_not_in_the_read_and_gets_nothing()
    {
        var (service, repo, notices) = World(Start.AddMinutes(-10));
        // The repository only returns ASSIGNED seats: a checked-in seat is simply not in the list.

        Assert.Equal(0, await service.SendDueRemindersAsync());
        Assert.Empty(notices.Sent);
        Assert.Empty(repo.Marked);
    }

    [Fact]
    public async Task A_failed_notice_is_not_repeated_the_marker_is_already_set()
    {
        var (service, repo, notices) = World(Start.AddMinutes(-10));
        notices.Throws = true;
        repo.Seats.Add(Seat(1, 11));

        Assert.Equal(0, await service.SendDueRemindersAsync());
        Assert.Contains(1L, repo.Marked);

        notices.Throws = false;
        Assert.Equal(0, await service.SendDueRemindersAsync());
        Assert.Empty(notices.Sent);
    }
}
