using CommonService.Domain.Enums;

namespace CommonService.Application.Interfaces.Ports;

/// <summary>One row of ADMIN_AUDIT_LOG (decisions SC-3). Append-only: the port has no update or delete.</summary>
public sealed record AuditEntry(
    AuditActorType ActorType,
    int? AdminId,
    string EntityType,
    string EntityId,
    string FieldName,
    string? OldValue,
    string? NewValue,
    string? Reason);

/// <summary>Writes the audit log in the same unit of work as the change (decisions G-5). Implemented by Admin (M6).</summary>
public interface IAuditLog
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
