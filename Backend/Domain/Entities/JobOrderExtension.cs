using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table JOB_ORDER_EXTENSION. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class JobOrderExtension
{
    /// <summary>job_order_extension.extension_id INT (PK)</summary>
    public int ExtensionId { get; set; }

    /// <summary>job_order_extension.order_id BIGINT (FK) (UNIQUE)</summary>
    public long OrderId { get; set; }

    /// <summary>job_order_extension.worker_id INT (FK)</summary>
    public int WorkerId { get; set; }

    /// <summary>job_order_extension.extra_hours DECIMAL(3,1)</summary>
    public decimal ExtraHours { get; set; }

    /// <summary>job_order_extension.extra_amount DECIMAL(18,2)</summary>
    public decimal ExtraAmount { get; set; }

    /// <summary>job_order_extension.worker_decision VARCHAR(10)</summary>
    public string WorkerDecision { get; set; } = string.Empty;

    /// <summary>job_order_extension.ext_status VARCHAR(15)</summary>
    public string ExtStatus { get; set; } = string.Empty;

    /// <summary>job_order_extension.requested_at DATETIME2</summary>
    public DateTime RequestedAt { get; set; }

    /// <summary>job_order_extension.decided_at DATETIME2 NULL</summary>
    public DateTime? DecidedAt { get; set; }

    /// <summary>job_order_extension.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }
}
