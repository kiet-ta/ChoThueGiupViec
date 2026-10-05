namespace CommonService.Infrastructure.Realtime;

/// <summary>
/// Storage interface for recent notifications, enabling the polling fallback endpoint.
/// </summary>
public interface INotificationStore
{
    void Add(NotificationMessageDto notification);
    IReadOnlyList<NotificationMessageDto> GetRecent(string role, int userId, DateTime? since = null, int limit = 50);
}
