using Backend.Application.Features.Agencies.Dtos;
using CommonService.Application.Common.Models;
using MediatR;

namespace Backend.Application.Features.Agencies.Commands;

/// <summary>
/// Command to get available subscription packages
/// </summary>
public record GetPackagesCommand : IRequest<ApiResponse<IReadOnlyList<PackageDto>>>;