using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies.Commands;
using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Application.Features.Agencies;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using MediatR;

namespace CommonService.Application.Features.Agencies.Handlers;

/// <summary>
/// Handler for GetAgencyWorkersQuery.
/// </summary>
public sealed class GetAgencyWorkersQueryHandler(
    IAgencyRepository agencyRepository,
    ICurrentUser currentUser) : IRequestHandler<GetAgencyWorkersQuery, AgencyWorkerListResponse>
{
    private int AgencyId => currentUser.UserId is int id && currentUser.Role == UserRole.Partner
        ? id
        : throw new ForbiddenAccessException("Authenticated partner agency is required.");

    public async Task<AgencyWorkerListResponse> Handle(GetAgencyWorkersQuery request, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await agencyRepository.GetAgencyWorkersAsync(
            AgencyId, request.Page, request.PageSize, request.Status, cancellationToken);

        return new AgencyWorkerListResponse(items, totalCount, request.Page, request.PageSize);
    }
}