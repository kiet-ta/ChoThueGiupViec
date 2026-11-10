using CommonService.Application.Features.Ledger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommonService.Infrastructure.Modules.Ledger;

/// <summary>
/// Runs <see cref="ILedgerReconciliationService.SweepAsync"/> every <c>Ledger:SweepIntervalMinutes</c> (default 5): any entry the
/// events did not write is written with the same idempotency key. A failing run is logged and never stops the loop.
/// </summary>
public class LedgerReconciliationWorker(IServiceScopeFactory scopeFactory, IOptions<LedgerOptions> options, ILogger<LedgerReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.Value.SweepIntervalMinutes)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ILedgerReconciliationService>().SweepAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "The ledger reconciliation sweep failed; it runs again at the next tick.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
