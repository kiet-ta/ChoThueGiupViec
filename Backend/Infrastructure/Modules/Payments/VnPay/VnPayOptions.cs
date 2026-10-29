namespace CommonService.Infrastructure.Modules.Payments.VnPay;

/// <summary>
/// Configuration section <c>VnPay</c>. Nothing is committed: <c>TmnCode</c> and <c>HashSecret</c> come from user-secrets / environment
/// (<c>VnPay__TmnCode</c>, <c>VnPay__HashSecret</c>, <c>VnPay__ReturnUrl</c>; decisions G-6). Sandbox only, no real money (G-1).
/// The IPN URL is not sent with a payment: it is set in the VNPay merchant portal and must point at <c>/api/payments/ipn/vnpay</c>.
/// </summary>
public sealed class VnPayOptions
{
    public const string SectionName = "VnPay";

    /// <summary>The only host this adapter may call (decisions G-1).</summary>
    public const string SandboxHost = "sandbox.vnpayment.vn";

    public string TmnCode { get; set; } = string.Empty;
    public string HashSecret { get; set; } = string.Empty;

    public string PaymentUrl { get; set; } = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";

    /// <summary>The querydr and refund API.</summary>
    public string ApiUrl { get; set; } = "https://sandbox.vnpayment.vn/merchant_webapi/api/transaction";

    /// <summary>Where VNPay sends the customer after paying (display only: it never settles a payment).</summary>
    public string ReturnUrl { get; set; } = string.Empty;

    /// <summary>True when real-looking credentials were supplied: nothing empty and no placeholder.</summary>
    public bool HasCredentials => IsReal(TmnCode) && IsReal(HashSecret) && !string.IsNullOrWhiteSpace(ReturnUrl);

    /// <summary>True when both URLs are https URLs of the VNPay sandbox host.</summary>
    public bool PointsAtSandbox => IsSandbox(PaymentUrl) && IsSandbox(ApiUrl);

    private static bool IsSandbox(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, SandboxHost, StringComparison.OrdinalIgnoreCase);

    private static bool IsReal(string value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase);
}
