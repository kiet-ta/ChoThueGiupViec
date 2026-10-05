namespace CommonService.Domain.Enums;

/// <summary>
/// DISPUTE_TICKET.fault_party (decision D3, 2026-10-04). The value also says which penalty applies:
/// FREELANCER -> deduct from the worker's payout / lock (PRD 4.3); AGENCY -> SLA points + escrow deduction (decisions Q09);
/// CUSTOMER -> the customer was at fault (a dispute raised by the worker, PRD 4.3). "WORKER" in decisions Q10/Q12 means FREELANCER or AGENCY.
/// </summary>
public enum FaultParty
{
    Freelancer,
    Agency,
    Customer
}
