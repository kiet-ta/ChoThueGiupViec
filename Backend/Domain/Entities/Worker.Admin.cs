using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

public partial class Worker
{
    /// <summary>
    /// Admin unlock of a LOCKED freelancer (decisions Q15): back to IDLE. The state machine stays the authority; whether the
    /// worker may be unlocked (freelancer, eKYC approved) is decided by the caller.
    /// </summary>
    public void Unlock()
    {
        TransitionTo(WorkStatus.Idle);
        UpdatedAt = DateTime.UtcNow;
    }
}
