using CommonService.Domain.Enums;

namespace CommonService.Application.Interfaces.Ports;

/// <summary>One realtime message (SignalR, decisions Q07). FCM push is deferred (Q07b).</summary>
/// <param name="RecipientRole">Who receives it.</param>
/// <param name="RecipientId">Customer / worker / partner / admin id according to the role.</param>
/// <param name="Topic">Machine-readable kind, for example <c>payment.status</c> or <c>job.tracking</c>.</param>
public sealed record NotificationMessage(
    UserRole RecipientRole,
    int RecipientId,
    string Topic,
    string Title,
    string Body,
    IReadOnlyDictionary<string, string>? Data = null);

public interface INotificationService
{
    Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}
