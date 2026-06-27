using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// OpenTelemetry-compatible metrics for the Insights playbook execution cache (D-P13).
/// Mirrors the design of <see cref="CacheMetrics"/> but uses an Insights-specific meter
/// so cache dashboards can distinguish synthesis-cache traffic from embedding-cache traffic.
/// </summary>
/// <remarks>
/// <para>
/// Meter name: <c>Sprk.Bff.Api.Insights.Cache</c>. Three counters are emitted:
/// </para>
/// <list type="bullet">
/// <item><c>insights_cache_hit</c> — cached InsightArtifact served without re-running the playbook</item>
/// <item><c>insights_cache_miss</c> — playbook engine invoked; result will be cached afterwards</item>
/// <item><c>insights_cache_eviction</c> — explicit invalidation (rare; mainly tests + admin tooling)</item>
/// </list>
/// <para>
/// Dimensions on every metric: <c>playbookId</c> (Guid string), <c>tenantId</c>,
/// <c>ttlSeconds</c> (only on hit/miss — eviction has no TTL context).
/// </para>
/// </remarks>
public class InsightsCacheMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;
    private readonly Counter<long> _evictions;
    private readonly Histogram<double> _latency;

    private const string MeterName = "Sprk.Bff.Api.Insights.Cache";

    public InsightsCacheMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _hits = _meter.CreateCounter<long>(
            name: "insights_cache_hit",
            unit: "{hit}",
            description: "Insights playbook cache hits (cached InsightArtifact returned)");

        _misses = _meter.CreateCounter<long>(
            name: "insights_cache_miss",
            unit: "{miss}",
            description: "Insights playbook cache misses (engine invoked, result will be cached)");

        _evictions = _meter.CreateCounter<long>(
            name: "insights_cache_eviction",
            unit: "{eviction}",
            description: "Insights playbook cache explicit invalidations");

        _latency = _meter.CreateHistogram<double>(
            name: "insights_cache_latency",
            unit: "ms",
            description: "Insights playbook cache get/set latency in milliseconds");
    }

    /// <summary>Record a cache hit (cached InsightArtifact returned without invoking engine).</summary>
    public void RecordHit(Guid playbookId, string tenantId, int ttlSeconds, double latencyMs)
    {
        var tags = new TagList
        {
            { "playbookId", playbookId.ToString("N") },
            { "tenantId",   tenantId },
            { "ttlSeconds", ttlSeconds }
        };
        _hits.Add(1, tags);
        _latency.Record(latencyMs,
            new KeyValuePair<string, object?>("cache.result", "hit"),
            new KeyValuePair<string, object?>("playbookId", playbookId.ToString("N")));
    }

    /// <summary>Record a cache miss (engine invocation will follow).</summary>
    public void RecordMiss(Guid playbookId, string tenantId, int ttlSeconds, double latencyMs)
    {
        var tags = new TagList
        {
            { "playbookId", playbookId.ToString("N") },
            { "tenantId",   tenantId },
            { "ttlSeconds", ttlSeconds }
        };
        _misses.Add(1, tags);
        _latency.Record(latencyMs,
            new KeyValuePair<string, object?>("cache.result", "miss"),
            new KeyValuePair<string, object?>("playbookId", playbookId.ToString("N")));
    }

    /// <summary>Record an explicit eviction (admin tooling / tests).</summary>
    public void RecordEviction(Guid playbookId, string tenantId)
    {
        var tags = new TagList
        {
            { "playbookId", playbookId.ToString("N") },
            { "tenantId",   tenantId }
        };
        _evictions.Add(1, tags);
    }

    public void Dispose() => _meter?.Dispose();
}
