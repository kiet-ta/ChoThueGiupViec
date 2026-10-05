using CommonService.Application.Features.Customers;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using CommonService.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Customers;

/// <summary>
/// CUSTOMER_ADDRESS repository of the Customers module (BE-M1-05).
/// </summary>
public class CustomerAddressRepository : EfRepository<CustomerAddress, int>, ICustomerAddressRepository
{
    public CustomerAddressRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<CustomerAddress>> ListByCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.AddressId)
            .ToListAsync(cancellationToken);
    }

    public async Task<CustomerAddress?> GetOwnedAsync(int customerId, int addressId, CancellationToken cancellationToken = default)
    {
        return await DbSet.FirstOrDefaultAsync(
            a => a.AddressId == addressId && a.CustomerId == customerId,
            cancellationToken);
    }

    public async Task<bool> IsReferencedByOrderAsync(int addressId, CancellationToken cancellationToken = default)
    {
        return await Context.JobOrders.AnyAsync(o => o.AddressId == addressId, cancellationToken);
    }
}
