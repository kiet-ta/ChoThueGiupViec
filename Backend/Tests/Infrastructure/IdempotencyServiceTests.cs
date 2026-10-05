using CommonService.Application.Common.Helpers;
using CommonService.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;

namespace CommonService.Tests.Infrastructure;

public class IdempotencyServiceTests
{
    private readonly CacheIdempotencyService _idempotencyService;

    public IdempotencyServiceTests()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cacheService = new InMemoryCacheService(memoryCache);
        _idempotencyService = new CacheIdempotencyService(cacheService);
    }

    [Fact]
    public async Task TryAcquireAsync_returns_true_once_and_false_subsequently()
    {
        var key = "order_12345_submit";

        var first = await _idempotencyService.TryAcquireAsync(key);
        var second = await _idempotencyService.TryAcquireAsync(key);

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public async Task SetResultAsync_and_GetResultAsync_store_and_retrieve_data()
    {
        var key = "webhook_momo_trans_999";
        var payload = new { OrderId = 123, Status = "SUCCESS" };

        await _idempotencyService.SetResultAsync(key, payload);
        var retrieved = await _idempotencyService.GetResultAsync<dynamic>(key);

        Assert.NotNull(retrieved);
    }

    [Fact]
    public async Task ReleaseAsync_clears_lock_allowing_reacquisition()
    {
        var key = "lock_test_key";

        var acquired1 = await _idempotencyService.TryAcquireAsync(key);
        Assert.True(acquired1);

        await _idempotencyService.ReleaseAsync(key);

        var acquired2 = await _idempotencyService.TryAcquireAsync(key);
        Assert.True(acquired2);
    }

    [Fact]
    public async Task ExecuteAsync_runs_action_first_time_and_returns_cached_second_time()
    {
        var key = "action_key_1";
        var callCount = 0;

        Task<string> Action()
        {
            callCount++;
            return Task.FromResult($"Result_{callCount}");
        }

        // First execution
        var result1 = await _idempotencyService.ExecuteAsync(key, Action);
        Assert.False(result1.IsCached);
        Assert.Equal("Result_1", result1.Value);
        Assert.Equal(1, callCount);

        // Second execution with same key
        var result2 = await _idempotencyService.ExecuteAsync(key, Action);
        Assert.True(result2.IsCached);
        Assert.Equal("Result_1", result2.Value);
        Assert.Equal(1, callCount); // Action was NOT called again
    }

    [Fact]
    public async Task ExecuteAsync_releases_lock_on_failure_allowing_subsequent_retry()
    {
        var key = "fail_key";
        var callCount = 0;

        Task<string> FailingAction()
        {
            callCount++;
            if (callCount == 1)
            {
                throw new InvalidOperationException("Network glitch");
            }
            return Task.FromResult("Recovered");
        }

        // First attempt throws
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _idempotencyService.ExecuteAsync(key, FailingAction));

        // Second attempt should succeed because lock was released on failure
        var result = await _idempotencyService.ExecuteAsync(key, FailingAction);
        Assert.False(result.IsCached);
        Assert.Equal("Recovered", result.Value);
    }

    [Fact]
    public void IdempotencyHelper_BuildKey_constructs_delimited_string()
    {
        var key = IdempotencyHelper.BuildKey("momo:webhook", 101, "TRANS999");
        Assert.Equal("momo:webhook:101:TRANS999", key);
    }

    [Fact]
    public void IdempotencyHelper_ComputePayloadHash_returns_deterministic_sha256()
    {
        var hash1 = IdempotencyHelper.ComputePayloadHash("{\"amount\": 50000}");
        var hash2 = IdempotencyHelper.ComputePayloadHash("{\"amount\": 50000}");
        var hash3 = IdempotencyHelper.ComputePayloadHash("{\"amount\": 60000}");

        Assert.NotEmpty(hash1);
        Assert.Equal(hash1, hash2);
        Assert.NotEqual(hash1, hash3);
    }
}
