using CommonService.Domain.Entities;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Backend.Application.Features.Agencies.Repositories;

/// <summary>
/// Repository for agency operations
/// </summary>
public interface IAgencyRepository
{
    /// <summary>
    /// Gets an agency by its ID
    /// </summary>
    Task<PartnerAgency?> GetAsync(int agencyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all agencies
    /// </summary>
    Task<IReadOnlyList<PartnerAgency>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new agency
    /// </summary>
    Task AddAsync(PartnerAgency agency, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing agency
    /// </summary>
    Task UpdateAsync(PartnerAgency agency, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an agency
    /// </summary>
    Task DeleteAsync(int agencyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves all changes made in this repository
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}