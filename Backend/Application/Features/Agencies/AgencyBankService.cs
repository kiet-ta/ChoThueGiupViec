using CommonService.Application.Exceptions;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Agencies;

/// <summary>The account the platform transfers the agency's payout to (<c>PARTNER_AGENCY.bank_name</c> / <c>bank_account_no</c>).</summary>
public sealed record AgencyBankDto(string BankName, string BankAccountNo, bool IsSet);

/// <summary>Body of <c>PUT /api/agencies/me/bank</c>: both fields are required there.</summary>
public sealed record UpdateAgencyBankRequest(string? BankName, string? BankAccountNo);

/// <summary>Persistence of the agency's bank account (M5 MAIN-FLOW-FIX).</summary>
public interface IAgencyBankRepository
{
    /// <summary>The stored bank name and account number (empty strings when never set), or null when the agency does not exist.</summary>
    Task<(string BankName, string BankAccountNo)?> GetAsync(int agencyId, CancellationToken cancellationToken = default);

    /// <summary>Stores the account; false when the agency does not exist.</summary>
    Task<bool> SetAsync(int agencyId, string bankName, string bankAccountNo, CancellationToken cancellationToken = default);
}

/// <summary>Field rules of the bank account in one pure place, shared by the registration and the update.</summary>
public static class AgencyBankValidator
{
    // Column sizes of PARTNER_AGENCY (PartnerAgencyConfiguration): bank_name NVARCHAR(100), bank_account_no VARCHAR(30).
    public const int BankNameMaxLength = 100;
    public const int BankAccountNoMaxLength = 30;

    /// <summary>provenance: a bank account number is digits only; 6 is a floor that rejects an obviously wrong entry (no bank list in the sources).</summary>
    public const int BankAccountNoMinLength = 6;

    /// <summary>
    /// Trims and checks the pair. With <paramref name="required"/> false (registration) both may be left out; giving only one of them is an error
    /// in either mode.
    /// </summary>
    public static (string BankName, string BankAccountNo) Validate(string? bankName, string? bankAccountNo, bool required)
    {
        var name = (bankName ?? string.Empty).Trim();
        var account = new string((bankAccountNo ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (name.Length == 0 && account.Length == 0)
        {
            if (required)
            {
                errors["bankName"] = ["Bank name is required."];
                errors["bankAccountNo"] = ["Bank account number is required."];
                throw new ValidationException(errors);
            }

            return (string.Empty, string.Empty);
        }

        if (name.Length == 0)
        {
            errors["bankName"] = ["Bank name is required with the account number."];
        }
        else if (name.Length > BankNameMaxLength)
        {
            errors["bankName"] = [$"Bank name must be at most {BankNameMaxLength} characters."];
        }

        if (account.Length == 0)
        {
            errors["bankAccountNo"] = ["Bank account number is required with the bank name."];
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

/// <summary>The Partner reads and sets the account the agency's payout is transferred to (decisions D18, policy PartnerOnly).</summary>
public sealed class AgencyBankService(IAgencyBankRepository repository, ICurrentUser currentUser)
{
    private int AgencyId => currentUser.UserId is int id && currentUser.Role == UserRole.Partner
        ? id
        : throw new ForbiddenAccessException("Authenticated partner agency is required.");

    public async Task<AgencyBankDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var agencyId = AgencyId;
        var stored = await repository.GetAsync(agencyId, cancellationToken) ?? throw new NotFoundException("Agency", agencyId);
        return new AgencyBankDto(stored.BankName, stored.BankAccountNo, stored.BankName.Length > 0 && stored.BankAccountNo.Length > 0);
    }

    public async Task<AgencyBankDto> SetAsync(UpdateAgencyBankRequest? request, CancellationToken cancellationToken = default)
    {
        var agencyId = AgencyId;
        if (request is null)
        {
            throw new ValidationException("request", "Request body is required.");
        }

        var (name, account) = AgencyBankValidator.Validate(request.BankName, request.BankAccountNo, required: true);
        if (!await repository.SetAsync(agencyId, name, account, cancellationToken))
        {
            throw new NotFoundException("Agency", agencyId);
        }

        return new AgencyBankDto(name, account, true);
    }
}
