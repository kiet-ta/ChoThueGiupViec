using CommonService.Application.Common.Options;
using CommonService.Application.Features.Payments.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommonService.Infrastructure.Services;

/// <summary>
/// Schedules automatic confirmation of Fake gateway payments in Development after a configurable delay (default: 3 seconds).
/// In non-development environments, this service is a no-op.
/// </summary>
public class SandboxPaymentAutoConfirmService(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    IOptions<BusinessRules> rules,
    ILogger<SandboxPaymentAutoConfirmService> logger) : ISandboxPaymentAutoConfirmService
{
    public void ScheduleAutoConfirm(long paymentId, string gatewayTxnRef)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        var paymentRules = rules.Value.Payments;
        if (!paymentRules.SandboxAutoConfirm)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(gatewayTxnRef) ||
            !gatewayTxnRef.StartsWith(DevPaymentService.FakeReferencePrefix, StringComparison.Ordinal))
        {
            return;
        }

        var delaySeconds = Math.Max(1, paymentRules.SandboxAutoConfirmDelaySeconds);
        logger.LogInformation(
            "Scheduling sandbox auto-confirm for payment {PaymentId} ({GatewayTxnRef}) in {DelaySeconds}s.",
            paymentId, gatewayTxnRef, delaySeconds);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                using var scope = scopeFactory.CreateScope();
                var devPaymentService = scope.ServiceProvider.GetRequiredService<DevPaymentService>();
                var outcome = await devPaymentService.ConfirmAsync(paymentId);
                logger.LogInformation(
                    "Sandbox auto-confirm for payment {PaymentId} completed with outcome: {Outcome}.",
                    paymentId, outcome);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Sandbox auto-confirm for payment {PaymentId} failed.", paymentId);
            }
        });
    }
}
