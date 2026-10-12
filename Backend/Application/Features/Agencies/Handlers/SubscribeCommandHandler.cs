using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Agencies.Commands;
using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Application.Common.Models;
using CommonService.Domain.Entities;
using MediatR;

namespace CommonService.Application.Features.Agencies.Handlers;

/// <summary>
/// Handles subscribing to or renewing an agency subscription package
/// </summary>
public sealed class SubscribeCommandHandler(
    IAgencyRepository agencyRepository,
    ISubscriptionRepository subscriptionRepository)
    : IRequestHandler<SubscribeCommand, ApiResponse<SubscriptionDto>>
{
    public async Task<ApiResponse<SubscriptionDto>> Handle(SubscribeCommand command, CancellationToken cancellationToken)
    {
        // TODO: Get agency ID from current user context
        // For now, using a placeholder - in real implementation, this would come from JWT claims via the controller
        int agencyId = 1; // TODO: Replace with actual agency ID from user context

        PackageDto packageDto;

        // Validate agency exists (optional, but good practice)
        var agency = await agencyRepository.GetByIdAsync(agencyId, cancellationToken);
        if (agency == null)
        {
            packageDto = new PackageDto(0, "", "", 0, 0, null, null, DateTime.UtcNow);
            var response = new SubscriptionDto(0, 0, 0, packageDto, DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow), "", false, DateTime.UtcNow, DateTime.UtcNow);
            return ApiResponse<SubscriptionDto>.Fail("Agency not found.", response);
        }

        // TODO: Check if agency already has an active subscription (if business logic requires)
        // For now, we allow creating a new subscription (which may replace the old one, depending on business rules)

        // Calculate dates
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var endDate = startDate.AddMonths(1); // Assume one-month subscription

        // Create subscription entity
        var subscription = new PartnerSubscription
        {
            AgencyId = agencyId,
            PackageId = command.PackageId,
            StartDate = startDate,
            EndDate = endDate,
            SubStatus = "Active",
            AutoRenew = false,
            CreatedAt = DateTime.UtcNow
        };

        // Add subscription to repository
        await subscriptionRepository.AddAsync(subscription, cancellationToken);
        await subscriptionRepository.SaveChangesAsync(cancellationToken);

        // Map PartnerSubscription to SubscriptionDto
        packageDto = new PackageDto(subscription.PackageId, "", "", 0, 0, null, null, DateTime.UtcNow);
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

        return ApiResponse<SubscriptionDto>.Ok(subscriptionDto, "Subscription successful.");
    }
}