using CommonService.Application.Common.Models;
using CommonService.Application.Features.Agencies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Agencies;

/// <summary>
/// Subscription packages for the Partner Portal (contract agencies.md 2, BE-M5-02, policy PartnerOnly): the packages, the caller's
/// subscription and worker quota, and buying or renewing a package through the payment gateway (the IPN activates it).
/// </summary>
[ApiController]
[Route("api/agencies")]
[Authorize(Policy = "PartnerOnly")]
public class AgencySubscriptionsController(SubscriptionService service) : ControllerBase
{
    /// <summary>The active packages (default seed: FREE, PRO_MONTHLY, PRO_QUARTERLY; decisions Q08).</summary>
    [HttpGet("packages")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PackageDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPackages(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<PackageDto>>.Ok(await service.GetPackagesAsync(ct)));

    /// <summary>The tier in force (a Pro subscription counts through its grace days), the running subscription and the worker quota.</summary>
    [HttpGet("subscriptions/me")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionStateDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(CancellationToken ct) =>
        Ok(ApiResponse<SubscriptionStateDto>.Ok(await service.GetMineAsync(ct)));

    /// <summary>Buys or renews a package: returns the QR to pay; the subscription turns ACTIVE when the IPN arrives.</summary>
    [HttpPost("subscriptions")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionPurchaseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest request, CancellationToken ct) =>
        Ok(ApiResponse<SubscriptionPurchaseDto>.Ok(await service.SubscribeAsync(request, ct)));
}

/// <summary>Admin side of the packages (contract agencies.md 2, decisions G-5): edits need a reason and write ADMIN_AUDIT_LOG.</summary>
[ApiController]
[Route("api/admin/subscription-packages")]
[Authorize(Policy = "AdminOnly")]
public class AdminSubscriptionPackagesController(SubscriptionService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PackageDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<PackageDto>>.Ok(await service.GetAllPackagesAsync(ct)));

    [HttpPut("{packageId:int}")]
    [ProducesResponseType(typeof(ApiResponse<PackageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int packageId, [FromBody] UpdatePackageRequest request, CancellationToken ct) =>
        Ok(ApiResponse<PackageDto>.Ok(await service.UpdatePackageAsync(packageId, request, ct)));
}
