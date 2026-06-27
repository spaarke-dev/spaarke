using System.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;

namespace Sprk.Bff.Api.Infrastructure.Cache;

/// <summary>
/// <see cref="IDistributedCache"/> decorator that emits the
/// <c>cache.hits</c> / <c>cache.misses</c> / <c>cache.redis_call_duration_ms</c> Meter
/// instruments on every cache call.
/// </summary>
/// <remarks>
/// Spaarke-redis-cache-remediation-r1 R7-S7 sub-gap #2 closure (2026-06-26).
/// Before this decorator existed, metrics were only emitted from
/// <see cref="TenantCache"/>, which meant the ~11 system-cache call sites (e.g.,
/// <c>CommunicationAccountService</c>, MSAL token cache, membership refresh) that
/// inject <c>IDistributedCache</c> directly emitted zero metrics. By moving emission
/// to the <c>IDistributedCache</c> layer, every cache I/O — whether routed through
/// the tenant-scoped wrapper or through the system-cache exception path — is
/// counted exactly once. <see cref="TenantCache"/> no longer emits its own
/// instruments to avoid double-counting.
///
/// The <c>resource</c> tag dimension cannot be derived at this layer (the cache
/// key is opaque, and <see cref="TenantCache"/>'s tenant-aware tagging happens
/// above it). For this layer we emit a single bounded <c>tier</c> dimension
/// (<c>tier=raw</c>) so dashboards can distinguish raw-layer counts from any
/// future wrapper-layer counts that re-introduce per-resource tagging.
/// </remarks>
internal sealed class MetricsDistributedCache : IDistributedCache
{
    private readonly IDistributedCache _inner;

    private static readonly KeyValuePair<string, object?> RawTierTag =
        new("tier", "raw");

    public MetricsDistributedCache(IDistributedCache inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public byte[]? Get(string key)
    {
        var sw = Stopwatch.StartNew();
        var bytes = _inner.Get(key);
        sw.Stop();
        RecordGet(bytes, sw.Elapsed.TotalMilliseconds);
        return bytes;
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        var sw = Stopwatch.StartNew();
        var bytes = await _inner.GetAsync(key, token).ConfigureAwait(false);
        sw.Stop();
        RecordGet(bytes, sw.Elapsed.TotalMilliseconds);
        return bytes;
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        var sw = Stopwatch.StartNew();
        _inner.Set(key, value, options);
        sw.Stop();
        RecordOp("set", sw.Elapsed.TotalMilliseconds);
    }

    public async Task SetAsync(
        string key,
        byte[] value,
        DistributedCacheEntryOptions options,
        CancellationToken token = default)
    {
        var sw = Stopwatch.StartNew();
        await _inner.SetAsync(key, value, options, token).ConfigureAwait(false);
        sw.Stop();
        RecordOp("set", sw.Elapsed.TotalMilliseconds);
    }

    public void Refresh(string key)
    {
        var sw = Stopwatch.StartNew();
        _inner.Refresh(key);
        sw.Stop();
        RecordOp("refresh", sw.Elapsed.TotalMilliseconds);
    }

    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        var sw = Stopwatch.StartNew();
        await _inner.RefreshAsync(key, token).ConfigureAwait(false);
        sw.Stop();
        RecordOp("refresh", sw.Elapsed.TotalMilliseconds);
    }

    public void Remove(string key)
    {
        var sw = Stopwatch.StartNew();
        _inner.Remove(key);
        sw.Stop();
        RecordOp("remove", sw.Elapsed.TotalMilliseconds);
    }

    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        var sw = Stopwatch.StartNew();
        await _inner.RemoveAsync(key, token).ConfigureAwait(false);
        sw.Stop();
        RecordOp("remove", sw.Elapsed.TotalMilliseconds);
    }

    private static void RecordGet(byte[]? bytes, double elapsedMs)
    {
        TenantCache.CallDurationHistogram.Record(
            elapsedMs,
            RawTierTag,
            new KeyValuePair<string, object?>("op", "get"));

        if (bytes is null || bytes.Length == 0)
        {
            TenantCache.MissesCounter.Add(1, RawTierTag);
        }
        else
        {
            TenantCache.HitsCounter.Add(1, RawTierTag);
        }
    }

    private static void RecordOp(string op, double elapsedMs)
    {
        TenantCache.CallDurationHistogram.Record(
            elapsedMs,
            RawTierTag,
            new KeyValuePair<string, object?>("op", op));
    }
}
