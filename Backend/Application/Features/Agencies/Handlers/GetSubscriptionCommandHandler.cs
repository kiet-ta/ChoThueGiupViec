using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Agencies.Commands;
using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Application.Common.Models;
using CommonService.Domain.Entities;
using MediatR;

namespace CommonService.Application.Features.Agencies.Handlers;

/// <summary>
/// Handles getting the current agency's subscription
/// </summary>
public sealed class GetSubscriptionCommandHandler : IRequestHandler<GetSubscriptionCommand, ApiResponse<SubscriptionDto>>
{
    private readonly IAgencyRepository _agencyRepository;
    private readonly ISubscriptionRepository _subscriptionRepository;

    public GetSubscriptionCommandHandler(
        IAgencyRepository agencyRepository,
        ISubscriptionRepository subscriptionRepository)
    {
        _agencyRepository = agencyRepository;
        _subscriptionRepository = subscriptionRepository;
    }

    public async Task<ApiResponse<SubscriptionDto>> Handle(GetSubscriptionCommand request, CancellationToken cancellationToken)
    {
        // TODO: Get agency ID from current user context
        // For now, using a placeholder - in real implementation, this would come from JWT claims
        int agencyId = 1; // TODO: Replace with actual agency ID from user context

        var agency = await _agencyRepository.GetByIdAsync(agencyId, cancellationToken);
        if (agency == null)
        {
            return ApiResponse<SubscriptionDto>.Fail("Agency not found.");
        }

        var subscription = await _subscriptionRepository.GetActiveSubscriptionByAgencyIdAsync(agencyId, cancellationToken);
        if (subscription == null)
        {
            return ApiResponse<SubscriptionDto>.Fail("No active subscription found for this agency.");
        }

        // Map PartnerSubscription to SubscriptionDto
        var packageDto = new PackageDto(
            subscription.PackageId,
            "", // TODO: Fetch actual package name
            "",
            0,
            0,
            null,
            null,
            DateTime.UtcNow // TODO: Set actual created date if available
        );
        var subscriptionDto = new SubscriptionDto(
            subscription.SubscriptionId,
            subscription.AgencyId,
            subscription.PackageId,
            packageDto,
            subscription.StartDate,
            subscription.EndDate,
            subscription.SubStatus,
            subscription.AutoRenew,
            subscription.CreatedAt,
            subscription.CreatedAt // Using created at as updated at for now
        );

        return ApiResponse<SubscriptionDto>.Ok(subscriptionDto);
    }
}