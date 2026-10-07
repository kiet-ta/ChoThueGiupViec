using CommonService.Application.Features.Admin;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Admin;

/// <summary>Read-only query over ADMIN_AUDIT_LOG: no tracking, no write method.</summary>
public sealed class EfAuditLogReadRepository(AppDbContext db) : IAuditLogReadRepository
{
    public async Task<(IReadOnlyList<AdminAuditLog> Items, int Total)> SearchAsync(
        AuditLogFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = db.AdminAuditLogs.AsNoTracking().AsQueryable();

        if (filter.EntityType is not null) query = query.Where(x => x.EntityType == filter.EntityType);
        if (filter.EntityId is not null) query = query.Where(x => x.EntityId == filter.EntityId);
        if (filter.ActorType is not null) query = query.Where(x => x.ActorType == filter.ActorType);
        if (filter.FromUtc is not null) query = query.Where(x => x.ChangedAt >= filter.FromUtc);
        if (filter.ToUtc is not null) query = query.Where(x => x.ChangedAt < filter.ToUtc);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.ChangedAt)
            .ThenByDescending(x => x.LogId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }
}
