using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table PARTNER_SUBSCRIPTION. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class PartnerSubscription
{
    /// <summary>partner_subscription.subscription_id INT (PK)</summary>
    public int SubscriptionId { get; set; }

    /// <summary>partner_subscription.agency_id INT (FK)</summary>
    public int AgencyId { get; set; }

    /// <summary>partner_subscription.package_id INT (FK)</summary>
    public int PackageId { get; set; }

    /// <summary>partner_subscription.start_date DATE</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>partner_subscription.end_date DATE</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>partner_subscription.sub_status VARCHAR(15)</summary>
    public string SubStatus { get; set; } = string.Empty;

    /// <summary>partner_subscription.auto_renew BIT</summary>
    public bool AutoRenew { get; set; }

    /// <summary>partner_subscription.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }
}
