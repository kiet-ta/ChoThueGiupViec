using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CommonService.Application.Features.Payments.Services;

/// <summary>
/// Gateway callback of a payment (BE-M2-05, contract payments.md 2.4). Every rejection changes nothing; a repeat is a no-op;
/// the PENDING -> SUCCESS update is conditional so concurrent identical IPNs produce one SUCCESS and one <see cref="OrderPaid"/>.
/// Logs never contain the payload or any signature.
/// </summary>
public sealed class IpnService(
    IPaymentGateway gateway,
    IPaymentRepository payments,
    IUnitOfWork unitOfWork,
    IClock clock,
    IPublisher publisher,
    INotificationService notifications,
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
                return await MarkPaidAsync(transaction.PaymentId, transaction.OrderId.Value, transaction.Amount, rawBody, cancellationToken);
            case PaymentStatus.Expired:
                // 6. The order follows the reconciliation job (BE-M2-05a).
                return await payments.TryMarkExpiredAsync(transaction.PaymentId, rawBody, cancellationToken)
                    ? IpnOutcome.Accepted
                    : IpnOutcome.AlreadyProcessed;
            default:
                return IpnOutcome.Accepted;
        }
    }

    /// <summary>Step 5 + 7.</summary>
    private async Task<IpnOutcome> MarkPaidAsync(long paymentId, long orderId, decimal amount, string rawBody, CancellationToken cancellationToken)
    {
        var paidAt = clock.UtcNow;
        OrderPaid? orderPaid = null;
        int customerId = 0;

        var won = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // The loser of a concurrent duplicate gets false here and behaves like step 4.
            if (!await payments.TryMarkSuccessAsync(paymentId, paidAt, rawBody, cancellationToken))
            {
                return false;
            }

            var order = await payments.GetOrderForUpdateAsync(orderId, cancellationToken);
            if (order is null)
            {
                logger.LogError("Payment {PaymentId} succeeded but order {OrderId} does not exist.", paymentId, orderId);
                return true;
            }

            customerId = order.CustomerId;
            if (order.OrderStatus != JobOrderStatus.PendingPayment)
            {
                // P4 (no leader decision): the money was received, so SUCCESS stays recorded; the order is left alone and refunding is a later ticket.
                logger.LogWarning(
                    "Payment {PaymentId} succeeded but order {OrderId} is {OrderStatus}, not PENDING_PAYMENT: order unchanged, no OrderPaid.",
                    paymentId, orderId, order.OrderStatus);
                return true;
            }

            order.TransitionTo(JobOrderStatus.Paid);
            order.UpdatedAt = paidAt;
            orderPaid = new OrderPaid(
                order.OrderId, order.CustomerId, amount, order.ShiftCode,
                order.ScheduledDate.ToDateTime(TimeOnly.MinValue), order.RequiredWorkers);
            return true;
        }, cancellationToken);

        if (!won)
        {
            return IpnOutcome.AlreadyProcessed;
        }

        // After the commit: the event, then the realtime push (best effort, the app polls GET /api/payments/{id} when the socket is down).
        if (orderPaid is not null)
        {
            await publisher.Publish(orderPaid, cancellationToken);
        }

        await NotifyAsync(customerId, paymentId, orderId, cancellationToken);
        return IpnOutcome.Accepted;
    }

    private async Task NotifyAsync(int customerId, long paymentId, long orderId, CancellationToken cancellationToken)
    {
        if (customerId == 0)
        {
            return;
        }

        try
        {
            await notifications.SendAsync(
                new NotificationMessage(
                    UserRole.Customer, customerId, "payment.status", "Payment received", "Your payment was received.",
                    new Dictionary<string, string>
                    {
                        ["paymentId"] = paymentId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["orderId"] = orderId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["txnStatus"] = PaymentStatus.Success.ToString().ToUpperInvariant(),
                    }),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "payment.status notification failed for payment {PaymentId}.", paymentId);
        }
    }
}
