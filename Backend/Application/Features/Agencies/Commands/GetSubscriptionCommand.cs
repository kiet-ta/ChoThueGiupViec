using CommonService.Application.Common.Models;
using CommonService.Application.Features.Agencies.Dtos;
using MediatR;

namespace CommonService.Application.Features.Agencies.Commands;

/// <summary>
/// Command to get the current agency's subscription
/// </summary>
public sealed record GetSubscriptionCommand : IRequest<ApiResponse<SubscriptionDto>>;