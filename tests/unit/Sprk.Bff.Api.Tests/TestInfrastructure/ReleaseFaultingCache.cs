using Microsoft.Extensions.Caching.Distributed;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>An <see cref="IDistributedCache"/> over another cache whose <c>RemoveAsync</c> can be made to fault: the processing-lock release
/// failure the idempotency service swallows, which leaves the lock behind until its owner takeover age (shared by the job-handler tests).</summary>
internal sealed class ReleaseFaultingCache(IDistributedCache inner) : IDistributedCache
{
    public bool FailRemove { get; set; }

    public byte[]? Get(string key) => inner.Get(key);
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => inner.GetAsync(key, token);
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => inner.Set(key, value, options);
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => inner.SetAsync(key, value, options, token);
    public void Refresh(string key) => inner.Refresh(key);
    public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);
    public void Remove(string key) => inner.Remove(key);
    public Task RemoveAsync(string key, CancellationToken token = default) =>
        FailRemove ? Task.FromException(new TimeoutException("cache")) : inner.RemoveAsync(key, token);
}
