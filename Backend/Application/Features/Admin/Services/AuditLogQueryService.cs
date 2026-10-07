using System.Globalization;
using CommonService.Application.Features.Admin.Dtos;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Admin.Services;

/// <summary>GET /api/admin/audit-logs rules of contract admin.md section 2.4 (BE-M6-09b).</summary>
public sealed class AuditLogQueryService(IAuditLogReadRepository repository) : IAuditLogQueryService
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public async Task<AuditLogQueryResult> SearchAsync(
        string? entityType, string? entityId, string? actorType, string? from, string? to,
        string? page, string? pageSize, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();

        var pageNumber = ParseInt(page, 1, 1, int.MaxValue, "page", "Page must be an integer of at least 1.", errors);
        var size = ParseInt(pageSize, DefaultPageSize, 1, MaxPageSize, "pageSize",
            $"Page size must be an integer between 1 and {MaxPageSize}.", errors);

        AuditActorType? actor = null;
        if (!string.IsNullOrWhiteSpace(actorType))
        {
            actor = ParseActor(actorType.Trim());
            if (actor is null) errors["actorType"] = ["Actor type must be ADMIN or SYSTEM."];
        }

        var fromUtc = ParseInstant(from, "from", errors);
        var toUtc = ParseInstant(to, "to", errors);
        if (fromUtc is not null && toUtc is not null && fromUtc >= toUtc)
        {
            errors["to"] = ["The 'to' instant must be later than 'from'."];
        }

        if (errors.Count > 0) return AuditLogQueryResult.ValidationError(errors);

        var filter = new AuditLogFilter(Blank(entityType), Blank(entityId), actor, fromUtc, toUtc);
        var (rows, total) = await repository.SearchAsync(filter, pageNumber, size, cancellationToken);

        return AuditLogQueryResult.Ok(new AuditLogPageDto
        {
            Items = rows.Select(ToDto).ToList(),
            Page = pageNumber,
            PageSize = size,
            Total = total,
        });
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ParseInt(string? raw, int fallback, int min, int max, string field, string message,
        Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (int.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            && value >= min && value <= max)
        {
            return value;
        }

        errors[field] = [message];
        return fallback;
    }

    private static AuditActorType? ParseActor(string raw)
    {
        foreach (var candidate in Enum.GetValues<AuditActorType>())
        {
            if (string.Equals(DbEnum.ToDb(candidate), raw, StringComparison.OrdinalIgnoreCase)) return candidate;
        }

        return null;
    }

    /// <summary>ISO-8601; a value without an offset is read as UTC ("2026-10-06" is midnight UTC).</summary>
    private static DateTime? ParseInstant(string? raw, string field, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateTimeOffset.TryParse(raw.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed.UtcDateTime;
        }

        errors[field] = [$"The '{field}' value must be an ISO-8601 date or UTC instant, for example 2026-10-06T00:00:00Z."];
        return null;
    }

    private static AuditLogEntryDto ToDto(AdminAuditLog row) => new()
    {
        LogId = row.LogId,
        ActorType = DbEnum.ToDb(row.ActorType),
        AdminId = row.AdminId,
        EntityType = row.EntityType,
        EntityId = row.EntityId,
        FieldName = row.FieldName,
        OldValue = row.OldValue,
        NewValue = row.NewValue,
        Reason = row.Reason,
        ChangedAt = DateTime.SpecifyKind(row.ChangedAt, DateTimeKind.Utc),
    };
}
