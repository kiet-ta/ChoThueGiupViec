using CommonService.Application.Features.Customers;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using CommonService.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Customers;

/// <summary>
/// Customer repository implementation for the Customers module.
/// Lives strictly inside Infrastructure/Modules/Customers per module encapsulation convention.
/// </summary>
public class CustomerRepository : EfRepository<Customer, int>, ICustomerRepository
{
    public CustomerRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<Customer?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        return await DbSet.FirstOrDefaultAsync(c => c.PhoneNumber == phoneNumber, cancellationToken);
    }
}
