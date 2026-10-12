using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Agencies;

/// <summary>EF Core implementation of <see cref="IAgencyRepository"/> on <c>PARTNER_AGENCY</c>.</summary>
public sealed class EfAgencyRepository(AppDbContext dbContext) : IAgencyRepository
{
    public Task<bool> TaxCodeExistsAsync(string taxCode, CancellationToken cancellationToken = default) =>
        dbContext.PartnerAgencies.AsNoTracking().AnyAsync(a => a.TaxCode == taxCode, cancellationToken);

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
        dbContext.PartnerAgencies.AsNoTracking().AnyAsync(a => a.ContactEmail == email, cancellationToken);

    public async Task AddAsync(PartnerAgency agency, CancellationToken cancellationToken = default)
    {
        dbContext.PartnerAgencies.Add(agency);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (SqlUniqueViolation.IsUniqueViolation(ex))
        {
            // Two registrations with the same tax code passed the existence check at the same time.
            // PARTNER_AGENCY has exactly one unique index besides the primary key (tax_code).
            dbContext.Entry(agency).State = EntityState.Detached;
            throw new BusinessRuleViolationException(
                "An agency with this tax code is already registered.",
                ex,
                AgencyErrorCodes.TaxCodeExists);
        }
    }
}
