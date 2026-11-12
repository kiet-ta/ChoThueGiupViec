namespace CommonService.Domain.Enums;

/// <summary>State of a worker's request to change the payout account (LEDGER-28): stored as PENDING, APPROVED, REJECTED.</summary>
public enum BankChangeStatus
{
    Pending, Approved, Rejected
}
