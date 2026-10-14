using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Agencies;

/// <summary>A subscription package as the Partner sees it (contract agencies.md 2).</summary>
public sealed record PackageDto(
    int PackageId,
    string PackageCode,
    string PackageName,
    string Tier,
    string BillingCycle,
    decimal Price,
    int WorkerQuota,
    decimal CommissionRate,
    bool HasRosterDashboard,
    bool HasAnalytics,
    bool PriorityDispatch,
    bool IsActive)
{
    public static PackageDto From(SubscriptionPackage p) => new(
        p.PackageId, p.PackageCode, p.PackageName, p.Tier, p.BillingCycle, p.Price, p.WorkerQuota, p.CommissionRate,
        p.HasRosterDashboard, p.HasAnalytics, p.PriorityDispatch, p.IsActive);
}

/// <summary>The caller's subscription state and quota (contract agencies.md 2).</summary>
/// <param name="Tier">The tier in force today: <c>PRO</c> while a Pro subscription runs or is inside its grace days, otherwise <c>FREE</c>.</param>
/// <param name="Subscription">The running subscription, or null on the FREE package.</param>
/// <param name="InGrace">True when the subscription ended less than <c>Subscription.GraceDays</c> ago (Pro is still in force).</param>
/// <param name="WorkerQuota">The quota in force.</param>
/// <param name="WorkerCount">Workers of the agency that are not locked.</param>
public sealed record SubscriptionStateDto(
    string Tier,
    PackageDto Package,
    SubscriptionDto? Subscription,
    bool InGrace,
    int WorkerQuota,
    int WorkerCount,
    bool HasRosterDashboard,
    bool HasAnalytics,
    bool PriorityDispatch);

public sealed record SubscriptionDto(
    int SubscriptionId,
    int AgencyId,
    int PackageId,
    DateOnly StartDate,
    DateOnly EndDate,
    string SubStatus,
    bool AutoRenew);

/// <summary>The request of <c>POST /api/agencies/subscriptions</c>.</summary>
public sealed record SubscribeRequest(int? PackageId);

/// <summary>A purchase waiting for payment: the subscription and the QR to pay it (the same QR is returned until it is paid or expires).</summary>
public sealed record SubscriptionPurchaseDto(
    SubscriptionDto Subscription,
    long PaymentId,
    string GatewayTxnRef,
    decimal Amount,
    string? QrPayload,
    string? PayUrl,
    DateTime ExpiresAt);

/// <summary>An Admin edit of a package (contract agencies.md 2, decisions G-5): at least one field and a reason.</summary>
public sealed record UpdatePackageRequest(decimal? Price, int? WorkerQuota, decimal? CommissionRate, string? Reason);

/// <summary>Codes in <c>data.code</c> of a 409 (decisions Q26 D6).</summary>
public static class SubscriptionErrorCodes
{
    public const string PackageNotFound = "PACKAGE_NOT_FOUND";
    public const string PackageNotPurchasable = "PACKAGE_NOT_PURCHASABLE";
    public const string AgencySuspended = "AGENCY_SUSPENDED";
    public const string QuotaExceeded = "WORKER_QUOTA_EXCEEDED";
}
