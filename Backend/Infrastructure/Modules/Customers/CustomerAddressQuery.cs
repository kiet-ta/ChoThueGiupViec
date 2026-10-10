using CommonService.Application.Features.Customers;
using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Modules.Customers;

/// <summary>
/// Real <see cref="ICustomerAddressQuery"/> (contract booking.md B2): Booking's read access to the address book, over the Customers
/// module's own repository, so Booking never reads CUSTOMER_ADDRESS itself. Replaces <c>FakeCustomerAddressQuery</c>.
/// </summary>
public sealed class CustomerAddressQuery(ICustomerAddressRepository addresses) : ICustomerAddressQuery
{
    public async Task<CustomerAddressInfo?> GetOwnedAsync(int customerId, int addressId, CancellationToken cancellationToken = default)
    {
        var address = await addresses.GetOwnedAsync(customerId, addressId, cancellationToken);
        return address is null
            ? null
            : new CustomerAddressInfo(address.AddressId, address.TotalAreaM2, address.Latitude, address.Longitude);
    }
}
