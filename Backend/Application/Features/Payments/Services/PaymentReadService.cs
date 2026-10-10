using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Payments.Services;

/// <summary>The customer's read side of Payments (contract payments.md 2.3): the polling fallback of the realtime <c>payment.status</c> push.</summary>
public interface IPaymentReadService
{
    /// <summary>NotFoundException (404) when the payment is unknown or its order is not the customer's.</summary>
    Task<PaymentDto> GetAsync(int customerId, long paymentId, CancellationToken cancellationToken = default);

    /// <summary>Order and extension transactions of one of the customer's orders, newest first. NotFoundException (404) when the order is not theirs.</summary>
    Task<IReadOnlyList<PaymentDto>> ListForOrderAsync(int customerId, long orderId, CancellationToken cancellationToken = default);
}

public sealed class PaymentReadService(IPaymentRepository payments, IOptions<BusinessRules> rules) : IPaymentReadService
{
    private readonly int _expiryMinutes = rules.Value.Payments.QrExpiryMinutes;

    public async Task<PaymentDto> GetAsync(int customerId, long paymentId, CancellationToken cancellationToken = default)
    {
        var transaction = await payments.GetPaymentAsync(paymentId, cancellationToken)
            ?? throw new NotFoundException("Payment", paymentId);

        // The owner is the customer of the order the transaction (or its extension) belongs to; a subscription payment is M5's.
        long? orderId = transaction.Purpose switch
        {
            PaymentPurpose.Order => transaction.OrderId,
            PaymentPurpose.Extension when transaction.ExtensionId is { } extensionId =>
                (await payments.GetExtensionAsync(extensionId, cancellationToken))?.OrderId,
            _ => null,
        };

        var order = orderId is null ? null : await payments.GetOrderAsync(orderId.Value, cancellationToken);
        if (order is null || order.CustomerId != customerId)
        {
            throw new NotFoundException("Payment", paymentId);
        }

        return ToDto(transaction, order);
    }

    public async Task<IReadOnlyList<PaymentDto>> ListForOrderAsync(int customerId, long orderId, CancellationToken cancellationToken = default)
    {
        var order = await payments.GetOrderAsync(orderId, cancellationToken);
        if (order is null || order.CustomerId != customerId)
        {
            throw new NotFoundException("Order", orderId);
        }

        var transactions = await payments.ListPaymentsOfOrderAsync(orderId, cancellationToken);
        return transactions.Select(t => ToDto(t, order)).ToList();
    }

    /// <summary>
    /// expiresAt is computed, not stored (contract 1.3, P3): the order's created_at + the expiry for an ORDER payment, the
    /// transaction's created_at + the expiry for an EXTENSION one. The pay URL is not stored, so it is only returned when the QR is created.
    /// </summary>
    private PaymentDto ToDto(PaymentTransaction t, OrderForPayment order)
    {
        var expiresAt = (t.Purpose == PaymentPurpose.Order ? order.CreatedAt : t.CreatedAt).AddMinutes(_expiryMinutes);
        return new PaymentDto(
            t.PaymentId, DbEnum.ToDb(t.Purpose), t.OrderId, t.ExtensionId, t.Gateway, t.Amount, DbEnum.ToDb(t.TxnStatus),
            t.TxnStatus == PaymentStatus.Pending ? t.QrPayload : null,
            PayUrl: null,
            expiresAt, t.PaidAt, t.CreatedAt, Sandbox: true);
    }
}
