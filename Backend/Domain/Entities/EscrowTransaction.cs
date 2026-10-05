using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table ESCROW_TRANSACTION. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class EscrowTransaction
{
    /// <summary>escrow_transaction.escrow_txn_id BIGINT (PK)</summary>
    public long EscrowTxnId { get; set; }

    /// <summary>escrow_transaction.agency_id INT</summary>
    public int AgencyId { get; set; }

    /// <summary>escrow_transaction.txn_type VARCHAR(10)</summary>
    public EscrowTransactionType TxnType { get; set; }

    /// <summary>escrow_transaction.amount DECIMAL(18,2)</summary>
    public decimal Amount { get; set; }

    /// <summary>escrow_transaction.balance_after DECIMAL(18,2)</summary>
    public decimal BalanceAfter { get; set; }

    /// <summary>escrow_transaction.sla_points_delta DECIMAL(5,2) NULL</summary>
    public decimal? SlaPointsDelta { get; set; }

    /// <summary>escrow_transaction.order_id BIGINT NULL</summary>
    public long? OrderId { get; set; }

    /// <summary>escrow_transaction.dispute_id INT NULL</summary>
    public int? DisputeId { get; set; }

    /// <summary>escrow_transaction.payment_id BIGINT NULL</summary>
    public long? PaymentId { get; set; }

    /// <summary>escrow_transaction.reason NVARCHAR(255)</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>escrow_transaction.created_by_admin_id INT NULL</summary>
    public int? CreatedByAdminId { get; set; }

    /// <summary>escrow_transaction.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }
}
