using CommonService.Application.Common.Models;
using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers;

/// <summary>
/// Polling fallback endpoint for notifications (Q07, BASE-12).
/// Used by clients when WebSocket / SignalR connection is unavailable.
/// Decisions D36 (SEC-NOTIF-01, #894): signed-in callers only, and only their own notifications.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationStore _store;
    private readonly ICurrentUser _currentUser;

    public NotificationsController(INotificationStore store, ICurrentUser currentUser)
    {
        _store = store;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Lists the caller's recent notifications since a given UTC timestamp (<c>GET /api/v1/notifications?since=&amp;limit=</c>).
    /// The recipient is always the caller (role and id from the token); anonymous -> 401.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<NotificationMessageDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public ActionResult<ApiResponse<IReadOnlyList<NotificationMessageDto>>> Poll(
        [FromQuery] DateTime? since = null,
        [FromQuery] int limit = 50)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not int userId || _currentUser.Role is not { } role)
        {
            return Unauthorized(ApiResponse<object>.Fail("Authentication is required."));
        }

        var results = _store.GetRecent(role.ToString(), userId, since, limit);
        return Ok(ApiResponse<IReadOnlyList<NotificationMessageDto>>.Ok(results, "Notifications retrieved"));
    }
}
