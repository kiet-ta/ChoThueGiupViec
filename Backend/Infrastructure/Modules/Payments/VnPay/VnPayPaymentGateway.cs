using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;

namespace CommonService.Infrastructure.Modules.Payments.VnPay;

/// <summary>
/// VNPay SANDBOX adapter behind <see cref="IPaymentGateway"/> (decisions G-1, G-7). Field names, signatures and codes are those of
/// <see cref="VnPayProtocol"/> (official documentation). The reference we store (gateway_txn_ref) is the VNPay <c>vnp_TxnRef</c> generated
/// here; it carries the creation date, which querydr and refund need and the port does not. Written without sandbox keys: it has not
/// been run against VNPay.
/// </summary>
public sealed class VnPayPaymentGateway(HttpClient http, VnPayOptions options) : IPaymentGateway
{
    public Task<PaymentQr> CreateQrAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTime.UtcNow;
        var txnRef = NewTxnRef(request.Purpose, request.PaymentRef, now);
        var fields = new Dictionary<string, string>
        {
            ["vnp_Version"] = VnPayProtocol.Version,
            ["vnp_Command"] = VnPayProtocol.CommandPay,
            ["vnp_TmnCode"] = options.TmnCode,
            ["vnp_Amount"] = VnPayProtocol.ToWireAmount(request.Amount).ToString(CultureInfo.InvariantCulture),
            ["vnp_CreateDate"] = VnPayProtocol.FormatDate(now),
            ["vnp_CurrCode"] = VnPayProtocol.Currency,
            ["vnp_IpAddr"] = VnPayProtocol.IpAddress,
            ["vnp_Locale"] = VnPayProtocol.Locale,
            ["vnp_OrderInfo"] = VnPayProtocol.OrderInfo(request.Description),
            ["vnp_OrderType"] = VnPayProtocol.OrderType,
            ["vnp_ReturnUrl"] = options.ReturnUrl,
            ["vnp_ExpireDate"] = VnPayProtocol.FormatDate(request.ExpiresAtUtc),
            ["vnp_TxnRef"] = txnRef,
        };

