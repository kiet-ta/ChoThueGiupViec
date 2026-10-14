using CommonService.Application.Features.Payments;
using CommonService.Domain.Entities;
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
    ILogger<IpnService> logger,
    IEnumerable<IPaymentPurposeSettlement>? purposeSettlements = null) : IIpnService
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

        // Order and extension payments are handled here; the other purposes (subscription, escrow deposit) are M5's: its handler
        // settles them (IPaymentPurposeSettlement), everything else about the IPN (signature, amount, repeats) stays here.
        var isOrder = transaction.Purpose == PaymentPurpose.Order && transaction.OrderId is not null;
        var isExtension = transaction.Purpose == PaymentPurpose.Extension && transaction.ExtensionId is not null;
        var purposeHandler = isOrder || isExtension
            ? null
            : purposeSettlements?.FirstOrDefault(h => h.Purpose == transaction.Purpose);
        if (purposeHandler is not null)
        {
            return await HandlePurposeAsync(purposeHandler, transaction, verification.Status, rawBody, cancellationToken);
        }

        if (!isOrder && !isExtension)
        {
            logger.LogWarning("IPN rejected: payment {PaymentId} has purpose {Purpose}, not handled by this endpoint.", transaction.PaymentId, transaction.Purpose);
            return IpnOutcome.Rejected;
        }

        // P4 (decision Q24): the gateway says SUCCESS for a transaction we already EXPIRED. The money was received: record it once
        // and refund it; the order or extension is not revived.
        if (transaction.TxnStatus == PaymentStatus.Expired && verification.Status == PaymentStatus.Success)
        {
            var recorded = await settlement.SettleLatePaymentAsync(
                transaction.PaymentId, transaction.Purpose, transaction.OrderId, transaction.ExtensionId, transaction.Amount, rawBody, cancellationToken);
            return recorded ? IpnOutcome.Accepted : IpnOutcome.AlreadyProcessed;
        }

        // 4. Idempotent: anything but PENDING is a repeat.
        if (transaction.TxnStatus != PaymentStatus.Pending)
        {
            return IpnOutcome.AlreadyProcessed;
        }

        switch (verification.Status)
        {
            case PaymentStatus.Success:
                var settled = isOrder
                    ? await settlement.SettlePaidAsync(transaction.PaymentId, transaction.OrderId!.Value, transaction.Amount, rawBody, cancellationToken)
                    : await settlement.SettleExtensionPaidAsync(transaction.PaymentId, transaction.ExtensionId!.Value, rawBody, cancellationToken);
                return settled ? IpnOutcome.Accepted : IpnOutcome.AlreadyProcessed;
            case PaymentStatus.Expired:
                // 6. The order follows the reconciliation job (BE-M2-05a).
                return await payments.TryMarkExpiredAsync(transaction.PaymentId, rawBody, cancellationToken)
                    ? IpnOutcome.Accepted
                    : IpnOutcome.AlreadyProcessed;
            default:
                return IpnOutcome.Accepted;
        }
    }

    private async Task<IpnOutcome> HandlePurposeAsync(
        IPaymentPurposeSettlement handler, PaymentTransaction transaction, PaymentStatus gatewayStatus, string rawBody, CancellationToken cancellationToken)
    {
        var wasExpired = transaction.TxnStatus == PaymentStatus.Expired;
        if (gatewayStatus == PaymentStatus.Success && transaction.TxnStatus is PaymentStatus.Pending or PaymentStatus.Expired)
        {
            return await handler.SettleSuccessAsync(transaction, wasExpired, rawBody, cancellationToken)
                ? IpnOutcome.Accepted
                : IpnOutcome.AlreadyProcessed;
        }

        if (transaction.TxnStatus != PaymentStatus.Pending)
        {
            return IpnOutcome.AlreadyProcessed;
        }

        if (gatewayStatus == PaymentStatus.Expired)
        {
            return await payments.TryMarkExpiredAsync(transaction.PaymentId, rawBody, cancellationToken)
                ? IpnOutcome.Accepted
                : IpnOutcome.AlreadyProcessed;
        }

        return IpnOutcome.Accepted;
    }
}
