using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Application.Features.Agencies;

/// <summary>An active subscription that ends within the reminder window and has no later one behind it.</summary>
/// <param name="ReminderDays">The threshold already reminded (7 or 1), null when none was sent yet.</param>
public sealed record ExpiringSubscription(int SubscriptionId, int AgencyId, DateOnly EndDate, int? ReminderDays);

/// <summary>Persistence of the expiry reminder (M5-DECISIONS-1, SC-16).</summary>
public interface ISubscriptionReminderRepository
{
    /// <summary>
    /// <c>ACTIVE</c> subscriptions whose <c>end_date</c> is between <paramref name="today"/> and <paramref name="lastEndDate"/> (inclusive)
    /// and that the agency has not already followed with another <c>ACTIVE</c> one ending later.
    /// </summary>
    Task<IReadOnlyList<ExpiringSubscription>> ListEndingAsync(DateOnly today, DateOnly lastEndDate, CancellationToken cancellationToken = default);

    /// <summary>Remembers the threshold reminded; the unit of work saves.</summary>
    Task<bool> MarkRemindedAsync(int subscriptionId, int days, CancellationToken cancellationToken = default);
}

public static class SubscriptionReminderTopics
{
    public const string Expiring = "subscription.expiring";
}

/// <summary>
/// Decisions D16: a subscription is bought by hand (no auto-renew), so the agency is reminded 7 days and 1 day before the end date,
/// once each (the threshold is kept on the row). Run by <c>SubscriptionExpiryWorker</c>. The message is a realtime notification (decisions
/// Q07: SignalR; push is deferred); the Subscription page shows the same warning.
/// </summary>
public sealed class SubscriptionReminderService(
    ISubscriptionReminderRepository repository,
    INotificationService notifications,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<SubscriptionReminderService>? logger = null)
{
    /// <summary>provenance: leader decision 2026-10-14 (M5-DECISIONS-1): 7 days and 1 day before the end date.</summary>
    public static readonly IReadOnlyList<int> Thresholds = [7, 1];

    private readonly ILogger<SubscriptionReminderService> _logger = logger ?? NullLogger<SubscriptionReminderService>.Instance;

    /// <summary>The threshold a subscription ending in <paramref name="daysLeft"/> days is at, or null when it is not within the window.</summary>
    public static int? ThresholdFor(int daysLeft)
    {
        if (daysLeft < 0)
        {
            return null;
        }

        // The smallest threshold that still covers the days left (1 for today and tomorrow, 7 up to a week).
        return Thresholds.OrderBy(t => t).Cast<int?>().FirstOrDefault(t => daysLeft <= t);
    }

    /// <summary>Returns how many reminders were sent in this run.</summary>
    public async Task<int> RemindAsync(CancellationToken cancellationToken = default)
    {
        var today = clock.LocalToday;
        var window = Thresholds.Max();
        var sent = 0;
        foreach (var subscription in await repository.ListEndingAsync(today, today.AddDays(window), cancellationToken))
        {
            var daysLeft = subscription.EndDate.DayNumber - today.DayNumber;
            if (ThresholdFor(daysLeft) is not int threshold || (subscription.ReminderDays is int done && done <= threshold))
            {
                continue;
            }

            try
            {
                var data = new Dictionary<string, string>
                {
                    ["subscriptionId"] = subscription.SubscriptionId.ToString(),
                    ["endDate"] = subscription.EndDate.ToString("yyyy-MM-dd"),
                    ["daysLeft"] = daysLeft.ToString(),
                };
                var when = daysLeft == 0 ? "hôm nay" : daysLeft == 1 ? "ngày mai" : $"sau {daysLeft} ngày";
                await notifications.SendAsync(
                    new NotificationMessage(
                        UserRole.Partner, subscription.AgencyId, SubscriptionReminderTopics.Expiring,
                        "Gói thuê bao sắp hết hạn",
                        $"Gói Pro hết hạn {when} ({subscription.EndDate:dd/MM/yyyy}). Hãy mua lại để nối tiếp; sau thời gian gia hạn, thợ vượt hạn mức gói Free sẽ bị khóa.",
                        data),
                    cancellationToken);

                await unitOfWork.ExecuteInTransactionAsync(
                    () => repository.MarkRemindedAsync(subscription.SubscriptionId, threshold, cancellationToken), cancellationToken);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "The expiry reminder of subscription {SubscriptionId} failed; the next sweep tries again.", subscription.SubscriptionId);
            }
        }

        return sent;
    }
}
