using CommonService.Application.Features.Identity;
using Xunit;

namespace CommonService.Tests.Identity;

public class PhoneValidatorTests
{
    [Theory]
    [InlineData("0901234567", "0901234567")]
    [InlineData("0389998888", "0389998888")]
    [InlineData("0771234567", "0771234567")]
    [InlineData("0851234567", "0851234567")]
    [InlineData("0561234567", "0561234567")]
    [InlineData("+84901234567", "0901234567")]
    [InlineData("84901234567", "0901234567")]
    [InlineData(" 090 123 4567 ", "0901234567")]
    [InlineData("090-123-4567", "0901234567")]
    [InlineData("(090) 123.4567", "0901234567")]
    public void Valid_Vietnamese_mobile_numbers_are_normalized(string input, string expected)
    {
        var (isValid, normalized) = PhoneValidator.TryNormalize(input);

        Assert.True(isValid);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("123456789")]
    [InlineData("090123456")] // 9 digits
    [InlineData("09012345678")] // 11 digits
    [InlineData("0241234567")] // Landline Hanoi
    [InlineData("0281234567")] // Landline HCMC
    [InlineData("0123456789")] // Old 11-digit prefix
    [InlineData("+14155552671")] // US phone
    [InlineData("0901234abc")] // Non-digit
    public void Invalid_phone_numbers_are_rejected(string? input)
    {
        var (isValid, normalized) = PhoneValidator.TryNormalize(input);

        Assert.False(isValid);
        Assert.Equal(string.Empty, normalized);
    }
}
