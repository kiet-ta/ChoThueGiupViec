namespace CommonService.Application.Features.Agencies;

/// <summary>
/// Values of <c>PARTNER_AGENCY.agency_status</c> (VARCHAR(10), no database enum).
/// <c>ACTIVE</c> and <c>SUSPENDED</c> come from decisions Q09; <c>PENDING</c> is the state of a freshly registered agency
/// whose escrow has not reached <c>Agency.MinEscrowBalance</c> yet (leader decision for BE-M5-01, issue #237).
/// </summary>
public static class AgencyStatuses
{
    public const string Pending = "PENDING";
    public const string Active = "ACTIVE";
    public const string Suspended = "SUSPENDED";
}

/// <summary>
/// Pure rule of decisions Q09: an agency is <c>ACTIVE</c> only when <c>escrow_deposit_balance</c> is at least
/// <c>Agency.MinEscrowBalance</c>. Registration (BE-M5-01) uses it for the initial status; the escrow top-up of
/// BE-M5-07 is expected to call it after a deposit. Re-activation of a <c>SUSPENDED</c> agency and the status after a
/// penalty are NOT decided yet (docx/BE-M5-01-notes.md, section 2), so this function only answers "does this balance
/// qualify"; it never returns <c>SUSPENDED</c>.
/// </summary>
public static class AgencyEligibility
{
    /// <summary><c>ACTIVE</c> when <paramref name="escrowBalance"/> &gt;= <paramref name="minEscrowBalance"/>, else <c>PENDING</c>.</summary>
    public static string StatusFor(decimal escrowBalance, decimal minEscrowBalance) =>
        escrowBalance >= minEscrowBalance ? AgencyStatuses.Active : AgencyStatuses.Pending;
}
