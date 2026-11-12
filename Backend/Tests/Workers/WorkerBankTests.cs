using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Services;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>
/// ECON-FLOW-FIX: the field rules of the account the worker's monthly payout is transferred to. How the account is set and changed (first
/// account at once, a change by an Admin-approved request, the 15-day lock) is in <see cref="WorkerBankChangeTests"/> (LEDGER-28).
/// </summary>
public class WorkerBankTests
{
    [Fact]
    public void A_valid_pair_is_trimmed_and_the_account_loses_its_spaces() =>
        Assert.Equal(("Vietcombank", "0011001234567"), WorkerBankValidator.Validate("  Vietcombank ", "0011 0012 34567"));

    [Fact]
    public void Both_fields_are_required()
    {
        var errors = Assert.Throws<ValidationException>(() => WorkerBankValidator.Validate(null, "")).Errors;

        Assert.Contains("bankName", errors.Keys);
        Assert.Contains("bankAccountNo", errors.Keys);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("12345678901234567890123456789012")]
    [InlineData("00110012A4")]
    public void An_account_number_must_be_6_to_30_digits(string account) =>
        Assert.Contains("bankAccountNo", Assert.Throws<ValidationException>(() => WorkerBankValidator.Validate("VCB", account)).Errors.Keys);

    [Fact]
    public void A_bank_name_over_the_column_size_is_rejected() =>
        Assert.Contains("bankName", Assert.Throws<ValidationException>(() => WorkerBankValidator.Validate(new string('B', 101), "0011001234")).Errors.Keys);
}
