namespace CommonService.Application.Features.Admin.Dtos;

/// <summary>An assignment where the worker pressed "customer absent" (contract admin.md section 1, <c>AbsenceReport</c>).</summary>
public sealed class AbsenceReportDto
{
    public long AssignmentId { get; init; }
    public long OrderId { get; init; }
    public string OrderCode { get; init; } = string.Empty;
    public int WorkerId { get; init; }
    public string WorkerName { get; init; } = string.Empty;

    /// <summary>FREELANCER or AGENCY_STAFF.</summary>
    public string WorkerType { get; init; } = string.Empty;

    public string? AgencyName { get; init; }
    public string CustomerName { get; init; } = string.Empty;

    /// <summary>PENDING, APPROVED or REJECTED.</summary>
    public string Status { get; init; } = string.Empty;

    public DateTime CheckedInAt { get; init; }
    public DateTime CustomerAbsentAt { get; init; }
    public bool GpsVerified { get; init; }
    public decimal DistanceM { get; init; }
    public decimal DeviceLat { get; init; }
    public decimal DeviceLng { get; init; }
    public int CallAttempts { get; init; }

    /// <summary>Whole minutes waited: since check-in until now while PENDING, until the report once decided.</summary>
    public int WaitedMinutes { get; init; }

    public decimal GrossAmount { get; init; }

    /// <summary>The 40 % preview until approved, then the stored amount (whole VND).</summary>
    public decimal AbsenceFeeAmount { get; init; }

    /// <summary><c>grossAmount - absenceFeeAmount</c>.</summary>
    public decimal CustomerRefundAmount { get; init; }

    public string? PhotoUrl { get; init; }

    /// <summary>True when the report is PENDING and every Q10 condition holds.</summary>
    public bool CanApprove { get; init; }

    /// <summary>GPS_NOT_VERIFIED, CALLS_BELOW_MINIMUM, WAIT_BELOW_MINIMUM or ABSENCE_NOT_REPORTED; empty when none.</summary>
    public List<string> BlockReasons { get; init; } = [];
}

public sealed class AbsenceReportPageDto
{
    public List<AbsenceReportDto> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }
}

/// <summary>Body of the reject endpoint.</summary>
public sealed class RejectAbsenceRequestDto
{
    public string? Reason { get; init; }
}
