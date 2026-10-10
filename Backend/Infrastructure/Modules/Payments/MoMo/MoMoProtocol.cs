using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CommonService.Domain.Enums;

namespace CommonService.Infrastructure.Modules.Payments.MoMo;

/// <summary>
/// MoMo's wire rules, read from the official documentation (decisions G-7), not guessed:
/// one-time payment https://developers.momo.vn/v3/docs/payment/api/wallet/onetime ,
/// notification https://developers.momo.vn/v3/docs/payment/api/result-handling/notification ,
/// query https://developers.momo.vn/v3/docs/payment/api/payment-api/query ,
/// refund https://developers.momo.vn/v3/docs/payment/api/payment-api/refund ,
/// result codes https://developers.momo.vn/v3/docs/payment/api/result-handling/resultcode/ .
/// Every signature is HMAC-SHA256 (lowercase hex) of "key=value" pairs joined by '&amp;' with the keys sorted a to z.
/// </summary>
public static class MoMoProtocol
{
    public const string RequestTypeCaptureWallet = "captureWallet";
    public const string Language = "vi";

    /// <summary>MoMo's limits for one payment or refund (VND).</summary>
    public const long MinAmount = 1_000;
    public const long MaxAmount = 50_000_000;

    /// <summary>Result codes the table marks "Final status = Yes" other than 0: the transaction failed for good.</summary>
    private static readonly HashSet<int> FinalFailures =
    [
        98, 99, 1001, 1002, 1003, 1004, 1005, 1006, 1007, 1017, 1026, 1080, 1081, 1088, 2019, 4001, 4002, 4100,
    ];

    /// <summary>0 = paid; a final failure = EXPIRED for us; anything else (1000 initiated, 7000/7002 processing, 9000 authorized, non-final errors) = still PENDING.</summary>
    public static PaymentStatus ToStatus(int resultCode) =>
        resultCode == 0 ? PaymentStatus.Success
        : FinalFailures.Contains(resultCode) ? PaymentStatus.Expired
        : PaymentStatus.Pending;

    public static string CreateRaw(string accessKey, long amount, string extraData, string ipnUrl, string orderId, string orderInfo,
        string partnerCode, string redirectUrl, string requestId, string requestType) =>
        $"accessKey={accessKey}&amount={Num(amount)}&extraData={extraData}&ipnUrl={ipnUrl}&orderId={orderId}&orderInfo={orderInfo}" +
        $"&partnerCode={partnerCode}&redirectUrl={redirectUrl}&requestId={requestId}&requestType={requestType}";

    public static string IpnRaw(string accessKey, string amount, string extraData, string message, string orderId, string orderInfo,
        string orderType, string partnerCode, string payType, string requestId, string responseTime, string resultCode, string transId) =>
        $"accessKey={accessKey}&amount={amount}&extraData={extraData}&message={message}&orderId={orderId}&orderInfo={orderInfo}" +
        $"&orderType={orderType}&partnerCode={partnerCode}&payType={payType}&requestId={requestId}&responseTime={responseTime}" +
        $"&resultCode={resultCode}&transId={transId}";

    public static string QueryRaw(string accessKey, string orderId, string partnerCode, string requestId) =>
        $"accessKey={accessKey}&orderId={orderId}&partnerCode={partnerCode}&requestId={requestId}";

    public static string RefundRaw(string accessKey, long amount, string description, string orderId, string partnerCode, string requestId, long transId) =>
        $"accessKey={accessKey}&amount={Num(amount)}&description={description}&orderId={orderId}&partnerCode={partnerCode}" +
        $"&requestId={requestId}&transId={Num(transId)}";

    /// <summary>HMAC-SHA256 of the raw string keyed by the secret key, as lowercase hex.</summary>
    public static string Sign(string secretKey, string raw)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secretKey), Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Compares two hex signatures without leaking where they differ.</summary>
    public static bool SignaturesMatch(string expectedHex, string? providedHex)
    {
        if (string.IsNullOrEmpty(providedHex))
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(expectedHex.ToLowerInvariant());
        var provided = Encoding.ASCII.GetBytes(providedHex.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    /// <summary>A whole number of VND inside MoMo's limits, or an exception: MoMo's amount is a Long.</summary>
    public static long ToMoMoAmount(decimal amount)
    {
        if (amount != decimal.Truncate(amount))
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "MoMo amounts are whole VND.");
        }

        var value = (long)amount;
        if (value is < MinAmount or > MaxAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, $"MoMo accepts {MinAmount} to {MaxAmount} VND.");
        }

        return value;
    }

    private static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);
}
