namespace CommonService.Infrastructure.Modules.Payments.SePay;

public sealed class SePayOptions
{
    public const string SectionName = "SePay";
    public const string SandboxHost = "userapi-sandbox.sepay.vn";
    public string AccountNumber { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = "https://userapi-sandbox.sepay.vn/v2";
    public string QrBaseUrl { get; set; } = "https://qr.sepay.vn/img";
    public string CodePrefix { get; set; } = "TA";

    public bool HasCredentials => IsReal(AccountNumber) && IsReal(BankName) && IsReal(WebhookSecret) && IsReal(ApiToken);
    public bool PointsAtSandbox => Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.Equals(uri.Host, SandboxHost, StringComparison.OrdinalIgnoreCase);

    private static bool IsReal(string value) => !string.IsNullOrWhiteSpace(value)
        && !value.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase);
}
