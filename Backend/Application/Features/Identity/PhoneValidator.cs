using System.Text.RegularExpressions;

namespace CommonService.Application.Features.Identity;

/// <summary>
/// Normalizes and validates Vietnamese mobile phone numbers per decision O4.
/// National format: digits only, 10 characters starting with 0 (03x, 05x, 07x, 08x, 09x).
/// </summary>
public static partial class PhoneValidator
{
    [GeneratedRegex(@"^0[35789]\d{8}$")]
    private static partial Regex VietnamMobileRegex();

    public static (bool IsValid, string NormalizedPhone) TryNormalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return (false, string.Empty);
        }

        var cleaned = input.Trim()
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace(".", string.Empty)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty);

        if (cleaned.StartsWith("+84", StringComparison.Ordinal))
        {
            cleaned = "0" + cleaned[3..];
        }
        else if (cleaned.StartsWith("84", StringComparison.Ordinal) && cleaned.Length == 11)
        {
            cleaned = "0" + cleaned[2..];
        }

        if (VietnamMobileRegex().IsMatch(cleaned))
        {
            return (true, cleaned);
        }

        return (false, string.Empty);
    }
}
