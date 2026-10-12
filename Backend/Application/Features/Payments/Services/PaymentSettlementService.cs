using CommonService.Application.Features.Booking;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CommonService.Application.Features.Payments.Services;

/// <summary>
/// The ONE place a payment is settled (contract payments.md 3.2): used by the IPN (BE-M2-05) and by the reconciliation job
/// (BE-M2-05a), so a late IPN after a reconciliation is an idempotent no-op.
/// </summary>
public interface IPaymentSettlementService
{
    /// <summary>
    /// PENDING -> SUCCESS in one transaction (conditional update decides the winner); the order PENDING_PAYMENT -> PAID -> DISPATCHING
    /// in that same transaction (decision Q24 / B4), then OrderPaid and the payment.status push. When the order is no longer waiting
    /// for payment the money is refunded 100 % (P4). False when another caller already changed the transaction.
    /// </summary>
    Task<bool> SettlePaidAsync(long paymentId, long orderId, decimal amount, string rawBody, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same for an EXTENSION transaction (BE-M2-08, contract payments.md 2.4 step 5): PENDING -> SUCCESS and ext_status PAID in one
    /// transaction, then ExtensionPaid and the push. The order is not changed. An extension no longer waiting for payment is refunded (P4).
    /// False when another caller already changed the transaction.
    /// </summary>
    Task<bool> SettleExtensionPaidAsync(long paymentId, int extensionId, string rawBody, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decision Q24 / P4: the gateway reports SUCCESS for a transaction we already EXPIRED. The money was received, so it is recorded
    /// (conditional EXPIRED -> SUCCESS, once) and refunded 100 % with reason PAID_AFTER_EXPIRY; the order or extension is not revived
    /// and no paid event is published. False when another caller already recorded it.
    /// </summary>
    Task<bool> SettleLatePaymentAsync(
        long paymentId, PaymentPurpose purpose, long? orderId, int? extensionId, decimal amount, string rawBody, CancellationToken cancellationToken = default);
}

public sealed class PaymentSettlementService(
    IPaymentRepository payments,
    IUnitOfWork unitOfWork,
    IClock clock,
    IPublisher publisher,
    INotificationService notifications,
    IRefundService refunds,
    IExtensionRefundService extensionRefunds,
    ILogger<PaymentSettlementService> logger) : IPaymentSettlementService
{
    /// <summary>Refund reason of a payment that arrived after its order or extension stopped waiting for it (P4).</summary>
    public const string PaidAfterExpiry = "PAID_AFTER_EXPIRY";

    public async Task<bool> SettlePaidAsync(long paymentId, long orderId, decimal amount, string rawBody, CancellationToken cancellationToken = default)
    {
        var paidAt = clock.UtcNow;
        OrderPaid? orderPaid = null;
        var customerId = 0;
        var refundLate = false;

        var won = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // The loser of a concurrent duplicate gets false here and behaves like an already processed repeat.
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
                // P4: the order stopped waiting (cancelled, expired); the money was received, so it stays SUCCESS and is given back.
                logger.LogWarning(
                    "Payment {PaymentId} succeeded but order {OrderId} is {OrderStatus}, not PENDING_PAYMENT: order unchanged, refunding.",
                    paymentId, orderId, order.OrderStatus);
                refundLate = true;
                return true;
            }

            // B4: PAID is only a moment inside this transaction; Dispatch sees the order already DISPATCHING when OrderPaid arrives.
            order.TransitionTo(JobOrderStatus.Paid);
            order.TransitionTo(JobOrderStatus.Dispatching);
            order.UpdatedAt = paidAt;
            orderPaid = new OrderPaid(
                order.OrderId, order.CustomerId, amount, order.ShiftCode,
                order.ScheduledDate.ToDateTime(TimeOnly.MinValue), order.RequiredWorkers);
            return true;
        }, cancellationToken);

        if (!won)
        {
            return false;
        }

        if (refundLate)
        {
            await RefundLateOrderAsync(paymentId, orderId, amount, cancellationToken);
            return true;
        }

        // After the commit: the event, then the realtime push (best effort, the app polls GET /api/payments/{id} when the socket is down).
        if (orderPaid is not null)
        {
            await publisher.Publish(orderPaid, cancellationToken);
        }

