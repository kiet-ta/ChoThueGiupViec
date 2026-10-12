using Backend.Application.Features.Agencies.Dtos;
using Backend.Application.Features.Agencies.Commands;
using Backend.Application.Features.Agencies.Repositories;
using CommonService.Application.Common.Models;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Backend.Application.Features.Agencies.Handlers;

/// <summary>
/// Handles the agency subscription command
/// </summary>
public sealed class SubscribeCommandHandler(
    ISubscriptionRepository subscriptionRepository,
    IAgencyRepository agencyRepository,
    IPaymentGateway paymentGateway,
    IPublisher publisher,
    ILogger<SubscribeCommandHandler> logger) : IRequestHandler<SubscribeCommand, ApiResponse<SubscriptionDto>>
{
    public async Task<ApiResponse<SubscriptionDto>> Handle(SubscribeCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing subscription request for package {PackageId}", request.PackageId);

        // 1. Get the active agency (in real implementation, this would come from authentication context)
        // For now, we'll get the first agency - in practice this would be based on authenticated user
        var agency = await agencyRepository.GetAsync(1, cancellationToken);
        if (agency == null)
        {
            return ApiResponse<SubscriptionDto>.Fail("Agency not found");
        }

        // 2. Get the subscription package
        var package = await subscriptionRepository.GetPackageByIdAsync(request.PackageId, cancellationToken);
        if (package == null)
        {
            return ApiResponse<SubscriptionDto>.Fail("Subscription package not found");
        }

        if (!package.IsActive)
        {
            return ApiResponse<SubscriptionDto>.Fail("Subscription package is not active");
        }

        // 3. Check if agency already has an active subscription
        var existingSubscription = await subscriptionRepository.GetActiveSubscriptionByAgencyIdAsync(agency.AgencyId, cancellationToken);
        if (existingSubscription != null && !existingSubscription.IsExpired)
        {
            return ApiResponse<SubscriptionDto>.Fail("Agency already has an active subscription");
        }

        // 4. Create payment request
        var paymentRequest = new CreatePaymentRequest(
            PaymentPurpose.Subscription,
            $"SUB-{agency.AgencyId}-{package.PackageId}",
            package.Price,
            $"Subscription to {package.PackageName}",
            DateTime.UtcNow.AddMinutes(15) // Payment QR expires in 15 minutes
        );

        // 5. Generate payment QR
        var paymentQr = await paymentGateway.CreateQrAsync(paymentRequest, cancellationToken);

        // 6. Return payment QR for user to scan
        // In a real implementation, we would save a pending subscription record here
        // and update it when payment is confirmed via webhook

        var response = new SubscriptionDto
        {
            SubscriptionId = 0, // Pending subscription
            AgencyId = agency.AgencyId,
            PackageId = package.PackageId,
            PackageCode = package.PackageCode,
            PackageName = package.PackageName,
            Tier = package.Tier,
            BillingCycle = package.BillingCycle,
            Price = package.Price,
            WorkerQuota = package.WorkerQuota,
            CommissionRate = package.CommissionRate,
            HasRosterDashboard = package.HasRosterDashboard,
            HasAnalytics = package.HasAnalytics,
            PriorityDispatch = package.PriorityDispatch,
            IsActive = package.IsActive,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow), // Will be set when payment confirmed
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)), // Will be set when payment confirmed
            SubStatus = "PendingPayment",
            AutoRenew = false, // Will be set based on package settings
            CreatedAt = DateTime.UtcNow
        };

        return ApiResponse<SubscriptionDto>.Ok(response, "Payment QR generated. Please scan to complete subscription.");
    }
}