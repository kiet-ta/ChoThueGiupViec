namespace CommonService.Application.Features.Agencies.Dtos;

/// <summary>
/// Request for listing agency workers with pagination.
/// </summary>
public sealed class AgencyWorkerListRequest
{
    /// <summary>Page number (1-based).</summary>
    public int Page { get; set; } = 1;

    /// <summary>Page size (1-100).</summary>
    public int PageSize { get; set; } = 20;

    /// <summary>Optional status filter.</summary>
    public string? Status { get; set; }
}