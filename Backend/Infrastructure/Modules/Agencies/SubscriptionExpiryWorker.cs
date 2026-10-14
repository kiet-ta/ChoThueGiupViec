using CommonService.Application.Features.Agencies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CommonService.Infrastructure.Modules.Agencies;

/// <summary>
/// Every <see cref="SweepInterval"/> runs <see cref="WorkerQuotaEnforcer"/> (after a subscription's grace days the workers above the FREE
/// quota are locked, decisions Q08) and <see cref="SubscriptionReminderService"/> (expiry reminders, D16). A failing run is logged and
/// never stops the loop.
/// </summary>
public sealed class SubscriptionExpiryWorker(IServiceScopeFactory scopeFactory, ILogger<SubscriptionExpiryWorker> logger) : BackgroundService
{
    /// <summary>provenance: a lock is allowed to lag the end of the grace period by at most one hour; the sweep reads agencies with workers only.</summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);
        try
        {
            do
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var enforcer = scope.ServiceProvider.GetRequiredService<WorkerQuotaEnforcer>();
                    var locked = await enforcer.EnforceAsync(stoppingToken);
                    if (locked > 0)
                    {
                        logger.LogInformation("Agencies: {Count} worker(s) above the quota were locked.", locked);
                    }

                    // The label follows the package in force, so it is switched off once the grace days pass.
                    await scope.ServiceProvider.GetRequiredService<VerifiedPartnerSync>().SyncAsync(null, stoppingToken);

                    var reminders = scope.ServiceProvider.GetRequiredService<SubscriptionReminderService>();
                    var reminded = await reminders.RemindAsync(stoppingToken);
                    if (reminded > 0)
                    {
                        logger.LogInformation("Agencies: {Count} subscription expiry reminder(s) sent.", reminded);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "The worker quota sweep failed; it runs again at the next tick.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
