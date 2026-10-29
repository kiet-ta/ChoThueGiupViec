using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Payments;
using CommonService.Infrastructure.Modules.Payments.VnPay;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CommonService.Tests.Payments;

/// <summary>
/// VNPay sandbox adapter. No network and no real key: a stub handler captures the HTTP calls and the credentials below are made-up
/// test values. Every expected signature is computed here, independently of the adapter, from the strings of VNPay's documentation.
/// </summary>
public sealed class VnPayPaymentGatewayTests
{
    private const string Tmn = "TESTTMN1";
    private const string Secret = "test-secret-not-a-real-key";
    private const string Return = "https://example.test/return";
    private static readonly DateTime Expires = new(2026, 10, 14, 3, 15, 0, DateTimeKind.Utc);

    private static VnPayOptions Options() => new() { TmnCode = Tmn, HashSecret = Secret, ReturnUrl = Return };

    /// <summary>HMAC-SHA512 as lowercase hex, written without using the adapter's own helper.</summary>
    private static string Hmac(string data)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(Secret));
        return string.Concat(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)).Select(b => b.ToString("x2")));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<string> _answers = new();
        public List<(string Url, JsonElement Body)> Calls { get; } = [];

        public StubHandler Answer(string json)
        {
            _answers.Enqueue(json);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var text = await request.Content!.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.RequestUri!.ToString(), JsonSerializer.Deserialize<JsonElement>(text)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_answers.Dequeue(), Encoding.UTF8, "application/json") };
        }
    }

    private static VnPayPaymentGateway Gateway(StubHandler? handler = null) => new(new HttpClient(handler ?? new StubHandler()), Options());

    private static Dictionary<string, string> Query(string url) =>
        url[(url.IndexOf('?') + 1)..].Split('&').Select(p => p.Split('=', 2))
            .ToDictionary(p => WebUtility.UrlDecode(p[0]), p => WebUtility.UrlDecode(p[1]));

    private static CreatePaymentRequest Request(decimal amount = 150_000m) =>
        new(PaymentPurpose.Order, "42", amount, "Thanh toán đơn #42: dọn nhà", Expires);

    /// <summary>A callback signed the way VNPay signs it.</summary>
    private static Dictionary<string, string> Ipn(string txnRef, string amountWire, string responseCode = "00", string transactionStatus = "00", string tmn = Tmn)
    {
        var fields = new Dictionary<string, string>
        {
            ["vnp_TmnCode"] = tmn,
            ["vnp_Amount"] = amountWire,
            ["vnp_BankCode"] = "NCB",
            ["vnp_OrderInfo"] = "Thanh toan don 42",
            ["vnp_TransactionNo"] = "14123456",
            ["vnp_ResponseCode"] = responseCode,
            ["vnp_TransactionStatus"] = transactionStatus,
            ["vnp_TxnRef"] = txnRef,
        };
        var raw = string.Join('&', fields.OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => $"{WebUtility.UrlEncode(f.Key)}={WebUtility.UrlEncode(f.Value)}"));
        fields["vnp_SecureHash"] = Hmac(raw);
        return fields;
    }

    private static string SignedResponse(string[] pipeFields, Dictionary<string, string> values)
    {
        values["vnp_SecureHash"] = Hmac(string.Join('|', pipeFields.Select(f => values.GetValueOrDefault(f, string.Empty))));
        return JsonSerializer.Serialize(values);
    }

    private static readonly string[] QueryFields =
    [
        "vnp_ResponseId", "vnp_Command", "vnp_ResponseCode", "vnp_Message", "vnp_TmnCode", "vnp_TxnRef", "vnp_Amount", "vnp_BankCode",
        "vnp_PayDate", "vnp_TransactionNo", "vnp_TransactionType", "vnp_TransactionStatus", "vnp_OrderInfo", "vnp_PromotionCode", "vnp_PromotionAmount",
    ];

    private static string QueryAnswer(string txnRef, string amountWire, string transactionStatus = "00", string responseCode = "00") =>
        SignedResponse(QueryFields, new Dictionary<string, string>
        {
            ["vnp_ResponseId"] = "r1",
            ["vnp_Command"] = "querydr",
            ["vnp_ResponseCode"] = responseCode,
            ["vnp_Message"] = "QueryDR Success",
            ["vnp_TmnCode"] = Tmn,
            ["vnp_TxnRef"] = txnRef,
            ["vnp_Amount"] = amountWire,
            ["vnp_BankCode"] = "NCB",
            ["vnp_PayDate"] = "20261014100000",
            ["vnp_TransactionNo"] = "14123456",
            ["vnp_TransactionType"] = "01",
            ["vnp_TransactionStatus"] = transactionStatus,
            ["vnp_OrderInfo"] = "Query",
        });

    [Fact]
    public async Task CreateQr_BuildsASignedPayUrl_WithAmountTimes100_AndUnaccentedOrderInfo()
    {
        var qr = await Gateway().CreateQrAsync(Request());

        Assert.StartsWith("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?", qr.PayUrl);
        Assert.Equal(qr.PayUrl, qr.QrPayload);
        Assert.InRange(qr.GatewayTxnRef.Length, 1, 64);
        var query = Query(qr.PayUrl!);
        Assert.Equal("15000000", query["vnp_Amount"]);
        Assert.Equal(qr.GatewayTxnRef, query["vnp_TxnRef"]);
        Assert.Equal(Tmn, query["vnp_TmnCode"]);
        Assert.Equal("pay", query["vnp_Command"]);
        Assert.Equal("20261014101500", query["vnp_ExpireDate"]);
        Assert.Equal("Thanh toan don 42 don nha", query["vnp_OrderInfo"]);

        var raw = string.Join('&', query.Where(q => q.Key != "vnp_SecureHash").OrderBy(q => q.Key, StringComparer.Ordinal)
            .Select(q => $"{WebUtility.UrlEncode(q.Key)}={WebUtility.UrlEncode(q.Value)}"));
        Assert.Equal(Hmac(raw), query["vnp_SecureHash"]);
    }

    [Fact]
    public async Task CreateQr_RefusesAFractionalOrOutOfRangeAmount()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Gateway().CreateQrAsync(Request(1_000.5m)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Gateway().CreateQrAsync(Request(0m)));
    }

    [Fact]
    public async Task VerifyIpn_AcceptsAValidSuccess()
    {
        var result = await Gateway().VerifyIpnAsync(Ipn("GVO42-20261014100000-abc", "15000000"));

        Assert.True(result.IsSignatureValid);
        Assert.Equal("GVO42-20261014100000-abc", result.GatewayTxnRef);
        Assert.Equal(150_000m, result.Amount);
        Assert.Equal(PaymentStatus.Success, result.Status);
    }

    [Fact]
    public async Task VerifyIpn_RejectsAForgedOrTamperedCallback()
    {
        var tampered = Ipn("GVO42-20261014100000-abc", "15000000");
        tampered["vnp_Amount"] = "100";
        Assert.False((await Gateway().VerifyIpnAsync(tampered)).IsSignatureValid);

        var noHash = Ipn("GVO42-20261014100000-abc", "15000000");
        noHash.Remove("vnp_SecureHash");
        Assert.False((await Gateway().VerifyIpnAsync(noHash)).IsSignatureValid);

        // Signed with the right secret but for another merchant code.
        Assert.False((await Gateway().VerifyIpnAsync(Ipn("GVO42-20261014100000-abc", "15000000", tmn: "OTHERTMN"))).IsSignatureValid);
    }

    [Theory]
    [InlineData("00", "00", PaymentStatus.Success)]
    [InlineData("24", "02", PaymentStatus.Expired)]
    [InlineData("00", "01", PaymentStatus.Pending)]
    [InlineData("24", "01", PaymentStatus.Pending)]
    [InlineData("00", "04", PaymentStatus.Pending)]
    public async Task VerifyIpn_MapsTheStatus(string responseCode, string transactionStatus, PaymentStatus expected)
    {
        var result = await Gateway().VerifyIpnAsync(Ipn("GVO42-20261014100000-abc", "15000000", responseCode, transactionStatus));

        Assert.True(result.IsSignatureValid);
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task QueryStatus_SendsASignedQuery_AndMapsThePaidAnswer()
    {
        const string txnRef = "GVO42-20261014100000-abc";
        var handler = new StubHandler().Answer(QueryAnswer(txnRef, "15000000"));

        var status = await Gateway(handler).QueryStatusAsync(txnRef);

        Assert.Equal(PaymentStatus.Success, status.Status);
        Assert.Equal(150_000m, status.Amount);
        var (url, body) = Assert.Single(handler.Calls);
        Assert.Equal("https://sandbox.vnpayment.vn/merchant_webapi/api/transaction", url);
        string F(string n) => body.GetProperty(n).GetString()!;
        Assert.Equal("querydr", F("vnp_Command"));
        Assert.Equal("20261014100000", F("vnp_TransactionDate"));
        var raw = string.Join('|', F("vnp_RequestId"), F("vnp_Version"), F("vnp_Command"), F("vnp_TmnCode"), F("vnp_TxnRef"),
            F("vnp_TransactionDate"), F("vnp_CreateDate"), F("vnp_IpAddr"), F("vnp_OrderInfo"));
        Assert.Equal(Hmac(raw), F("vnp_SecureHash"));
    }

    [Fact]
    public async Task QueryStatus_NotFound_StaysPending_AndAFailedPaymentIsExpired()
    {
        const string txnRef = "GVO42-20261014100000-abc";
        var handler = new StubHandler()
            .Answer(QueryAnswer(txnRef, "0", transactionStatus: "", responseCode: "91"))
            .Answer(QueryAnswer(txnRef, "15000000", transactionStatus: "02"));

        Assert.Equal(PaymentStatus.Pending, (await Gateway(handler).QueryStatusAsync(txnRef)).Status);
        Assert.Equal(PaymentStatus.Expired, (await Gateway(handler).QueryStatusAsync(txnRef)).Status);
    }

    [Fact]
    public async Task QueryStatus_AnAnswerWithABadSignature_Throws()
    {
        const string txnRef = "GVO42-20261014100000-abc";
        var forged = QueryAnswer(txnRef, "15000000").Replace("15000000", "1");
        var handler = new StubHandler().Answer(forged);

        await Assert.ThrowsAsync<HttpRequestException>(() => Gateway(handler).QueryStatusAsync(txnRef));
    }

    [Fact]
    public async Task Refund_Full_UsesType02_AndThePaymentsTransactionNumber()
    {
        const string txnRef = "GVO42-20261014100000-abc";
        var refundFields = QueryFields[..13];
        var handler = new StubHandler()
            .Answer(QueryAnswer(txnRef, "15000000"))
            .Answer(SignedResponse(refundFields, new Dictionary<string, string>
            {
                ["vnp_ResponseId"] = "r2",
                ["vnp_Command"] = "refund",
                ["vnp_ResponseCode"] = "00",
                ["vnp_Message"] = "Refund Success",
                ["vnp_TmnCode"] = Tmn,
                ["vnp_TxnRef"] = txnRef,
                ["vnp_Amount"] = "15000000",
                ["vnp_TransactionType"] = "02",
                ["vnp_TransactionStatus"] = "05",
            }));

        var result = await Gateway(handler).RefundAsync(txnRef, 150_000m, "Hủy đơn");

        Assert.True(result.Succeeded);
        Assert.True(result.GatewaySupported);
        var body = handler.Calls[1].Body;
        string F(string n) => body.GetProperty(n).GetString()!;
        Assert.Equal("02", F("vnp_TransactionType"));
        Assert.Equal("14123456", F("vnp_TransactionNo"));
        Assert.Equal("15000000", F("vnp_Amount"));
        var raw = string.Join('|', F("vnp_RequestId"), F("vnp_Version"), F("vnp_Command"), F("vnp_TmnCode"), F("vnp_TransactionType"),
            F("vnp_TxnRef"), F("vnp_Amount"), F("vnp_TransactionNo"), F("vnp_TransactionDate"), F("vnp_CreateBy"), F("vnp_CreateDate"),
            F("vnp_IpAddr"), F("vnp_OrderInfo"));
        Assert.Equal(Hmac(raw), F("vnp_SecureHash"));
    }

    [Fact]
    public async Task Refund_Partial_UsesType03()
    {
        const string txnRef = "GVO42-20261014100000-abc";
        var handler = new StubHandler()
            .Answer(QueryAnswer(txnRef, "15000000"))
            .Answer(SignedResponse(QueryFields[..13], new Dictionary<string, string>
            {
                ["vnp_ResponseId"] = "r2",
                ["vnp_ResponseCode"] = "00",
                ["vnp_TransactionStatus"] = "05",
            }));

        var result = await Gateway(handler).RefundAsync(txnRef, 60_000m, "Vang mat");

        Assert.True(result.Succeeded);
        Assert.Equal("03", handler.Calls[1].Body.GetProperty("vnp_TransactionType").GetString());
        Assert.Equal("6000000", handler.Calls[1].Body.GetProperty("vnp_Amount").GetString());
    }

    [Fact]
    public async Task Refund_OfAPaymentVnPayDoesNotReportPaid_IsRefusedWithoutCallingRefund()
    {
        const string txnRef = "GVO42-20261014100000-abc";
        var handler = new StubHandler().Answer(QueryAnswer(txnRef, "15000000", transactionStatus: "01"));

        var result = await Gateway(handler).RefundAsync(txnRef, 150_000m, "x");

        Assert.False(result.Succeeded);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public void Options_NeedRealCredentials_AndOnlyTheSandbox()
    {
        Assert.False(new VnPayOptions().HasCredentials);
        Assert.False(new VnPayOptions { TmnCode = "VNPAY_PLACEHOLDER", HashSecret = "x", ReturnUrl = Return }.HasCredentials);
        Assert.True(Options().HasCredentials);
        Assert.True(Options().PointsAtSandbox);
        Assert.False(new VnPayOptions { PaymentUrl = "https://pay.vnpay.vn/vpcpay.html" }.PointsAtSandbox);
    }

    [Fact]
    public void Module_RegistersTheGatewayOnlyWithCredentials_AndStopsForANonSandboxUrl()
    {
        IServiceCollection Services(Dictionary<string, string?> settings)
        {
            var services = new ServiceCollection();
            PaymentsModule.AddVnPayGateway(services, new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
            return services;
        }

        Assert.Empty(Services(new Dictionary<string, string?>()));
        Assert.NotEmpty(Services(new Dictionary<string, string?>
        {
            ["VnPay:TmnCode"] = Tmn,
            ["VnPay:HashSecret"] = Secret,
            ["VnPay:ReturnUrl"] = Return,
        }));
        Assert.Throws<InvalidOperationException>(() => Services(new Dictionary<string, string?>
        {
            ["VnPay:TmnCode"] = Tmn,
            ["VnPay:HashSecret"] = Secret,
            ["VnPay:ReturnUrl"] = Return,
            ["VnPay:PaymentUrl"] = "https://pay.vnpay.vn/vpcpay.html",
        }));
    }
}
