using System.Collections.Concurrent;
using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

/// <summary>In-memory address book for tests and for running Booking before the Customers implementation is wired. Empty by default.</summary>
public sealed class FakeCustomerAddressQuery : ICustomerAddressQuery
{
    private readonly ConcurrentDictionary<(int CustomerId, int AddressId), CustomerAddressInfo> _addresses = new();

    public void Add(int customerId, CustomerAddressInfo address) => _addresses[(customerId, address.AddressId)] = address;

    public Task<CustomerAddressInfo?> GetOwnedAsync(int customerId, int addressId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_addresses.TryGetValue((customerId, addressId), out var address) ? address : null);
}
