namespace CommonService.Application.Features.Dispatch;

/// <summary>One row of the Admin incident monitor (contract dispatch.md 3.3, <c>IncidentLogDto</c>).</summary>
public sealed record AdminIncidentDto(
    long IncidentId,
    long AssignmentId,
    int WorkerId,
    string IncidentType,
    string Description,
    string? PhotoEvidenceUrl,
    decimal Latitude,
    decimal Longitude,
    DateTime ReportedAt,
    bool IsPenaltyExempt,
    string ReDispatchStatus,
    DateTime ReDispatchDeadline,
    int? SubstituteWorkerId);

/// <summary>Reads the force majeure incidents for the Admin console (BR-10). Read only.</summary>
public interface IAdminIncidentQuery
{
    /// <summary>The newest incidents first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<AdminIncidentDto>> ListAsync(int limit, CancellationToken cancellationToken = default);
}

/// <summary>The stored <c>redispatch_status</c> values and the three values of the contract.</summary>
public static class AdminIncidentStatus
{
    public const int MaxListed = 200;

    public const string Searching = "SEARCHING";
    public const string Reassigned = "REASSIGNED";
    public const string Failed = "FAILED";

    /// <summary>PENDING_REDISPATCH is searching, REPLACED is reassigned, a timeout or a customer who declined ends in failure (refund).</summary>
    public static string ToContract(string stored) => stored switch
    {
        "PENDING_REDISPATCH" => Searching,
        "REPLACED" => Reassigned,
        "DECLINED_BY_CUSTOMER" or "TIMEOUT_CANCELLED" => Failed,
        _ => stored,
    };
}
