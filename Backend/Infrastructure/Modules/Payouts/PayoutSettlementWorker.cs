using CommonService.Application.Features.Payouts;
using CommonService.Application.Features.Payouts.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CommonService.Infrastructure.Modules.Payouts;

/// <summary>
/// Settles the previous month from the ledger when it is due (<see cref="PayoutSettlementSchedule"/>): nobody clicks (decision D28). It
/// looks every hour and once shortly after start-up, so a month missed while the service was down is settled as soon as it is back.
/// A closed month answers at once, so repeating is safe; a failing run is logged and never stops the loop.
/// </summary>
public class PayoutSettlementWorker(IServiceScopeFactory scopeFactory, IClock clock, ILogger<PayoutSettlementWorker> logger) : BackgroundService
{
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            using var timer = new PeriodicTimer(PayoutSettlementSchedule.CheckInterval);
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    /// <summary>One look at the schedule; public for the tests.</summary>
    public async Task<string?> RunOnceAsync(CancellationToken cancellationToken)
    {
        var due = PayoutSettlementSchedule.DueMonth(clock);
        if (due is null) return null;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<IPayoutBatchService>().SettleAsync(due, cancellationToken: cancellationToken);
            if (result.StatusCode == 201)
            {
                logger.LogInformation("Payouts: {Period} settled from the ledger ({Items} items, total {Total:0}).", due, result.Data?.ItemCount, result.Data?.TotalAmount);
            }
            else if (!result.Success)
            {
                logger.LogWarning("Payouts: {Period} was not settled ({Status}): {Message}", due, result.StatusCode, result.ErrorMessage);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The monthly settlement of {Period} failed; it runs again at the next check.", due);
        }

        return due;
    }
}
