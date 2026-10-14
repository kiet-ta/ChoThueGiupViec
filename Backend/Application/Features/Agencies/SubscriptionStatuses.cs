namespace CommonService.Application.Features.Agencies;

/// <summary><c>PARTNER_SUBSCRIPTION.sub_status</c> values (decisions Q08). Exact, upper case.</summary>
public static class SubscriptionStatuses
{
    /// <summary>Paid and running; counts until its end date plus <c>Subscription.GraceDays</c>.</summary>
    public const string Active = "ACTIVE";

    /// <summary>Bought, waiting for the payment webhook.</summary>
    public const string PendingPayment = "PENDING_PAYMENT";

    /// <summary>Ended and past the grace period.</summary>
    public const string Expired = "EXPIRED";
}
