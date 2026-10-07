namespace CommonService.Application.Features.Admin;

/// <summary>Thresholds of the operations dashboard, bound from the "Admin" configuration section (contract admin.md question A2).</summary>
public sealed class AdminDashboardOptions
{
    /// <summary>An open dispute due within this many hours (overdue included) counts as "near SLA". Chosen by the designer, not decided by the leader.</summary>
    public int DisputeNearSlaHours { get; set; } = 6;
}

/// <summary>The instants (UTC) the counts are taken against: Asia/Ho_Chi_Minh day and ISO week converted by <c>IClock</c>.</summary>
public sealed record DashboardWindow(
    DateTime TodayStartUtc, DateTime TodayEndUtc, DateTime WeekStartUtc, DateTime WeekEndUtc, DateTime NearSlaBeforeUtc);

/// <summary>The raw counts of the dashboard.</summary>
public sealed record DashboardCounts(
    int OrdersToday, int OrdersThisWeek, int ShiftsInProgress, int ShiftsCompletedToday, int DisputesOpen, int DisputesNearSla);

/// <summary>
/// Read-only counts over <c>JOB_ORDER</c>, <c>JOB_ASSIGNMENT</c> and <c>DISPUTE_TICKET</c> (BE-M6-06b; contract question A6: no read port
/// exists yet, so the Admin module reads them directly and writes nothing).
/// </summary>
public interface IAdminDashboardRepository
{
    Task<DashboardCounts> GetCountsAsync(DashboardWindow window, CancellationToken cancellationToken = default);
}
