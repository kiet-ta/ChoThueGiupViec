using CommonService.Application.Interfaces.IServices;
using Microsoft.Extensions.Caching.Memory;

namespace CommonService.Infrastructure.Services;

public class InMemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;

    public InMemoryCacheService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public Task<T?> GetAsync<T>(string key)
    {
        return Task.FromResult(_cache.Get<T>(key));
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        var options = new MemoryCacheEntryOptions();
        if (expiry.HasValue)
        {
            options.AbsoluteExpirationRelativeToNow = expiry;
        }
        _cache.Set(key, value, options);
        return Task.CompletedTask;
    }

    public  Task RemoveAsync(string key)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }
}
