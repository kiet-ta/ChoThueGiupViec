using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table SUBSCRIPTION_PACKAGE. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class SubscriptionPackage
{
    /// <summary>subscription_package.package_id INT (PK)</summary>
    public int PackageId { get; set; }

    /// <summary>subscription_package.package_code VARCHAR(20) (UNIQUE)</summary>
    public string PackageCode { get; set; } = string.Empty;

    /// <summary>subscription_package.package_name NVARCHAR(100)</summary>
    public string PackageName { get; set; } = string.Empty;

    /// <summary>subscription_package.tier VARCHAR(5)</summary>
    public string Tier { get; set; } = string.Empty;

    /// <summary>subscription_package.billing_cycle VARCHAR(10)</summary>
    public string BillingCycle { get; set; } = string.Empty;

    /// <summary>subscription_package.price DECIMAL(18,2)</summary>
    public decimal Price { get; set; }

    /// <summary>subscription_package.worker_quota INT</summary>
    public int WorkerQuota { get; set; }

    /// <summary>subscription_package.commission_rate DECIMAL(4,3)</summary>
    public decimal CommissionRate { get; set; }

    /// <summary>subscription_package.has_roster_dashboard BIT</summary>
    public bool HasRosterDashboard { get; set; }

    /// <summary>subscription_package.has_analytics BIT</summary>
    public bool HasAnalytics { get; set; }

    /// <summary>subscription_package.priority_dispatch BIT</summary>
    public bool PriorityDispatch { get; set; }

    /// <summary>subscription_package.is_active BIT</summary>
    public bool IsActive { get; set; }
}
