using System;

namespace Backend.Application.Features.Agencies.Dtos;

/// <summary>
/// Data Transfer Object for agency subscription state
/// </summary>
public class SubscriptionDto
{
    /// <summary>
    /// The subscription ID
    /// </>
    public int SubscriptionId { get; init; }

    /// <summary>
    /// The agency ID
    /// </summary>
    public int AgencyId { get; init; }

    /// <summary>
    /// The package ID
    /// </summary>
    public int PackageId { get; init; }

    /// <summary>
    /// The package code
    /// </summary>
    public required string PackageCode { get; init; }

    /// <summary>
    /// The package name
    /// </summary>
    public required string PackageName { get; init; }

    /// <summary>
    /// The subscription tier (FREE/PRO)
    /// </summary>
    public required string Tier { get; init; }

    /// <summary>
    /// The billing cycle (none/MONTHLY/QUARTERLY)
    /// </summary>
    public string? BillingCycle { get; init; }

    /// <summary>
    /// The subscription price in VND
    /// </summary>
    public decimal Price { get; init; }

    /// <summary>
    /// The worker quota for this package
    /// </summary>
    public int WorkerQuota { get; init; }

    /// <summary>
    /// The commission rate for this package
    /// </summary>
    public decimal CommissionRate { get; init; }

    /// <summary>
    /// Whether this package includes roster dashboard
    /// </summary>
    public bool HasRosterDashboard { get; init; }

    /// <summary>
    /// Whether this package includes analytics
    /// </summary>
    public bool HasAnalytics { get; init; }

    /// <summary>
    /// Whether this package includes priority dispatch
    /// </summary>
    public bool PriorityDispatch { get; init; }

    /// <summary>
    /// Whether the package is active
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// The subscription start date
    /// </summary>
    public DateOnly StartDate { get; init; }

    /// <summary>
    /// The subscription end date
    /// </summary>
    public DateOnly EndDate { get; init; }

    /// <summary>
    /// The subscription status (Active, Expired, etc.)
    /// </summary>
    public required string SubStatus { get; init; }

    /// <summary>
    /// Whether the subscription is set to auto-renew
    /// </summary>
    public bool AutoRenew { get; init; }

    /// <summary>
    /// The date the subscription was created
    /// </summary>
    public DateTime CreatedAt { get; init; }
}