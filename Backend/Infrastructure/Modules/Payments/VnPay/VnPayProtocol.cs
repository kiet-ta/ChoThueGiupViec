using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using CommonService.Domain.Enums;

namespace CommonService.Infrastructure.Modules.Payments.VnPay;

/// <summary>
/// VNPay's wire rules, read from the official documentation (decisions G-7), not guessed:
/// pay https://sandbox.vnpayment.vn/apis/docs/thanh-toan-pay/pay.html ,
/// querydr and refund https://sandbox.vnpayment.vn/apis/docs/truy-van-hoan-tien/querydr&amp;refund.html .
/// The pay URL and the IPN are signed over the sorted, URL-encoded <c>name=value</c> pairs; the querydr and refund bodies over their
/// fields joined by '|'. Every signature is HMAC-SHA512 (lowercase hex).
/// </summary>
public static class VnPayProtocol
{
    public const string Version = "2.1.0";
    public const string CommandPay = "pay";
    public const string CommandQuery = "querydr";
    public const string CommandRefund = "refund";
    public const string Currency = "VND";
    public const string Locale = "vn";
    public const string OrderType = "other";

    /// <summary>The port carries no customer IP; the sandbox only needs a well-formed one (7 to 45 characters).</summary>
    public const string IpAddress = "127.0.0.1";

    public const string SecureHash = "vnp_SecureHash";
    private const string SecureHashType = "vnp_SecureHashType";

    /// <summary>The amount field is the VND amount multiplied by 100; one payment is below 1,000,000,000 VND (field length 12).</summary>
    public const long MaxAmount = 999_999_999;

    public static string Sign(string hashSecret, string data) =>
        Convert.ToHexStringLower(HMACSHA512.HashData(Encoding.UTF8.GetBytes(hashSecret), Encoding.UTF8.GetBytes(data)));

    /// <summary>Compares two hex signatures without leaking where they differ.</summary>
    public static bool SignaturesMatch(string expectedHex, string? providedHex)
    {
        if (string.IsNullOrWhiteSpace(providedHex))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedHex.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(providedHex.Trim().ToLowerInvariant()));
    }

    /// <summary>
    /// The string that is signed (and, with the hash appended, sent): <c>vnp_</c> fields only, without the hash fields and empty values,
    /// sorted by name (ordinal), each name and value URL-encoded.
    /// </summary>
    public static string SignedQuery(IEnumerable<KeyValuePair<string, string>> fields) =>
        string.Join('&', fields
            .Where(f => f.Key.StartsWith("vnp_", StringComparison.Ordinal)
                && f.Key != SecureHash && f.Key != SecureHashType && !string.IsNullOrEmpty(f.Value))
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => $"{WebUtility.UrlEncode(f.Key)}={WebUtility.UrlEncode(f.Value)}"));

    /// <summary>A whole number of VND the API can carry, as the wire value (multiplied by 100), or an exception.</summary>
    public static long ToWireAmount(decimal amount)
    {
        if (amount != decimal.Truncate(amount))
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "VNPay amounts are whole VND.");
        }

        if (amount is < 1 or > MaxAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, $"VNPay accepts 1 to {MaxAmount} VND.");
        }

        return (long)amount * 100;
    }

    /// <summary>The wire amount back to VND; null when it is not a number of whole VND.</summary>
    public static decimal? FromWireAmount(string? wire) =>
        long.TryParse(wire, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value % 100 == 0 ? value / 100 : null;

    /// <summary>VNPay dates are <c>yyyyMMddHHmmss</c> in GMT+7.</summary>
    public static string FormatDate(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(7).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    /// <summary>The order text: unaccented Vietnamese without special characters, 1 to 255 characters.</summary>
    public static string OrderInfo(string description)
    {
        var decomposed = (description ?? string.Empty).Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var text = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            text.Append(char.IsAsciiLetterOrDigit(c) || c == ' ' ? c : ' ');
        }

        var clean = string.Join(' ', text.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return clean.Length == 0 ? "Thanh toan" : clean.Length > 255 ? clean[..255] : clean;
    }

    /// <summary>
    /// 00 / 00 = paid. Transaction status 02 = failed for good = EXPIRED for us. Anything else (01 not complete, a customer who left
    /// the page, a refund state) stays PENDING and the QR expiry decides, so a mis-click never cancels an order.
    /// </summary>
    public static PaymentStatus ToStatus(string? responseCode, string? transactionStatus) =>
        transactionStatus == "00" && responseCode == "00" ? PaymentStatus.Success
        : transactionStatus == "02" ? PaymentStatus.Expired
        : PaymentStatus.Pending;
}
