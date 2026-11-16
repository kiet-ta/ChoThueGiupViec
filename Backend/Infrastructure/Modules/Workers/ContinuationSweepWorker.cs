using CommonService.Application.Features.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CommonService.Infrastructure.Modules.Workers;

/// <summary>
/// Runs <see cref="IContinuationService.OpenDueAsync"/> every minute (decision D36): a large house whose worker did not run
/// <c>submit-completion</c> within <c>Dispatch.ContinuationConfirmMinutes</c> of the shift end opens its 2nd job. A failing run is logged and
/// never stops the loop.
/// </summary>
public class ContinuationSweepWorker(IServiceScopeFactory scopeFactory, ILogger<ContinuationSweepWorker> logger) : BackgroundService
{
    /// <summary>provenance: the window is 15 minutes, so a one-minute tick opens the 2nd job at most a minute late for one indexed query per tick.</summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var opened = await scope.ServiceProvider.GetRequiredService<IContinuationService>().OpenDueAsync(stoppingToken);
                    if (opened > 0)
                    {
                        logger.LogInformation("Workers: {Count} second job(s) of large houses opened.", opened);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Continuation sweep failed; it will run again at the next tick.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
