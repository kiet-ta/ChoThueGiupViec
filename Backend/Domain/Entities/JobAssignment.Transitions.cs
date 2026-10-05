using CommonService.Domain.Enums;
using CommonService.Domain.StateMachines;

namespace CommonService.Domain.Entities;

public partial class JobAssignment
{
    /// <summary>Move the assignment to <paramref name="next"/>, or throw <see cref="InvalidStateTransitionException"/>.</summary>
    public void TransitionTo(JobAssignmentStatus next) => AssignmentStatus = JobAssignmentStateMachine.Instance.Require(AssignmentStatus, next);
}
