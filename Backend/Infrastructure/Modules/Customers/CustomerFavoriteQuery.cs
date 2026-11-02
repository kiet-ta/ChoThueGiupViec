using CommonService.Application.Features.Customers;
using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Modules.Customers;

/// <summary>Real <see cref="ICustomerFavoriteQuery"/> over the Customers module's own repository.</summary>
public sealed class CustomerFavoriteQuery(IFavoriteWorkerRepository favorites) : ICustomerFavoriteQuery
{
    public async Task<bool> IsFavoriteAsync(int customerId, int workerId, CancellationToken cancellationToken = default)
    {
        var list = await favorites.ListByCustomerAsync(customerId, cancellationToken);
        return list.Any(f => f.WorkerId == workerId);
    }
}
