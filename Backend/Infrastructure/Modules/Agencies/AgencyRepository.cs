using CommonService.Application.Features.Agencies;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Agencies;

public class AgencyRepository : IAgencyRepository
{
    private readonly AppDbContext _db;

    public AgencyRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PartnerAgency?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _db.PartnerAgencies.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<PartnerAgency?> GetByTaxCodeAsync(string taxCode, CancellationToken cancellationToken = default)
    {
        return await _db.PartnerAgencies.FirstOrDefaultAsync(a => a.TaxCode == taxCode, cancellationToken);
    }

    public async Task<PartnerAgency?> GetByBusinessRegistrationNumberAsync(string businessRegistrationNumber, CancellationToken cancellationToken = default)
    {
        return await _db.PartnerAgencies.FirstOrDefaultAsync(a => a.BusinessRegistrationNumber == businessRegistrationNumber, cancellationToken);
    }

    public async Task<bool> ExistsByTaxCodeOrBusinessRegistrationNumberAsync(string taxCode, string businessRegistrationNumber, CancellationToken cancellationToken = default)
    {
        return await _db.PartnerAgencies.AnyAsync(
            a => a.TaxCode == taxCode || a.BusinessRegistrationNumber == businessRegistrationNumber,
            cancellationToken);
    }

    public async Task<PartnerAgency> AddAsync(PartnerAgency entity, CancellationToken cancellationToken = default)
    {
        await _db.PartnerAgencies.AddAsync(entity, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public void Update(PartnerAgency entity)
    {
        _db.PartnerAgencies.Update(entity);
    }

    public void Delete(PartnerAgency entity)
    {
        _db.PartnerAgencies.Remove(entity);
    }

    public async Task<IReadOnlyList<PartnerAgency>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        var list = await _db.PartnerAgencies.ToListAsync(cancellationToken);
        return list;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}