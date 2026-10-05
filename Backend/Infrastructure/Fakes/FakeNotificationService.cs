using System.Collections.Concurrent;
using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

/// <summary>Collects messages in memory.</summary>
public sealed class FakeNotificationService : INotificationService
{
    private readonly ConcurrentQueue<NotificationMessage> _sent = new();

    public IReadOnlyCollection<NotificationMessage> Sent => _sent.ToArray();

    public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
