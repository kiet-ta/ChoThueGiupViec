using Backend.Application.Features.Agencies;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Infrastructure.Modules.Agencies;

/// <summary>
/// Entity Framework implementation of the agency repository
/// </summary>
public class EfAgencyRepository : IAgencyRepository
{
    private readonly AppDbContext _dbContext;

    public EfAgencyRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> TaxCodeExistsAsync(string taxCode, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<PartnerAgency>()
            .AnyAsync(a => a.TaxCode == taxCode, cancellationToken);
    }

    public async Task AddAsync(PartnerAgency agency, CancellationToken cancellationToken = default)
    {
        await _dbContext.Set<PartnerAgency>().AddAsync(agency, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}