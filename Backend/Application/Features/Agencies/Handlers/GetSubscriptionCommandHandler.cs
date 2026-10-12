using Backend.Application.Features.Agencies.Dtos;
using Backend.Application.Features.Agencies.Commands;
using Backend.Application.Features.Agencies.Repositories;
using CommonService.Application.Common.Models;
using MediatR;

namespace Backend.Application.Features.Agencies.Handlers;

/// <summary>
/// Handles the get agency subscription command
/// </summary>
public sealed class GetSubscriptionCommandHandler(
    ISubscriptionRepository subscriptionRepository,
    IAgencyRepository agencyRepository) : IRequestHandler<GetSubscriptionCommand, ApiResponse<SubscriptionDto>>
{
    public async Task<ApiResponse<SubscriptionDto>> Handle(GetSubscriptionCommand request, CancellationToken cancellationToken)
    {
        // Get the active agency (in real implementation, this would come from authentication context)
        // For now, we'll get the first agency - in practice this would be based on authenticated user
        var agency = await agencyRepository.GetAsync(1, cancellationToken);
        if (agency == null)
        {
            return ApiResponse<SubscriptionDto>.Fail("Agency not found");
        }

        // Get the active subscription for this agency
        var subscription = await subscriptionRepository.GetActiveSubscriptionByAgencyIdAsync(agency.AgencyId, cancellationToken);
        if (subscription == null)
        {
            // Return a default inactive subscription state
            return ApiResponse<SubscriptionDto>.Ok(new SubscriptionDto
            {
                SubscriptionId = 0,
                AgencyId = agency.AgencyId,
                PackageId = 0,
                PackageCode = "NONE",
                PackageName = "No Active Subscription",
                Tier = "FREE",
                BillingCycle = null,
                Price = 0,
                WorkerQuota = 3, // Default free tier quota
                CommissionRate = 0.200m, // Default free tier commission
                HasRosterDashboard = false,
                HasAnalytics = false,
                PriorityDispatch = false,
                IsActive = false,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow),
                SubStatus = "None",
                AutoRenew = false,
                CreatedAt = DateTime.UtcNow
            }, "No active subscription found");
        }

        // Check if subscription is expired but still in grace period
        var isExpired = subscription.EndDate < DateOnly.FromDateTime(DateTime.UtcNow);
        var isInGracePeriod = isExpired &&
            (DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - subscription.EndDate.DayNumber) <= 7;

        var effectiveStatus = subscription.SubStatus;
        if (isExpired && !isInGracePeriod)
        {
            effectiveStatus = "Expired";
        }
        else if (isExpired && isInGracePeriod)
        {
            effectiveStatus = "ExpiredGracePeriod";
        }

        // Get the subscription package details
        var package = await subscriptionRepository.GetPackageByIdAsync(subscription.PackageId, cancellationToken);

        // Map the subscription entity to DTO
        var subscriptionDto = new SubscriptionDto
        {
            SubscriptionId = subscription.SubscriptionId,
            AgencyId = subscription.AgencyId,
            PackageId = subscription.PackageId,
            PackageCode = package?.PackageCode ?? string.Empty,
            PackageName = package?.PackageName ?? string.Empty,
            Tier = package?.Tier ?? string.Empty,
            BillingCycle = package?.BillingCycle ?? string.Empty,
            Price = package?.Price ?? 0,
            WorkerQuota = package?.WorkerQuota ?? 0,
            CommissionRate = package?.CommissionRate ?? 0,
            HasRosterDashboard = package?.HasRosterDashboard ?? false,
            HasAnalytics = package?.HasAnalytics ?? false,
            PriorityDispatch = package?.PriorityDispatch ?? false,
            IsActive = effectiveStatus == "Active",
            StartDate = subscription.StartDate,
            EndDate = subscription.EndDate,
            SubStatus = effectiveStatus,
            AutoRenew = subscription.AutoRenew,
            CreatedAt = subscription.CreatedAt
        };

        return ApiResponse<SubscriptionDto>.Ok(subscriptionDto, "Subscription retrieved successfully");
    }
}