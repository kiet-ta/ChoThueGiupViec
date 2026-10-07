using CommonService.Application.Features.Admin.Dtos;
using CommonService.Application.Interfaces.Ports;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Admin.Services;

public interface IAdminDashboardService
{
    Task<DashboardDto> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The Admin operations dashboard (BE-M6-06b, contract admin.md 2.3 and question A2). Days and the ISO week are those of
/// Asia/Ho_Chi_Minh (decision G-3) and are converted to UTC for the queries.
/// </summary>
public sealed class AdminDashboardService(IAdminDashboardRepository dashboard, IClock clock, IOptions<AdminDashboardOptions> options) : IAdminDashboardService
{
    public async Task<DashboardDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var window = WindowFor(now, clock, options.Value.DisputeNearSlaHours);
        var counts = await dashboard.GetCountsAsync(window, cancellationToken);

        return new DashboardDto
        {
            GeneratedAt = DateTime.SpecifyKind(now, DateTimeKind.Utc),
            Orders = new DashboardOrdersDto { Today = counts.OrdersToday, ThisWeek = counts.OrdersThisWeek },
            Shifts = new DashboardShiftsDto { InProgress = counts.ShiftsInProgress, CompletedToday = counts.ShiftsCompletedToday },
            Disputes = new DashboardDisputesDto { Open = counts.DisputesOpen, NearSla = counts.DisputesNearSla },
        };
    }

    /// <summary>
    /// Today = the local day of <paramref name="nowUtc"/>; this week = Monday 00:00 of that day's ISO week to the next Monday 00:00 (local),
    /// so a Sunday evening still belongs to the week that is ending. Both are returned as UTC instants.
    /// </summary>
    public static DashboardWindow WindowFor(DateTime nowUtc, IClock clock, int nearSlaHours)
    {
        var localToday = DateOnly.FromDateTime(clock.ToLocal(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)));
        var daysSinceMonday = ((int)localToday.DayOfWeek + 6) % 7; // Monday = 0 ... Sunday = 6
        var weekStart = localToday.AddDays(-daysSinceMonday);

        DateTime Utc(DateOnly day) =>
            DateTime.SpecifyKind(clock.ToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified)), DateTimeKind.Utc); // the database layer rejects any other Kind

        return new DashboardWindow(
            Utc(localToday), Utc(localToday.AddDays(1)),
            Utc(weekStart), Utc(weekStart.AddDays(7)),
            DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc).AddHours(nearSlaHours));
    }
}
