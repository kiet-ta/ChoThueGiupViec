using CommonService.Application.Features.Admin;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Admin;

/// <summary>Read-only dashboard counts over JOB_ORDER, JOB_ASSIGNMENT and DISPUTE_TICKET (contract admin.md 2.3, question A2).</summary>
public sealed class EfAdminDashboardRepository(AppDbContext db) : IAdminDashboardRepository
{
    // DISPUTE_TICKET.dispute_status values of an unresolved ticket (disputes.md).
    private static readonly string[] OpenDisputeStatuses = ["OPEN", "IN_REVIEW"];

    public async Task<DashboardCounts> GetCountsAsync(DashboardWindow window, CancellationToken cancellationToken = default)
    {
        // One DbContext cannot run queries in parallel, so the six counts run one after the other.
        var ordersToday = await db.JobOrders.AsNoTracking()
            .CountAsync(o => o.CreatedAt >= window.TodayStartUtc && o.CreatedAt < window.TodayEndUtc, cancellationToken);
        var ordersThisWeek = await db.JobOrders.AsNoTracking()
            .CountAsync(o => o.CreatedAt >= window.WeekStartUtc && o.CreatedAt < window.WeekEndUtc, cancellationToken);

        var inProgress = await db.JobAssignments.AsNoTracking()
            .CountAsync(a => a.AssignmentStatus == JobAssignmentStatus.CheckedIn
                || a.AssignmentStatus == JobAssignmentStatus.InProgress
                || a.AssignmentStatus == JobAssignmentStatus.AwaitingAcceptance, cancellationToken);
        var completedToday = await db.JobAssignments.AsNoTracking()
            .CountAsync(a => a.AssignmentStatus == JobAssignmentStatus.Completed
                && a.CompletedAt >= window.TodayStartUtc && a.CompletedAt < window.TodayEndUtc, cancellationToken);

        var open = await db.DisputeTickets.AsNoTracking()
            .CountAsync(d => OpenDisputeStatuses.Contains(d.DisputeStatus), cancellationToken);
        var nearSla = await db.DisputeTickets.AsNoTracking()
            .CountAsync(d => OpenDisputeStatuses.Contains(d.DisputeStatus) && d.SlaDueAt <= window.NearSlaBeforeUtc, cancellationToken);

        return new DashboardCounts(ordersToday, ordersThisWeek, inProgress, completedToday, open, nearSla);
    }
}
