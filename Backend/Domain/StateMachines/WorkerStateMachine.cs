using CommonService.Domain.Enums;

namespace CommonService.Domain.StateMachines;

/// <summary>
/// WORKER.work_status. PRD 2.8: a freelancer becomes IDLE after eKYC (confidence >= 85). BR-05: after an approved absence the worker returns to IDLE.
/// Decisions Q15: Admin can lock and unlock an account. PENDING / BUSY / LOCKED are M1 naming proposals (see WorkStatus).
/// AGENCY-UAT-02: Added Retiring and Retired for agency worker retirement. Transitions: Idle|Busy|Locked -> Retiring, Retiring -> Retired. No edge leaves Retired.
/// </summary>
public static class WorkerStateMachine
{
    public static readonly StateMachine<WorkStatus> Instance = new("Worker",
    [
        (WorkStatus.Pending, WorkStatus.Idle),
        (WorkStatus.Pending, WorkStatus.Locked),
        (WorkStatus.Idle, WorkStatus.Busy),
        (WorkStatus.Idle, WorkStatus.Locked),
        (WorkStatus.Idle, WorkStatus.Retiring),
        (WorkStatus.Busy, WorkStatus.Idle),
        (WorkStatus.Busy, WorkStatus.Locked),
        (WorkStatus.Busy, WorkStatus.Retiring),
        (WorkStatus.Locked, WorkStatus.Idle),
        (WorkStatus.Locked, WorkStatus.Retiring),
        (WorkStatus.Retiring, WorkStatus.Retired),
    ]);
}
