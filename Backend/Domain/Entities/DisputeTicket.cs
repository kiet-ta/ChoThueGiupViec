using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table DISPUTE_TICKET. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class DisputeTicket
{
    /// <summary>dispute_ticket.dispute_id INT (PK)</summary>
    public int DisputeId { get; set; }

    /// <summary>dispute_ticket.order_id BIGINT (FK) (UNIQUE)</summary>
    public long OrderId { get; set; }

    /// <summary>dispute_ticket.resolved_by INT NULL (FK)</summary>
    public int? ResolvedBy { get; set; }

    /// <summary>dispute_ticket.raised_by VARCHAR(10)</summary>
    public string RaisedBy { get; set; } = string.Empty;

    /// <summary>dispute_ticket.category VARCHAR(15)</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>dispute_ticket.description NVARCHAR(1000)</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>dispute_ticket.evidence_urls NVARCHAR(MAX) NULL</summary>
    public string? EvidenceUrls { get; set; }

    /// <summary>dispute_ticket.dispute_status VARCHAR(12)</summary>
    public string DisputeStatus { get; set; } = string.Empty;

    /// <summary>dispute_ticket.fault_party VARCHAR(12) NULL</summary>
    public string? FaultParty { get; set; }

    /// <summary>dispute_ticket.compensation_amount DECIMAL(18,2) NULL</summary>
    public decimal? CompensationAmount { get; set; }

    /// <summary>dispute_ticket.sla_due_at DATETIME2</summary>
    public DateTime SlaDueAt { get; set; }

    /// <summary>dispute_ticket.resolved_at DATETIME2 NULL</summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>dispute_ticket.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }
}
