using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Infrastructure.Realtime;

/// <summary>
/// Auto-discovered business module for realtime SignalR communication (BASE-12, Q07).
/// Registers SignalR, notification store, and SignalRNotificationService, and maps /hubs/notifications.
/// </summary>
public sealed class RealtimeModule : IModule
{
    public string Name => "Realtime";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSignalR();
        services.AddSingleton<INotificationStore, InMemoryNotificationStore>();
        services.AddSingleton<INotificationService, SignalRNotificationService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<NotificationHub>("/hubs/notifications");
    }
}
