using CommonService.Application.Interfaces.IRepositories;
using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Customers;

/// <summary>
/// Repository contract for Customer aggregate root.
/// Lives inside Application/Features/Customers per module encapsulation convention.
/// </summary>
public interface ICustomerRepository : IRepository<Customer, int>
{
    Task<Customer?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);
}
