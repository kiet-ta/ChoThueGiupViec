namespace CommonService.Application.Features.Payments.Services;

/// <summary>The <c>Payment</c> shape of contract payments.md 1.3. <c>gateway_txn_ref</c> and <c>ipn_payload</c> are never exposed.</summary>
public sealed record PaymentDto(
    long PaymentId,
    string Purpose,
    long? OrderId,
    int? ExtensionId,
    string Gateway,
    decimal Amount,
    string TxnStatus,
    string? QrPayload,
    string? PayUrl,
    DateTime ExpiresAt,
    DateTime? PaidAt,
    DateTime CreatedAt,
    bool Sandbox);

/// <param name="Created">True when a new transaction was created (HTTP 201), false when the existing PENDING one is returned (HTTP 200).</param>
public sealed record PaymentQrResult(PaymentDto Payment, bool Created);

/// <summary>The gateway refused or did not answer; nothing was stored and the client may retry (contract payments.md 2.1, P5). Maps to HTTP 502.</summary>
public sealed class PaymentGatewayUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public interface IPaymentQrService
{
    /// <summary>
    /// POST /api/payments/orders/{orderId}/qr: NotFoundException (order unknown or not the customer's),
    /// BusinessRuleViolationException with Code INVALID_STATE / PAYMENT_EXPIRED (409), PaymentGatewayUnavailableException (502).
    /// </summary>
    Task<PaymentQrResult> CreateOrderQrAsync(int customerId, long orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// POST /api/payments/extensions/{extensionId}/qr (contract payments.md 2.2): NotFoundException (extension unknown or its order is not
    /// the customer's), BusinessRuleViolationException with Code INVALID_STATE (extension not PENDING_PAYMENT) / PAYMENT_EXPIRED,
    /// PaymentGatewayUnavailableException (502).
    /// </summary>
    Task<PaymentQrResult> CreateExtensionQrAsync(int customerId, int extensionId, CancellationToken cancellationToken = default);
}
