using CommonService.Application.Features.Booking;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CommonService.Application.Features.Payments.Services;

/// <summary>
/// The ONE place an order becomes PAID (contract payments.md 3.2): used by the IPN (BE-M2-05) and by the reconciliation job
/// (BE-M2-05a), so a late IPN after a reconciliation is an idempotent no-op.
/// </summary>
public interface IPaymentSettlementService
{
    /// <summary>
    /// PENDING -> SUCCESS in one transaction (conditional update decides the winner), the order PENDING_PAYMENT -> PAID,
    /// then OrderPaid and the payment.status push. False when another caller already changed the transaction.
    /// </summary>
    Task<bool> SettlePaidAsync(long paymentId, long orderId, decimal amount, string rawBody, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same for an EXTENSION transaction (BE-M2-08, contract payments.md 2.4 step 5): PENDING -> SUCCESS and ext_status PAID in one
    /// transaction, then ExtensionPaid and the push. The order is not changed. False when another caller already changed the transaction.
    /// </summary>
    Task<bool> SettleExtensionPaidAsync(long paymentId, int extensionId, string rawBody, CancellationToken cancellationToken = default);
}

public sealed class PaymentSettlementService(
    IPaymentRepository payments,
    IUnitOfWork unitOfWork,
    IClock clock,
    IPublisher publisher,
    INotificationService notifications,
    ILogger<PaymentSettlementService> logger) : IPaymentSettlementService
{
    public async Task<bool> SettlePaidAsync(long paymentId, long orderId, decimal amount, string rawBody, CancellationToken cancellationToken = default)
    {
        var paidAt = clock.UtcNow;
        OrderPaid? orderPaid = null;
        var customerId = 0;

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
            return false;
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
                // No leader decision for a late payment: the money was received, so SUCCESS stays recorded and the extension is left alone.
                logger.LogWarning(
                    "Payment {PaymentId} succeeded but extension {ExtensionId} is {ExtStatus}, not PENDING_PAYMENT: extension unchanged, no ExtensionPaid.",
                    paymentId, extensionId, extension.ExtStatus);
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

        if (extensionPaid is not null)
        {
            await publisher.Publish(extensionPaid, cancellationToken);
        }

        var order = orderId == 0 ? null : await payments.GetOrderAsync(orderId, cancellationToken);
        await NotifyAsync(order?.CustomerId ?? 0, paymentId, orderId, cancellationToken, extensionId);
        return true;
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
