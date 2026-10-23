using CommonService.Application.Features.Agencies.Dtos;
using MediatR;

namespace CommonService.Application.Features.Agencies.Commands;

/// <summary>
/// Query to get the list of workers for the authenticated agency.
/// </summary>
public sealed record GetAgencyWorkersQuery(
    int Page = 1,
    int PageSize = 20,
    string? Status = null
) : IRequest<AgencyWorkerListResponse>;