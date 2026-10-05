using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <summary>
/// Order status derived from its assignments (decision D7). An order may need 2 workers (BR-02, assignment_seq 1 and 2);
/// the BR-02 fallback (one worker doing two consecutive shifts) is simply two assignments of the same worker.
/// </summary>
public partial class JobOrder
{
    private static readonly HashSet<JobAssignmentStatus> Accepted =
    [
        JobAssignmentStatus.Assigned, JobAssignmentStatus.CheckedIn, JobAssignmentStatus.InProgress,
        JobAssignmentStatus.AwaitingAcceptance, JobAssignmentStatus.Completed,
    ];

    /// <summary>
    /// Recompute the order status from the statuses of ALL its assignments (including cancelled/reassigned ones) and apply it:
    /// COMPLETED when <see cref="RequiredWorkers"/> assignments are COMPLETED; ASSIGNED when enough are accepted;
    /// otherwise DISPATCHING (a worker cancelled or had an incident: only the missing seat is re-dispatched).
    /// Applies only while the order is DISPATCHING or ASSIGNED. An ABSENT assignment is left to the Admin approval flow (BR-05):
    /// no automatic change. Returns true when the status changed.
    /// </summary>
    public bool SyncWithAssignments(IEnumerable<JobAssignmentStatus> assignmentStatuses)
    {
        if (OrderStatus is not (JobOrderStatus.Dispatching or JobOrderStatus.Assigned))
        {
            return false;
        }

        var statuses = assignmentStatuses.ToList();
        if (statuses.Contains(JobAssignmentStatus.Absent))
        {
            return false;
        }

        var completed = statuses.Count(s => s == JobAssignmentStatus.Completed);
        var accepted = statuses.Count(Accepted.Contains);
        var target = completed >= RequiredWorkers ? JobOrderStatus.Completed
            : accepted >= RequiredWorkers ? JobOrderStatus.Assigned
            : JobOrderStatus.Dispatching;

        if (target == OrderStatus)
        {
            return false;
        }

        TransitionTo(target);
        return true;
    }
}
