using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Application.Common.Models;
using MediatR;

namespace CommonService.Application.Features.Agencies.Commands;

/// <summary>
/// Command to get available subscription packages
/// </summary>
public sealed record GetPackagesCommand : IRequest<ApiResponse<IReadOnlyList<PackageDto>>>;