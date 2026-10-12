using CommonService.Domain.Entities;

namespace Backend.Application.Features.Agencies;

/// <summary>
/// Repository for agency-related operations
/// </summary>
public interface IAgencyRepository
{
    /// <summary>
    /// Checks if an agency with the specified tax code already exists
    /// </summary>
    /// <param name="taxCode">The tax code to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if an agency with the tax code exists, false otherwise</returns>
    Task<bool> TaxCodeExistsAsync(string taxCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new agency to the repository
    /// </summary>
    /// <param name="agency">The agency to add</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AddAsync(PartnerAgency agency, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves all changes made in this repository
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}