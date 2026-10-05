using CommonService.Application.Features.Customers.Dtos;

namespace CommonService.Application.Features.Customers.Services;

/// <summary>
/// Read and update the signed-in customer's own profile (contract customers.md §2.1, BE-M1-04).
/// The customer id always comes from ICurrentUser in the controller, never from the request.
/// </summary>
public interface ICustomerProfileService
{
    Task<CustomerProfileResult> GetAsync(int customerId, CancellationToken ct = default);

    Task<CustomerProfileResult> UpdateAsync(int customerId, UpdateCustomerProfileDto request, CancellationToken ct = default);
}
