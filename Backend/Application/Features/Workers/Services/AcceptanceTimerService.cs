using System.Globalization;
using CommonService.Application.Common.Helpers;
using CommonService.Application.Common.Options;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Workers.Services;

/// <summary>A job waiting for the customer, as the acceptance timer reads it.</summary>
public record WaitingAcceptance(long AssignmentId, long OrderId, int CustomerId, int WorkerId, DateTime SubmittedAt, DateTime? RemindedAt);

/// <summary>The reads and the one write the acceptance timer needs (decision D32); the completion itself goes through <see cref="IJobCompletionService"/>.</summary>
public interface IAcceptanceTimerRepository
{
    /// <summary>AWAITING_ACCEPTANCE assignments whose T is at or before <paramref name="submittedAtOrBeforeUtc"/>, oldest first, at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<WaitingAcceptance>> GetWaitingAsync(DateTime submittedAtOrBeforeUtc, int take, CancellationToken cancellationToken = default);

    /// <summary>Marks the reminder as sent, once: false when another run marked it first or the job is not waiting any more.</summary>
    Task<bool> TryMarkRemindedAsync(long assignmentId, DateTime atUtc, CancellationToken cancellationToken = default);
}

public record AcceptanceTimerResult(int Reminded, int AutoAccepted);

public interface IAcceptanceTimerService
{
    /// <summary>One pass: the reminders due at T+ReminderHours and the automatic acceptances due at T+AutoAcceptHours (decision D32).</summary>
    Task<AcceptanceTimerResult> RunOnceAsync(CancellationToken cancellationToken = default);
}

/// <summary>Topics of the acceptance messages.</summary>
public static class AcceptanceNotificationTopics
{
    public const string Reminder = "acceptance.reminder";
    public const string AutoAccepted = "acceptance.auto";
}

/// <summary>
/// The acceptance timer (decision D32, ACCEPT-01). A job whose order has an OPEN or IN_REVIEW dispute is left alone: the verdict closes it.
/// The reminder is sent once (conditional update); the automatic acceptance uses the same conditional completion as the customer's accept,
/// so two runs, or a run and a customer tapping at the same moment, complete a job once. Messages are best effort.
/// </summary>
public class AcceptanceTimerService(
    IAcceptanceTimerRepository repository,
    IJobCompletionService completion,
    IOpenDisputeQuery disputes,
    INotificationService notifications,
    IClock clock,
    IOptions<BusinessRules> rules,
    ILogger<AcceptanceTimerService> logger) : IAcceptanceTimerService
{
    /// <summary>The most jobs handled in one pass; the next pass (one minute later) continues.</summary>
    public const int BatchSize = 200;

    public async Task<AcceptanceTimerResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var acceptance = rules.Value.Acceptance;
        var now = clock.UtcNow;
        var waiting = await repository.GetWaitingAsync(now.AddHours(-acceptance.ReminderHours), BatchSize, cancellationToken);
        if (waiting.Count == 0) return new AcceptanceTimerResult(0, 0);

        var open = await disputes.GetOpenOrderIdsAsync(waiting.Select(w => w.OrderId).Distinct().ToArray(), cancellationToken);
        int reminded = 0, accepted = 0;
        foreach (var job in waiting)
        {
            if (open.Contains(job.OrderId)) continue; // the verdict decides this job

            try
            {
                if (AcceptanceWindow.IsAutoAcceptDue(job.SubmittedAt, false, now, acceptance))
                {
                    if (!await completion.CompleteBySystemAsync(job.AssignmentId, CompletionSource.Auto, cancellationToken)) continue;
                    accepted++;
                    await NotifyAsync(UserRole.Customer, job.CustomerId, job, AcceptanceNotificationTopics.AutoAccepted,
                        "Ca làm đã tự nghiệm thu", "Đã hết 24 giờ chờ nghiệm thu nên ca làm được tự nghiệm thu.", cancellationToken);
                    await NotifyAsync(UserRole.Worker, job.WorkerId, job, AcceptanceNotificationTopics.AutoAccepted,
                        "Ca làm đã hoàn tất", "Ca làm đã được tự nghiệm thu. Tiền công đã được ghi nhận.", cancellationToken);
                }
                else if (job.RemindedAt is null && await repository.TryMarkRemindedAsync(job.AssignmentId, now, cancellationToken))
                {
                    reminded++;
                    var at = AcceptanceWindow.AutoAcceptAt(job.SubmittedAt, acceptance);
                    await NotifyAsync(UserRole.Customer, job.CustomerId, job, AcceptanceNotificationTopics.Reminder,
                        "Nhắc nghiệm thu", $"Ca sẽ tự nghiệm thu lúc {LocalText(at)} nếu bạn không phản hồi.", cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Acceptance timer: assignment {AssignmentId} failed; the next pass tries again.", job.AssignmentId);
            }
        }

        if (accepted > 0 || reminded > 0)
        {
            logger.LogInformation("Acceptance timer: {Reminded} reminder(s), {Accepted} automatic acceptance(s).", reminded, accepted);
        }

        return new AcceptanceTimerResult(reminded, accepted);
    }

    /// <summary>Vietnam time, <c>HH:mm dd/MM</c> (decision G-3).</summary>
    private string LocalText(DateTime utc) =>
        clock.ToLocal(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToString("HH:mm dd/MM", CultureInfo.InvariantCulture);

    private async Task NotifyAsync(UserRole role, int userId, WaitingAcceptance job, string topic, string title, string body, CancellationToken cancellationToken)
    {
        try
        {
            await notifications.SendAsync(new NotificationMessage(role, userId, topic, title, body, new Dictionary<string, string>
            {
                ["assignmentId"] = job.AssignmentId.ToString(CultureInfo.InvariantCulture),
                ["orderId"] = job.OrderId.ToString(CultureInfo.InvariantCulture),
            }), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Acceptance timer: could not notify {Role} {UserId} for assignment {AssignmentId}.", role, userId, job.AssignmentId);
        }
    }
}
