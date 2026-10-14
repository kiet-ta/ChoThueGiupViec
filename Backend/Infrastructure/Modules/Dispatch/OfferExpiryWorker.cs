using CommonService.Application.Features.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CommonService.Infrastructure.Modules.Dispatch;

/// <summary>
/// Runs <see cref="DispatchOfferEngine.ExpirePendingOffersAsync"/> every <see cref="SweepInterval"/> so that a seat whose 30-second offer
/// ran out goes to the next candidate (BR-03). A failing run is logged and never stops the loop.
/// </summary>
public sealed class OfferExpiryWorker(IServiceScopeFactory scopeFactory, ILogger<OfferExpiryWorker> logger) : BackgroundService
{
    /// <summary>
    /// provenance: an offer is moved on at most this long after its 30 s window (Dispatch.OfferTimeoutSeconds, BR-03) ends; 5 s keeps that
    /// delay at one sixth of the window while costing one indexed query per tick.
    /// </summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

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
                    var engine = scope.ServiceProvider.GetRequiredService<DispatchOfferEngine>();
                    var expired = await engine.ExpirePendingOffersAsync(stoppingToken);
                    if (expired > 0)
                    {
                        logger.LogInformation("Dispatch: {Count} expired offer(s) moved to the next candidate.", expired);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Dispatch offer expiry run failed; it will run again at the next tick.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
