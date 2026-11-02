using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;

namespace CommonService.Infrastructure.Modules.Payments.SePay;

public sealed class SePayPaymentGateway(HttpClient http, SePayOptions options, TimeProvider? timeProvider = null) : IPaymentGateway
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public Task<PaymentQr> CreateQrAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var code = SePayProtocol.NewCode(options.CodePrefix);
        var url = SePayProtocol.QrUrl(options, request.Amount, code);
        return Task.FromResult(new PaymentQr(code, url, url, request.ExpiresAtUtc));
    }

    public Task<IpnVerification> VerifyIpnAsync(IReadOnlyDictionary<string, string> payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var invalid = new IpnVerification(false, null, 0m, PaymentStatus.Pending);
        if (!payload.TryGetValue("x-sepay-signature", out var signature)
            || !payload.TryGetValue("x-sepay-timestamp", out var timestamp)
            || !payload.TryGetValue("x-sepay-raw-body", out var raw)
            || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || Math.Abs((decimal)_clock.GetUtcNow().ToUnixTimeSeconds() - seconds) > SePayProtocol.TimestampWindowSeconds
            || !SePayProtocol.SignaturesMatch(SePayProtocol.Signature(options.WebhookSecret, timestamp, raw), signature))
            return Task.FromResult(invalid);

        try
        {
            using var json = JsonDocument.Parse(raw);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("transferType", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "in"
                || !root.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(code.GetString())
                || !root.TryGetProperty("transferAmount", out var amount) || amount.ValueKind != JsonValueKind.Number
                || !amount.TryGetInt64(out var integer) || integer <= 0)
                return Task.FromResult(invalid);
            return Task.FromResult(new IpnVerification(true, code.GetString(), integer, PaymentStatus.Success));
        }
        catch (JsonException) { return Task.FromResult(invalid); }
    }

    public async Task<GatewayTransactionStatus> QueryStatusAsync(string gatewayTxnRef, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayTxnRef);
        if (!options.PointsAtSandbox) throw new InvalidOperationException("SePay API must use the sandbox host.");
        var url = $"{options.ApiBaseUrl.TrimEnd('/')}/transactions?q={Uri.EscapeDataString(gatewayTxnRef)}&transfer_type=in&per_page=20";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiToken);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw new HttpRequestException("Invalid SePay response.");
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var code = Text(item, "code");
                var content = Text(item, "transaction_content");
                if ((string.Equals(code, gatewayTxnRef, StringComparison.OrdinalIgnoreCase)
                    || (content?.Contains(gatewayTxnRef, StringComparison.OrdinalIgnoreCase) ?? false))
                    && item.TryGetProperty("amount_in", out var amount) && amount.ValueKind == JsonValueKind.Number
                    && amount.TryGetDecimal(out var value) && value > 0)
                    return new GatewayTransactionStatus(gatewayTxnRef, PaymentStatus.Success, value);
            }
            return new GatewayTransactionStatus(gatewayTxnRef, PaymentStatus.Pending, 0m);
        }
        catch (JsonException ex) { throw new HttpRequestException("Invalid SePay response.", ex); }
    }

    private static string? Text(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;

    public Task<GatewayRefundResult> RefundAsync(string gatewayTxnRef, decimal amount, string reason, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GatewayRefundResult(false, false, "SePay has no refund API; recorded in the database only."));
}
