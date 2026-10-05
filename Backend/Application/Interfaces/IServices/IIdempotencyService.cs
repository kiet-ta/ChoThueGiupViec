namespace CommonService.Application.Interfaces.IServices;

/// <summary>
/// Result of an idempotent execution attempt.
/// </summary>
public sealed record IdempotencyExecutionResult<T>(
    bool IsCached,
    T? Value,
    bool Conflict = false);

/// <summary>
/// Service to prevent double-click and replay attacks on webhooks and sensitive commands.
/// </summary>
public interface IIdempotencyService
{
    /// <summary>
    /// Attempts to acquire an execution lock for the given key.
    /// Returns true if lock was acquired (first execution).
    /// Returns false if already locked or completed.
    /// </summary>
    Task<bool> TryAcquireAsync(string key, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the execution result for this key so subsequent requests return the cached result.
    /// </summary>
    Task SetResultAsync<T>(string key, T result, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a previously stored result for this key, if available.
    /// </summary>
    Task<T?> GetResultAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the idempotency key (e.g. if the operation failed and retry is allowed).
    /// </summary>
    Task ReleaseAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an operation idempotently:
    /// - If already completed, returns cached result.
    /// - If another concurrent request holds the lock, returns conflict.
    /// - Otherwise executes the operation, caches the result, and returns it.
    /// </summary>
    Task<IdempotencyExecutionResult<T>> ExecuteAsync<T>(
        string key,
        Func<Task<T>> operation,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);
}
