using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table PAYOUT_BATCH. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class PayoutBatch
{
    /// <summary>payout_batch.batch_id INT (PK)</summary>
    public int BatchId { get; set; }

    /// <summary>payout_batch.period_month CHAR(7) (UNIQUE)</summary>
    public string PeriodMonth { get; set; } = string.Empty;

    /// <summary>payout_batch.confirmed_by INT NULL (FK)</summary>
    public int? ConfirmedBy { get; set; }

    /// <summary>payout_batch.batch_status VARCHAR(10)</summary>
    public string BatchStatus { get; set; } = string.Empty;

    /// <summary>payout_batch.total_amount DECIMAL(18,2)</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>payout_batch.export_file_url NVARCHAR(500) NULL</summary>
    public string? ExportFileUrl { get; set; }

    /// <summary>payout_batch.confirmed_at DATETIME2 NULL</summary>
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>payout_batch.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }
}
