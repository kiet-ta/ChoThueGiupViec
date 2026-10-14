namespace CommonService.Domain.Entities;

/// <remarks>
/// Table ESCROW_APPEAL (SC-13). An agency's appeal against one PENALTY row of its escrow ledger, within <c>Sla.AppealHours</c> (decisions
/// Q09); the Admin decides it manually. At most one appeal per penalty (unique index on <see cref="EscrowTxnId"/>).
/// </remarks>
public partial class EscrowAppeal
{
    /// <summary>escrow_appeal.appeal_id INT IDENTITY (PK)</summary>
    public int AppealId { get; set; }

    /// <summary>escrow_appeal.escrow_txn_id BIGINT (FK ESCROW_TRANSACTION, UNIQUE): the PENALTY row appealed</summary>
    public long EscrowTxnId { get; set; }

    /// <summary>escrow_appeal.agency_id INT (FK PARTNER_AGENCY)</summary>
    public int AgencyId { get; set; }

    /// <summary>escrow_appeal.reason NVARCHAR(500)</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>escrow_appeal.status VARCHAR(10): PENDING, ACCEPTED or REJECTED</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>escrow_appeal.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>escrow_appeal.decided_by_admin_id INT NULL (FK ADMIN)</summary>
    public int? DecidedByAdminId { get; set; }

    /// <summary>escrow_appeal.decision_note NVARCHAR(255) NULL</summary>
    public string? DecisionNote { get; set; }

    /// <summary>escrow_appeal.decided_at DATETIME2 NULL</summary>
    public DateTime? DecidedAt { get; set; }
}
