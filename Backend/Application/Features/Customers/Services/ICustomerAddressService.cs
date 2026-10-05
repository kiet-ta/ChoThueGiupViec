using CommonService.Application.Features.Customers.Dtos;

namespace CommonService.Application.Features.Customers.Services;

/// <summary>
/// Address book of the signed-in customer (contract customers.md §2.2, BE-M1-05).
/// The customer id always comes from ICurrentUser in the controller, never from the request.
/// </summary>
public interface ICustomerAddressService
{
    Task<CustomerAddressResult<IReadOnlyList<AddressDto>>> ListAsync(int customerId, CancellationToken ct = default);

    Task<CustomerAddressResult<AddressDto>> GetAsync(int customerId, int addressId, CancellationToken ct = default);

    Task<CustomerAddressResult<AddressDto>> CreateAsync(int customerId, AddressRequestDto request, CancellationToken ct = default);

    Task<CustomerAddressResult<AddressDto>> UpdateAsync(int customerId, int addressId, AddressRequestDto request, CancellationToken ct = default);

    Task<CustomerAddressResult<object?>> DeleteAsync(int customerId, int addressId, CancellationToken ct = default);
}
