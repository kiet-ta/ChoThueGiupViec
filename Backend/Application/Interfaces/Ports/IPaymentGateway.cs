using CommonService.Domain.Enums;

namespace CommonService.Application.Interfaces.Ports;

/// <param name="PaymentRef">Caller's reference of what is being paid (order id, extension id, subscription id as text).</param>
/// <param name="Amount">Whole VND (decisions G-2).</param>
public sealed record CreatePaymentRequest(
    PaymentPurpose Purpose,
    string PaymentRef,
    decimal Amount,
    string Description,
    DateTime ExpiresAtUtc);

/// <param name="GatewayTxnRef">Gateway transaction reference; UNIQUE in PAYMENT_TRANSACTION (decisions Q04).</param>
/// <param name="QrPayload">Text to render as a QR code.</param>
public sealed record PaymentQr(string GatewayTxnRef, string QrPayload, string? PayUrl, DateTime ExpiresAtUtc);

/// <param name="IsSignatureValid">False means the IPN is rejected and must change nothing (decisions Q04).</param>
public sealed record IpnVerification(bool IsSignatureValid, string? GatewayTxnRef, decimal Amount, PaymentStatus Status);

/// <param name="Status">Pending, Success or Expired as known by the gateway.</param>
public sealed record GatewayTransactionStatus(string GatewayTxnRef, PaymentStatus Status, decimal Amount);

/// <param name="GatewaySupported">False when the gateway has no refund API: the caller then records the refund in the database only (decisions Q04).</param>
public sealed record GatewayRefundResult(bool Succeeded, bool GatewaySupported, string? Message);

/// <summary>MoMo sandbox adapter (decisions Q04; VietQR is deferred, Q04b). Implemented by Payments (M2).</summary>
public interface IPaymentGateway
{
    Task<PaymentQr> CreateQrAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Check the signature of an IPN callback and extract its content. The payload is the raw key/value pairs of the callback.</summary>
    Task<IpnVerification> VerifyIpnAsync(IReadOnlyDictionary<string, string> payload, CancellationToken cancellationToken = default);

    /// <summary>Ask the gateway for the state of a transaction (reconciliation job, decisions Q04).</summary>
    Task<GatewayTransactionStatus> QueryStatusAsync(string gatewayTxnRef, CancellationToken cancellationToken = default);

    Task<GatewayRefundResult> RefundAsync(string gatewayTxnRef, decimal amount, string reason, CancellationToken cancellationToken = default);
}
