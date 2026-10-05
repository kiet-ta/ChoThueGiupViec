using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table ADMIN_AUDIT_LOG. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class AdminAuditLog
{
    /// <summary>admin_audit_log.log_id BIGINT (PK)</summary>
    public long LogId { get; set; }

    /// <summary>admin_audit_log.actor_type VARCHAR(10)</summary>
    public AuditActorType ActorType { get; set; }

    /// <summary>admin_audit_log.admin_id INT NULL</summary>
    public int? AdminId { get; set; }

    /// <summary>admin_audit_log.entity_type VARCHAR(30)</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>admin_audit_log.entity_id VARCHAR(40)</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>admin_audit_log.field_name VARCHAR(50)</summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>admin_audit_log.old_value NVARCHAR(500) NULL</summary>
    public string? OldValue { get; set; }

    /// <summary>admin_audit_log.new_value NVARCHAR(500) NULL</summary>
    public string? NewValue { get; set; }

    /// <summary>admin_audit_log.reason NVARCHAR(255) NULL</summary>
    public string? Reason { get; set; }

    /// <summary>admin_audit_log.changed_at DATETIME2</summary>
    public DateTime ChangedAt { get; set; }
}
