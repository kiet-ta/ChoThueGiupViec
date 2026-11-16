using CommonService.Application.Features.Admin;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Admin;

/// <summary>Read-only lookup over WORKER with the agency name of an agency worker: no tracking, no write method.</summary>
public class EfAdminWorkerSearchRepository(AppDbContext db) : IAdminWorkerSearchRepository
{
    public async Task<(IReadOnlyList<AdminWorkerRow> Items, int Total)> SearchAsync(
        AdminWorkerFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        // An explicit left join: Worker has no navigation property to PartnerAgency (0-JOIN model, PRD 5.2).
        var query =
            from w in db.Workers.AsNoTracking()
            join a in db.PartnerAgencies.AsNoTracking() on w.AgencyId equals (int?)a.AgencyId into agencies
            from agency in agencies.DefaultIfEmpty()
            select new { Worker = w, AgencyName = agency == null ? null : agency.LegalName };

        if (filter.WorkerType is { } type) query = query.Where(x => x.Worker.WorkerType == type);
        if (filter.WorkStatus is { } status) query = query.Where(x => x.Worker.WorkStatus == status);

        if (filter.Text is { } text)
        {
            var id = filter.WorkerId;
            query = query.Where(x =>
                (id != null && x.Worker.WorkerId == id)
                || x.Worker.FullName.Contains(text)
                || x.Worker.PhoneNumber.Contains(text)
                || x.Worker.NationalId.Contains(text));
        }

        var total = await query.CountAsync(cancellationToken);

        var exactId = filter.WorkerId ?? 0;
        var rows = await query
            .OrderByDescending(x => x.Worker.WorkerId == exactId) // the worker whose number was typed comes first
            .ThenByDescending(x => x.Worker.WorkerId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AdminWorkerRow(
                x.Worker.WorkerId, x.Worker.FullName, x.Worker.PhoneNumber, x.Worker.NationalId, x.Worker.WorkerType, x.Worker.AgencyId,
                x.AgencyName, x.Worker.WorkStatus, x.Worker.KycStatus, x.Worker.IsSuperFreelancer, x.Worker.RatingAvg, x.Worker.CompletedJobs))
            .ToListAsync(cancellationToken);

        return (rows, total);
    }
}
