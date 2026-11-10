using CommonService.Application.Features.Agencies;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Agencies;

/// <summary>EF Core implementation of <see cref="IAgencyReportRepository"/>: filters on <c>agency_id</c> only, no JOIN (PRD 5.2). Not verified on a live SQL Server.</summary>
public class EfAgencyReportRepository(AppDbContext db) : IAgencyReportRepository
{
    public async Task<(IReadOnlyList<AgencyJobRow> Items, int Total)> ListJobsAsync(
        int agencyId, JobAssignmentStatus? status, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = db.JobAssignments.AsNoTracking()
            .Where(a => a.AgencyId == agencyId && (status == null || a.AssignmentStatus == status))
            .Join(db.Workers.AsNoTracking(),
                a => a.WorkerId,
                w => w.WorkerId,
                (a, w) => new { Assignment = a, WorkerName = w.FullName });
        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.Assignment.CreatedAt).ThenByDescending(x => x.Assignment.AssignmentId)
            .Skip(skip).Take(take)
            .Select(x => new AgencyJobRow(
                x.Assignment.AssignmentId, x.Assignment.OrderId, x.Assignment.WorkerId, x.WorkerName, x.Assignment.ServiceTier, x.Assignment.AssignmentSeq, x.Assignment.AssignmentStatus,
                x.Assignment.GrossAmount, x.Assignment.CommissionRate, x.Assignment.PayoutAmount, x.Assignment.CreatedAt, x.Assignment.CompletedAt))
            .ToListAsync(cancellationToken);
        return (rows, total);
    }

    public async Task<IReadOnlyList<WorkerPerformanceRow>> PerformanceAsync(
        int agencyId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var groups = await db.JobAssignments.AsNoTracking()
            .Where(a => a.AgencyId == agencyId && a.CreatedAt >= fromUtc && a.CreatedAt < toUtc)
            .GroupBy(a => a.WorkerId)
            .Select(g => new
            {
                WorkerId = g.Key,
                Assigned = g.Count(),
                Completed = g.Count(a => a.AssignmentStatus == JobAssignmentStatus.Completed),
                Cancelled = g.Count(a => a.AssignmentStatus == JobAssignmentStatus.Cancelled
                    || a.AssignmentStatus == JobAssignmentStatus.CancelledByWorker
                    || a.AssignmentStatus == JobAssignmentStatus.Reassigned),
                Absent = g.Count(a => a.AssignmentStatus == JobAssignmentStatus.Absent),
                Incident = g.Count(a => a.AssignmentStatus == JobAssignmentStatus.Incident),
                PayoutTotal = g.Where(a => a.AssignmentStatus == JobAssignmentStatus.Completed).Sum(a => (decimal?)a.PayoutAmount) ?? 0m,
            })
            .ToListAsync(cancellationToken);

        var names = await GetWorkerNamesAsync(groups.Select(g => g.WorkerId).ToList(), cancellationToken);
        return groups
            .Select(g => new WorkerPerformanceRow(
                g.WorkerId, names.TryGetValue(g.WorkerId, out var name) ? name : string.Empty,
                g.Assigned, g.Completed, g.Cancelled, g.Absent, g.Incident, g.PayoutTotal))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<int, string>> GetWorkerNamesAsync(IReadOnlyCollection<int> workerIds, CancellationToken cancellationToken = default)
    {
        if (workerIds.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        var rows = await db.Workers.AsNoTracking()
            .Where(w => workerIds.Contains(w.WorkerId))
            .Select(w => new { w.WorkerId, w.FullName })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.WorkerId, r => r.FullName);
    }
}
