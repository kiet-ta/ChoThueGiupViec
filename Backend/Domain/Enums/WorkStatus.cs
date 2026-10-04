namespace CommonService.Domain.Enums;

/// <summary>WORKER.work_status. BUSY means "on site": from check-in until the assignment is COMPLETED or an absence is approved (decision D6); accepting a future assignment does NOT change work_status, double booking is prevented by the slot UNIQUE. Initial value = first member. IDLE is in the PRD (2.8, BR-05); PENDING (not yet activated), BUSY and LOCKED (Q15 account lock) are M1 naming proposals for M4 to confirm in workers.md.</summary>
public enum WorkStatus
{
    Pending,
    Idle,
    Busy,
    Locked
}
