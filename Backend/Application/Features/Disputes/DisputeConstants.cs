namespace CommonService.Application.Features.Disputes;

/// <summary>
/// Values of DISPUTE_TICKET text columns (contract disputes.md, recommended defaults D1 and D3). One place to change them.
/// </summary>
public static class DisputeConstants
{
    public const string Open = "OPEN";
    public const string InReview = "IN_REVIEW";
    public const string Resolved = "RESOLVED";
    public const string Dismissed = "DISMISSED";

    /// <summary>dispute_status is VARCHAR(12).</summary>
    public static readonly IReadOnlyList<string> Statuses = [Open, InReview, Resolved, Dismissed];

    /// <summary>The statuses of a ticket nobody has decided yet.</summary>
    public static readonly IReadOnlyList<string> Unresolved = [Open, InReview];

    public const string RaisedByCustomer = "CUSTOMER";
    public const string RaisedByWorker = "WORKER";

    /// <summary>category is VARCHAR(15): PRD 4.3 lists damage, cleanliness and attitude; ABSENT_FEE is the customer dispute of Q10.</summary>
    public static readonly IReadOnlyList<string> Categories = ["QUALITY", "ATTITUDE", "PROPERTY_DAMAGE", "ABSENT_FEE", "OTHER"];

    /// <summary>The category of the customer dispute of an absence fee (Q10); its reversal never costs SLA points or escrow (D9).</summary>
    public const string AbsentFeeCategory = "ABSENT_FEE";

    public const int MaxDescriptionLength = 1000; // DISPUTE_TICKET.description NVARCHAR(1000)
    public const int MaxEvidenceCount = 10;
    public const int MaxEvidenceUrlLength = 500;

    public const string PriorityHigh = "HIGH";
    public const string PriorityMedium = "MEDIUM";
    public const string PriorityLow = "LOW";
}

/// <summary>Disputes thresholds, bound from the "Disputes" configuration section; the defaults are the recommended ones of disputes.md.</summary>
public sealed class DisputeOptions
{
    /// <summary>How long after the end of the shift (or completion) a dispute may still be filed (PRD 4.3: 24 h).</summary>
    public int FileWindowHours { get; set; } = 24;

    /// <summary>SLA of a ticket from creation (drawio: <c>sla_due_at = +48 h</c>).</summary>
    public int SlaHours { get; set; } = 48;

    /// <summary>Less time left than this is HIGH priority.</summary>
    public int PriorityHighHours { get; set; } = 6;

    /// <summary>Less time left than this (and not HIGH) is MEDIUM priority; the rest is LOW.</summary>
    public int PriorityMediumHours { get; set; } = 24;

    /// <summary>The dashboard and the <c>nearSla</c> filter: unresolved and due within this many hours.</summary>
    public int NearSlaHours { get; set; } = 6;
}
