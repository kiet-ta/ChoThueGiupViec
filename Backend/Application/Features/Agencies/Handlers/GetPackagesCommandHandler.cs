using Backend.Application.Features.Agencies.Dtos;
using Backend.Application.Features.Agencies.Commands;
using Backend.Application.Features.Agencies.Repositories;
using CommonService.Application.Common.Models;
using MediatR;

namespace Backend.Application.Features.Agencies.Handlers;

/// <summary>
/// Handles the get subscription packages command
/// </summary>
public sealed class GetPackagesCommandHandler(
    ISubscriptionRepository subscriptionRepository) : IRequestHandler<GetPackagesCommand, ApiResponse<IReadOnlyList<PackageDto>>>
{
    public async Task<ApiResponse<IReadOnlyList<PackageDto>>> Handle(GetPackagesCommand request, CancellationToken cancellationToken)
    {
        // Get all active subscription packages
        var packages = await subscriptionRepository.GetActivePackagesAsync(cancellationToken);

        // Map entities to DTOs
        var packageDtos = packages.Select(p => new PackageDto
        {
            PackageId = p.PackageId,
            PackageCode = p.PackageCode,
            PackageName = p.PackageName,
            Tier = p.Tier,
            BillingCycle = p.BillingCycle,
            Price = p.Price,
            WorkerQuota = p.WorkerQuota,
            CommissionRate = p.CommissionRate,
            HasRosterDashboard = p.HasRosterDashboard,
            HasAnalytics = p.HasAnalytics,
            PriorityDispatch = p.PriorityDispatch,
            IsActive = p.IsActive
        }).ToList();

        return ApiResponse<IReadOnlyList<PackageDto>>.Ok(packageDtos, "Subscription packages retrieved successfully");
    }
}