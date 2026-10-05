using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table JOB_ASSIGNMENT. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class JobAssignment
{
    /// <summary>job_assignment.assignment_id BIGINT (PK)</summary>
    public long AssignmentId { get; set; }

    /// <summary>job_assignment.order_id BIGINT (FK)</summary>
    public long OrderId { get; set; }

    /// <summary>job_assignment.customer_id INT (FK)</summary>
    public int CustomerId { get; set; }

    /// <summary>job_assignment.worker_id INT (FK)</summary>
    public int WorkerId { get; set; }

    /// <summary>job_assignment.agency_id INT NULL (FK)</summary>
    public int? AgencyId { get; set; }

    /// <summary>job_assignment.slot_id INT (FK)</summary>
    public int SlotId { get; set; }

    /// <summary>job_assignment.payout_item_id INT NULL (FK)</summary>
    public int? PayoutItemId { get; set; }

    /// <summary>job_assignment.service_tier VARCHAR(10)</summary>
    public ServiceTier ServiceTier { get; set; }

    /// <summary>job_assignment.assignment_seq TINYINT</summary>
    public byte AssignmentSeq { get; set; }

    /// <summary>job_assignment.work_zone NVARCHAR(100) NULL</summary>
    public string? WorkZone { get; set; }

    /// <summary>job_assignment.assignment_status VARCHAR(20)</summary>
    public JobAssignmentStatus AssignmentStatus { get; private set; }

    /// <summary>job_assignment.dispatch_radius_km TINYINT</summary>
    public byte DispatchRadiusKm { get; set; }

    /// <summary>job_assignment.matching_score DECIMAL(5,2) NULL</summary>
    public decimal? MatchingScore { get; set; }

    /// <summary>job_assignment.gross_amount DECIMAL(18,2)</summary>
    public decimal GrossAmount { get; set; }

    /// <summary>job_assignment.commission_rate DECIMAL(4,3)</summary>
    public decimal CommissionRate { get; set; }

    /// <summary>job_assignment.payout_amount DECIMAL(18,2)</summary>
    public decimal PayoutAmount { get; set; }

    /// <summary>job_assignment.absence_fee_amount DECIMAL(18,2) NULL</summary>
    public decimal? AbsenceFeeAmount { get; set; }

    /// <summary>job_assignment.accepted_at DATETIME2 NULL. Null while OFFERED (decision D1 / SC-9: offers are persisted).</summary>
    public DateTime? AcceptedAt { get; set; }

    /// <summary>job_assignment.started_at DATETIME2 NULL</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>job_assignment.completed_at DATETIME2 NULL</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>job_assignment.customer_confirmed_at DATETIME2 NULL</summary>
    public DateTime? CustomerConfirmedAt { get; set; }

    /// <summary>job_assignment.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>job_assignment.updated_at DATETIME2</summary>
    public DateTime UpdatedAt { get; set; }
}
