using CommonService.Application.Common.Models;
using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Realtime;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers;

/// <summary>
/// Polling fallback endpoint for notifications (Q07, BASE-12).
/// Used by clients when WebSocket / SignalR connection is unavailable.
/// </summary>
[ApiController]
[Route("api/notifications")]
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
    /// Polls recent notifications for a user since a given UTC timestamp.
    /// If caller is authenticated, role and userId default to the caller's claims.
    /// </summary>
    [HttpGet("poll")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<NotificationMessageDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public ActionResult<ApiResponse<IReadOnlyList<NotificationMessageDto>>> Poll(
        [FromQuery] string? role = null,
        [FromQuery] int? userId = null,
        [FromQuery] DateTime? since = null,
        [FromQuery] int limit = 50)
    {
        var effectiveRole = role;
        var effectiveUserId = userId;

        if (_currentUser.IsAuthenticated && _currentUser.UserId.HasValue && _currentUser.Role.HasValue)
        {
            effectiveRole ??= _currentUser.Role.Value.ToString();
            effectiveUserId ??= _currentUser.UserId.Value;
        }

        if (string.IsNullOrWhiteSpace(effectiveRole) || !effectiveUserId.HasValue)
        {
            return BadRequest(ApiResponse<object>.Fail("Role and userId are required when unauthenticated."));
        }

        var results = _store.GetRecent(effectiveRole, effectiveUserId.Value, since, limit);
        return Ok(ApiResponse<IReadOnlyList<NotificationMessageDto>>.Ok(results, "Notifications retrieved"));
    }
}
