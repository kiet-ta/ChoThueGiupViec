using CommonService.Application.Features.Agencies;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Agencies;

/// <summary>EF Core implementation of <see cref="IWorkerImportRepository"/>. Not verified against a live SQL Server on this host.</summary>
public sealed class EfWorkerImportRepository(AppDbContext db) : IWorkerImportRepository
{
    public async Task<DateTime?> GetGuaranteeSignedAtAsync(int agencyId, CancellationToken cancellationToken = default) =>
        await db.PartnerAgencies.AsNoTracking()
            .Where(a => a.AgencyId == agencyId)
            .Select(a => a.GuaranteeSignedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> SetGuaranteeAsync(int agencyId, DateTime signedAtUtc, string fileUrl, CancellationToken cancellationToken = default)
    {
        var agency = await db.PartnerAgencies.FirstOrDefaultAsync(a => a.AgencyId == agencyId, cancellationToken);
        if (agency is null)
        {
            return false;
        }

        agency.GuaranteeSignedAt = signedAtUtc;
        agency.GuaranteeFileUrl = fileUrl.Length > 500 ? fileUrl[..500] : fileUrl;
        return true;
    }

    public async Task<IReadOnlySet<string>> FindExistingPhonesAsync(IReadOnlyCollection<string> phones, CancellationToken cancellationToken = default) =>
        (await db.Workers.AsNoTracking().Where(w => phones.Contains(w.PhoneNumber)).Select(w => w.PhoneNumber).ToListAsync(cancellationToken))
        .ToHashSet(StringComparer.Ordinal);

    public async Task<IReadOnlySet<string>> FindExistingNationalIdsAsync(IReadOnlyCollection<string> nationalIds, CancellationToken cancellationToken = default) =>
        (await db.Workers.AsNoTracking().Where(w => nationalIds.Contains(w.NationalId)).Select(w => w.NationalId).ToListAsync(cancellationToken))
        .ToHashSet(StringComparer.Ordinal);

    public void AddWorkers(IEnumerable<Worker> workers) => db.Workers.AddRange(workers);
}
