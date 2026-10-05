using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace CommonService.Infrastructure.Realtime;

/// <summary>
/// SignalR hub providing realtime notifications for payment status, job tracking, and worker offers (decisions Q07).
/// Endpoints mapped at /hubs/notifications.
/// </summary>
public class NotificationHub : Hub<INotificationClient>
{
    public static string FormatUserGroup(string role, int userId) =>
        $"{role.ToLowerInvariant()}:{userId}";

    public static string FormatTopicGroup(string topic) =>
        $"topic:{topic.ToLowerInvariant()}";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var roleClaim = user.FindFirst(ClaimTypes.Role)?.Value
                ?? user.FindFirst("role")?.Value;
            var idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("sub")?.Value
                ?? user.FindFirst("userId")?.Value;

            if (!string.IsNullOrWhiteSpace(roleClaim) && int.TryParse(idClaim, out var userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, FormatUserGroup(roleClaim, userId));
            }
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Explicitly join the notification group for a specific user role and ID.
    /// Useful for test clients or before JWT authentication is wired up.
    /// </summary>
    public async Task JoinUserGroup(string role, int userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        await Groups.AddToGroupAsync(Context.ConnectionId, FormatUserGroup(role, userId));
    }

    /// <summary>
    /// Leave the notification group for a specific user role and ID.
    /// </summary>
    public async Task LeaveUserGroup(string role, int userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, FormatUserGroup(role, userId));
    }

    /// <summary>
    /// Subscribe to a specific topic (e.g. "payment.status", "job.tracking").
    /// </summary>
    public async Task JoinTopic(string topic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        await Groups.AddToGroupAsync(Context.ConnectionId, FormatTopicGroup(topic));
    }

    /// <summary>
    /// Unsubscribe from a specific topic.
    /// </summary>
    public async Task LeaveTopic(string topic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, FormatTopicGroup(topic));
    }
}