        var signed = VnPayProtocol.SignedQuery(fields);
        var url = $"{options.PaymentUrl}?{signed}&{VnPayProtocol.SecureHash}={VnPayProtocol.Sign(options.HashSecret, signed)}";
        return Task.FromResult(new PaymentQr(txnRef, url, url, request.ExpiresAtUtc));
    }

    public Task<IpnVerification> VerifyIpnAsync(IReadOnlyDictionary<string, string> payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var invalid = Task.FromResult(new IpnVerification(false, null, 0m, PaymentStatus.Pending));

        string? Field(string name) => payload.TryGetValue(name, out var value) ? value : null;

        var expected = VnPayProtocol.Sign(options.HashSecret, VnPayProtocol.SignedQuery(payload));
        if (Field("vnp_TmnCode") != options.TmnCode
            || string.IsNullOrEmpty(Field("vnp_TxnRef"))
            || !VnPayProtocol.SignaturesMatch(expected, Field(VnPayProtocol.SecureHash)))
        {
            return invalid;
        }

        var amount = VnPayProtocol.FromWireAmount(Field("vnp_Amount"));
        if (amount is null)
        {
            return invalid;
        }

        return Task.FromResult(new IpnVerification(
            true, Field("vnp_TxnRef"), amount.Value, VnPayProtocol.ToStatus(Field("vnp_ResponseCode"), Field("vnp_TransactionStatus"))));
    }

    public async Task<GatewayTransactionStatus> QueryStatusAsync(string gatewayTxnRef, CancellationToken cancellationToken = default)
    {
        using var response = await QueryAsync(gatewayTxnRef, cancellationToken);
        var root = response.RootElement;
        // 91 = no such transaction (yet): still pending, the QR expiry decides.
        var status = Text(root, "vnp_ResponseCode") == "00"
            ? VnPayProtocol.ToStatus("00", Text(root, "vnp_TransactionStatus"))
            : PaymentStatus.Pending;
        return new GatewayTransactionStatus(gatewayTxnRef, status, VnPayProtocol.FromWireAmount(Text(root, "vnp_Amount")) ?? 0m);
    }

    public async Task<GatewayRefundResult> RefundAsync(string gatewayTxnRef, decimal amount, string reason, CancellationToken cancellationToken = default)
    {
        long wireAmount;
        try
        {
            wireAmount = VnPayProtocol.ToWireAmount(amount);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return new GatewayRefundResult(false, true, ex.Message);
        }

        if (!TryTransactionDate(gatewayTxnRef, out var transactionDate))
        {
            return new GatewayRefundResult(false, true, "The VNPay transaction reference has no creation date.");
        }

        // VNPay refunds by txnRef + the transaction number of the original payment, and needs full or partial: ask for both first.
        string transactionNo;
        long original;
        using (var query = await QueryAsync(gatewayTxnRef, cancellationToken))
        {
            var root = query.RootElement;
            var paid = Text(root, "vnp_ResponseCode") == "00" && Text(root, "vnp_TransactionStatus") == "00";
            if (!paid || !long.TryParse(Text(root, "vnp_Amount"), NumberStyles.None, CultureInfo.InvariantCulture, out original))
            {
                return new GatewayRefundResult(false, true, "VNPay does not report a successful payment for this transaction.");
            }

            transactionNo = Text(root, "vnp_TransactionNo") ?? string.Empty;
        }

        var requestId = NewRequestId();
        var createDate = VnPayProtocol.FormatDate(DateTime.UtcNow);
        var type = wireAmount == original ? "02" : "03";
        var orderInfo = VnPayProtocol.OrderInfo(reason);
        const string createBy = "system";
        var wire = wireAmount.ToString(CultureInfo.InvariantCulture);
        var body = new Dictionary<string, string>
        {
            ["vnp_RequestId"] = requestId,
            ["vnp_Version"] = VnPayProtocol.Version,
            ["vnp_Command"] = VnPayProtocol.CommandRefund,
            ["vnp_TmnCode"] = options.TmnCode,
            ["vnp_TransactionType"] = type,
            ["vnp_TxnRef"] = gatewayTxnRef,
            ["vnp_Amount"] = wire,
            ["vnp_TransactionNo"] = transactionNo,
            ["vnp_TransactionDate"] = transactionDate,
            ["vnp_CreateBy"] = createBy,
            ["vnp_CreateDate"] = createDate,
            ["vnp_IpAddr"] = VnPayProtocol.IpAddress,
            ["vnp_OrderInfo"] = orderInfo,
        };
        body[VnPayProtocol.SecureHash] = VnPayProtocol.Sign(options.HashSecret, string.Join('|',
            requestId, VnPayProtocol.Version, VnPayProtocol.CommandRefund, options.TmnCode, type, gatewayTxnRef, wire, transactionNo,
            transactionDate, createBy, createDate, VnPayProtocol.IpAddress, orderInfo));

        using var response = await PostAsync(body, ResponseSignedFields.Refund, cancellationToken);
        var result = response.RootElement;
        var code = Text(result, "vnp_ResponseCode");
        // Transaction status 09 = refund rejected, even when the request itself was accepted.
        return code == "00" && Text(result, "vnp_TransactionStatus") != "09"
            ? new GatewayRefundResult(true, true, null)
            : new GatewayRefundResult(false, true, $"VNPay refused the refund (code {code}).");
    }

    private Task<JsonDocument> QueryAsync(string txnRef, CancellationToken cancellationToken)
    {
        if (!TryTransactionDate(txnRef, out var transactionDate))
        {
            throw new HttpRequestException("The VNPay transaction reference has no creation date.");
        }

        var requestId = NewRequestId();
        var createDate = VnPayProtocol.FormatDate(DateTime.UtcNow);
        var orderInfo = $"Query {txnRef}";
        var body = new Dictionary<string, string>
        {
            ["vnp_RequestId"] = requestId,
            ["vnp_Version"] = VnPayProtocol.Version,
            ["vnp_Command"] = VnPayProtocol.CommandQuery,
            ["vnp_TmnCode"] = options.TmnCode,
            ["vnp_TxnRef"] = txnRef,
            ["vnp_OrderInfo"] = orderInfo,
            ["vnp_TransactionDate"] = transactionDate,
            ["vnp_CreateDate"] = createDate,
            ["vnp_IpAddr"] = VnPayProtocol.IpAddress,
            [VnPayProtocol.SecureHash] = VnPayProtocol.Sign(options.HashSecret, string.Join('|',
                requestId, VnPayProtocol.Version, VnPayProtocol.CommandQuery, options.TmnCode, txnRef, transactionDate, createDate,
                VnPayProtocol.IpAddress, orderInfo)),
        };
        return PostAsync(body, ResponseSignedFields.Query, cancellationToken);
    }

    /// <summary>The response fields, in order, that VNPay signs (a missing optional field counts as empty).</summary>
    private static class ResponseSignedFields
    {
        public static readonly string[] Query =
        [
            "vnp_ResponseId", "vnp_Command", "vnp_ResponseCode", "vnp_Message", "vnp_TmnCode", "vnp_TxnRef", "vnp_Amount", "vnp_BankCode",
            "vnp_PayDate", "vnp_TransactionNo", "vnp_TransactionType", "vnp_TransactionStatus", "vnp_OrderInfo", "vnp_PromotionCode",
            "vnp_PromotionAmount",
        ];

        public static readonly string[] Refund = Query[..13];
    }

    /// <summary>POSTs JSON and returns the JSON answer once its signature is checked; anything else is a transport failure.</summary>
    private async Task<JsonDocument> PostAsync(Dictionary<string, string> body, string[] signedFields, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(options.ApiUrl, body, cancellationToken);
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

        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object || Text(document.RootElement, "vnp_ResponseCode") is null)
        {
            document?.Dispose();
            throw new HttpRequestException($"VNPay answered HTTP {(int)response.StatusCode} without a response code.");
        }

        var expected = VnPayProtocol.Sign(options.HashSecret, string.Join('|', signedFields.Select(f => Text(document.RootElement, f) ?? string.Empty)));
        if (!VnPayProtocol.SignaturesMatch(expected, Text(document.RootElement, VnPayProtocol.SecureHash)))
        {
            document.Dispose();
            throw new HttpRequestException("VNPay answered with an invalid signature.");
        }

        return document;
    }

    /// <summary>A JSON string or number as text (VNPay sends amounts either way), or null.</summary>
    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            }
            : null;

    /// <summary>Unique, ASCII, at most 64 characters (gateway_txn_ref is VARCHAR(64)): "GV" + O/E/S + the caller's reference + "-" + creation date (GMT+7) + "-" + 12 hex.</summary>
    private static string NewTxnRef(PaymentPurpose purpose, string paymentRef, DateTime utcNow)
    {
        var letter = purpose switch { PaymentPurpose.Order => 'O', PaymentPurpose.Extension => 'E', _ => 'S' };
        var reference = new string(paymentRef.Where(char.IsAsciiLetterOrDigit).Take(12).ToArray());
        return $"GV{letter}{reference}-{VnPayProtocol.FormatDate(utcNow)}-{Guid.NewGuid().ToString("N")[..12]}";
    }

    /// <summary>The creation date inside a reference made by <see cref="NewTxnRef"/>.</summary>
    private static bool TryTransactionDate(string txnRef, out string date)
    {
        var parts = txnRef.Split('-');
        date = parts.Length == 3 ? parts[1] : string.Empty;
        return date.Length == 14 && date.All(char.IsAsciiDigit);
    }

    private static string NewRequestId() => Guid.NewGuid().ToString("N");
}
