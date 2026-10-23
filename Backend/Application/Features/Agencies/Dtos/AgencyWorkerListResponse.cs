namespace CommonService.Application.Features.Agencies.Dtos;

/// <summary>
/// Response for listing agency workers with pagination.
/// </summary>
public sealed record AgencyWorkerListResponse(
    IReadOnlyList<AgencyWorkerItem> Items,
    int Page,
    int PageSize,
    int Total
);

/// <summary>
/// Individual worker item in the agency worker list.
/// </summary>
public sealed record AgencyWorkerItem(
    int WorkerId,
    string FullName,
    string WorkStatus,
    int FutureAssignedJobs
);