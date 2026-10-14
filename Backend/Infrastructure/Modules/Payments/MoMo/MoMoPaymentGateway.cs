using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;

namespace CommonService.Infrastructure.Modules.Payments.MoMo;

/// <summary>
/// MoMo SANDBOX adapter behind <see cref="IPaymentGateway"/> (BE-M2-06, decisions Q04, G-1, G-7). Field names, signatures and result
/// codes are those of <see cref="MoMoProtocol"/> (official documentation). The gateway reference we store (gateway_txn_ref) is the
/// MoMo <c>orderId</c> generated here. Written without sandbox keys: it has not been run against MoMo.
/// </summary>
public sealed class MoMoPaymentGateway(HttpClient http, MoMoOptions options) : IPaymentGateway
{
    public async Task<PaymentQr> CreateQrAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var amount = MoMoProtocol.ToMoMoAmount(request.Amount);
        var orderId = NewOrderId(request.Purpose, request.PaymentRef);
        var requestId = NewRequestId();
        const string extraData = "";
        var signature = MoMoProtocol.Sign(options.SecretKey, MoMoProtocol.CreateRaw(
            options.AccessKey, amount, extraData, options.IpnUrl, orderId, request.Description, options.PartnerCode,
            options.RedirectUrl, requestId, MoMoProtocol.RequestTypeCaptureWallet));

        var body = new Dictionary<string, object>
        {
            ["partnerCode"] = options.PartnerCode,
            ["requestId"] = requestId,
            ["amount"] = amount,
            ["orderId"] = orderId,
            ["orderInfo"] = request.Description,
            ["redirectUrl"] = options.RedirectUrl,
            ["ipnUrl"] = options.IpnUrl,
            ["requestType"] = MoMoProtocol.RequestTypeCaptureWallet,
            ["extraData"] = extraData,
            ["lang"] = MoMoProtocol.Language,
            ["signature"] = signature,
        };

        using var response = await PostAsync(options.Endpoint, body, cancellationToken);
        var resultCode = ResultCode(response);
        if (resultCode != 0)
        {
            throw new HttpRequestException($"MoMo refused to create the payment (resultCode {resultCode}).");
        }

