using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Agencies.Dtos;
using Xunit;

namespace CommonService.Tests.Agencies;

/// <summary>
/// BE-M5-01 field rules: MST format (leader decision), phone (decisions O4), password (decisions Q16), email, lengths.
/// </summary>
public class AgencyRegistrationValidatorTests
{
    private const int MinPasswordLength = 10;

    private static RegisterAgencyRequest Valid() => new(
        TaxCode: "0312345678",
        LegalName: "Cong ty TNHH Don Dep ABC",
        LegalRepresentative: "Nguyen Van A",
        ContactPhone: "0901234567",
        ContactEmail: "Partner@Example.com",
        Password: "Abcdefgh12");

    private static ValidationException Rejected(RegisterAgencyRequest? request) =>
        Assert.Throws<ValidationException>(() => AgencyRegistrationValidator.Validate(request, MinPasswordLength));

    [Fact]
    public void Valid_request_is_accepted_and_normalized()
    {
        var request = Valid() with
        {
            TaxCode = "  0312345678-001 ",
            LegalName = "  Cong ty ABC ",
            LegalRepresentative = " Nguyen Van A ",
            ContactPhone = "+84 901 234 567",
            ContactEmail = "  Partner@Example.COM "
        };

        var result = AgencyRegistrationValidator.Validate(request, MinPasswordLength);

        Assert.Equal("0312345678-001", result.TaxCode);
        Assert.Equal("Cong ty ABC", result.LegalName);
        Assert.Equal("Nguyen Van A", result.LegalRepresentative);
        Assert.Equal("0901234567", result.ContactPhone);
        Assert.Equal("partner@example.com", result.ContactEmail);
        Assert.Equal("Abcdefgh12", result.Password);
    }

