using CommonService.Application.Common.Options;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Payments.Services;

/// <param name="Settled">Transactions marked SUCCESS (order PAID) from the gateway's answer.</param>
/// <param name="Expired">Transactions marked EXPIRED.</param>
/// <param name="Cancelled">Orders cancelled with PAYMENT_EXPIRED.</param>
/// <param name="Failed">Items that raised an error (logged, the run went on).</param>
public sealed record ReconciliationResult(int Settled, int Expired, int Cancelled, int Failed);

public interface IPaymentReconciliationService
{
    /// <summary>One pass of the reconciliation job (contract payments.md 3.1, 3.3). Safe to repeat.</summary>
    Task<ReconciliationResult> RunOnceAsync(CancellationToken cancellationToken = default);
}

public static class PaymentCancelReasons
{
    public const string PaymentExpired = "PAYMENT_EXPIRED";
}

/// <summary>
/// BE-M2-05a: a customer who paid is never left with an unpaid order (a lost IPN is replaced by asking the gateway), and an order
/// nobody pays is cancelled after <c>Payments.QrExpiryMinutes</c> with no charge. EXTENSION transactions are not touched (BE-M2-08).
/// Open item: the Premium capacity hold is not released here (the port releases by reservation id only; separate Scope exception).
/// </summary>
public sealed class PaymentReconciliationService(
    IPaymentRepository payments,
    IPaymentGateway gateway,
    IPaymentSettlementService settlement,
    IUnitOfWork unitOfWork,
    IClock clock,
    IPublisher publisher,
    IOptions<BusinessRules> rules,
    ILogger<PaymentReconciliationService> logger) : IPaymentReconciliationService
{
    private const int BatchSize = 200;
    private readonly PaymentRules _payments = rules.Value.Payments;

    public async Task<ReconciliationResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        int settled = 0, expired = 0, cancelled = 0, failed = 0;
        var now = clock.UtcNow;

        // 3.1 + 3.3: PENDING transactions old enough to ask the gateway about.
        var pending = await payments.ListPendingOrderPaymentsCreatedBeforeAsync(now.AddMinutes(-_payments.ReconcileAfterMinutes), BatchSize, cancellationToken);
        foreach (var transaction in pending)
        {
            try
            {
                var outcome = await ReconcileTransactionAsync(transaction, now, cancellationToken);
                settled += outcome.Settled;
                expired += outcome.Expired;
                cancelled += outcome.Cancelled;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                logger.LogError(ex, "Reconciliation of payment {PaymentId} failed; continuing.", transaction.PaymentId);
            }
        }

        // 3.3: orders past their deadline that have no live transaction at all.
        var orphans = await payments.ListUnpaidOrderIdsWithoutLivePaymentAsync(now.AddMinutes(-_payments.QrExpiryMinutes), BatchSize, cancellationToken);
        foreach (var orderId in orphans)
        {
            try
            {
                if (await CancelUnpaidOrderAsync(orderId, paymentId: null, now, cancellationToken))
                {
                    cancelled++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                logger.LogError(ex, "Cancelling the unpaid order {OrderId} failed; continuing.", orderId);
            }
        }

        return new ReconciliationResult(settled, expired, cancelled, failed);
    }

    private async Task<(int Settled, int Expired, int Cancelled)> ReconcileTransactionAsync(
        Domain.Entities.PaymentTransaction transaction, DateTime now, CancellationToken cancellationToken)
    {
        var orderId = transaction.OrderId!.Value;
        var status = await gateway.QueryStatusAsync(transaction.GatewayTxnRef, cancellationToken);
        var marker = $"{{\"source\":\"reconciliation\",\"status\":\"{status.Status.ToString().ToUpperInvariant()}\"}}";

        if (status.Status == PaymentStatus.Success)
        {
            if (status.Amount != transaction.Amount)
            {
                logger.LogWarning("Reconciliation: amount mismatch for payment {PaymentId}; not marked paid.", transaction.PaymentId);
                return (0, 0, 0);
            }

            // The same path as the IPN, so a later IPN is a no-op.
            return await settlement.SettlePaidAsync(transaction.PaymentId, orderId, transaction.Amount, marker, cancellationToken)
                ? (1, 0, 0)
                : (0, 0, 0);
        }

        var order = await payments.GetOrderAsync(orderId, cancellationToken);
        var pastDeadline = order is not null && now >= order.CreatedAt.AddMinutes(_payments.QrExpiryMinutes);
        if (pastDeadline)
        {
            if (await CancelUnpaidOrderAsync(orderId, transaction.PaymentId, now, cancellationToken))
            {
                return (0, 1, 1);
            }

            // The order is no longer waiting for payment (changed meanwhile) and the gateway did not get paid: close the transaction anyway.
            return await payments.TryMarkExpiredAsync(transaction.PaymentId, marker, cancellationToken) ? (0, 1, 0) : (0, 0, 0);
        }

        if (status.Status == PaymentStatus.Expired && await payments.TryMarkExpiredAsync(transaction.PaymentId, marker, cancellationToken))
        {
            return (0, 1, 0);
        }

        return (0, 0, 0);
    }

    /// <summary>
    /// Expires the transaction (when there is one) and cancels the still unpaid order in ONE transaction, then publishes OrderCancelled.
    /// False when the order is no longer PENDING_PAYMENT (paid or cancelled meanwhile): nothing is changed.
    /// </summary>
    private async Task<bool> CancelUnpaidOrderAsync(long orderId, long? paymentId, DateTime now, CancellationToken cancellationToken)
    {
        OrderCancelled? cancelled = null;

        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var order = await payments.GetOrderForUpdateAsync(orderId, cancellationToken);
            if (order is null || order.OrderStatus != JobOrderStatus.PendingPayment)
            {
                return false;
            }

            if (paymentId is not null)
            {
                await payments.TryMarkExpiredAsync(paymentId.Value, "{\"source\":\"reconciliation\",\"status\":\"EXPIRED\"}", cancellationToken);
            }

            order.TransitionTo(JobOrderStatus.Cancelled);
            order.CancelReason = PaymentCancelReasons.PaymentExpired;
            order.UpdatedAt = now;
            cancelled = new OrderCancelled(order.OrderId, order.CustomerId, PaymentCancelReasons.PaymentExpired, now);
            return true;
        }, cancellationToken);

        if (cancelled is null)
        {
            return false;
        }

        await publisher.Publish(cancelled, cancellationToken);
        return true;
    }
}
