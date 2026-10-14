using CommonService.Application.Features.Agencies;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using Xunit;

namespace CommonService.Tests.Agencies;

/// <summary>M5-DECISIONS-1: the 7-day and 1-day expiry reminders of a manually bought subscription.</summary>
public class SubscriptionReminderTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc); // 10:00 local, 14 October

    private sealed class InMemoryReminders : ISubscriptionReminderRepository
    {
        public List<ExpiringSubscription> Rows { get; } = [];
        public Dictionary<int, int> Marked { get; } = [];

        public Task<IReadOnlyList<ExpiringSubscription>> ListEndingAsync(DateOnly today, DateOnly lastEndDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExpiringSubscription>>(
                Rows.Where(r => r.EndDate >= today && r.EndDate <= lastEndDate).Select(r => r with { ReminderDays = Marked.TryGetValue(r.SubscriptionId, out var d) ? d : r.ReminderDays }).ToList());

        public Task<bool> MarkRemindedAsync(int subscriptionId, int days, CancellationToken cancellationToken = default)
        {
            Marked[subscriptionId] = days;
            return Task.FromResult(true);
        }
    }

    private static (SubscriptionReminderService Service, InMemoryReminders Repo, FakeNotificationService Notifications, FakeClock Clock) Create()
    {
        var repo = new InMemoryReminders();
        var notifications = new FakeNotificationService();
        var clock = new FakeClock();
        clock.Set(Now);
        return (new SubscriptionReminderService(repo, notifications, new WorkerImportTests.PassThroughUnitOfWork(), clock), repo, notifications, clock);
    }

    private static DateOnly Today => DateOnly.FromDateTime(Now.AddHours(7));

    [Theory]
    [InlineData(-1, null)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 7)]
    [InlineData(7, 7)]
    [InlineData(8, null)]
    public void The_threshold_is_the_smallest_one_that_covers_the_days_left(int daysLeft, int? expected) =>
        Assert.Equal(expected, SubscriptionReminderService.ThresholdFor(daysLeft));

    [Fact]
    public async Task A_subscription_ending_in_a_week_is_reminded_once_and_not_again_the_same_day()
    {
        var (service, repo, notifications, _) = Create();
        repo.Rows.Add(new ExpiringSubscription(1, 9, Today.AddDays(7), null));

        Assert.Equal(1, await service.RemindAsync());
        Assert.Equal(0, await service.RemindAsync());

        var message = Assert.Single(notifications.Sent);
        Assert.Equal(UserRole.Partner, message.RecipientRole);
        Assert.Equal(9, message.RecipientId);
        Assert.Equal(SubscriptionReminderTopics.Expiring, message.Topic);
        Assert.Equal(7, repo.Marked[1]);
    }

    [Fact]
    public async Task The_last_day_reminder_follows_the_week_reminder()
    {
        var (service, repo, notifications, _) = Create();
        repo.Rows.Add(new ExpiringSubscription(1, 9, Today.AddDays(1), ReminderDays: 7));

        Assert.Equal(1, await service.RemindAsync());

        Assert.Equal(1, repo.Marked[1]);
        Assert.Contains("ngày mai", Assert.Single(notifications.Sent).Body);
    }

    [Fact]
    public async Task A_subscription_outside_the_window_or_already_reminded_at_its_threshold_is_left_alone()
    {
        var (service, repo, notifications, _) = Create();
        repo.Rows.Add(new ExpiringSubscription(1, 9, Today.AddDays(20), null));
        repo.Rows.Add(new ExpiringSubscription(2, 9, Today.AddDays(1), ReminderDays: 1));

        Assert.Equal(0, await service.RemindAsync());
        Assert.Empty(notifications.Sent);
    }
}
