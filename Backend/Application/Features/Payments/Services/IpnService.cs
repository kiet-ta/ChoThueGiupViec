using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CommonService.Application.Features.Payments.Services;

/// <summary>
/// Gateway callback of a payment (BE-M2-05, contract payments.md 2.4). Every rejection changes nothing; a repeat is a no-op;
/// the PENDING -> SUCCESS update (in <see cref="IPaymentSettlementService"/>) is conditional so concurrent identical IPNs
/// produce one SUCCESS and one OrderPaid. Logs never contain the payload or any signature.
/// </summary>
public sealed class IpnService(
    IPaymentGateway gateway,
    IPaymentRepository payments,
    IPaymentSettlementService settlement,
    ILogger<IpnService> logger) : IIpnService
{
    public async Task<IpnOutcome> HandleAsync(IReadOnlyDictionary<string, string> payload, string rawBody, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        // 1. Signature.
        var verification = await gateway.VerifyIpnAsync(payload, cancellationToken);
        if (!verification.IsSignatureValid)
        {
            logger.LogWarning("IPN rejected: invalid signature.");
            return IpnOutcome.Rejected;
        }

        // 2. Known transaction (gateway_txn_ref is UNIQUE).
        var transaction = string.IsNullOrWhiteSpace(verification.GatewayTxnRef)
            ? null
            : await payments.FindByGatewayRefAsync(verification.GatewayTxnRef, cancellationToken);
        if (transaction is null)
        {
            logger.LogWarning("IPN rejected: unknown gateway transaction reference.");
            return IpnOutcome.Rejected;
        }

        // 3. Never mark paid on an amount mismatch.
        if (verification.Amount != transaction.Amount)
        {
            logger.LogWarning("IPN rejected: amount mismatch for payment {PaymentId}.", transaction.PaymentId);
            return IpnOutcome.Rejected;
        }

        // Only the order payment is handled here; the extension path is BE-M2-08.
        if (transaction.Purpose != PaymentPurpose.Order || transaction.OrderId is null)
        {
            logger.LogWarning("IPN rejected: payment {PaymentId} has purpose {Purpose}, not handled by this endpoint yet.", transaction.PaymentId, transaction.Purpose);
            return IpnOutcome.Rejected;
        }

        // 4. Idempotent: anything but PENDING is a repeat.
        if (transaction.TxnStatus != PaymentStatus.Pending)
        {
            return IpnOutcome.AlreadyProcessed;
        }

        switch (verification.Status)
        {
            case PaymentStatus.Success:
                return await settlement.SettlePaidAsync(transaction.PaymentId, transaction.OrderId.Value, transaction.Amount, rawBody, cancellationToken)
                    ? IpnOutcome.Accepted
                    : IpnOutcome.AlreadyProcessed;
            case PaymentStatus.Expired:
                // 6. The order follows the reconciliation job (BE-M2-05a).
                return await payments.TryMarkExpiredAsync(transaction.PaymentId, rawBody, cancellationToken)
                    ? IpnOutcome.Accepted
                    : IpnOutcome.AlreadyProcessed;
            default:
                return IpnOutcome.Accepted;
        }
    }
}
