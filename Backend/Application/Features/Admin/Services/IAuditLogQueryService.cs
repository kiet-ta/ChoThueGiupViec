namespace CommonService.Application.Features.Admin.Services;

public interface IAuditLogQueryService
{
    /// <summary>
    /// Every argument is the raw query-string text so that a bad value becomes a 400 with field messages
    /// (decision O5) instead of a model-binding error. Null or blank means "not given".
    /// </summary>
    Task<AuditLogQueryResult> SearchAsync(
        string? entityType, string? entityId, string? actorType, string? from, string? to,
        string? page, string? pageSize, CancellationToken cancellationToken = default);
}
