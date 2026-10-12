using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Payments;
using CommonService.Infrastructure.Modules.Payments.MoMo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CommonService.Tests.Payments;

/// <summary>
/// BE-M2-06: MoMo sandbox adapter. No network and no real key: a stub handler captures the HTTP calls and the credentials below are
/// made-up test values. Every expected signature is computed here, independently of the adapter, from the raw string of MoMo's documentation.
/// </summary>
public sealed class MoMoPaymentGatewayTests
{
    private const string Partner = "TESTPARTNER";
    private const string Access = "test-access";
    private const string Secret = "test-secret-not-a-real-key";
    private const string Create = "https://test-payment.momo.vn/v2/gateway/api/create";
    private const string Ipn = "https://example.test/api/payments/ipn/momo";
    private const string Redirect = "https://example.test/return";
    private static readonly DateTime Expires = new(2026, 10, 14, 3, 15, 0, DateTimeKind.Utc);

    private static MoMoOptions Options() => new()
    {
        PartnerCode = Partner,
        AccessKey = Access,
        SecretKey = Secret,
        Endpoint = Create,
        IpnUrl = Ipn,
        RedirectUrl = Redirect,
    };

    /// <summary>HMAC-SHA256 as lowercase hex, written without using the adapter's own helper.</summary>
    private static string Hmac(string raw)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        return string.Concat(hmac.ComputeHash(Encoding.UTF8.GetBytes(raw)).Select(b => b.ToString("x2")));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _answers = new();
        public List<(HttpMethod Method, string Url, JsonElement Body)> Calls { get; } = [];

