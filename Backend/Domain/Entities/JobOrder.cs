using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table JOB_ORDER. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class JobOrder
{
    /// <summary>job_order.order_id BIGINT (PK)</summary>
    public long OrderId { get; set; }

    /// <summary>job_order.order_code VARCHAR(20) (UNIQUE)</summary>
    public string OrderCode { get; set; } = string.Empty;

    /// <summary>job_order.customer_id INT (FK)</summary>
    public int CustomerId { get; set; }

    /// <summary>job_order.address_id INT (FK)</summary>
    public int AddressId { get; set; }

    /// <summary>job_order.service_tier VARCHAR(10)</summary>
    public ServiceTier ServiceTier { get; set; }

    /// <summary>job_order.scheduled_date DATE</summary>
    public DateOnly ScheduledDate { get; set; }

    /// <summary>job_order.shift_code VARCHAR(10)</summary>
    public string ShiftCode { get; set; } = string.Empty;

    /// <summary>job_order.area_snapshot_m2 DECIMAL(8,2)</summary>
    public decimal AreaSnapshotM2 { get; set; }

    /// <summary>job_order.required_workers TINYINT</summary>
    public byte RequiredWorkers { get; set; }

    /// <summary>job_order.required_skill NVARCHAR(100) NULL</summary>
    public string? RequiredSkill { get; set; }

    /// <summary>job_order.total_amount DECIMAL(18,2)</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>job_order.order_status VARCHAR(20)</summary>
    public JobOrderStatus OrderStatus { get; private set; }

    /// <summary>job_order.customer_note NVARCHAR(500) NULL</summary>
    public string? CustomerNote { get; set; }

    /// <summary>job_order.cancel_reason NVARCHAR(255) NULL</summary>
    public string? CancelReason { get; set; }

    /// <summary>job_order.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>job_order.updated_at DATETIME2</summary>
    public DateTime UpdatedAt { get; set; }
}
