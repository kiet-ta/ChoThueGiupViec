using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table PAYMENT_TRANSACTION. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class PaymentTransaction
{
    /// <summary>payment_transaction.payment_id BIGINT (PK)</summary>
    public long PaymentId { get; set; }

    /// <summary>payment_transaction.gateway_txn_ref VARCHAR(64) (UNIQUE)</summary>
    public string GatewayTxnRef { get; set; } = string.Empty;

    /// <summary>payment_transaction.order_id BIGINT NULL (FK)</summary>
    public long? OrderId { get; set; }

    /// <summary>payment_transaction.extension_id INT NULL (FK)</summary>
    public int? ExtensionId { get; set; }

    /// <summary>payment_transaction.subscription_id INT NULL (FK)</summary>
    public int? SubscriptionId { get; set; }

    /// <summary>payment_transaction.purpose VARCHAR(12)</summary>
    public PaymentPurpose Purpose { get; set; }

    /// <summary>payment_transaction.gateway VARCHAR(10)</summary>
    public string Gateway { get; set; } = string.Empty;

    /// <summary>payment_transaction.amount DECIMAL(18,2)</summary>
    public decimal Amount { get; set; }

    /// <summary>payment_transaction.txn_status VARCHAR(10)</summary>
    public PaymentStatus TxnStatus { get; set; }

    /// <summary>payment_transaction.qr_payload NVARCHAR(1000) NULL</summary>
    public string? QrPayload { get; set; }

    /// <summary>payment_transaction.paid_at DATETIME2 NULL</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>payment_transaction.ipn_payload NVARCHAR(MAX) NULL</summary>
    public string? IpnPayload { get; set; }

    /// <summary>payment_transaction.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>payment_transaction.refunded_amount DECIMAL(18,2) NOT NULL DEFAULT 0 (SC-10, contract payments.md P2). Total given back so far; status is REFUNDED once it is above 0.</summary>
    public decimal RefundedAmount { get; set; }

    /// <summary>payment_transaction.refund_reason NVARCHAR(255) NULL (SC-10). The reason of the latest refund.</summary>
    public string? RefundReason { get; set; }

    /// <summary>payment_transaction.refunded_at DATETIME2 NULL (SC-10). When the latest refund was recorded.</summary>
    public DateTime? RefundedAt { get; set; }
}
