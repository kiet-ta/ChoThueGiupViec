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
/// Pure rules of decisions Q09 and Q25 (R3, R4): an agency <em>becomes</em> <c>ACTIVE</c> only when
/// <c>escrow_deposit_balance</c> is at least <c>Agency.MinEscrowBalance</c>. That one rule serves the first deposit and
/// the return from <c>SUSPENDED</c>. Registration (BE-M5-01) uses it for the initial status; the escrow top-up of
/// BE-M5-07 calls it after a deposit. An <c>ACTIVE</c> agency stays <c>ACTIVE</c> after a penalty until its balance
/// reaches 0 (then <c>SUSPENDED</c>, Q09); while it is below the minimum it carries an escrow warning
/// (<see cref="HasEscrowWarning"/>), which is derived from the balance and never stored.
/// </summary>
public static class AgencyEligibility
{
    /// <summary><c>ACTIVE</c> when <paramref name="escrowBalance"/> &gt;= <paramref name="minEscrowBalance"/>, else <c>PENDING</c>.</summary>
    public static string StatusFor(decimal escrowBalance, decimal minEscrowBalance) =>
        escrowBalance >= minEscrowBalance ? AgencyStatuses.Active : AgencyStatuses.Pending;

    /// <summary>
    /// True for an <c>ACTIVE</c> agency whose balance fell below <paramref name="minEscrowBalance"/>: still operating,
    /// but it needs a top-up (decisions Q25 R4). Not the SLA warning of Q09 (score &lt;= 70). A <c>PENDING</c> or
    /// <c>SUSPENDED</c> agency never has it: its status already says that the minimum is not met.
    /// </summary>
    public static bool HasEscrowWarning(string agencyStatus, decimal escrowBalance, decimal minEscrowBalance) =>
        agencyStatus == AgencyStatuses.Active && escrowBalance < minEscrowBalance;
}
