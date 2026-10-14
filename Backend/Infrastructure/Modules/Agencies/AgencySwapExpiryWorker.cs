using CommonService.Application.Features.Agencies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CommonService.Infrastructure.Modules.Agencies;

/// <summary>
/// Runs <see cref="AgencySwapService.ExpireDueAsync"/> every <see cref="SweepInterval"/>: a swap window past its deadline is handed to
/// Dispatch (<c>AgencySwapWindowExpired</c>, decisions Q09). A failing run is logged and never stops the loop.
/// </summary>
public sealed class AgencySwapExpiryWorker(IServiceScopeFactory scopeFactory, ILogger<AgencySwapExpiryWorker> logger) : BackgroundService
{
    /// <summary>provenance: the hand-over may lag the 30-minute deadline by at most this long; one indexed query per tick.</summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

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
                    var service = scope.ServiceProvider.GetRequiredService<AgencySwapService>();
                    var handed = await service.ExpireDueAsync(stoppingToken);
                    if (handed > 0)
                    {
                        logger.LogInformation("Agencies: {Count} swap window(s) handed to Dispatch.", handed);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "The swap window sweep failed; it runs again at the next tick.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
