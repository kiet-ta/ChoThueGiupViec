using CommonService.Application.Features.Agencies;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Agencies;

/// <summary>EF Core implementation of <see cref="IAgencyBankRepository"/>.</summary>
public sealed class EfAgencyBankRepository(AppDbContext db) : IAgencyBankRepository
{
    public async Task<(string BankName, string BankAccountNo)?> GetAsync(int agencyId, CancellationToken cancellationToken = default)
    {
        var row = await db.PartnerAgencies.AsNoTracking()
            .Where(a => a.AgencyId == agencyId)
            .Select(a => new { a.BankName, a.BankAccountNo })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : (row.BankName, row.BankAccountNo);
    }

    public async Task<bool> SetAsync(int agencyId, string bankName, string bankAccountNo, CancellationToken cancellationToken = default) =>
        await db.PartnerAgencies.Where(a => a.AgencyId == agencyId)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.BankName, bankName).SetProperty(a => a.BankAccountNo, bankAccountNo), cancellationToken) > 0;
}
