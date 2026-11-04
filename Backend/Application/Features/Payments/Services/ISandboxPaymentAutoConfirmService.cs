namespace CommonService.Application.Features.Payments.Services;

/// <summary>
/// Development-only sandbox auto-confirmation service.
/// Schedules automatic confirmation of fake gateway payments after a configured delay (default: 3 seconds)
/// without requiring manual Admin approval.
/// </summary>
public interface ISandboxPaymentAutoConfirmService
{
    /// <summary>Schedules automatic confirmation of the fake payment transaction after the configured delay.</summary>
    void ScheduleAutoConfirm(long paymentId, string gatewayTxnRef);
}
