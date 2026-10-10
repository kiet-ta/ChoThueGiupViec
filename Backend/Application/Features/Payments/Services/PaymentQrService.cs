using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Payments.Services;

public static class PaymentErrorCodes
{
    public const string InvalidState = "INVALID_STATE";
    public const string PaymentExpired = "PAYMENT_EXPIRED";
}

/// <summary>
/// Creates the payment QR of an order (BE-M2-04, contract payments.md 2.1): Pay-per-Job, 100 % of total_amount, sandbox only (Q04).
/// Idempotent: a PENDING transaction of the order that has not expired is returned instead of creating a second QR.
/// Not here: IPN, reconciliation, order cancellation on expiry (BE-M2-05, 05a).
/// </summary>
public sealed class PaymentQrService(
    IPaymentRepository payments,
    IPaymentGateway gateway,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<BusinessRules> rules) : IPaymentQrService
{
    private readonly BusinessRules _rules = rules.Value;

    public async Task<PaymentQrResult> CreateOrderQrAsync(int customerId, long orderId, CancellationToken cancellationToken = default)
    {
        var order = await payments.GetOrderAsync(orderId, cancellationToken);
        if (order is null || order.CustomerId != customerId)
        {
            throw new NotFoundException("Order", orderId);
        }

        if (order.OrderStatus != JobOrderStatus.PendingPayment)
        {
            throw new BusinessRuleViolationException("The order is not waiting for payment.", PaymentErrorCodes.InvalidState);
        }

        // Not stored (no column): the deadline is deterministic from the order's created_at and the configured expiry (contract P3).
        var expiresAt = order.CreatedAt.AddMinutes(_rules.Payments.QrExpiryMinutes);
        var now = clock.UtcNow;
        if (now >= expiresAt)
        {
            throw new BusinessRuleViolationException("The payment deadline of the order has passed.", PaymentErrorCodes.PaymentExpired);
        }

        var existing = await payments.FindPendingOrderPaymentAsync(orderId, cancellationToken);
        if (existing is not null)
        {
            return new PaymentQrResult(ToDto(existing, expiresAt, payUrl: null), Created: false);
        }

        PaymentQr qr;
        try
        {
            qr = await gateway.CreateQrAsync(
                new CreatePaymentRequest(PaymentPurpose.Order, orderId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    order.TotalAmount, order.OrderCode, expiresAt),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PaymentGatewayUnavailableException("The payment gateway did not create the QR.", ex);
        }

        var transaction = new PaymentTransaction
        {
            GatewayTxnRef = qr.GatewayTxnRef,
            OrderId = orderId,
            Purpose = PaymentPurpose.Order,
            Gateway = GatewayName(),
            Amount = order.TotalAmount,
            TxnStatus = PaymentStatus.Pending,
            QrPayload = qr.QrPayload,
            CreatedAt = now,
        };
        payments.Add(transaction);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new PaymentQrResult(ToDto(transaction, expiresAt, qr.PayUrl), Created: true);
    }

    /// <summary>
    /// PAYMENT_TRANSACTION.gateway is MOMO for the real adapter and FAKE for a Fake (contract 1.3, column VARCHAR(10)).
    /// The port has no name, so a Fake is recognised by its type name until BE-M2-06 brings the real adapter.
    /// </summary>
    private string GatewayName() => gateway.GetType().Name.StartsWith("Fake", StringComparison.Ordinal) ? "FAKE" : "MOMO";

    private static PaymentDto ToDto(PaymentTransaction t, DateTime expiresAt, string? payUrl) => new(
        t.PaymentId, t.Purpose.ToString().ToUpperInvariant(), t.OrderId, t.ExtensionId, t.Gateway, t.Amount,
        t.TxnStatus.ToString().ToUpperInvariant(),
        t.TxnStatus == PaymentStatus.Pending ? t.QrPayload : null,
        t.TxnStatus == PaymentStatus.Pending ? payUrl : null,
        expiresAt, t.PaidAt, t.CreatedAt, Sandbox: true);
}
