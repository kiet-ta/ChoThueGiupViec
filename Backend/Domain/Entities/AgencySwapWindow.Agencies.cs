namespace CommonService.Domain.Entities;

/// <remarks>
/// Table AGENCY_SWAP_WINDOW (SC-14). The 30-minute window in which an agency may replace the worker of an ASSIGNED Premium job with
/// another worker of its own (decisions Q09, spec 2.5 phase 2). One row per assignment; the window ends at <see cref="DeadlineAt"/>.
/// </remarks>
public partial class AgencySwapWindow
{
    /// <summary>agency_swap_window.assignment_id BIGINT (PK, FK JOB_ASSIGNMENT)</summary>
    public long AssignmentId { get; set; }

    /// <summary>agency_swap_window.agency_id INT (FK PARTNER_AGENCY)</summary>
    public int AgencyId { get; set; }

    /// <summary>agency_swap_window.opened_at DATETIME2</summary>
    public DateTime OpenedAt { get; set; }

    /// <summary>agency_swap_window.deadline_at DATETIME2: the earlier of opened_at + 30 min and shift start - 2 h</summary>
    public DateTime DeadlineAt { get; set; }

    /// <summary>agency_swap_window.reason NVARCHAR(255)</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>agency_swap_window.status VARCHAR(10): OPEN, SWAPPED or EXPIRED</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>agency_swap_window.new_worker_id INT NULL: the replacement when SWAPPED</summary>
    public int? NewWorkerId { get; set; }

    /// <summary>agency_swap_window.closed_at DATETIME2 NULL</summary>
    public DateTime? ClosedAt { get; set; }
}
