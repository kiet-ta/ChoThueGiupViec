using CommonService.Application.Exceptions;

namespace CommonService.Application.Features.Workers.Services;

/// <summary>
/// Field rules of the payout account of a worker (ECON-FLOW-FIX, decisions D19): the same rules as the agency's account (a name of at most
/// 100 characters, 6 to 30 digits with spaces ignored), kept here so Workers does not depend on the Agencies module.
/// </summary>
public static class WorkerBankValidator
{
    public const int BankNameMaxLength = 100;
    public const int BankAccountNoMaxLength = 30;
    public const int BankAccountNoMinLength = 6;

    public static (string BankName, string BankAccountNo) Validate(string? bankName, string? bankAccountNo)
    {
        var name = (bankName ?? string.Empty).Trim();
        var account = new string((bankAccountNo ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (name.Length == 0)
        {
            errors["bankName"] = ["Bank name is required."];
        }
        else if (name.Length > BankNameMaxLength)
        {
            errors["bankName"] = [$"Bank name must be at most {BankNameMaxLength} characters."];
        }

        if (account.Length == 0)
        {
            errors["bankAccountNo"] = ["Bank account number is required."];
        }
        else if (account.Length < BankAccountNoMinLength || account.Length > BankAccountNoMaxLength || !account.All(char.IsAsciiDigit))
        {
            errors["bankAccountNo"] = [$"Bank account number must be {BankAccountNoMinLength} to {BankAccountNoMaxLength} digits."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return (name, account);
    }
}
