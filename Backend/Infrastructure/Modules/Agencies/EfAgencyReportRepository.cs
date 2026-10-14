using CommonService.Application.Features.Agencies;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Agencies;

/// <summary>EF Core implementation of <see cref="IAgencyReportRepository"/>: filters on <c>agency_id</c> only, no JOIN (PRD 5.2). Not verified on a live SQL Server.</summary>
public sealed class EfAgencyReportRepository(AppDbContext db) : IAgencyReportRepository
{
    public async Task<(IReadOnlyList<AgencyJobRow> Items, int Total)> ListJobsAsync(
        int agencyId, JobAssignmentStatus? status, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = db.JobAssignments.AsNoTracking()
            .Where(a => a.AgencyId == agencyId && (status == null || a.AssignmentStatus == status));
        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.AssignmentId)
            .Skip(skip).Take(take)
            .Select(a => new AgencyJobRow(
                a.AssignmentId, a.OrderId, a.WorkerId, a.ServiceTier, a.AssignmentSeq, a.AssignmentStatus,
                a.GrossAmount, a.CommissionRate, a.PayoutAmount, a.CreatedAt, a.CompletedAt))
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
