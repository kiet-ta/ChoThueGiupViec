using CommonService.Application.Features.Agencies.Dtos;

namespace CommonService.Application.Features.Agencies.Services;

/// <summary>Workers of the authenticated Partner Agency (AGENCY-UAT-02).</summary>
public interface IAgencyWorkerService
{
    /// <summary>One page of the agency's workers. Throws <c>ForbiddenAccessException</c> when the caller is not a partner agency.</summary>
    Task<AgencyWorkerListResponse> GetWorkersAsync(int page, int pageSize, string? status, CancellationToken cancellationToken = default);
}
