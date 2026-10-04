using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table WORKER. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class Worker
{
    /// <summary>worker.worker_id INT (PK)</summary>
    public int WorkerId { get; set; }

    /// <summary>worker.phone_number VARCHAR(15) (UNIQUE)</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>worker.national_id VARCHAR(12) (UNIQUE)</summary>
    public string NationalId { get; set; } = string.Empty;

    /// <summary>worker.agency_id INT NULL (FK)</summary>
    public int? AgencyId { get; private set; }

    /// <summary>worker.kyc_reviewed_by INT NULL (FK)</summary>
    public int? KycReviewedBy { get; set; }

    /// <summary>worker.full_name NVARCHAR(100)</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>worker.worker_type VARCHAR(12)</summary>
    public WorkerType WorkerType { get; private set; }

    /// <summary>worker.is_super_freelancer BIT</summary>
    public bool IsSuperFreelancer { get; set; }

    /// <summary>worker.ekyc_confidence DECIMAL(5,2) NULL</summary>
    public decimal? EkycConfidence { get; set; }

    /// <summary>worker.kyc_status VARCHAR(15)</summary>
    public string KycStatus { get; set; } = string.Empty;

    /// <summary>worker.rating_avg DECIMAL(3,2)</summary>
    public decimal RatingAvg { get; set; }

    /// <summary>worker.completed_jobs INT</summary>
    public int CompletedJobs { get; set; }

    /// <summary>worker.work_status VARCHAR(10)</summary>
    public WorkStatus WorkStatus { get; private set; }

    /// <summary>worker.current_lat DECIMAL(9,6) NULL</summary>
    public decimal? CurrentLat { get; set; }

    /// <summary>worker.current_lng DECIMAL(9,6) NULL</summary>
    public decimal? CurrentLng { get; set; }

    /// <summary>worker.bank_account_no VARCHAR(30) NULL</summary>
    public string? BankAccountNo { get; set; }

    /// <summary>worker.bank_name NVARCHAR(100) NULL</summary>
    public string? BankName { get; set; }

    /// <summary>worker.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>worker.updated_at DATETIME2</summary>
    public DateTime UpdatedAt { get; set; }
}
