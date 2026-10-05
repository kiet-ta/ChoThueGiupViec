using CommonService.Domain.Enums;

namespace CommonService.Domain.StateMachines;

/// <summary>
/// JOB_ASSIGNMENT life cycle (the flat execution node). Path of PRD 4.2: OFFERED -> ASSIGNED -> CHECKED_IN -> IN_PROGRESS ->
/// AWAITING_ACCEPTANCE -> COMPLETED. Exceptions: BR-05 (ABSENT after check-in), BR-07 (rework sends AWAITING_ACCEPTANCE back to IN_PROGRESS),
/// BR-10 (INCIDENT on the road, then REASSIGNED or CANCELLED), decisions Q15 (CANCELLED_BY_WORKER), drawio (REASSIGNED).
/// Deliberately minimal: a transition that no source requires is not listed; add it through a ticket when a module needs it.
/// </summary>
public static class JobAssignmentStateMachine
{
    public static readonly StateMachine<JobAssignmentStatus> Instance = new("JobAssignment",
    [
        (JobAssignmentStatus.Offered, JobAssignmentStatus.Assigned),
        (JobAssignmentStatus.Offered, JobAssignmentStatus.Cancelled),
        (JobAssignmentStatus.Assigned, JobAssignmentStatus.CheckedIn),
        (JobAssignmentStatus.Assigned, JobAssignmentStatus.Cancelled),
        (JobAssignmentStatus.Assigned, JobAssignmentStatus.CancelledByWorker),
        (JobAssignmentStatus.Assigned, JobAssignmentStatus.Reassigned),
        (JobAssignmentStatus.Assigned, JobAssignmentStatus.Incident),
        (JobAssignmentStatus.CheckedIn, JobAssignmentStatus.InProgress),
        (JobAssignmentStatus.CheckedIn, JobAssignmentStatus.Absent),
        (JobAssignmentStatus.InProgress, JobAssignmentStatus.AwaitingAcceptance),
        (JobAssignmentStatus.AwaitingAcceptance, JobAssignmentStatus.InProgress),
        (JobAssignmentStatus.AwaitingAcceptance, JobAssignmentStatus.Completed),
        (JobAssignmentStatus.Incident, JobAssignmentStatus.Reassigned),
        (JobAssignmentStatus.Incident, JobAssignmentStatus.Cancelled),
    ]);
}
