using Backend.Application.Features.Agencies.Dtos;
using CommonService.Application.Common.Models;
using MediatR;

namespace Backend.Application.Features.Agencies.Commands;

/// <summary>
/// Command to get current agency subscription
/// </summary>
public record GetSubscriptionCommand : IRequest<ApiResponse<SubscriptionDto>>;