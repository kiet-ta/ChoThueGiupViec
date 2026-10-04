namespace CommonService.Domain.Enums;

/// <summary>WORKER.work_status. Initial value = first member. IDLE is in the PRD (2.8, BR-05); PENDING (not yet activated), BUSY and LOCKED (Q15 account lock) are M1 naming proposals for M4 to confirm in workers.md.</summary>
public enum WorkStatus
{
    Pending,
    Idle,
    Busy,
    Locked
}
