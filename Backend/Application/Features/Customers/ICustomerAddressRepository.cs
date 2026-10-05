using CommonService.Application.Interfaces.IRepositories;
using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Customers;

/// <summary>
/// Repository contract for CUSTOMER_ADDRESS (Customers module, BE-M1-05).
/// Every lookup is scoped by customer id so another customer's address is never returned.
/// </summary>
public interface ICustomerAddressRepository : IRepository<CustomerAddress, int>
{
    /// <summary>All addresses of the customer (tracked), default first, then newest first.</summary>
    Task<IReadOnlyList<CustomerAddress>> ListByCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>The address if it exists and belongs to the customer, otherwise null (tracked).</summary>
    Task<CustomerAddress?> GetOwnedAsync(int customerId, int addressId, CancellationToken cancellationToken = default);

    /// <summary>True when a JOB_ORDER points at the address (JOB_ORDER.address_id, decisions Q21 C5).</summary>
    Task<bool> IsReferencedByOrderAsync(int addressId, CancellationToken cancellationToken = default);
}
