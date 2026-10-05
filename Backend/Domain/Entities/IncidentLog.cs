using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table INCIDENT_LOG. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class IncidentLog
{
    /// <summary>incident_log.incident_id BIGINT (PK)</summary>
    public long IncidentId { get; set; }

    /// <summary>incident_log.assignment_id BIGINT (FK)</summary>
    public long AssignmentId { get; set; }

    /// <summary>incident_log.incident_type VARCHAR(20)</summary>
    public string IncidentType { get; set; } = string.Empty;

    /// <summary>incident_log.description NVARCHAR(500) NULL</summary>
    public string? Description { get; set; }

    /// <summary>incident_log.photo_url NVARCHAR(500)</summary>
    public string PhotoUrl { get; set; } = string.Empty;

    /// <summary>incident_log.latitude DECIMAL(9,6)</summary>
    public decimal Latitude { get; set; }

    /// <summary>incident_log.longitude DECIMAL(9,6)</summary>
    public decimal Longitude { get; set; }

    /// <summary>incident_log.redispatch_status VARCHAR(20)</summary>
    public string RedispatchStatus { get; set; } = string.Empty;

    /// <summary>incident_log.penalty_waived BIT</summary>
    public bool PenaltyWaived { get; set; }

    /// <summary>incident_log.reported_at DATETIME2</summary>
    public DateTime ReportedAt { get; set; }
}
