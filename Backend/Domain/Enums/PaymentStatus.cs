namespace CommonService.Domain.Enums;

/// <summary>PAYMENT_TRANSACTION.txn_status: SUCCESS (drawio MF1), PENDING / EXPIRED / REFUNDED (decisions Q04).</summary>
public enum PaymentStatus
{
    Pending,
    Success,
    Expired,
    Refunded
}
