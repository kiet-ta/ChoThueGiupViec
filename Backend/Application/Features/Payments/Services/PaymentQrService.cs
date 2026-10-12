using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Booking;
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
/// Creates the payment QR of an order (BE-M2-04, contract payments.md 2.1) or of an extension (BE-M2-08, 2.2): Pay-per-Job,
/// 100 % of the amount, sandbox only (Q04). Idempotent: a PENDING transaction that has not expired is returned instead of
/// creating a second QR. Not here: IPN, reconciliation, expiry handling (BE-M2-05, 05a).
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

        var request = new CreatePaymentRequest(
            PaymentPurpose.Order, orderId.ToString(System.Globalization.CultureInfo.InvariantCulture), order.TotalAmount, order.OrderCode, expiresAt);
        var transaction = new PaymentTransaction { OrderId = orderId, Purpose = PaymentPurpose.Order, Amount = order.TotalAmount };
        var payUrl = await RequestQrAsync(request, transaction, now, cancellationToken);

        return new PaymentQrResult(ToDto(transaction, expiresAt, payUrl), Created: true);
    }

    public async Task<PaymentQrResult> CreateExtensionQrAsync(int customerId, int extensionId, CancellationToken cancellationToken = default)
    {
        var extension = await payments.GetExtensionAsync(extensionId, cancellationToken);
        if (extension is null || extension.CustomerId != customerId)
        {
            throw new NotFoundException("Extension", extensionId);
        }

        if (extension.ExtStatus != ExtensionStatuses.PendingPayment)
        {
            throw new BusinessRuleViolationException("The extension is not waiting for payment.", PaymentErrorCodes.InvalidState);
        }

        var now = clock.UtcNow;
        var window = TimeSpan.FromMinutes(_rules.Payments.QrExpiryMinutes);

        // An extension has no deadline of its own (contract 1.3): its QR lives Payments.QrExpiryMinutes from the transaction's creation.
        var existing = await payments.FindPendingExtensionPaymentAsync(extensionId, cancellationToken);
        if (existing is not null)
        {
            var existingExpiresAt = existing.CreatedAt + window;
            if (now >= existingExpiresAt)
            {
                throw new BusinessRuleViolationException("The payment deadline of the extension has passed.", PaymentErrorCodes.PaymentExpired);
            }

            return new PaymentQrResult(ToDto(existing, existingExpiresAt, payUrl: null), Created: false);
        }

        var expiresAt = now + window;
        var request = new CreatePaymentRequest(
            PaymentPurpose.Extension, extensionId.ToString(System.Globalization.CultureInfo.InvariantCulture), extension.ExtraAmount,
            $"{extension.OrderCode} extension", expiresAt);
        var transaction = new PaymentTransaction { OrderId = null, ExtensionId = extensionId, Purpose = PaymentPurpose.Extension, Amount = extension.ExtraAmount };
        var payUrl = await RequestQrAsync(request, transaction, now, cancellationToken);

        return new PaymentQrResult(ToDto(transaction, expiresAt, payUrl), Created: true);
    }

    /// <summary>Asks the gateway for the QR (a failure stores nothing) and inserts the PENDING transaction; returns the pay URL (not stored).</summary>
    private async Task<string?> RequestQrAsync(CreatePaymentRequest request, PaymentTransaction transaction, DateTime now, CancellationToken cancellationToken)
    {
        PaymentQr qr;
        try
        {
            qr = await gateway.CreateQrAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PaymentGatewayUnavailableException("The payment gateway did not create the QR.", ex);
        }

        transaction.GatewayTxnRef = qr.GatewayTxnRef;
        transaction.Gateway = GatewayName();
        transaction.TxnStatus = PaymentStatus.Pending;
        transaction.QrPayload = qr.QrPayload;
        transaction.CreatedAt = now;
        payments.Add(transaction);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return qr.PayUrl;
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
