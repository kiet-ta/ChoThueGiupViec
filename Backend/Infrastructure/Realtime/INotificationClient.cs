namespace CommonService.Infrastructure.Realtime;

/// <summary>
/// Strongly-typed client contract for NotificationHub.
/// </summary>
public interface INotificationClient
{
    Task ReceiveNotification(NotificationMessageDto notification);
}
