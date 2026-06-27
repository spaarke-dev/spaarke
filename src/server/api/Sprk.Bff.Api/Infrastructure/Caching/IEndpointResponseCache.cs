namespace Sprk.Bff.Api.Infrastructure.Caching;

/// <summary>
/// Wrapper service for endpoint-scoped response caching. Hides the concrete
/// <c>IMemoryCache</c> dependency so that <c>*Endpoints</c> classes do not
/// import <c>Microsoft.Extensions.Caching.Memory</c> directly.
///
/// Rationale (ADR-009 + NetArchTest ADR009_CachingTests.MemoryCacheShouldNotBeSingleton):
/// the architecture rule bans direct <c>IMemoryCache</c> injection into
/// <c>*Endpoints</c> / <c>*Controller</c> types. Cross-request caching belongs
/// in dedicated services (or Redis via <c>IDistributedCache</c>). For process-wide
/// read-only registries (handler discovery, NavMap, playbook lookups) the
/// runtime semantics of <c>IMemoryCache</c> are appropriate — this wrapper
/// preserves those semantics while keeping the endpoint surface facade-isolated.
///
/// Added 2026-06-26 by ci-cd-unit-test-remediation-r1 task CICD-087 per spec FR-A06.
/// </summary>
public interface IEndpointResponseCache
{
    /// <summary>
    /// Try to retrieve a cached value of type <typeparamref name="T"/> by key.
    /// </summary>
    bool TryGet<T>(string key, out T? value) where T : class;

    /// <summary>
    /// Set a value in the cache with an absolute expiration TTL.
    /// </summary>
    void Set<T>(string key, T value, TimeSpan ttl) where T : class;

    /// <summary>
    /// Get a cached value, or invoke the async factory on miss and cache the result.
    /// Sets <c>Size = 1</c> on the cache entry for compatibility with size-limited
    /// MemoryCache configurations (see <c>PrivilegeGroupResolver</c> for prior art).
    /// </summary>
    Task<T?> GetOrCreateAsync<T>(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T?>> factory,
        CancellationToken cancellationToken = default) where T : class;
}
