namespace CommonService.Application.Features.Admin.Dtos;

/// <summary>One worker in the Admin lookup (contract admin.md section 2.8, ADMIN-UAT-14).</summary>
public class AdminWorkerDto
{
    public int WorkerId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string NationalId { get; init; } = string.Empty;

    /// <summary>FREELANCER or AGENCY_STAFF.</summary>
    public string WorkerType { get; init; } = string.Empty;

    public int? AgencyId { get; init; }
    public string? AgencyName { get; init; }

    /// <summary>PENDING, IDLE, BUSY, LOCKED, RETIRING or RETIRED.</summary>
    public string WorkStatus { get; init; } = string.Empty;

    public string KycStatus { get; init; } = string.Empty;
    public bool IsSuperFreelancer { get; init; }
    public decimal RatingAvg { get; init; }
    public int CompletedJobs { get; init; }
}

/// <summary>The paged shape of the admin lists: { items, page, pageSize, total }.</summary>
public class AdminWorkerPageDto
{
    public IReadOnlyList<AdminWorkerDto> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }
}
