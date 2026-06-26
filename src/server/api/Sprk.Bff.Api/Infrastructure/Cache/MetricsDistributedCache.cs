using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Telemetry;
using StackExchange.Redis;

namespace Sprk.Bff.Api.Infrastructure.Cache;

/// <summary>
/// <see cref="IDistributedCache"/> decorator that emits the
/// <c>cache.hits</c> / <c>cache.misses</c> / <c>cache.redis_call_duration_ms</c> Meter
/// instruments on every cache call, plus the <c>cache.failures</c> Counter on exception
/// (R2 FR-01).
/// </summary>
/// <remarks>
/// R1 R7-S7 sub-gap #2 closure (2026-06-26) wired emission at the IDistributedCache
/// layer so the ~11 system-cache call sites that inject IDistributedCache directly
/// also get counted exactly once. R2 FR-01 adds try/catch + cache.failures so Redis
/// outages surface in App Insights instead of silently degrading.
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
        try
        {
            var bytes = _inner.Get(key);
            sw.Stop();
            RecordGet(bytes, sw.Elapsed.TotalMilliseconds);
            return bytes;
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordFailure("get", ex);
            throw;
        }
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var bytes = await _inner.GetAsync(key, token).ConfigureAwait(false);
            sw.Stop();
            RecordGet(bytes, sw.Elapsed.TotalMilliseconds);
            return bytes;
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordFailure("get", ex);
            throw;
        }
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            _inner.Set(key, value, options);
            sw.Stop();
            RecordOp("set", sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordFailure("set", ex);
            throw;
        }
    }

    public async Task SetAsync(
        string key,
        byte[] value,
        DistributedCacheEntryOptions options,
        CancellationToken token = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.SetAsync(key, value, options, token).ConfigureAwait(false);
            sw.Stop();
            RecordOp("set", sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordFailure("set", ex);
            throw;
        }
    }

    public void Refresh(string key)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            _inner.Refresh(key);
            sw.Stop();
            RecordOp("refresh", sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordFailure("refresh", ex);
            throw;
        }
    }

    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.RefreshAsync(key, token).ConfigureAwait(false);
            sw.Stop();
            RecordOp("refresh", sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordFailure("refresh", ex);
            throw;
        }
    }

    public void Remove(string key)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            _inner.Remove(key);
            sw.Stop();
            RecordOp("remove", sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordFailure("remove", ex);
            throw;
        }
    }

    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.RemoveAsync(key, token).ConfigureAwait(false);
            sw.Stop();
            RecordOp("remove", sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordFailure("remove", ex);
            throw;
        }
    }

    private static void RecordGet(byte[]? bytes, double elapsedMs)
    {
        CacheMetrics.CallDurationHistogram.Record(
            elapsedMs,
            RawTierTag,
            new KeyValuePair<string, object?>("op", "get"));

        if (bytes is null || bytes.Length == 0)
        {
            CacheMetrics.MissesCounter.Add(1, RawTierTag);
        }
        else
        {
            CacheMetrics.HitsCounter.Add(1, RawTierTag);
        }
    }

    private static void RecordOp(string op, double elapsedMs)
    {
        CacheMetrics.CallDurationHistogram.Record(
            elapsedMs,
            RawTierTag,
            new KeyValuePair<string, object?>("op", op));
    }

    private static void RecordFailure(string op, Exception ex)
    {
        CacheMetrics.FailuresCounter.Add(
            1,
            RawTierTag,
            new KeyValuePair<string, object?>("op", op),
            new KeyValuePair<string, object?>("outcome", ClassifyException(ex)));
    }

    internal static string ClassifyException(Exception ex) => ex switch
    {
        OperationCanceledException => "canceled",
        TimeoutException => "timeout", // RedisTimeoutException derives from TimeoutException — covered by this arm.
        RedisConnectionException => "connection",
        SocketException => "connection",
        JsonException => "serialization",
        _ => "other",
    };
}
