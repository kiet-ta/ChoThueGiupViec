using CommonService.Application.Features.Agencies;
using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core repository implementation for PartnerAgency
/// </summary>
public class EfAgencyRepository : EfRepository<PartnerAgency, int>, IAgencyRepository
{
    public EfAgencyRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<PartnerAgency?> GetByTaxCodeAsync(string taxCode, CancellationToken cancellationToken = default)
    {
        return await DbSet.FirstOrDefaultAsync(x => x.TaxCode == taxCode, cancellationToken);
    }

    public Task<bool> ExistsByTaxCodeAsync(string taxCode, CancellationToken cancellationToken = default)
    {
        return DbSet.AnyAsync(x => x.TaxCode == taxCode, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await Context.SaveChangesAsync(cancellationToken);
    }
}