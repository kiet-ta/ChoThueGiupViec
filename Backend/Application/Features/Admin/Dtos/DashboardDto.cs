namespace CommonService.Application.Features.Admin.Dtos;

/// <summary>The operations dashboard (contract admin.md 2.3): exactly these three groups, no other metric (decision G-7).</summary>
public sealed class DashboardDto
{
    public DateTime GeneratedAt { get; init; }
    public DashboardOrdersDto Orders { get; init; } = new();
    public DashboardShiftsDto Shifts { get; init; } = new();
    public DashboardDisputesDto Disputes { get; init; } = new();
}

public sealed class DashboardOrdersDto
{
    /// <summary>Orders created today (Asia/Ho_Chi_Minh).</summary>
    public int Today { get; init; }

    /// <summary>Orders created this ISO week (Monday start, Asia/Ho_Chi_Minh).</summary>
    public int ThisWeek { get; init; }
}

public sealed class DashboardShiftsDto
{
    /// <summary>Assignments CHECKED_IN, IN_PROGRESS or AWAITING_ACCEPTANCE.</summary>
    public int InProgress { get; init; }

    /// <summary>Assignments COMPLETED today (Asia/Ho_Chi_Minh).</summary>
    public int CompletedToday { get; init; }
}

public sealed class DashboardDisputesDto
{
    /// <summary>Disputes OPEN or IN_REVIEW ("Tranh chấp tồn").</summary>
    public int Open { get; init; }

    /// <summary>Open disputes due within the configured hours, overdue ones included.</summary>
    public int NearSla { get; init; }
}
