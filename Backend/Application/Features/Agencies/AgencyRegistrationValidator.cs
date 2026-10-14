using System.Net.Mail;
using System.Text.RegularExpressions;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Application.Features.Identity;

namespace CommonService.Application.Features.Agencies;

/// <summary>Machine-readable <c>data.code</c> values of the 409 answers of agency registration.</summary>
public static class AgencyErrorCodes
{
    public const string TaxCodeExists = "AGENCY_TAX_CODE_EXISTS";
    public const string EmailExists = "AGENCY_EMAIL_EXISTS";
}

/// <summary>The request after validation: trimmed text, normalized phone and lower-cased email.</summary>
public sealed record ValidatedAgencyRegistration(
    string TaxCode,
    string LegalName,
    string LegalRepresentative,
    string ContactPhone,
    string ContactEmail,
    string Password);

/// <summary>
/// All field rules of agency registration in one pure place (no framework, no database), so each rule has a unit test.
/// Errors of every field are reported together as one <see cref="ValidationException"/> (HTTP 400, shared envelope).
/// </summary>
public static class AgencyRegistrationValidator
{
    // Column sizes of PARTNER_AGENCY (Infrastructure/Persistence/Configurations/PartnerAgencyConfiguration.cs).
    internal const int TaxCodeMaxLength = 14;
    internal const int LegalNameMaxLength = 200;
    internal const int LegalRepresentativeMaxLength = 100;
    internal const int ContactEmailMaxLength = 255;

    /// <summary>
    /// Vietnamese tax code (MST): 10 digits for an enterprise, or 10 digits + "-" + 3 digits (13 digits, 14 characters)
    /// for a dependent unit such as a branch. The shape follows Circular 105/2020/TT-BTC article 5, which secondary sources
    /// report as replaced by Circular 86/2024/TT-BTC with the same two shapes (not re-checked against the Official Gazette).
    /// The 10th check digit is deliberately NOT verified: its algorithm is not confirmed from an official source here
    /// (leader decision for BE-M5-01). ASCII digits only; <c>\d</c> would also accept other Unicode digits.
    /// </summary>
    private static readonly Regex TaxCodePattern = new(
        "^[0-9]{10}(-[0-9]{3})?$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Validates <paramref name="request"/>. <paramref name="minPasswordLength"/> is <c>Auth.MinPasswordLength</c>
    /// (decisions Q16: at least that many characters, with an upper-case letter, a lower-case letter and a digit).
    /// </summary>
    public static ValidatedAgencyRegistration Validate(RegisterAgencyRequest? request, int minPasswordLength)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (request is null)
        {
            errors["request"] = ["Request body is required."];
            throw new ValidationException(errors);
        }

        var taxCode = (request.TaxCode ?? string.Empty).Trim();
        if (taxCode.Length == 0)
        {
            errors["taxCode"] = ["Tax code is required."];
        }
        else if (taxCode.Length > TaxCodeMaxLength || !TaxCodePattern.IsMatch(taxCode))
        {
            errors["taxCode"] = ["Tax code must be 10 digits, or 10 digits, a hyphen and 3 digits (for example 0123456789-001)."];
        }

        var legalName = (request.LegalName ?? string.Empty).Trim();
        if (legalName.Length == 0)
        {
            errors["legalName"] = ["Legal name is required."];
        }
        else if (legalName.Length > LegalNameMaxLength)
        {
            errors["legalName"] = [$"Legal name must be at most {LegalNameMaxLength} characters."];
        }

        var representative = (request.LegalRepresentative ?? string.Empty).Trim();
        if (representative.Length == 0)
        {
            errors["legalRepresentative"] = ["Legal representative is required."];
        }
        else if (representative.Length > LegalRepresentativeMaxLength)
        {
            errors["legalRepresentative"] = [$"Legal representative must be at most {LegalRepresentativeMaxLength} characters."];
        }

        // Decisions O4: Vietnamese mobile numbers only, stored in national form 0XXXXXXXXX.
        var (phoneIsValid, normalizedPhone) = PhoneValidator.TryNormalize(request.ContactPhone);
        if (!phoneIsValid)
        {
            errors["contactPhone"] = ["A valid Vietnamese mobile phone number is required."];
        }

        var email = (request.ContactEmail ?? string.Empty).Trim();
        if (email.Length == 0)
        {
            errors["contactEmail"] = ["Contact email is required."];
        }
        else if (email.Length > ContactEmailMaxLength || !IsValidEmail(email))
        {
            errors["contactEmail"] = [$"A valid email address of at most {ContactEmailMaxLength} characters is required."];
        }

        var password = request.Password ?? string.Empty;
        var passwordErrors = PasswordErrors(password, minPasswordLength);
        if (passwordErrors.Length > 0)
        {
            errors["password"] = passwordErrors;
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return new ValidatedAgencyRegistration(
            taxCode,
            legalName,
            representative,
            normalizedPhone,
            email.ToLowerInvariant(),
            password);
    }

    private static string[] PasswordErrors(string password, int minLength)
    {
        if (password.Length == 0)
        {
            return ["Password is required."];
        }

        var errors = new List<string>();
        if (password.Length < minLength)
        {
            errors.Add($"Password must be at least {minLength} characters long.");
        }

        if (!password.Any(char.IsUpper))
        {
            errors.Add("Password must contain an upper-case letter.");
        }

        if (!password.Any(char.IsLower))
        {
            errors.Add("Password must contain a lower-case letter.");
        }

        if (!password.Any(char.IsAsciiDigit))
        {
            errors.Add("Password must contain a digit.");
        }

        return errors.ToArray();
    }

    // Same rule as PasswordLoginService.IsValidEmail, so an email accepted here can be used to sign in.
    private static bool IsValidEmail(string value) =>
        MailAddress.TryCreate(value, out var parsed) && parsed.Address == value;
}
