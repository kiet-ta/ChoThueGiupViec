using CommonService.Domain.Enums;
using CommonService.Domain.StateMachines;

namespace CommonService.Domain.Entities;

public partial class JobOrder
{
    /// <summary>Move the order to <paramref name="next"/>, or throw <see cref="InvalidStateTransitionException"/>.</summary>
    public void TransitionTo(JobOrderStatus next) => OrderStatus = JobOrderStateMachine.Instance.Require(OrderStatus, next);
}
