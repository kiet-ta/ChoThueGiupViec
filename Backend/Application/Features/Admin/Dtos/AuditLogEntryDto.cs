namespace CommonService.Application.Features.Admin.Dtos;

/// <summary>One row of ADMIN_AUDIT_LOG as shown to an Admin (contract admin.md section 1, AuditLogEntry).</summary>
public sealed class AuditLogEntryDto
{
    public long LogId { get; init; }

    /// <summary>ADMIN or SYSTEM.</summary>
    public string ActorType { get; init; } = string.Empty;

    public int? AdminId { get; init; }
    public string EntityType { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string FieldName { get; init; } = string.Empty;
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string? Reason { get; init; }

    /// <summary>UTC.</summary>
    public DateTime ChangedAt { get; init; }
}

/// <summary>The paged shape used by the admin list endpoints: { items, page, pageSize, total } (admin.md section 2.4).</summary>
public sealed class AuditLogPageDto
{
    public IReadOnlyList<AuditLogEntryDto> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }
}
