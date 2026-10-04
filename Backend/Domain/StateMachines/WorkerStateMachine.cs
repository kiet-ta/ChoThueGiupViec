using CommonService.Domain.Enums;

namespace CommonService.Domain.StateMachines;

/// <summary>
/// WORKER.work_status. PRD 2.8: a freelancer becomes IDLE after eKYC (confidence >= 85). BR-05: after an approved absence the worker returns to IDLE.
/// Decisions Q15: Admin can lock and unlock an account. PENDING / BUSY / LOCKED are M1 naming proposals (see WorkStatus).
/// </summary>
public static class WorkerStateMachine
{
    public static readonly StateMachine<WorkStatus> Instance = new("Worker",
    [
        (WorkStatus.Pending, WorkStatus.Idle),
        (WorkStatus.Pending, WorkStatus.Locked),
        (WorkStatus.Idle, WorkStatus.Busy),
        (WorkStatus.Idle, WorkStatus.Locked),
        (WorkStatus.Busy, WorkStatus.Idle),
        (WorkStatus.Busy, WorkStatus.Locked),
        (WorkStatus.Locked, WorkStatus.Idle),
    ]);
}
