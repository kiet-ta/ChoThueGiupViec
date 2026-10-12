using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Application.Common.Models;
using MediatR;

namespace CommonService.Application.Features.Agencies.Commands;

/// <summary>
/// Command to subscribe to or renew an agency subscription package
/// </summary>
public sealed record SubscribeCommand(
    int PackageId
) : IRequest<ApiResponse<SubscriptionDto>>;