        var payUrl = Text(response, "payUrl");
        var qrPayload = Text(response, "qrCodeUrl") ?? payUrl
            ?? throw new HttpRequestException("MoMo answered without payUrl or qrCodeUrl.");
        return new PaymentQr(orderId, qrPayload, payUrl, request.ExpiresAtUtc);
    }

    public Task<IpnVerification> VerifyIpnAsync(IReadOnlyDictionary<string, string> payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var invalid = Task.FromResult(new IpnVerification(false, null, 0m, PaymentStatus.Pending));

        string? Field(string name) => payload.TryGetValue(name, out var value) ? value : null;

        // Every field of the documented IPN signature must be present (an empty string is a value; a missing key is not).
        string[] names = ["amount", "extraData", "message", "orderId", "orderInfo", "orderType", "partnerCode", "payType", "requestId", "responseTime", "resultCode", "transId"];
        if (names.Any(n => Field(n) is null) || Field("partnerCode") != options.PartnerCode)
        {
            return invalid;
        }

        var expected = MoMoProtocol.Sign(options.SecretKey, MoMoProtocol.IpnRaw(
            options.AccessKey, Field("amount")!, Field("extraData")!, Field("message")!, Field("orderId")!, Field("orderInfo")!,
            Field("orderType")!, Field("partnerCode")!, Field("payType")!, Field("requestId")!, Field("responseTime")!,
            Field("resultCode")!, Field("transId")!));
        if (!MoMoProtocol.SignaturesMatch(expected, Field("signature")))
        {
            return invalid;
        }

        if (!long.TryParse(Field("amount"), NumberStyles.None, CultureInfo.InvariantCulture, out var amount)
            || !int.TryParse(Field("resultCode"), NumberStyles.None, CultureInfo.InvariantCulture, out var resultCode))
        {
            return invalid;
        }

        return Task.FromResult(new IpnVerification(true, Field("orderId"), amount, MoMoProtocol.ToStatus(resultCode)));
    }

    public async Task<GatewayTransactionStatus> QueryStatusAsync(string gatewayTxnRef, CancellationToken cancellationToken = default)
    {
        using var response = await QueryAsync(gatewayTxnRef, cancellationToken);
        return new GatewayTransactionStatus(gatewayTxnRef, MoMoProtocol.ToStatus(ResultCode(response)), Number(response, "amount") ?? 0m);
    }

    public async Task<GatewayRefundResult> RefundAsync(string gatewayTxnRef, decimal amount, string reason, CancellationToken cancellationToken = default)
    {
        long refundAmount;
        try
        {
            refundAmount = MoMoProtocol.ToMoMoAmount(amount);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return new GatewayRefundResult(false, true, ex.Message);
        }

        // MoMo refunds by the transId of the original payment, which the port does not carry: ask for it first.
        long transId;
        using (var original = await QueryAsync(gatewayTxnRef, cancellationToken))
        {
            var found = Number(original, "transId");
            if (ResultCode(original) != 0 || found is null or <= 0m)
            {
                return new GatewayRefundResult(false, true, "MoMo does not report a successful payment for this transaction.");
            }

            transId = (long)found.Value;
        }

        var orderId = $"RF{Guid.NewGuid():N}";
        var requestId = NewRequestId();
        var description = reason ?? string.Empty;
        var signature = MoMoProtocol.Sign(options.SecretKey, MoMoProtocol.RefundRaw(
            options.AccessKey, refundAmount, description, orderId, options.PartnerCode, requestId, transId));

        var body = new Dictionary<string, object>
        {
            ["partnerCode"] = options.PartnerCode,
            ["orderId"] = orderId,
            ["requestId"] = requestId,
            ["amount"] = refundAmount,
            ["transId"] = transId,
            ["lang"] = MoMoProtocol.Language,
            ["description"] = description,
            ["signature"] = signature,
        };

        using var response = await PostAsync(options.UrlFor("refund"), body, cancellationToken);
        var resultCode = ResultCode(response);
        return resultCode == 0
            ? new GatewayRefundResult(true, true, null)
            : new GatewayRefundResult(false, true, $"MoMo refused the refund (resultCode {resultCode}).");
    }

    private Task<JsonDocument> QueryAsync(string orderId, CancellationToken cancellationToken)
    {
        var requestId = NewRequestId();
        var body = new Dictionary<string, object>
        {
            ["partnerCode"] = options.PartnerCode,
            ["requestId"] = requestId,
            ["orderId"] = orderId,
            ["lang"] = MoMoProtocol.Language,
            ["signature"] = MoMoProtocol.Sign(options.SecretKey, MoMoProtocol.QueryRaw(options.AccessKey, orderId, options.PartnerCode, requestId)),
        };
        return PostAsync(options.UrlFor("query"), body, cancellationToken);
    }

    /// <summary>POSTs JSON and returns the JSON answer. MoMo reports business errors in <c>resultCode</c>; a body that is not a JSON object is a transport failure.</summary>
    private async Task<JsonDocument> PostAsync(string url, Dictionary<string, object> body, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(url, body, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            // Not JSON: handled below.
        }

        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("resultCode", out _))
        {
            document?.Dispose();
            throw new HttpRequestException($"MoMo answered HTTP {(int)response.StatusCode} without a result code.");
        }

        return document;
    }

    private static int ResultCode(JsonDocument document) =>
        document.RootElement.GetProperty("resultCode") is { ValueKind: JsonValueKind.Number } code && code.TryGetInt32(out var value)
            ? value
            : throw new HttpRequestException("MoMo answered with a result code that is not a number.");

    private static string? Text(JsonDocument document, string name) =>
        document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static decimal? Number(JsonDocument document, string name) =>
        document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number
            : null;

    /// <summary>Unique, ASCII, at most 64 characters (gateway_txn_ref is VARCHAR(64)): "GV" + O/E/S + the caller's reference + "-" + 32 hex.</summary>
    private static string NewOrderId(PaymentPurpose purpose, string paymentRef)
    {
        var letter = purpose switch { PaymentPurpose.Order => 'O', PaymentPurpose.Extension => 'E', _ => 'S' };
        var reference = new string(paymentRef.Where(char.IsAsciiLetterOrDigit).Take(24).ToArray());
        return $"GV{letter}{reference}-{Guid.NewGuid():N}";
    }

    private static string NewRequestId() => Guid.NewGuid().ToString("N");
}
