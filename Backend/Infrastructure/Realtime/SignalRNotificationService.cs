using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace CommonService.Infrastructure.Realtime;

/// <summary>
/// Production INotificationService implementation dispatching via SignalR and storing in the fallback store.
/// Customer payment status and job tracking are dispatched to role:id groups in realtime (decisions Q07); a message reaches only its
/// recipient's group (decisions D36).
/// </summary>
public class SignalRNotificationService : INotificationService
{
    private readonly IHubContext<NotificationHub, INotificationClient> _hubContext;
    private readonly INotificationStore _store;
    private readonly ILogger<SignalRNotificationService> _logger;

    public SignalRNotificationService(
        IHubContext<NotificationHub, INotificationClient> hubContext,
        INotificationStore store,
        ILogger<SignalRNotificationService> logger)
    {
        _hubContext = hubContext;
        _store = store;
        _logger = logger;
    }

    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var dto = new NotificationMessageDto(
            RecipientRole: message.RecipientRole.ToString(),
            RecipientId: message.RecipientId,
            Topic: message.Topic,
            Title: message.Title,
            Body: message.Body,
            Data: message.Data,
            TimestampUtc: DateTime.UtcNow
        );

        // Store for polling fallback
        _store.Add(dto);

        var userGroup = NotificationHub.FormatUserGroup(dto.RecipientRole, dto.RecipientId);

        _logger.LogInformation(
            "Dispatching realtime notification {Topic} to group {UserGroup} for recipient {RecipientId}",
            dto.Topic, userGroup, dto.RecipientId);

        // Decisions D36 (#894): only the recipient's own group. There is no per-topic broadcast; clients read dto.Topic.
        await _hubContext.Clients.Group(userGroup).ReceiveNotification(dto);
    }
}
