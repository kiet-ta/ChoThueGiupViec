using CommonService.Application.Common.Models;
using CommonService.Application.Features.Agencies.Dtos;
using MediatR;

namespace CommonService.Application.Features.Agencies.Commands;

/// <summary>Registers a new Partner Agency (BE-M5-01). Handled by <c>RegisterAgencyCommandHandler</c>.</summary>
public sealed record RegisterAgencyCommand(RegisterAgencyRequest? Request)
    : IRequest<ApiResponse<RegisterAgencyResponse>>;
