namespace CommonService.Domain.Enums;

/// <summary>PAYMENT_TRANSACTION.purpose. EXTENSION and SUBSCRIPTION are named in the drawio notes; ORDER is the remaining case of the "exactly one of order_id / extension_id / subscription_id" CHECK.</summary>
public enum PaymentPurpose
{
    Order,
    Extension,
    Subscription
}
