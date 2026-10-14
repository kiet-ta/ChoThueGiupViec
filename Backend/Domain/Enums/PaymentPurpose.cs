namespace CommonService.Domain.Enums;

/// <summary>PAYMENT_TRANSACTION.purpose. EXTENSION and SUBSCRIPTION are named in the drawio notes; ORDER is the remaining case of the "exactly one of order_id / extension_id / subscription_id" CHECK.</summary>
public enum PaymentPurpose
{
    Order,
    Extension,
    Subscription,

    /// <summary>An agency tops up its escrow (decisions Q09, BE-M5-07); carries <c>agency_id</c>, stored ESCROW_DEPOSIT (SC-12).</summary>
    EscrowDeposit
}
