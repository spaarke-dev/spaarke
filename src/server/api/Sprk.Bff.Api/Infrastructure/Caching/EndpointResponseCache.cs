using Microsoft.Extensions.Caching.Memory;

namespace Sprk.Bff.Api.Infrastructure.Caching;

/// <summary>
/// Default implementation of <see cref="IEndpointResponseCache"/> backed by
/// <see cref="IMemoryCache"/>. Singleton lifetime matches the underlying
/// <c>IMemoryCache</c> registration (process-wide).
///
/// Added 2026-06-26 by ci-cd-unit-test-remediation-r1 task CICD-087 per spec FR-A06.
/// </summary>
public sealed class EndpointResponseCache(IMemoryCache memoryCache) : IEndpointResponseCache
{
    public bool TryGet<T>(string key, out T? value) where T : class
        => memoryCache.TryGetValue(key, out value);

    public void Set<T>(string key, T value, TimeSpan ttl) where T : class
    {
        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(ttl)
            .SetPriority(CacheItemPriority.Normal)
            .SetSize(1);
        memoryCache.Set(key, value, options);
    }

    public async Task<T?> GetOrCreateAsync<T>(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T?>> factory,
        CancellationToken cancellationToken = default) where T : class
    {
        return await memoryCache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ttl;
            entry.Size = 1;
            return await factory(cancellationToken);
        });
    }
}
