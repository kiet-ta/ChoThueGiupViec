namespace CommonService.Infrastructure.Modules.Payments.MoMo;

/// <summary>
/// The existing configuration section <c>MoMo</c> (appsettings.json holds placeholders only; real sandbox values come from
/// user-secrets / environment, decisions G-6). Sandbox only, no real money (G-1).
/// </summary>
public sealed class MoMoOptions
{
    public const string SectionName = "MoMo";

    /// <summary>The only host this adapter may call (decisions G-1: "Payments use the MoMo sandbox only").</summary>
    public const string SandboxHost = "test-payment.momo.vn";

    public string PartnerCode { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>The full URL of the create API, e.g. <c>https://test-payment.momo.vn/v2/gateway/api/create</c>; query and refund are its siblings.</summary>
    public string Endpoint { get; set; } = string.Empty;

    public string IpnUrl { get; set; } = string.Empty;
    public string RedirectUrl { get; set; } = string.Empty;

    /// <summary>True when real-looking credentials were supplied: nothing empty and none of the committed placeholders.</summary>
    public bool HasCredentials =>
        IsReal(PartnerCode) && IsReal(AccessKey) && IsReal(SecretKey)
        && !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(IpnUrl) && !string.IsNullOrWhiteSpace(RedirectUrl);

    /// <summary>True when <see cref="Endpoint"/> is an https URL of the MoMo sandbox host.</summary>
    public bool PointsAtSandbox =>
        Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, SandboxHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>The URL of a sibling API of the create endpoint: <c>.../api/create</c> -> <c>.../api/query</c> or <c>.../api/refund</c>.</summary>
    public string UrlFor(string operation)
    {
        var trimmed = Endpoint.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? trimmed : $"{trimmed[..slash]}/{operation}";
    }

    private static bool IsReal(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase)
        && !value.StartsWith("MOMO_SANDBOX_", StringComparison.Ordinal);
}
