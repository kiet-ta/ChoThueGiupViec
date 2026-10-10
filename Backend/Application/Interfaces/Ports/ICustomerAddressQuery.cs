namespace CommonService.Application.Interfaces.Ports;

/// <param name="AddressId">CUSTOMER_ADDRESS.address_id.</param>
/// <param name="TotalAreaM2">S_total of the address (PRD 1.1), computed by the server.</param>
public sealed record CustomerAddressInfo(int AddressId, decimal TotalAreaM2, decimal Latitude, decimal Longitude);

/// <summary>
/// Booking's read access to the customer's address book (contract booking.md B2). Booking never reads CUSTOMER_ADDRESS itself.
/// Implemented by Customers (M1); consumed by Booking (M2).
/// </summary>
public interface ICustomerAddressQuery
{
    /// <summary>The address when it belongs to the customer, otherwise null (unknown id and someone else's address look the same).</summary>
    Task<CustomerAddressInfo?> GetOwnedAsync(int customerId, int addressId, CancellationToken cancellationToken = default);
}
