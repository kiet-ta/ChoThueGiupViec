using CommonService.Application.Features.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CommonService.Infrastructure.Modules.Dispatch;

/// <summary>
/// Runs <see cref="PremiumShiftStartService.CancelUnstaffedOrdersAsync"/> every <see cref="SweepInterval"/> (decision D38): a Premium order whose
/// seats are not all present at the shift start + <c>Premium.CheckInGraceMinutes</c> is cancelled, refunded and its agencies penalised. A failing
/// run is logged and never stops the loop.
/// </summary>
public class PremiumStartSweepWorker(IServiceScopeFactory scopeFactory, ILogger<PremiumStartSweepWorker> logger) : BackgroundService
{
    /// <summary>provenance: same tick as the swap window sweep (30 s): the order is cancelled at most half a minute after the 5-minute grace, for one indexed read per tick.</summary>
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
                    var cancelled = await scope.ServiceProvider.GetRequiredService<PremiumShiftStartService>().CancelUnstaffedOrdersAsync(stoppingToken);
                    if (cancelled > 0)
                    {
                        logger.LogInformation("Dispatch: {Count} unstaffed Premium order(s) cancelled at the shift start.", cancelled);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "The Premium shift-start sweep failed; it runs again at the next tick.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
