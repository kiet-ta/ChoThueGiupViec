using System.Globalization;
using CommonService.Domain.ValueObjects;

namespace CommonService.Tests.Domain;

public class VndTests
{
    // Test data uses '.' as the decimal separator; parse it the same way on every machine (vi-VN reads "2.5" as 25).
    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("2.5", "3")]
    [InlineData("2.4", "2")]
    [InlineData("-2.5", "-3")]
    [InlineData("200.5", "201")]
    [InlineData("128000", "128000")]
    public void Round_is_whole_vnd_half_away_from_zero(string amount, string expected) =>
        Assert.Equal(D(expected), Vnd.Round(D(amount)));

    [Theory]
    [InlineData("160000", "0.200", "128000")] // decisions Q01 ECONOMY UP_TO_30, freelancer 20 % (Q11)
    [InlineData("390000", "0.000", "390000")] // agency PRO package, 0 % (Q08)
    [InlineData("1003", "0.200", "802")]      // commission 200.6 -> 201
    [InlineData("1002", "0.250", "751")]      // commission 250.5 -> 251 (half away from zero)
    public void Net_is_gross_minus_rounded_commission(string gross, string rate, string expected) =>
        Assert.Equal(D(expected), Vnd.Net(D(gross), D(rate)));

    [Theory]
    [InlineData("-1", "0.2")]
    [InlineData("100", "-0.1")]
    [InlineData("100", "1.1")]
    public void Net_rejects_impossible_inputs(string gross, string rate) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Vnd.Net(D(gross), D(rate)));
}
