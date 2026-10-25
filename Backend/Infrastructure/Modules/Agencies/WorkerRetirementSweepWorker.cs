using CommonService.Application.Features.Agencies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CommonService.Infrastructure.Modules.Agencies;

/// <summary>
/// Runs <see cref="WorkerRetirementSweepService.SweepAsync"/> every <see cref="SweepInterval"/>: a retiring worker's future jobs go to the
/// emergency rescue again, and a retiring worker without any job becomes retired. A failing run is logged and never stops the loop.
/// </summary>
public sealed class WorkerRetirementSweepWorker(IServiceScopeFactory scopeFactory, ILogger<WorkerRetirementSweepWorker> logger) : BackgroundService
{
    /// <summary>provenance: leader decision 2026-10-25 (AGENCY-FIX-01), a lost rescue hand-over must not wait an hour for a shift that starts sooner.</summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

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
                    var service = scope.ServiceProvider.GetRequiredService<WorkerRetirementSweepService>();
                    var (retired, republished) = await service.SweepAsync(stoppingToken);
                    if (retired > 0 || republished > 0)
                    {
                        logger.LogInformation("Agencies: {Retired} worker(s) retired, {Republished} job(s) handed to the rescue again.", retired, republished);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "The worker retirement sweep failed; it runs again at the next tick.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
