using CommonService.Application.Interfaces.IRepositories;
using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Agencies;

/// <summary>
/// Repository contract for Agency aggregate root.
/// Lives inside Application/Features/Agencies per module encapsulation convention.
/// </summary>
public interface IAgencyRepository : IRepository<PartnerAgency, int>
{
    Task<PartnerAgency?> GetByTaxCodeAsync(string taxCode, CancellationToken cancellationToken = default);
    Task<PartnerAgency?> GetByBusinessRegistrationNumberAsync(string businessRegistrationNumber, CancellationToken cancellationToken = default);
    Task<bool> ExistsByTaxCodeOrBusinessRegistrationNumberAsync(string taxCode, string businessRegistrationNumber, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}