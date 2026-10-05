using System.Security.Cryptography;
using System.Text;

namespace CommonService.Application.Common.Helpers;

/// <summary>
/// Helper utilities to construct consistent idempotency keys for APIs and webhooks.
/// </summary>
public static class IdempotencyHelper
{
    /// <summary>
    /// Builds a namespaced idempotency key from multiple component strings.
    /// E.g. BuildKey("momo:webhook", orderId, transId) -> "momo:webhook:{orderId}:{transId}"
    /// </summary>
    public static string BuildKey(string prefix, params object?[] parts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        if (parts == null || parts.Length == 0)
        {
            return prefix;
        }

        var sb = new StringBuilder(prefix);
        foreach (var part in parts)
        {
            sb.Append(':');
            sb.Append(part?.ToString() ?? "null");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Computes a hex SHA256 hash of a payload string, useful for deduplicating webhook bodies.
    /// </summary>
    public static string ComputePayloadHash(string payload)
    {
        if (payload == null)
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(payload);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