        public StubHandler Answer(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            _answers.Enqueue((status, json));
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var text = await request.Content!.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method, request.RequestUri!.ToString(), JsonSerializer.Deserialize<JsonElement>(text)));
            var (status, body) = _answers.Dequeue();
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static MoMoPaymentGateway Gateway(StubHandler handler) => new(new HttpClient(handler), Options());

    private static CreatePaymentRequest OrderRequest(decimal amount = 260000m) =>
        new(PaymentPurpose.Order, "42", amount, "GV261014ABC123", Expires);

    private const string CreateOk =
        """{"partnerCode":"TESTPARTNER","requestId":"r","orderId":"o","amount":260000,"responseTime":1,"message":"Successful.","resultCode":0,"payUrl":"https://test-payment.momo.vn/pay/abc","deeplink":"momo://x","qrCodeUrl":"00020101021226..."}""";

    [Fact]
    public void Sign_IsHmacSha256_AsLowercaseHex()
    {
        // The well-known HMAC-SHA256 test vector.
        var signature = MoMoProtocol.Sign("key", "The quick brown fox jumps over the lazy dog");

        Assert.Equal("f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8", signature);
    }

    [Fact]
    public async Task CreateQr_PostsTheDocumentedFields_SignedOverTheDocumentedRawString()
    {
        var handler = new StubHandler().Answer(CreateOk);

        var qr = await Gateway(handler).CreateQrAsync(OrderRequest());

        var (method, url, body) = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal(Create, url);
        var orderId = body.GetProperty("orderId").GetString()!;
        var requestId = body.GetProperty("requestId").GetString()!;
        Assert.Equal(Partner, body.GetProperty("partnerCode").GetString());
        Assert.Equal(260000, body.GetProperty("amount").GetInt64());
        Assert.Equal(JsonValueKind.Number, body.GetProperty("amount").ValueKind);
        Assert.Equal("GV261014ABC123", body.GetProperty("orderInfo").GetString());
        Assert.Equal(Redirect, body.GetProperty("redirectUrl").GetString());
        Assert.Equal(Ipn, body.GetProperty("ipnUrl").GetString());
        Assert.Equal("captureWallet", body.GetProperty("requestType").GetString());
        Assert.Equal(string.Empty, body.GetProperty("extraData").GetString());
        Assert.Equal("vi", body.GetProperty("lang").GetString());
        Assert.False(body.TryGetProperty("accessKey", out _)); // only inside the signature, never sent
        Assert.False(body.TryGetProperty("secretKey", out _));

        var raw = $"accessKey={Access}&amount=260000&extraData=&ipnUrl={Ipn}&orderId={orderId}&orderInfo=GV261014ABC123" +
                  $"&partnerCode={Partner}&redirectUrl={Redirect}&requestId={requestId}&requestType=captureWallet";
        Assert.Equal(Hmac(raw), body.GetProperty("signature").GetString());

        Assert.Equal(orderId, qr.GatewayTxnRef);
        Assert.Equal("00020101021226...", qr.QrPayload);
        Assert.Equal("https://test-payment.momo.vn/pay/abc", qr.PayUrl);
        Assert.Equal(Expires, qr.ExpiresAtUtc);
    }

    [Fact]
    public async Task CreateQr_GeneratesAUniqueAsciiOrderId_ThatFitsTheGatewayTxnRefColumn()
    {
        var handler = new StubHandler().Answer(CreateOk).Answer(CreateOk);
        var gateway = Gateway(handler);

        var first = await gateway.CreateQrAsync(OrderRequest());
        var second = await gateway.CreateQrAsync(new CreatePaymentRequest(PaymentPurpose.Extension, "9", 97500m, "ext", Expires));

        Assert.NotEqual(first.GatewayTxnRef, second.GatewayTxnRef);
        Assert.StartsWith("GVO42-", first.GatewayTxnRef);
        Assert.StartsWith("GVE9-", second.GatewayTxnRef);
        Assert.All(new[] { first.GatewayTxnRef, second.GatewayTxnRef }, r =>
        {
            Assert.True(r.Length <= 64);
            Assert.Matches("^[A-Za-z0-9-]+$", r);
        });
    }

    [Fact]
    public async Task CreateQr_WithoutAQrCodeUrl_UsesThePayUrlAsTheQrPayload()
    {
        var handler = new StubHandler().Answer("""{"resultCode":0,"payUrl":"https://test-payment.momo.vn/pay/abc"}""");

        var qr = await Gateway(handler).CreateQrAsync(OrderRequest());

        Assert.Equal("https://test-payment.momo.vn/pay/abc", qr.QrPayload);
    }

    [Theory]
    [InlineData("""{"resultCode":41,"message":"Duplicated orderId"}""", HttpStatusCode.OK)]
    [InlineData("""{"resultCode":0}""", HttpStatusCode.OK)]
    [InlineData("<html>Bad gateway</html>", HttpStatusCode.BadGateway)]
    [InlineData("""{"message":"no code"}""", HttpStatusCode.InternalServerError)]
    public async Task CreateQr_WhenMoMoRefusesOrAnswersGarbage_Throws(string answer, HttpStatusCode status)
    {
        var handler = new StubHandler().Answer(answer, status);

        await Assert.ThrowsAsync<HttpRequestException>(() => Gateway(handler).CreateQrAsync(OrderRequest()));
    }

    [Theory]
    [InlineData("999")]
    [InlineData("50000001")]
    [InlineData("260000.5")]
    public async Task CreateQr_WithAnAmountMoMoDoesNotAccept_ThrowsBeforeCallingMoMo(string amount)
    {
        var handler = new StubHandler();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Gateway(handler).CreateQrAsync(OrderRequest(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture))));

        Assert.Empty(handler.Calls);
    }

    private static Dictionary<string, string> IpnPayload(string resultCode = "0", string amount = "260000", string partner = Partner)
    {
        var p = new Dictionary<string, string>
        {
            ["partnerCode"] = partner,
            ["orderId"] = "GVO42-abc",
            ["requestId"] = "req-1",
            ["amount"] = amount,
            ["orderInfo"] = "GV261014ABC123",
            ["orderType"] = "momo_wallet",
            ["transId"] = "4014083433",
            ["resultCode"] = resultCode,
            ["message"] = "Successful.",
            ["payType"] = "qr",
            ["responseTime"] = "1721720663942",
            ["extraData"] = "",
        };
        p["signature"] = Hmac(
            $"accessKey={Access}&amount={p["amount"]}&extraData=&message=Successful.&orderId=GVO42-abc&orderInfo=GV261014ABC123" +
            $"&orderType=momo_wallet&partnerCode={p["partnerCode"]}&payType=qr&requestId=req-1&responseTime=1721720663942" +
            $"&resultCode={p["resultCode"]}&transId=4014083433");
        return p;
    }

    [Fact]
    public async Task VerifyIpn_WithTheDocumentedSignature_IsValid_AndReportsTheOrderAmountAndSuccess()
    {
        var result = await Gateway(new StubHandler()).VerifyIpnAsync(IpnPayload());

        Assert.True(result.IsSignatureValid);
        Assert.Equal("GVO42-abc", result.GatewayTxnRef);
        Assert.Equal(260000m, result.Amount);
        Assert.Equal(PaymentStatus.Success, result.Status);
    }

    [Theory]
    [InlineData("1006", PaymentStatus.Expired)] // the user declined
    [InlineData("1005", PaymentStatus.Expired)] // the QR expired
    [InlineData("1000", PaymentStatus.Pending)] // waiting for the user
    [InlineData("9000", PaymentStatus.Pending)] // authorized, not captured
    public async Task VerifyIpn_MapsTheResultCode(string resultCode, PaymentStatus expected)
    {
        var result = await Gateway(new StubHandler()).VerifyIpnAsync(IpnPayload(resultCode));

        Assert.True(result.IsSignatureValid);
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task VerifyIpn_AcceptsAnUppercaseHexSignature()
    {
        var payload = IpnPayload();
        payload["signature"] = payload["signature"].ToUpperInvariant();

        Assert.True((await Gateway(new StubHandler()).VerifyIpnAsync(payload)).IsSignatureValid);
    }

    [Fact]
    public async Task VerifyIpn_RejectsATamperedAmount_AWrongSignature_AForeignPartner_AndAMissingField()
    {
        var gateway = Gateway(new StubHandler());

        var tampered = IpnPayload();
        tampered["amount"] = "1"; // signature was made for 260000
        var wrong = IpnPayload();
        wrong["signature"] = new string('0', 64);
        var noSignature = IpnPayload();
        noSignature.Remove("signature");
        var foreign = IpnPayload(partner: "SOMEONE_ELSE"); // correctly signed, but not our partner code
        var missing = IpnPayload();
        missing.Remove("transId");

        Assert.False((await gateway.VerifyIpnAsync(tampered)).IsSignatureValid);
        Assert.False((await gateway.VerifyIpnAsync(wrong)).IsSignatureValid);
        Assert.False((await gateway.VerifyIpnAsync(noSignature)).IsSignatureValid);
        Assert.False((await gateway.VerifyIpnAsync(foreign)).IsSignatureValid);
        Assert.False((await gateway.VerifyIpnAsync(missing)).IsSignatureValid);
        Assert.False((await gateway.VerifyIpnAsync(new Dictionary<string, string>())).IsSignatureValid);
    }

    [Fact]
    public async Task QueryStatus_PostsToTheQueryApi_SignedOverItsRawString_AndMapsTheAnswer()
    {
        var handler = new StubHandler().Answer("""{"resultCode":0,"amount":260000,"transId":4014083433,"orderId":"GVO42-abc"}""");

        var status = await Gateway(handler).QueryStatusAsync("GVO42-abc");

        var (_, url, body) = Assert.Single(handler.Calls);
        Assert.Equal("https://test-payment.momo.vn/v2/gateway/api/query", url);
        var requestId = body.GetProperty("requestId").GetString()!;
        Assert.Equal("GVO42-abc", body.GetProperty("orderId").GetString());
        Assert.Equal(Partner, body.GetProperty("partnerCode").GetString());
        Assert.Equal("vi", body.GetProperty("lang").GetString());
        Assert.Equal(Hmac($"accessKey={Access}&orderId=GVO42-abc&partnerCode={Partner}&requestId={requestId}"), body.GetProperty("signature").GetString());
        Assert.Equal(new GatewayTransactionStatus("GVO42-abc", PaymentStatus.Success, 260000m), status);
    }

    [Theory]
    [InlineData(1000, PaymentStatus.Pending)]
    [InlineData(42, PaymentStatus.Pending)] // order not found at MoMo yet: not a final failure
    [InlineData(1005, PaymentStatus.Expired)]
    public async Task QueryStatus_MapsNonSuccessCodes(int code, PaymentStatus expected)
    {
        var handler = new StubHandler().Answer($$"""{"resultCode":{{code}},"amount":260000}""");

        Assert.Equal(expected, (await Gateway(handler).QueryStatusAsync("GVO42-abc")).Status);
    }

    [Theory]
    [InlineData(0, PaymentStatus.Success)]
    [InlineData(98, PaymentStatus.Expired)]
    [InlineData(99, PaymentStatus.Expired)]
    [InlineData(1001, PaymentStatus.Expired)]
    [InlineData(1002, PaymentStatus.Expired)]
    [InlineData(1003, PaymentStatus.Expired)]
    [InlineData(1004, PaymentStatus.Expired)]
    [InlineData(1007, PaymentStatus.Expired)]
    [InlineData(1017, PaymentStatus.Expired)]
    [InlineData(1026, PaymentStatus.Expired)]
    [InlineData(4001, PaymentStatus.Expired)]
    [InlineData(4100, PaymentStatus.Expired)]
    [InlineData(10, PaymentStatus.Pending)]
    [InlineData(13, PaymentStatus.Pending)]
    [InlineData(7000, PaymentStatus.Pending)]
    [InlineData(7002, PaymentStatus.Pending)]
    [InlineData(123456, PaymentStatus.Pending)] // an unknown code never marks a payment paid or failed
    public void ResultCodes_FollowMoMosTable_OnlyZeroIsPaid(int code, PaymentStatus expected) =>
        Assert.Equal(expected, MoMoProtocol.ToStatus(code));

    [Fact]
    public async Task Refund_AsksMoMoForTheTransId_ThenRefundsWithANewOrderId_SignedOverTheRefundRawString()
    {
        var handler = new StubHandler()
            .Answer("""{"resultCode":0,"amount":260000,"transId":4014083433}""")
            .Answer("""{"resultCode":0,"amount":156000,"transId":4014083999}""");

        var result = await Gateway(handler).RefundAsync("GVO42-abc", 156000m, "ABSENCE_60");

        Assert.Equal(new GatewayRefundResult(true, true, null), result);
        Assert.Equal(2, handler.Calls.Count);
        Assert.Equal("https://test-payment.momo.vn/v2/gateway/api/query", handler.Calls[0].Url);
        Assert.Equal("GVO42-abc", handler.Calls[0].Body.GetProperty("orderId").GetString());

        var (_, url, body) = handler.Calls[1];
        Assert.Equal("https://test-payment.momo.vn/v2/gateway/api/refund", url);
        var orderId = body.GetProperty("orderId").GetString()!;
        var requestId = body.GetProperty("requestId").GetString()!;
        Assert.NotEqual("GVO42-abc", orderId); // MoMo requires a NEW orderId for a refund
        Assert.StartsWith("RF", orderId);
        Assert.Equal(156000, body.GetProperty("amount").GetInt64());
        Assert.Equal(4014083433, body.GetProperty("transId").GetInt64());
        Assert.Equal("ABSENCE_60", body.GetProperty("description").GetString());
        Assert.Equal("vi", body.GetProperty("lang").GetString());
        Assert.Equal(
            Hmac($"accessKey={Access}&amount=156000&description=ABSENCE_60&orderId={orderId}&partnerCode={Partner}&requestId={requestId}&transId=4014083433"),
            body.GetProperty("signature").GetString());
    }

    [Theory]
    [InlineData("""{"resultCode":1000,"amount":260000}""")]
    [InlineData("""{"resultCode":0,"amount":260000}""")]
    public async Task Refund_WhenMoMoHasNoSuccessfulPaymentOrNoTransId_DoesNotCallRefund(string queryAnswer)
    {
        var handler = new StubHandler().Answer(queryAnswer);

        var result = await Gateway(handler).RefundAsync("GVO42-abc", 156000m, "x");

        Assert.False(result.Succeeded);
        Assert.True(result.GatewaySupported);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task Refund_WhenMoMoRefuses_ReportsAFailureOfASupportedGateway()
    {
        var handler = new StubHandler()
            .Answer("""{"resultCode":0,"amount":260000,"transId":4014083433}""")
            .Answer("""{"resultCode":1081,"message":"already refunded"}""");

        var result = await Gateway(handler).RefundAsync("GVO42-abc", 156000m, "x");

        Assert.False(result.Succeeded);
        Assert.True(result.GatewaySupported);
        Assert.Contains("1081", result.Message);
    }

    [Fact]
    public async Task Refund_OfAnAmountMoMoDoesNotAccept_FailsWithoutCallingMoMo()
    {
        var handler = new StubHandler();

        var result = await Gateway(handler).RefundAsync("GVO42-abc", 500m, "x");

        Assert.False(result.Succeeded);
        Assert.True(result.GatewaySupported);
        Assert.Empty(handler.Calls);
    }

    private static IConfiguration Config(Dictionary<string, string?> momo) =>
        new ConfigurationBuilder().AddInMemoryCollection(momo.ToDictionary(kv => $"MoMo:{kv.Key}", kv => kv.Value)).Build();

    private static Dictionary<string, string?> RealLooking() => new()
    {
        ["PartnerCode"] = Partner,
        ["AccessKey"] = Access,
        ["SecretKey"] = Secret,
        ["Endpoint"] = Create,
        ["IpnUrl"] = Ipn,
        ["RedirectUrl"] = Redirect,
    };

    [Fact]
    public void Registration_WithTheCommittedPlaceholders_RegistersNothing_SoTheFakeStays()
    {
        // The exact values of Backend/appsettings.json.
        var placeholders = new Dictionary<string, string?>
        {
            ["PartnerCode"] = "MOMO_SANDBOX_PARTNER",
            ["AccessKey"] = "MOMO_SANDBOX_ACCESS_KEY",
            ["SecretKey"] = "MOMO_SANDBOX_SECRET_KEY_PLACEHOLDER",
            ["Endpoint"] = Create,
            ["IpnUrl"] = "http://localhost:5004/api/payments/momo-ipn",
            ["RedirectUrl"] = "http://localhost:5004/api/payments/momo-return",
        };
        var services = new ServiceCollection();

        PaymentsModule.AddMoMoGateway(services, Config(placeholders));
        services.AddFakePorts();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<FakePaymentGateway>(provider.GetRequiredService<IPaymentGateway>());
    }

    [Fact]
    public void Registration_WithNoMoMoSection_OrAMissingKey_RegistersNothing()
    {
        var empty = new ServiceCollection();
        var partial = new ServiceCollection();
        var missingSecret = RealLooking();
        missingSecret["SecretKey"] = "";

        PaymentsModule.AddMoMoGateway(empty, new ConfigurationBuilder().Build());
        PaymentsModule.AddMoMoGateway(partial, Config(missingSecret));

        Assert.DoesNotContain(empty, d => d.ServiceType == typeof(IPaymentGateway));
        Assert.DoesNotContain(partial, d => d.ServiceType == typeof(IPaymentGateway));
    }

    [Fact]
    public void Registration_WithRealLookingSandboxValues_UsesTheMoMoAdapter_EvenAfterTheFakesAreAdded()
    {
        var services = new ServiceCollection();

        PaymentsModule.AddMoMoGateway(services, Config(RealLooking()));
        services.AddFakePorts();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<MoMoPaymentGateway>(provider.GetRequiredService<IPaymentGateway>());
    }

    [Theory]
    [InlineData("https://payment.momo.vn/v2/gateway/api/create")] // production host
    [InlineData("http://test-payment.momo.vn/v2/gateway/api/create")] // not https
    [InlineData("not a url")]
    public void Registration_WithCredentials_ButNotTheSandboxEndpoint_StopsTheStartup(string endpoint)
    {
        var config = RealLooking();
        config["Endpoint"] = endpoint;

        var error = Assert.Throws<InvalidOperationException>(() => PaymentsModule.AddMoMoGateway(new ServiceCollection(), Config(config)));

        Assert.Contains("sandbox", error.Message);
    }

    [Fact]
    public void UrlFor_ReplacesTheLastSegmentOfTheCreateEndpoint()
    {
        Assert.Equal("https://test-payment.momo.vn/v2/gateway/api/refund", Options().UrlFor("refund"));
        Assert.Equal("https://test-payment.momo.vn/v2/gateway/api/query", Options().UrlFor("query"));
    }
}
