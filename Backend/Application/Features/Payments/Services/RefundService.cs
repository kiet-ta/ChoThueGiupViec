using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CommonService.Application.Features.Payments.Services;

/// <summary>
/// Real <see cref="IRefundService"/> (BE-M2-07, contract payments.md 3.4, P2). Customer money is protected first: the refund is
/// RESERVED in the database with one conditional update BEFORE the gateway is called, so concurrent refunds can never exceed the
/// paid amount; if the gateway refuses, the reservation is reverted exactly and nothing stays recorded.
/// A gateway without a refund API is recorded in the database only (decisions Q04).
/// </summary>
public sealed class RefundService(
    IPaymentRepository payments,
    IPaymentGateway gateway,
    IUnitOfWork unitOfWork,
    IClock clock,
    IPublisher publisher,
    INotificationService notifications,
    ILogger<RefundService> logger) : IRefundService
{
    internal const int MaxReasonLength = 255;

    public async Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var amount = Vnd.Round(request.Amount);
        if (amount <= 0)
        {
            return Fail("The refund amount must be greater than 0.");
        }

        var order = await payments.GetOrderAsync(request.OrderId, cancellationToken);
        var transaction = order is null ? null : await payments.FindRefundableOrderPaymentAsync(request.OrderId, cancellationToken);
        if (order is null || transaction is null)
        {
            return Fail("The order has no paid transaction to refund.");
        }

        var remaining = transaction.Amount - transaction.RefundedAmount;
        if (amount > remaining)
        {
            return Fail($"The refund ({amount:0}) is above the amount not refunded yet ({remaining:0}).");
        }

        var reason = Truncate(string.IsNullOrWhiteSpace(request.Reason) ? "REFUND" : request.Reason.Trim());
        var refundedAt = clock.UtcNow;

        // 1. Reserve in the database (conditional, atomic).
        if (!await payments.TryReserveRefundAsync(transaction.PaymentId, amount, reason, refundedAt, cancellationToken))
        {
            return Fail("The refund could not be reserved (the transaction changed or the amount is no longer available).");
        }

        // 2. Ask the gateway. A refusal or an error reverts the reservation.
        var dbOnly = false;
        try
        {
            var result = await gateway.RefundAsync(transaction.GatewayTxnRef, amount, reason, cancellationToken);
            if (!result.Succeeded && result.GatewaySupported)
            {
                await RevertAsync(transaction.PaymentId, amount);
                return Fail(result.Message ?? "The payment gateway refused the refund.");
            }

            dbOnly = !result.GatewaySupported;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Gateway refund of payment {PaymentId} failed; the reservation is reverted.", transaction.PaymentId);
            await RevertAsync(transaction.PaymentId, amount);
            return Fail("The payment gateway did not answer the refund.");
        }

        // 3. The money moved (or was recorded): tell the others. A failing listener never turns a done refund into a failure.
        await PublishAsync(new OrderRefunded(order.OrderId, order.CustomerId, amount, reason, refundedAt), cancellationToken);
        await NotifyAsync(order.CustomerId, transaction.PaymentId, order.OrderId, cancellationToken);

        return new RefundResult(
            true,
            amount,
            dbOnly ? "Refund recorded in the database only (the gateway has no refund API)." : "Refund done through the payment gateway.");
    }

    private static RefundResult Fail(string message) => new(false, 0m, message);

    private static string Truncate(string value) => value.Length <= MaxReasonLength ? value : value[..MaxReasonLength];

    private async Task RevertAsync(long paymentId, decimal amount) =>
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await payments.RevertRefundAsync(paymentId, amount, CancellationToken.None);
            return true;
        }, CancellationToken.None);

    private async Task PublishAsync(OrderRefunded refunded, CancellationToken cancellationToken)
    {
        try
        {
            await publisher.Publish(refunded, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "OrderRefunded listener failed for order {OrderId}; the refund itself is recorded.", refunded.OrderId);
        }
    }

    private async Task NotifyAsync(int customerId, long paymentId, long orderId, CancellationToken cancellationToken)
    {
        try
        {
            await notifications.SendAsync(
                new NotificationMessage(
                    UserRole.Customer, customerId, "payment.status", "Refund issued", "A refund was issued for your order.",
                    new Dictionary<string, string>
                    {
                        ["paymentId"] = paymentId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["orderId"] = orderId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["txnStatus"] = PaymentStatus.Refunded.ToString().ToUpperInvariant(),
                    }),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "payment.status notification failed for payment {PaymentId}.", paymentId);
        }
    }
}
