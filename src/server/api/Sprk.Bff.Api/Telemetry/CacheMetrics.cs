using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// Metrics for Graph token cache performance (OpenTelemetry-compatible).
/// Tracks: cache hits, misses, hit rate, latency.
///
/// Usage:
/// - Meter name: "Sprk.Bff.Api.Cache" for OpenTelemetry configuration
/// - Metrics: cache.hits, cache.misses, cache.latency
/// - Dimensions: cache.type (graph), cache.result (hit/miss)
/// </summary>
public class CacheMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _cacheHits;
    private readonly Counter<long> _cacheMisses;
    private readonly Histogram<double> _cacheLatency;

    // Meter name for OpenTelemetry
    private const string MeterName = "Sprk.Bff.Api.Cache";

    public CacheMetrics()
    {
        // Create meter (OpenTelemetry-compatible)
        _meter = new Meter(MeterName, "1.0.0");

        // Counter: Total cache hits
        _cacheHits = _meter.CreateCounter<long>(
            name: "cache.hits",
            unit: "{hit}",
            description: "Total number of cache hits");

        // Counter: Total cache misses
        _cacheMisses = _meter.CreateCounter<long>(
            name: "cache.misses",
            unit: "{miss}",
            description: "Total number of cache misses");

        // Histogram: Cache operation latency
        _cacheLatency = _meter.CreateHistogram<double>(
            name: "cache.latency",
            unit: "ms",
            description: "Cache operation latency in milliseconds");
    }

    /// <summary>
    /// Record a cache hit.
    /// </summary>
    /// <param name="latencyMs">Cache lookup latency in milliseconds</param>
    /// <param name="cacheType">Type of cache (default: "graph")</param>
    public void RecordHit(double latencyMs, string cacheType = "graph")
    {
        _cacheHits.Add(1, new KeyValuePair<string, object?>("cache.type", cacheType));
        _cacheLatency.Record(latencyMs,
            new KeyValuePair<string, object?>("cache.result", "hit"),
            new KeyValuePair<string, object?>("cache.type", cacheType));
    }

    /// <summary>
    /// Record a cache miss.
    /// </summary>
    /// <param name="latencyMs">Cache lookup latency in milliseconds</param>
    /// <param name="cacheType">Type of cache (default: "graph")</param>
    public void RecordMiss(double latencyMs, string cacheType = "graph")
    {
        _cacheMisses.Add(1, new KeyValuePair<string, object?>("cache.type", cacheType));
        _cacheLatency.Record(latencyMs,
            new KeyValuePair<string, object?>("cache.result", "miss"),
            new KeyValuePair<string, object?>("cache.type", cacheType));
    }

    /// <summary>
    /// Dispose the meter when the service is disposed.
    /// </summary>
    public void Dispose()
    {
        _meter?.Dispose();
    }
}
