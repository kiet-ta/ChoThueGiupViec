using CommonService.Application.Interfaces.IRepositories;
using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Agencies;

/// <summary>
/// Repository contract for PartnerAgency aggregate root.
/// Lives inside Application/Features/Agencies per module encapsulation convention.
/// </summary>
public interface IAgencyRepository : IRepository<PartnerAgency, int>
{
    Task<PartnerAgency?> GetByIdAsync(int agencyId, CancellationToken cancellationToken = default);
    Task<PartnerAgency?> GetByTaxCodeAsync(string taxCode, CancellationToken cancellationToken = default);
    Task<bool> ExistsByTaxCodeAsync(string taxCode, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}