        await NotifyAsync(customerId, paymentId, orderId, cancellationToken);
        return true;
    }

    public async Task<bool> SettleExtensionPaidAsync(long paymentId, int extensionId, string rawBody, CancellationToken cancellationToken = default)
    {
        var paidAt = clock.UtcNow;
        ExtensionPaid? extensionPaid = null;
        long orderId = 0;
        var refundLate = false;

        var won = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await payments.TryMarkSuccessAsync(paymentId, paidAt, rawBody, cancellationToken))
            {
                return false;
            }

            var extension = await payments.GetExtensionForUpdateAsync(extensionId, cancellationToken);
            if (extension is null)
            {
                logger.LogError("Payment {PaymentId} succeeded but extension {ExtensionId} does not exist.", paymentId, extensionId);
                return true;
            }

            orderId = extension.OrderId;
            if (extension.ExtStatus != ExtensionStatuses.PendingPayment)
            {
                // P4: the extension stopped waiting (expired, declined); the money was received, so it stays SUCCESS and is given back.
                logger.LogWarning(
                    "Payment {PaymentId} succeeded but extension {ExtensionId} is {ExtStatus}, not PENDING_PAYMENT: extension unchanged, refunding.",
                    paymentId, extensionId, extension.ExtStatus);
                refundLate = true;
                return true;
            }

            extension.ExtStatus = ExtensionStatuses.Paid;
            extensionPaid = new ExtensionPaid(
                extension.ExtensionId, extension.OrderId, extension.WorkerId, extension.ExtraHours, extension.ExtraAmount, paidAt);
            return true;
        }, cancellationToken);

        if (!won)
        {
            return false;
        }

        if (refundLate)
        {
            await RefundLateExtensionAsync(paymentId, extensionId, cancellationToken);
            return true;
        }

        if (extensionPaid is not null)
        {
            await publisher.Publish(extensionPaid, cancellationToken);
        }

        var order = orderId == 0 ? null : await payments.GetOrderAsync(orderId, cancellationToken);
        await NotifyAsync(order?.CustomerId ?? 0, paymentId, orderId, cancellationToken, extensionId);
        return true;
    }

    public async Task<bool> SettleLatePaymentAsync(
        long paymentId, PaymentPurpose purpose, long? orderId, int? extensionId, decimal amount, string rawBody, CancellationToken cancellationToken = default)
    {
        // Conditional EXPIRED -> SUCCESS: a replay or a concurrent copy records (and refunds) once.
        if (!await payments.TryMarkSuccessFromExpiredAsync(paymentId, clock.UtcNow, rawBody, cancellationToken))
        {
            return false;
        }

        if (purpose == PaymentPurpose.Order && orderId is not null)
        {
            await RefundLateOrderAsync(paymentId, orderId.Value, amount, cancellationToken);
        }
        else if (purpose == PaymentPurpose.Extension && extensionId is not null)
        {
            await RefundLateExtensionAsync(paymentId, extensionId.Value, cancellationToken);
        }

        return true;
    }

    private async Task RefundLateOrderAsync(long paymentId, long orderId, decimal amount, CancellationToken cancellationToken)
    {
        var refund = await refunds.RefundAsync(new RefundRequest(orderId, amount, PaidAfterExpiry), cancellationToken);
        if (!refund.Succeeded)
        {
            logger.LogError(
                "Late payment {PaymentId} of order {OrderId} was recorded but its refund failed: {Message}. It must be refunded by hand until a retry exists.",
                paymentId, orderId, refund.Message);
        }
    }

    private async Task RefundLateExtensionAsync(long paymentId, int extensionId, CancellationToken cancellationToken)
    {
        var refund = await extensionRefunds.RefundExtensionAsync(extensionId, PaidAfterExpiry, cancellationToken);
        if (!refund.Succeeded)
        {
            logger.LogError(
                "Late payment {PaymentId} of extension {ExtensionId} was recorded but its refund failed: {Message}. It must be refunded by hand until a retry exists.",
                paymentId, extensionId, refund.Message);
        }
    }

    private async Task NotifyAsync(int customerId, long paymentId, long orderId, CancellationToken cancellationToken, int? extensionId = null)
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
                        ["extensionId"] = extensionId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
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
