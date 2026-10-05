using System.Collections.Concurrent;

namespace CommonService.Infrastructure.Realtime;

/// <summary>
/// Thread-safe in-memory circular storage for recent notifications.
/// Retains up to 2,000 notifications to serve polling requests when WebSocket is disconnected.
/// </summary>
public sealed class InMemoryNotificationStore : INotificationStore
{
    private const int MaxCapacity = 2000;
    private readonly ConcurrentQueue<NotificationMessageDto> _queue = new();

    public void Add(NotificationMessageDto notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        _queue.Enqueue(notification);

        while (_queue.Count > MaxCapacity && _queue.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<NotificationMessageDto> GetRecent(
        string role,
        int userId,
        DateTime? since = null,
        int limit = 50)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        var targetRole = role.Trim();
        var maxItems = Math.Clamp(limit, 1, 100);

        return _queue
            .Where(n =>
                string.Equals(n.RecipientRole, targetRole, StringComparison.OrdinalIgnoreCase) &&
                n.RecipientId == userId &&
                (!since.HasValue || n.TimestampUtc > since.Value))
            .OrderByDescending(n => n.TimestampUtc)
            .Take(maxItems)
            .ToList();
    }
}
