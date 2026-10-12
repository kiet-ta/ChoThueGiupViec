using CommonService.Application.Interfaces.IRepositories;
using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Agencies;

/// <summary>
/// Repository contract for PartnerSubscription aggregate root.
/// Lives inside Application/Features/Agencies per module encapsulation convention.
/// </summary>
public interface ISubscriptionRepository : IRepository<PartnerSubscription, int>
{
    Task<PartnerSubscription?> GetByIdAsync(int subscriptionId, CancellationToken cancellationToken = default);
    Task<PartnerSubscription?> GetByAgencyIdAsync(int agencyId, CancellationToken cancellationToken = default);
    Task<PartnerSubscription?> GetActiveSubscriptionByAgencyIdAsync(int agencyId, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}