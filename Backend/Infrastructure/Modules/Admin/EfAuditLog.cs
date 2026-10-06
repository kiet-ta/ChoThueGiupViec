using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;

namespace CommonService.Infrastructure.Modules.Admin;

/// <summary>
/// Real <see cref="IAuditLog"/> over ADMIN_AUDIT_LOG (decisions SC-3, G-5).
/// Append-only: it only adds rows. <see cref="WriteAsync"/> does NOT call SaveChanges: the row joins the caller's
/// unit of work, so it is committed or rolled back together with the change it records.
/// Callers write the entry BEFORE their own SaveChangesAsync / IUnitOfWork commit.
/// </summary>
public sealed class EfAuditLog(AppDbContext db, IClock clock) : IAuditLog
{
    // Column sizes of ADMIN_AUDIT_LOG (AdminAuditLogConfiguration): an audit value is never truncated silently.
    internal const int MaxEntityType = 30;
    internal const int MaxEntityId = 40;
    internal const int MaxFieldName = 50;
    internal const int MaxValue = 500;
    internal const int MaxReason = 255;

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Validate(entry);

        db.AdminAuditLogs.Add(new AdminAuditLog
        {
            ActorType = entry.ActorType,
            AdminId = entry.AdminId,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            FieldName = entry.FieldName,
            OldValue = entry.OldValue,
            NewValue = entry.NewValue,
            Reason = entry.Reason,
            ChangedAt = clock.UtcNow,
        });
        return Task.CompletedTask;
    }

    private static void Validate(AuditEntry entry)
    {
        Required(entry.EntityType, MaxEntityType, nameof(entry.EntityType));
        Required(entry.EntityId, MaxEntityId, nameof(entry.EntityId));
        Required(entry.FieldName, MaxFieldName, nameof(entry.FieldName));
        Optional(entry.OldValue, MaxValue, nameof(entry.OldValue));
        Optional(entry.NewValue, MaxValue, nameof(entry.NewValue));
        Optional(entry.Reason, MaxReason, nameof(entry.Reason));

        switch (entry.ActorType)
        {
            case AuditActorType.Admin:
                if (entry.AdminId is null)
                    throw new ArgumentException("An ADMIN audit entry needs the acting adminId.", nameof(entry));
                if (string.IsNullOrWhiteSpace(entry.Reason))
                    throw new ArgumentException("An ADMIN audit entry needs a non-empty reason (decisions G-5).", nameof(entry));
                break;
            case AuditActorType.System:
                if (entry.AdminId is not null)
                    throw new ArgumentException("A SYSTEM audit entry must not carry an adminId.", nameof(entry));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(entry), entry.ActorType, "Unknown audit actor type.");
        }
    }

    private static void Required(string value, int max, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name} is required.", name);
        Optional(value, max, name);
    }

    private static void Optional(string? value, int max, string name)
    {
        if (value is not null && value.Length > max)
            throw new ArgumentException($"{name} is longer than {max} characters.", name);
    }
}
