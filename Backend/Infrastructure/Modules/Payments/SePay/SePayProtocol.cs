using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CommonService.Infrastructure.Modules.Payments.SePay;

public static class SePayProtocol
{
    public const int TimestampWindowSeconds = 300;

    public static string Signature(string secret, string timestamp, string rawBody)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{rawBody}"));
        return "sha256=" + Convert.ToHexStringLower(hash);
    }

    public static bool SignaturesMatch(string expected, string? actual)
    {
        if (actual is null) return false;
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    public static string NewCode(string prefix)
    {
        var digits = new char[10];
        for (var i = 0; i < digits.Length; i++) digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        return prefix + new string(digits);
    }

    public static string QrUrl(SePayOptions options, decimal amount, string code)
    {
        if (amount < 1000m || amount > 499000000m || decimal.Truncate(amount) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount));
        return $"{options.QrBaseUrl}?acc={Uri.EscapeDataString(options.AccountNumber)}&bank={Uri.EscapeDataString(options.BankName)}&amount={decimal.ToInt64(amount).ToString(CultureInfo.InvariantCulture)}&des={Uri.EscapeDataString(code)}&template=compact";
    }
}
