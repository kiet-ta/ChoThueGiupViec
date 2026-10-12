using CommonService.Application.Common.Options;
using CommonService.Application.Features.Payments.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommonService.Infrastructure.Modules.Payments;

/// <summary>
/// Runs <see cref="IPaymentReconciliationService"/> every <c>Payments.ReconcileIntervalSeconds</c> (contract payments.md 3.1).
/// The first run happens after one interval. A failing run is logged and never stops the loop.
/// </summary>
public sealed class PaymentReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BusinessRules> rules,
    ILogger<PaymentReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, rules.Value.Payments.ReconcileIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IPaymentReconciliationService>();
                    var result = await service.RunOnceAsync(stoppingToken);
                    if (result.Settled + result.Expired + result.Cancelled + result.Failed > 0)
                    {
                        logger.LogInformation(
                            "Payment reconciliation: settled {Settled}, expired {Expired}, orders cancelled {Cancelled}, failed {Failed}.",
                            result.Settled, result.Expired, result.Cancelled, result.Failed);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Payment reconciliation run failed; it will run again at the next tick.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
