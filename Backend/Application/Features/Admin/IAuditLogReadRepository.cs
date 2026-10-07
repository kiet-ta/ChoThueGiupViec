using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Admin;

/// <param name="EntityType">Exact match on entity_type.</param>
/// <param name="EntityId">Exact match on entity_id.</param>
/// <param name="ActorType">ADMIN or SYSTEM.</param>
/// <param name="FromUtc">Inclusive lower bound of changed_at.</param>
/// <param name="ToUtc">Exclusive upper bound of changed_at.</param>
public sealed record AuditLogFilter(
    string? EntityType,
    string? EntityId,
    AuditActorType? ActorType,
    DateTime? FromUtc,
    DateTime? ToUtc);

/// <summary>
/// Read side of ADMIN_AUDIT_LOG (BE-M6-09b). Read-only by design: writing goes through the IAuditLog port, and the
/// table is append-only (decisions G-5), so this contract has no update or delete.
/// </summary>
public interface IAuditLogReadRepository
{
    /// <summary>Newest first (changed_at, then log_id, descending). <paramref name="page"/> is 1-based.</summary>
    Task<(IReadOnlyList<AdminAuditLog> Items, int Total)> SearchAsync(
        AuditLogFilter filter, int page, int pageSize, CancellationToken cancellationToken = default);
}
