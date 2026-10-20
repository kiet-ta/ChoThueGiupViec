using System.Globalization;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using Xunit;

namespace CommonService.Tests.Payments;

/// <summary>
/// ADMIN-UAT-01: the amount of an IPN payload is written with a "." decimal point, whatever the machine's regional format is. On a
/// Vietnamese Windows PC <c>decimal.TryParse("5000000.00")</c> reads 500000000, so the dev confirmation was rejected as an amount mismatch.
/// </summary>
public class FakePaymentGatewayCultureTests
{
    private static Dictionary<string, string> Payload(string amount) => new()
    {
        ["signature"] = "valid",
        ["gatewayTxnRef"] = "FAKE-abc",
        ["amount"] = amount,
        ["status"] = "success",
    };

    [Theory]
    [InlineData("vi-VN")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("")]
    public async Task The_amount_is_read_with_the_invariant_culture_under_any_current_culture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

            var verification = await new FakePaymentGateway().VerifyIpnAsync(Payload("5000000.00"));

            Assert.True(verification.IsSignatureValid);
            Assert.Equal(5000000.00m, verification.Amount);
            Assert.Equal(PaymentStatus.Success, verification.Status);
            Assert.Equal("FAKE-abc", verification.GatewayTxnRef);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("150000", 150000)]
    [InlineData("1234.5", 1234.5)]
    [InlineData("not a number", 0)]
    public async Task A_whole_amount_a_fraction_and_garbage_are_read_the_same_under_vi_VN(string raw, double expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");

            var verification = await new FakePaymentGateway().VerifyIpnAsync(Payload(raw));

            Assert.Equal((decimal)expected, verification.Amount);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
