using System.Globalization;
using CommonService.Application.Common.Options;
using CommonService.Application.Features.Booking;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Dispatch;

/// <summary>A seat that may need the check-in reminder (decision D38).</summary>
public record ReminderSeat(long AssignmentId, long OrderId, int WorkerId, DateOnly ScheduledDate, string ShiftCode);

public interface IPremiumReminderRepository
{
    /// <summary>Seats of PREMIUM orders (PAID / DISPATCHING / ASSIGNED, scheduled on or before <paramref name="latestDate"/>) whose assignment is still <c>ASSIGNED</c> and was not reminded.</summary>
    Task<IReadOnlyList<ReminderSeat>> GetSeatsToRemindAsync(DateOnly latestDate, CancellationToken cancellationToken = default);

    /// <summary>One conditional update (only while not reminded and still <c>ASSIGNED</c>); true for the caller that won it, so a reminder is sent at most once.</summary>
    Task<bool> TryMarkRemindedAsync(long assignmentId, DateTime atUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// Decision D38 (PREM-START-02): every worker of a PREMIUM order who has not checked in gets ONE reminder to move, <c>Premium.ReminderMinutes</c>
/// (30) before the check-in time. The marker <c>JOB_ASSIGNMENT.checkin_reminded_at</c> is claimed first, so a restart or a second instance never
/// sends it twice. A worker who has already checked in is not reminded (the read only returns <c>ASSIGNED</c> seats).
/// </summary>
public class PremiumCheckInReminderService(
    IPremiumReminderRepository repository,
    INotificationService notifications,
    IClock clock,
    IOptions<BusinessRules> rules,
    ILogger<PremiumCheckInReminderService>? logger = null)
{
    public const string ReminderTopic = "dispatch.checkin_reminder";

    private readonly ILogger<PremiumCheckInReminderService> _logger = logger ?? NullLogger<PremiumCheckInReminderService>.Instance;

    /// <summary>Returns how many reminders were sent by this run.</summary>
    public async Task<int> SendDueRemindersAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var lead = TimeSpan.FromMinutes(rules.Value.Premium.ReminderMinutes);
        var sent = 0;
        foreach (var seat in await repository.GetSeatsToRemindAsync(clock.LocalToday.AddDays(1), cancellationToken))
        {
            var start = BookingWindowPolicy.GetShiftStartUtc(seat.ScheduledDate, seat.ShiftCode, clock);
            if (start is null || now < start.Value - lead || now >= start.Value)
            {
                continue;
            }

            if (!await repository.TryMarkRemindedAsync(seat.AssignmentId, now, cancellationToken))
            {
                continue;
            }

            var minutes = Math.Max(1, (int)Math.Ceiling((start.Value - now).TotalMinutes));
            try
            {
                await notifications.SendAsync(
                    new NotificationMessage(
                        UserRole.Worker, seat.WorkerId, ReminderTopic, "Sắp đến giờ làm",
                        $"Còn khoảng {minutes} phút nữa đến ca của bạn. Hãy di chuyển đến địa chỉ của khách ngay để check-in đúng giờ.",
                        new Dictionary<string, string>
                        {
                            ["assignmentId"] = seat.AssignmentId.ToString(CultureInfo.InvariantCulture),
                            ["orderId"] = seat.OrderId.ToString(CultureInfo.InvariantCulture),
                            ["shiftStartUtc"] = start.Value.ToString("O", CultureInfo.InvariantCulture),
                        }),
                    cancellationToken);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The marker is already set: a failed notice is not repeated (a missed reminder is better than a spam).
                _logger.LogWarning(ex, "Could not send the check-in reminder of assignment {AssignmentId}.", seat.AssignmentId);
            }
        }

        return sent;
    }
}
