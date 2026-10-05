namespace CommonService.Domain.Enums;

/// <summary>JOB_ASSIGNMENT.assignment_status. Initial value = first member. Names from overview section 6, PRD BR-05/BR-07/BR-10, drawio (REASSIGNED) and decisions Q15 (a distinct "cancelled by worker" status).</summary>
public enum JobAssignmentStatus
{
    Offered,
    Assigned,
    CheckedIn,
    InProgress,
    AwaitingAcceptance,
    Completed,
    Cancelled,
    CancelledByWorker,
    Absent,
    Incident,
    Reassigned
}