    [Theory]
    [InlineData("0312345678")]
    [InlineData("0312345678-001")]
    [InlineData("0000000000")]
    [InlineData("0312345678-999")]
    public void Tax_code_accepts_company_and_branch_shapes(string taxCode)
    {
        var result = AgencyRegistrationValidator.Validate(Valid() with { TaxCode = taxCode }, MinPasswordLength);

        Assert.Equal(taxCode, result.TaxCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("031234567")] // 9 digits
    [InlineData("03123456789")] // 11 digits
    [InlineData("0312345678001")] // 13 digits without hyphen
    [InlineData("0312345678-01")] // branch suffix too short
    [InlineData("0312345678-0001")] // branch suffix too long
    [InlineData("031234567a")] // letter
    [InlineData("0312345678-00a")]
    [InlineData("0312-345678")]
    [InlineData("0312345678_001")]
    [InlineData("٠٣١٢٣٤٥٦٧٨")] // Arabic-Indic digits: \d would accept them, [0-9] must not
    [InlineData("99999999999999999999")] // 20 digits, more than VARCHAR(14)
    public void Tax_code_rejects_everything_else(string? taxCode)
    {
        var error = Rejected(Valid() with { TaxCode = taxCode });

        Assert.Contains("taxCode", error.Errors.Keys);
        Assert.DoesNotContain("password", error.Errors.Keys);
    }

    [Theory]
    [InlineData("0901234567", "0901234567")]
    [InlineData("+84901234567", "0901234567")]
    [InlineData("090-123-4567", "0901234567")]
    public void Phone_is_normalized_to_national_form(string input, string expected)
    {
        var result = AgencyRegistrationValidator.Validate(Valid() with { ContactPhone = input }, MinPasswordLength);

        Assert.Equal(expected, result.ContactPhone);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("0241234567")] // landline
    [InlineData("090123456")]
    [InlineData("+14155552671")]
    [InlineData("abc")]
    public void Phone_must_be_a_vietnamese_mobile_number(string? phone)
    {
        var error = Rejected(Valid() with { ContactPhone = phone });

        Assert.Contains("contactPhone", error.Errors.Keys);
    }

    [Theory]
    [InlineData("Abcdefgh12")] // exactly the minimum length
    [InlineData("Abcdefgh123456")]
    [InlineData("MatKhau@2026x")]
    public void Password_policy_accepts_min_length_with_upper_lower_digit(string password)
    {
        var result = AgencyRegistrationValidator.Validate(Valid() with { Password = password }, MinPasswordLength);

        Assert.Equal(password, result.Password);
    }

    [Theory]
    [InlineData("Abcdefg12")] // 9 characters
    [InlineData("abcdefgh12")] // no upper
    [InlineData("ABCDEFGH12")] // no lower
    [InlineData("Abcdefghij")] // no digit
    [InlineData("          ")] // 10 spaces
    [InlineData("a")]
    [InlineData("")]
    [InlineData(null)]
    public void Password_policy_rejects_weak_passwords(string? password)
    {
        var error = Rejected(Valid() with { Password = password });

        Assert.Contains("password", error.Errors.Keys);
    }

    [Fact]
    public void Password_minimum_length_comes_from_the_argument_not_a_constant()
    {
        var request = Valid() with { Password = "Abcdef12" }; // 8 characters

        Assert.Throws<ValidationException>(() => AgencyRegistrationValidator.Validate(request, 10));
        var accepted = AgencyRegistrationValidator.Validate(request, 8);
        Assert.Equal("Abcdef12", accepted.Password);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-an-email")]
    [InlineData("a@")]
    [InlineData("@example.com")]
    [InlineData("Name <a@example.com>")] // display-name form: the parsed address differs from the input
    public void Email_must_be_a_plain_address(string? email)
    {
        var error = Rejected(Valid() with { ContactEmail = email });

        Assert.Contains("contactEmail", error.Errors.Keys);
    }

    [Fact]
    public void Email_longer_than_the_column_is_rejected()
    {
        var local = new string('a', 64);
        var email = $"{local}@{new string('b', 60)}.{new string('c', 60)}.{new string('d', 60)}.{new string('e', 60)}.com";
        Assert.True(email.Length > 255);

        var error = Rejected(Valid() with { ContactEmail = email });

        Assert.Contains("contactEmail", error.Errors.Keys);
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void Legal_name_is_limited_to_the_column_size(int length, bool accepted)
    {
        var request = Valid() with { LegalName = new string('N', length) };

        if (accepted)
        {
            Assert.Equal(length, AgencyRegistrationValidator.Validate(request, MinPasswordLength).LegalName.Length);
        }
        else
        {
            Assert.Contains("legalName", Rejected(request).Errors.Keys);
        }
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Legal_representative_is_limited_to_the_column_size(int length, bool accepted)
    {
        var request = Valid() with { LegalRepresentative = new string('R', length) };

        if (accepted)
        {
            Assert.Equal(length, AgencyRegistrationValidator.Validate(request, MinPasswordLength).LegalRepresentative.Length);
        }
        else
        {
            Assert.Contains("legalRepresentative", Rejected(request).Errors.Keys);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Legal_name_and_representative_are_required(string? value)
    {
        var error = Rejected(Valid() with { LegalName = value, LegalRepresentative = value });

        Assert.Contains("legalName", error.Errors.Keys);
        Assert.Contains("legalRepresentative", error.Errors.Keys);
    }

    [Fact]
    public void Missing_body_is_a_validation_error()
    {
        var error = Rejected(null);

        Assert.Contains("request", error.Errors.Keys);
    }

    [Fact]
    public void All_field_errors_are_reported_together()
    {
        var error = Rejected(new RegisterAgencyRequest(null, null, null, null, null, null));

        Assert.Equal(
            new[] { "contactEmail", "contactPhone", "legalName", "legalRepresentative", "password", "taxCode" },
            error.Errors.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Error_messages_never_echo_the_password()
    {
        var secret = "weakpw";
        var error = Rejected(Valid() with { Password = secret });

        Assert.All(error.Errors.Values.SelectMany(v => v), message => Assert.DoesNotContain(secret, message));
    }
}
