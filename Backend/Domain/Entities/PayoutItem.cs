using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table PAYOUT_ITEM. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class PayoutItem
{
    /// <summary>payout_item.item_id INT (PK)</summary>
    public int ItemId { get; set; }

    /// <summary>payout_item.batch_id INT (FK)</summary>
    public int BatchId { get; set; }

    /// <summary>payout_item.worker_id INT NULL (FK)</summary>
    public int? WorkerId { get; set; }

    /// <summary>payout_item.agency_id INT NULL (FK)</summary>
    public int? AgencyId { get; set; }

    /// <summary>payout_item.payee_type VARCHAR(10)</summary>
    public PayeeType PayeeType { get; set; }

    /// <summary>payout_item.job_count INT</summary>
    public int JobCount { get; set; }

    /// <summary>payout_item.gross_amount DECIMAL(18,2)</summary>
    public decimal GrossAmount { get; set; }

    /// <summary>payout_item.commission_amount DECIMAL(18,2)</summary>
    public decimal CommissionAmount { get; set; }

    /// <summary>payout_item.penalty_amount DECIMAL(18,2)</summary>
    public decimal PenaltyAmount { get; set; }

    /// <summary>payout_item.net_amount DECIMAL(18,2)</summary>
    public decimal NetAmount { get; set; }

    /// <summary>payout_item.bank_account_no VARCHAR(30)</summary>
    public string BankAccountNo { get; set; } = string.Empty;

    /// <summary>payout_item.item_status VARCHAR(12)</summary>
    public string ItemStatus { get; set; } = string.Empty;

    /// <summary>payout_item.transferred_at DATETIME2 NULL</summary>
    public DateTime? TransferredAt { get; set; }
}
