using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Agencies.Services;

/// <summary>Lists the workers of the authenticated Partner Agency (AGENCY-UAT-02).</summary>
public class AgencyWorkerService(
    IAgencyRepository agencyRepository,
    ICurrentUser currentUser) : IAgencyWorkerService
{
    private int AgencyId => currentUser.UserId is int id && currentUser.Role == UserRole.Partner
        ? id
        : throw new ForbiddenAccessException("Authenticated partner agency is required.");

    public async Task<AgencyWorkerListResponse> GetWorkersAsync(
        int page,
        int pageSize,
        string? status,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await agencyRepository.GetAgencyWorkersAsync(
            AgencyId, page, pageSize, status, cancellationToken);

        return new AgencyWorkerListResponse(items, totalCount, page, pageSize);
    }
}
