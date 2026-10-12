using Backend.Application.Features.Agencies.Dtos;
using CommonService.Application.Common.Models;
using MediatR;

namespace Backend.Application.Features.Agencies.Commands;

/// <summary>
/// Command to subscribe to an agency package
/// </summary>
public record SubscribeCommand(
    int PackageId
) : IRequest<ApiResponse<SubscriptionDto>>;