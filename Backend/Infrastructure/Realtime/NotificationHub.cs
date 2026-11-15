using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace CommonService.Infrastructure.Realtime;

/// <summary>
/// SignalR hub providing realtime notifications for payment status, job tracking, and worker offers (decisions Q07).
/// Endpoints mapped at /hubs/notifications.
/// Decisions D36 (SEC-NOTIF-01, #894): a connection joins only its own <c>role:id</c> group, taken from its token on connect. There
/// are no client-callable group or topic methods, so an anonymous connection joins nothing and no one can listen to another user.
/// Clients tell messages apart by <see cref="NotificationMessageDto.Topic"/>.
/// </summary>
public class NotificationHub : Hub<INotificationClient>
{
    public static string FormatUserGroup(string role, int userId) =>
        $"{role.ToLowerInvariant()}:{userId}";

    /// <summary>The caller's own user group from its claims, or null when it is anonymous or the claims are incomplete.</summary>
    public static string? ResolveUserGroup(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var roleClaim = user.FindFirst(ClaimTypes.Role)?.Value
            ?? user.FindFirst("role")?.Value;
        var idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? user.FindFirst("userId")?.Value;

        return !string.IsNullOrWhiteSpace(roleClaim) && int.TryParse(idClaim, out var userId)
            ? FormatUserGroup(roleClaim, userId)
            : null;
    }

    public override async Task OnConnectedAsync()
    {
        var group = ResolveUserGroup(Context.User);
        if (group != null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, group);
        }

        await base.OnConnectedAsync();
    }
}
