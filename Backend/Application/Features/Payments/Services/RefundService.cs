using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CommonService.Application.Features.Payments.Services;

/// <summary>Refund of an extension payment (BE-M2-08, contract payments.md 3.4: "internal to the module", the port only takes an order id).</summary>
public interface IExtensionRefundService
{
    /// <summary>Refunds the extension's paid transaction (the amount not refunded yet, 100 % when untouched). Never throws for a business refusal.</summary>
    Task<RefundResult> RefundExtensionAsync(int extensionId, string reason, CancellationToken cancellationToken = default);
}

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
    ILogger<RefundService> logger) : IRefundService, IExtensionRefundService
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

        return await RefundCoreAsync(order, transaction, amount, request.Reason, extensionId: null, cancellationToken);
    }

    public async Task<RefundResult> RefundExtensionAsync(int extensionId, string reason, CancellationToken cancellationToken = default)
    {
        var extension = await payments.GetExtensionAsync(extensionId, cancellationToken);
        var transaction = extension is null ? null : await payments.FindRefundableExtensionPaymentAsync(extensionId, cancellationToken);
        var order = extension is null ? null : await payments.GetOrderAsync(extension.OrderId, cancellationToken);
        if (extension is null || transaction is null || order is null)
        {
            return Fail("The extension has no paid transaction to refund.");
        }

        var remaining = transaction.Amount - transaction.RefundedAmount;
        if (remaining <= 0)
        {
            return Fail("The extension is already fully refunded.");
        }

        return await RefundCoreAsync(order, transaction, remaining, reason, extensionId, cancellationToken);
    }

    private async Task<RefundResult> RefundCoreAsync(
        OrderForPayment order, PaymentTransaction transaction, decimal amount, string requestedReason, int? extensionId, CancellationToken cancellationToken)
    {
        var remaining = transaction.Amount - transaction.RefundedAmount;
        if (amount > remaining)
        {
            return Fail($"The refund ({amount:0}) is above the amount not refunded yet ({remaining:0}).");
        }

        var reason = Truncate(string.IsNullOrWhiteSpace(requestedReason) ? "REFUND" : requestedReason.Trim());
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
        await NotifyAsync(order.CustomerId, transaction.PaymentId, order.OrderId, extensionId, cancellationToken);

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

    private async Task NotifyAsync(int customerId, long paymentId, long orderId, int? extensionId, CancellationToken cancellationToken)
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
                        ["extensionId"] = extensionId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
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
