using CommonService.Domain.Enums;
using CommonService.Domain.StateMachines;

namespace CommonService.Domain.Entities;

/// <summary>
/// Worker is Single Table Inheritance (PRD 2.2). The database CHECK
/// (worker_type = 'FREELANCER' AND agency_id IS NULL) OR (worker_type = 'AGENCY_STAFF' AND agency_id IS NOT NULL)
/// is enforced here by the only two factories; WorkerType and AgencyId have private setters.
/// </summary>
public partial class Worker
{
    /// <summary>A freelancer starts PENDING and becomes IDLE after eKYC (PRD 2.8, decisions Q05).</summary>
    public static Worker CreateFreelancer(string phoneNumber, string nationalId, string fullName) => new()
    {
        PhoneNumber = phoneNumber,
        NationalId = nationalId,
        FullName = fullName,
        WorkerType = WorkerType.Freelancer,
        AgencyId = null,
        WorkStatus = WorkStatus.Pending,
    };

    /// <summary>Agency staff are imported by the agency, skip eKYC and the Admin queue (PRD 2.8): they start IDLE.</summary>
    public static Worker CreateAgencyStaff(int agencyId, string phoneNumber, string nationalId, string fullName)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(agencyId);
        return new()
        {
            PhoneNumber = phoneNumber,
            NationalId = nationalId,
            FullName = fullName,
            WorkerType = WorkerType.AgencyStaff,
            AgencyId = agencyId,
            WorkStatus = WorkStatus.Idle,
        };
    }

    /// <summary>Move the worker to <paramref name="next"/>, or throw <see cref="InvalidStateTransitionException"/>.</summary>
    public void TransitionTo(WorkStatus next) => WorkStatus = WorkerStateMachine.Instance.Require(WorkStatus, next);
}
