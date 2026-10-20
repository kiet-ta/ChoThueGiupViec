using CommonService.Application.Features.Dispatch;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Dispatch;

/// <summary><c>INCIDENT_LOG</c> with the worker of its assignment, read only (no navigation properties: plain ids).</summary>
public sealed class EfAdminIncidentQuery(AppDbContext db) : IAdminIncidentQuery
{
    public async Task<IReadOnlyList<AdminIncidentDto>> ListAsync(int limit, CancellationToken cancellationToken = default)
    {
        var rows = await (
            from i in db.IncidentLogs.AsNoTracking()
            join a in db.JobAssignments.AsNoTracking() on i.AssignmentId equals a.AssignmentId
            orderby i.ReportedAt descending, i.IncidentId descending
            select new
            {
                i.IncidentId,
                i.AssignmentId,
                a.WorkerId,
                i.IncidentType,
                i.Description,
                i.PhotoUrl,
                i.Latitude,
                i.Longitude,
                i.ReportedAt,
                i.PenaltyWaived,
                i.RedispatchStatus,
            })
            .Take(Math.Clamp(limit, 1, AdminIncidentStatus.MaxListed))
            .ToListAsync(cancellationToken);

        return rows.Select(r =>
        {
            var reported = DateTime.SpecifyKind(r.ReportedAt, DateTimeKind.Utc);
            return new AdminIncidentDto(
                r.IncidentId,
                r.AssignmentId,
                r.WorkerId,
                r.IncidentType,
                r.Description ?? string.Empty,
                string.IsNullOrWhiteSpace(r.PhotoUrl) ? null : r.PhotoUrl,
                r.Latitude,
                r.Longitude,
                reported,
                r.PenaltyWaived,
                AdminIncidentStatus.ToContract(r.RedispatchStatus),
                reported.AddMinutes(IncidentService.RedispatchWindowMinutes),
                null);
        }).ToList();
    }
}
