namespace CommonService.Infrastructure.Realtime;

/// <summary>
/// Data transfer object for SignalR notification messages.
/// Broadcast to connected clients over the /hubs/notifications hub.
/// </summary>
public sealed record NotificationMessageDto(
    string RecipientRole,
    int RecipientId,
    string Topic,
    string Title,
    string Body,
    IReadOnlyDictionary<string, string>? Data = null,
    DateTime TimestampUtc = default);
