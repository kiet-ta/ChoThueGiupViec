namespace CommonService.Application.Features.Disputes.Dtos;

/// <summary>Body of the two POST .../disputes endpoints (contract disputes.md 2.1).</summary>
public sealed class FileDisputeRequestDto
{
    /// <summary>Nullable so that a missing value is a validation error, not order 0.</summary>
    public long? OrderId { get; init; }

    public string? Category { get; init; }
    public string? Description { get; init; }
    public List<string>? EvidenceUrls { get; init; }
}

/// <summary>A dispute as its two parties see it (contract disputes.md section 1). The handling admin is not shown to them.</summary>
public class DisputeDto
{
    public int DisputeId { get; init; }
    public long OrderId { get; init; }

    /// <summary>CUSTOMER or WORKER.</summary>
    public string RaisedBy { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public List<string> EvidenceUrls { get; init; } = [];

    /// <summary>OPEN, IN_REVIEW, RESOLVED or DISMISSED.</summary>
    public string DisputeStatus { get; init; } = string.Empty;

    /// <summary>FREELANCER, AGENCY or CUSTOMER; null while open and after a dismissal.</summary>
    public string? FaultParty { get; init; }

    public decimal? CompensationAmount { get; init; }
    public DateTime SlaDueAt { get; init; }
    public DateTime? ResolvedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>The Admin's view: the same plus who took the ticket (<c>resolved_by</c>).</summary>
public sealed class AdminDisputeDto : DisputeDto
{
    public int? ResolvedBy { get; init; }
}

public sealed class DisputeWorkerDto
{
    public int WorkerId { get; init; }
    public string FullName { get; init; } = string.Empty;

    /// <summary>FREELANCER or AGENCY_STAFF.</summary>
    public string WorkerType { get; init; } = string.Empty;

    public string? AgencyName { get; init; }
}

/// <summary>One row of the Admin queue (contract disputes.md section 1, DisputeSummary).</summary>
public sealed class DisputeSummaryDto
{
    public int DisputeId { get; init; }
    public long OrderId { get; init; }
    public string OrderCode { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public List<DisputeWorkerDto> Workers { get; init; } = [];
    public string RaisedBy { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;

    /// <summary>HIGH, MEDIUM or LOW, derived from the time left (never stored).</summary>
    public string Priority { get; init; } = string.Empty;

    public DateTime SlaDueAt { get; init; }

    /// <summary>Whole seconds until the SLA; negative when overdue; 0 once the ticket is decided.</summary>
    public long SlaSecondsRemaining { get; init; }

    public string DisputeStatus { get; init; } = string.Empty;

    /// <summary>True for the customer dispute of an absence fee (category ABSENT_FEE).</summary>
    public bool AutoCancelled { get; init; }
}

public sealed class DisputePageDto
{
    public List<DisputeSummaryDto> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }
}

public sealed class ShiftTimelineEntryDto
{
    public DateTime At { get; init; }

    /// <summary>CHECK_IN, PHOTO_AFTER, CUSTOMER_DISPUTED or CHECK_OUT.</summary>
    public string Type { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;
    public double? VolScore { get; init; }
    public bool? GpsVerified { get; init; }
    public decimal? DistanceM { get; init; }
}

public sealed class DisputePhotoDto
{
    public long AssignmentId { get; init; }

    /// <summary>BEFORE or AFTER.</summary>
    public string Phase { get; init; } = string.Empty;

    public int AngleNo { get; init; }
    public string Url { get; init; } = string.Empty;
    public double VolScore { get; init; }
    public bool IsAccepted { get; init; }
}

/// <summary>The case file of one dispute for the Admin console (contract disputes.md 2.2).</summary>
public sealed class DisputeCaseFileDto
{
    public AdminDisputeDto Dispute { get; init; } = new();
    public DisputeSummaryDto Summary { get; init; } = new();
    public List<ShiftTimelineEntryDto> ShiftTimeline { get; init; } = [];

    /// <summary>No table holds a completion checklist yet, so this is always null (contract disputes.md D6).</summary>
    public object? Checklist { get; init; }

    public List<DisputePhotoDto> Photos { get; init; } = [];
}
