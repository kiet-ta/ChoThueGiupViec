using CommonService.Application.Features.Workers.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CommonService.Infrastructure.Modules.Workers;

/// <summary>
/// Runs <see cref="IAcceptanceTimerService.RunOnceAsync"/> every minute (decision D32): reminders at T+20 h and automatic acceptances at
/// T+24 h. A failing pass is logged and never stops the loop; the conditional writes make a repeated pass harmless.
/// </summary>
public class AcceptanceTimerWorker(IServiceScopeFactory scopeFactory, ILogger<AcceptanceTimerWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IAcceptanceTimerService>().RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "The acceptance timer failed; it runs again at the next tick.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
