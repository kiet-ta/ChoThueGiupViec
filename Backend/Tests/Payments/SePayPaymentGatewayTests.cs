using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using CommonService.Application.Features.Payments.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Payments;
using CommonService.Infrastructure.Modules.Payments.SePay;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CommonService.Tests.Payments;

public sealed class SePayPaymentGatewayTests
{
    private const string Secret = "fake-test-secret";
    private const string Reference = "TA0123456789";
    private const long Now = 1800000000;
    private const string Body = "{\"transferType\":\"in\",\"code\":\"TA0123456789\",\"transferAmount\":260000}";

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(Now);
    }

    private sealed class StubHandler(string response = "{}", HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Url { get; private set; }
        public string? Authorization { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Url = request.RequestUri!.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(response) });
        }
    }

    private static SePayOptions Options() => new() { AccountNumber = "fake account", BankName = "fake bank", WebhookSecret = Secret, ApiToken = "fake-test-token" };
    private static SePayPaymentGateway Gateway(StubHandler handler) => new(new HttpClient(handler), Options(), new Clock());
    private static CreatePaymentRequest Request(decimal amount) => new(PaymentPurpose.Order, "1", amount, "test", DateTime.UnixEpoch);

    private static Dictionary<string, string> Payload(string body = Body, long timestamp = Now, string secret = Secret)
    {
        var time = timestamp.ToString(CultureInfo.InvariantCulture);
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{time}.{body}"));
        return new Dictionary<string, string>
        {
            ["x-sepay-signature"] = "sha256=" + Convert.ToHexStringLower(signature),
            ["x-sepay-timestamp"] = time,
            ["x-sepay-raw-body"] = body,
        };
    }

    [Fact]
    public async Task CreateQr_GeneratesUniqueCodesAndEncodedUrlWithoutHttp()
    {
        var handler = new StubHandler();
        var gateway = Gateway(handler);
        var first = await gateway.CreateQrAsync(Request(260000));
        var second = await gateway.CreateQrAsync(Request(260000));
        Assert.Matches("^TA[0-9]{10}$", first.GatewayTxnRef);
        Assert.NotEqual(first.GatewayTxnRef, second.GatewayTxnRef);
        Assert.Contains("acc=fake%20account&bank=fake%20bank&amount=260000&des=" + first.GatewayTxnRef, first.QrPayload);
        Assert.Equal(first.QrPayload, first.PayUrl);
        Assert.Equal(DateTime.UnixEpoch, first.ExpiresAtUtc);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    [InlineData(260000.5)]
    [InlineData(499000001)]
    public async Task CreateQr_RejectsInvalidAmounts(double amount) =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Gateway(new StubHandler()).CreateQrAsync(Request((decimal)amount)));

    [Fact]
    public async Task VerifyIpn_ValidSignatureAndRawBody()
    {
        var verified = await Gateway(new StubHandler()).VerifyIpnAsync(Payload());
        Assert.Equal(new IpnVerification(true, Reference, 260000m, PaymentStatus.Success), verified);
    }

    [Fact]
    public async Task VerifyIpn_RejectsTamperingWrongSecretMissingHeaderAndReserialization()
    {
        var gateway = Gateway(new StubHandler());
        var tampered = Payload();
        tampered["x-sepay-raw-body"] = Body.Replace("260000", "260001");
        var respaced = Payload();
        respaced["x-sepay-raw-body"] = Body.Replace(",", ", ");
        var missing = Payload();
        missing.Remove("x-sepay-signature");
        foreach (var payload in new[] { tampered, respaced, missing, Payload(secret: "fake-wrong-secret"), Payload(timestamp: Now - 301), Payload(timestamp: Now + 301) })
            Assert.False((await gateway.VerifyIpnAsync(payload)).IsSignatureValid);
    }

    [Theory]
    [InlineData("{\"transferType\":\"out\",\"code\":\"TA0123456789\",\"transferAmount\":1}")]
    [InlineData("{\"transferType\":\"in\",\"code\":\"\",\"transferAmount\":1}")]
    [InlineData("{\"transferType\":\"in\",\"code\":null,\"transferAmount\":1}")]
    [InlineData("{\"transferType\":\"in\",\"code\":\"TA0123456789\",\"transferAmount\":1.5}")]
    [InlineData("{\"transferType\":\"in\",\"code\":\"TA0123456789\",\"transferAmount\":0}")]
    public async Task VerifyIpn_RejectsInvalidFields(string body) => Assert.False((await Gateway(new StubHandler()).VerifyIpnAsync(Payload(body))).IsSignatureValid);

    [Theory]
    [InlineData("{\"data\":[{\"code\":\"TA0123456789\",\"amount_in\":260000}]}", PaymentStatus.Success, 260000)]
    [InlineData("{\"data\":[{\"transaction_content\":\"pay ta0123456789 now\",\"amount_in\":1234}]}", PaymentStatus.Success, 1234)]
    [InlineData("{\"data\":[{\"code\":\"other\",\"amount_in\":260000}]}", PaymentStatus.Pending, 0)]
    [InlineData("{\"data\":[{\"code\":\"TA0123456789\",\"amount_in\":0}]}", PaymentStatus.Pending, 0)]
    public async Task QueryStatus_MatchesAndSendsSandboxBearer(string body, PaymentStatus status, int amount)
    {
        var handler = new StubHandler(body);
        var result = await Gateway(handler).QueryStatusAsync(Reference);
        Assert.Equal(new GatewayTransactionStatus(Reference, status, amount), result);
        Assert.Equal("Bearer fake-test-token", handler.Authorization);
        Assert.Equal("https://userapi-sandbox.sepay.vn/v2/transactions?q=TA0123456789&transfer_type=in&per_page=20", handler.Url);
    }

    [Theory]
    [InlineData("not json", HttpStatusCode.OK)]
    [InlineData("{}", HttpStatusCode.InternalServerError)]
    public async Task QueryStatus_ThrowsOnBadResponse(string body, HttpStatusCode status) =>
        await Assert.ThrowsAsync<HttpRequestException>(() => Gateway(new StubHandler(body, status)).QueryStatusAsync(Reference));

    [Fact]
    public async Task Refund_IsUnsupportedAndDoesNotCallNetwork()
    {
        var handler = new StubHandler();
        var result = await Gateway(handler).RefundAsync(Reference, 1000, "test");
        Assert.False(result.Succeeded);
        Assert.False(result.GatewaySupported);
        Assert.Equal(0, handler.Calls);
    }

    private static IConfiguration Config(string? url = null, bool credentials = true) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["SePay:AccountNumber"] = credentials ? "fake-account" : "PLACEHOLDER",
        ["SePay:BankName"] = "fake-bank",
        ["SePay:WebhookSecret"] = "fake-secret",
        ["SePay:ApiToken"] = "fake-token",
        ["SePay:ApiBaseUrl"] = url ?? "https://userapi-sandbox.sepay.vn/v2",
    }).Build();

    [Theory]
    [InlineData("https://userapi.sepay.vn/v2")]
    [InlineData("http://userapi-sandbox.sepay.vn/v2")]
    [InlineData("https://userapi-sandbox.sepay.vn.evil.example/v2")]
    public void OptionsAndModule_RejectNonSandbox(string url)
    {
        Assert.False(new SePayOptions { ApiBaseUrl = url }.PointsAtSandbox);
        Assert.Throws<InvalidOperationException>(() => PaymentsModule.AddSePayGateway(new ServiceCollection(), Config(url)));
        Assert.Throws<InvalidOperationException>(() => PaymentsModule.AddSePayGateway(new ServiceCollection(), Config(url, credentials: false)));
    }

    [Fact]
    public void Module_RegistersOnlyWithCredentialsAndSandbox()
    {
        var empty = new ServiceCollection();
        PaymentsModule.AddSePayGateway(empty, Config(credentials: false));
        Assert.DoesNotContain(empty, d => d.ServiceType == typeof(IPaymentGateway));
        Assert.False(new SePayOptions { AccountNumber = "placeholder" }.HasCredentials);
        Assert.True(Options().PointsAtSandbox);
        var services = new ServiceCollection();
        PaymentsModule.AddSePayGateway(services, Config());
        using var provider = services.BuildServiceProvider();
        Assert.IsType<SePayPaymentGateway>(provider.GetRequiredService<IPaymentGateway>());
    }

    [Fact]
    public void PaymentQrService_IdentifiesSePay()
    {
        var service = new PaymentQrService(null!, Gateway(new StubHandler()), null!, null!, Microsoft.Extensions.Options.Options.Create(new CommonService.Application.Common.Options.BusinessRules()));
        var method = typeof(PaymentQrService).GetMethod("GatewayName", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal("SEPAY", method.Invoke(service, null));
    }
}
