using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Agencies;

/// <summary>Persistence port of the Agencies module (registration part; later M5 tickets extend it).</summary>
public interface IAgencyRepository
{
    /// <summary>True when an agency with this exact tax code exists.</summary>
    Task<bool> TaxCodeExistsAsync(string taxCode, CancellationToken cancellationToken = default);

    /// <summary>True when an agency with this contact email exists (the caller passes the lower-cased email).</summary>
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the agency and saves. When the unique index on <c>tax_code</c> is violated by a concurrent registration,
    /// throws <see cref="CommonService.Application.Exceptions.BusinessRuleViolationException"/> with code
    /// <see cref="AgencyErrorCodes.TaxCodeExists"/> (HTTP 409), never a 500.
    /// </summary>
    Task AddAsync(PartnerAgency agency, CancellationToken cancellationToken = default);
}
