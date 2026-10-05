using CommonService.Application.Interfaces.IServices;

namespace CommonService.Infrastructure.Services;

/// <summary>
/// Cache-backed implementation of IIdempotencyService.
/// Works with InMemoryCacheService in single-instance/testing, and with Redis in production.
/// </summary>
public sealed class CacheIdempotencyService : IIdempotencyService
{
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(10);
    private readonly ICacheService _cache;

    public CacheIdempotencyService(ICacheService cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    private static string FormatLockKey(string key) => $"idemp:lock:{key}";
    private static string FormatResultKey(string key) => $"idemp:res:{key}";

    public async Task<bool> TryAcquireAsync(string key, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var effectiveExpiry = expiry ?? DefaultExpiry;

        var lockKey = FormatLockKey(key);
        var resKey = FormatResultKey(key);

        var isLocked = await _cache.GetAsync<bool?>(lockKey);
        if (isLocked == true)
        {
            return false;
        }

        var existingResult = await _cache.GetAsync<object>(resKey);
        if (existingResult != null)
        {
            return false;
        }

        await _cache.SetAsync(lockKey, true, effectiveExpiry);
        return true;
    }

    public async Task SetResultAsync<T>(string key, T result, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var effectiveExpiry = expiry ?? DefaultExpiry;

        var resKey = FormatResultKey(key);
        await _cache.SetAsync(resKey, result, effectiveExpiry);
    }

    public async Task<T?> GetResultAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var resKey = FormatResultKey(key);
        return await _cache.GetAsync<T>(resKey);
    }

    public async Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await _cache.RemoveAsync(FormatLockKey(key));
        await _cache.RemoveAsync(FormatResultKey(key));
    }

    public async Task<IdempotencyExecutionResult<T>> ExecuteAsync<T>(
        string key,
        Func<Task<T>> operation,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(operation);

        var existing = await GetResultAsync<T>(key, cancellationToken);
        if (existing != null)
        {
            return new IdempotencyExecutionResult<T>(IsCached: true, Value: existing);
        }

        var acquired = await TryAcquireAsync(key, expiry, cancellationToken);
        if (!acquired)
        {
            var cachedNow = await GetResultAsync<T>(key, cancellationToken);
            if (cachedNow != null)
            {
                return new IdempotencyExecutionResult<T>(IsCached: true, Value: cachedNow);
            }

            return new IdempotencyExecutionResult<T>(IsCached: false, Value: default, Conflict: true);
        }

        try
        {
            var result = await operation();
            await SetResultAsync(key, result, expiry, cancellationToken);
            return new IdempotencyExecutionResult<T>(IsCached: false, Value: result);
        }
        catch
        {
            await ReleaseAsync(key, cancellationToken);
            throw;
        }
    }
}
