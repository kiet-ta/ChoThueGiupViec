namespace CommonService.Application.Features.Admin;

/// <summary>Text values of the absence approval (contract admin.md 2.2, question A3). One place to change them.</summary>
public static class AbsenceConstants
{
    public const string Pending = "PENDING";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";

    public static readonly IReadOnlyList<string> Statuses = [Pending, Approved, Rejected];

    // Why an approval is refused (decision Q10): the four conditions.
    public const string GpsNotVerified = "GPS_NOT_VERIFIED";
    public const string CallsBelowMinimum = "CALLS_BELOW_MINIMUM";
    public const string WaitBelowMinimum = "WAIT_BELOW_MINIMUM";
    public const string AbsenceNotReported = "ABSENCE_NOT_REPORTED";

    /// <summary>The audit row that records a decision (question A3: a rejection exists only here).</summary>
    public const string AuditEntityType = "JOB_ASSIGNMENT";

    public const string AuditFieldName = "absence_report";

    public const int MaxReasonLength = 255; // ADMIN_AUDIT_LOG.reason NVARCHAR(255)
}
