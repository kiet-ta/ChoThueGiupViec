using CommonService.Application.Features.Admin;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Admin;

/// <summary>Absence reports over <c>CHECK_IN_LOG</c> and the flat <c>JOB_ASSIGNMENT</c> node, read-only except for one conditional update (contract A3, A6).</summary>
public sealed class EfAbsenceRepository(AppDbContext db) : IAbsenceRepository
{
    /// <summary>The joined columns, shaped with member initialisers so that filters and paging translate to SQL.</summary>
    private sealed class Raw
    {
        public long AssignmentId { get; init; }
        public long OrderId { get; init; }
        public string OrderCode { get; init; } = string.Empty;
        public int WorkerId { get; init; }
        public string WorkerName { get; init; } = string.Empty;
        public WorkerType WorkerType { get; init; }
        public string? AgencyName { get; init; }
        public string CustomerName { get; init; } = string.Empty;
        public JobAssignmentStatus AssignmentStatus { get; init; }
        public decimal GrossAmount { get; init; }
        public decimal? AbsenceFeeAmount { get; init; }
        public DateTime CheckedInAt { get; init; }
        public DateTime? CustomerAbsentAt { get; init; }
        public bool GpsVerified { get; init; }
        public decimal DistanceM { get; init; }
        public decimal DeviceLat { get; init; }
        public decimal DeviceLng { get; init; }
        public byte CallAttempts { get; init; }
        public string? PhotoUrl { get; init; }
        public bool Rejected { get; init; }
    }

    public async Task<(IReadOnlyList<AbsenceReportRow> Items, int Total)> SearchAsync(
        string status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = Reports();
        query = status switch
        {
            AbsenceConstants.Approved => query.Where(r => r.AssignmentStatus == JobAssignmentStatus.Absent),
            AbsenceConstants.Rejected => query.Where(r => r.AssignmentStatus != JobAssignmentStatus.Absent && r.Rejected),
            _ => query.Where(r => r.AssignmentStatus != JobAssignmentStatus.Absent && !r.Rejected),
        };

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(r => r.CustomerAbsentAt).ThenBy(r => r.AssignmentId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (rows.Select(ToRow).ToList(), total);
    }

    public async Task<AbsenceReportRow?> GetAsync(long assignmentId, CancellationToken cancellationToken = default)
    {
        var raw = await Reports().Where(r => r.AssignmentId == assignmentId).FirstOrDefaultAsync(cancellationToken);
        return raw is null ? null : ToRow(raw);
    }

    public async Task<bool> TryMarkAbsentAsync(long assignmentId, decimal absenceFeeAmount, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var changed = await db.JobAssignments
            .Where(a => a.AssignmentId == assignmentId && a.AssignmentStatus == JobAssignmentStatus.CheckedIn)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.AssignmentStatus, JobAssignmentStatus.Absent)
                .SetProperty(a => a.AbsenceFeeAmount, (decimal?)absenceFeeAmount)
                .SetProperty(a => a.UpdatedAt, nowUtc), cancellationToken);
        return changed == 1;
    }

    private static AbsenceReportRow ToRow(Raw r) => new(
        r.AssignmentId, r.OrderId, r.OrderCode, r.WorkerId, r.WorkerName, r.WorkerType, r.AgencyName, r.CustomerName,
        r.AssignmentStatus, r.GrossAmount, r.AbsenceFeeAmount, r.CheckedInAt, r.CustomerAbsentAt ?? default,
        r.GpsVerified, r.DistanceM, r.DeviceLat, r.DeviceLng, r.CallAttempts, r.PhotoUrl, r.Rejected);

    /// <summary>One row per check-in that carries <c>customer_absent_at</c>, with the names and the "rejected" flag of the audit log.</summary>
    private IQueryable<Raw> Reports() =>
        from l in db.CheckInLogs.AsNoTracking()
        join a in db.JobAssignments.AsNoTracking() on l.AssignmentId equals a.AssignmentId
        join o in db.JobOrders.AsNoTracking() on a.OrderId equals o.OrderId
        join c in db.Customers.AsNoTracking() on a.CustomerId equals c.CustomerId
        join w in db.Workers.AsNoTracking() on a.WorkerId equals w.WorkerId
        where l.CustomerAbsentAt != null
        select new Raw
        {
            AssignmentId = a.AssignmentId,
            OrderId = a.OrderId,
            OrderCode = o.OrderCode,
            WorkerId = a.WorkerId,
            WorkerName = w.FullName,
            WorkerType = w.WorkerType,
            AgencyName = db.PartnerAgencies.Where(p => p.AgencyId == w.AgencyId).Select(p => p.LegalName).FirstOrDefault(),
            CustomerName = c.FullName,
            AssignmentStatus = a.AssignmentStatus,
            GrossAmount = a.GrossAmount,
            AbsenceFeeAmount = a.AbsenceFeeAmount,
            CheckedInAt = l.CheckedInAt,
            CustomerAbsentAt = l.CustomerAbsentAt,
            GpsVerified = l.GpsVerified,
            DistanceM = l.DistanceM,
            DeviceLat = l.DeviceLat,
            DeviceLng = l.DeviceLng,
            CallAttempts = l.CallAttempts,
            PhotoUrl = l.FallbackPhotoUrl,
            Rejected = db.AdminAuditLogs.Any(x =>
                x.EntityType == AbsenceConstants.AuditEntityType
                && x.FieldName == AbsenceConstants.AuditFieldName
                && x.NewValue == AbsenceConstants.Rejected
                && x.EntityId == a.AssignmentId.ToString()),
        };
}
