using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// Canonical metrics surface for the BFF cache layer (OpenTelemetry-compatible).
/// Single Meter named <c>Sprk.Bff.Api.Cache</c> owns all cache instruments.
/// </summary>
/// <remarks>
/// <para>
/// FR-02 of <c>spaarke-redis-cache-remediation-r2</c>: this class is the single owner
/// of <c>Meter("Sprk.Bff.Api.Cache")</c>. Previously the meter was created in two places
/// (this file as an instance class + <see cref="Sprk.Bff.Api.Infrastructure.Cache.TenantCache"/>
/// static fields), causing OpenTelemetry to emit measurements through two distinct
/// <see cref="Meter"/> instances under the same name — unstable. R2 collapses to a single
/// canonical static class.
/// </para>
/// <para>
/// Per ADR-010 (DI minimalism): a static class is preferred over an instance class when
/// state is purely diagnostic. There is no DI registration; consumers call the static
/// <c>Record*</c> methods (or instruments) directly.
/// </para>
/// <para>
/// Instruments emitted:
/// </para>
/// <list type="bullet">
/// <item><c>cache.hits</c> — Counter&lt;long&gt;; tags vary by call site (decorator layer adds <c>tier=raw</c>).</item>
/// <item><c>cache.misses</c> — Counter&lt;long&gt;.</item>
/// <item><c>cache.failures</c> — Counter&lt;long&gt; with <c>op</c> + <c>outcome</c> tags (FR-01, task 001).</item>
/// <item><c>cache.redis_call_duration_ms</c> — Histogram&lt;double&gt; emitted by the decorator.</item>
/// <item><c>cache.latency</c> — Histogram&lt;double&gt; emitted by per-cache consumers (legacy compatibility).</item>
/// </list>
/// </remarks>
public static class CacheMetrics
{
    /// <summary>Meter name registered in <c>TelemetryModule.AddMeter(...)</c>.</summary>
    public const string MeterName = "Sprk.Bff.Api.Cache";

    /// <summary>Single canonical <see cref="Meter"/> instance for the cache layer.</summary>
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>Total cache hits.</summary>
    public static readonly Counter<long> HitsCounter = Meter.CreateCounter<long>(
        name: "cache.hits",
        unit: "{hit}",
        description: "Total number of cache hits.");

    /// <summary>Total cache misses.</summary>
    public static readonly Counter<long> MissesCounter = Meter.CreateCounter<long>(
        name: "cache.misses",
        unit: "{miss}",
        description: "Total number of cache misses.");

    /// <summary>
    /// FR-01 (spaarke-redis-cache-remediation-r2 task 001): cache.failures counter
    /// dimensioned by outcome (timeout/canceled/connection/serialization/other) and op
    /// (get/set/refresh/remove). Emitted by the MetricsDistributedCache decorator.
    /// </summary>
    public static readonly Counter<long> FailuresCounter = Meter.CreateCounter<long>(
        name: "cache.failures",
        unit: "{failure}",
        description: "Count of cache operation failures by outcome and op.");

    /// <summary>Cache call duration at the IDistributedCache decorator layer.</summary>
    public static readonly Histogram<double> CallDurationHistogram = Meter.CreateHistogram<double>(
        name: "cache.redis_call_duration_ms",
        unit: "ms",
        description: "Cache I/O duration in milliseconds at the IDistributedCache decorator.");

    /// <summary>
    /// Latency histogram emitted by per-cache consumers (GraphTokenCache, EmbeddingCache,
    /// CachedAccessDataSource, etc.). Tagged with <c>cache.type</c> + <c>cache.result</c>.
    /// </summary>
    public static readonly Histogram<double> LatencyHistogram = Meter.CreateHistogram<double>(
        name: "cache.latency",
        unit: "ms",
        description: "Per-consumer cache operation latency in milliseconds.");

    /// <summary>
    /// FR-03 (spaarke-redis-cache-remediation-r2 task 003): cache.hits.by_resource counter
    /// dimensioned by the logical <c>resource</c> name from the TenantCache key shape
    /// <c>tenant:{tid}:{resource}:{id}:v{n}</c>. Emitted at the <see cref="Sprk.Bff.Api.Infrastructure.Cache.TenantCache"/>
    /// wrapper layer only — the primary <see cref="HitsCounter"/> at the decorator layer
    /// remains undimensioned by resource (R1 amendment to ADR-009 dropped per-tag dims at
    /// the decorator layer due to unbounded cardinality). At the wrapper layer, cardinality
    /// is naturally bounded (~10-20 resource names per NFR-06).
    /// </summary>
    public static readonly Counter<long> HitsByResourceCounter = Meter.CreateCounter<long>(
        name: "cache.hits.by_resource",
        unit: "{hit}",
        description: "Cache hits at the TenantCache wrapper layer, dimensioned by logical resource name.");

    /// <summary>
    /// FR-03 (spaarke-redis-cache-remediation-r2 task 003): cache.misses.by_resource counter
    /// dimensioned by the logical <c>resource</c> name. See <see cref="HitsByResourceCounter"/>
    /// for cardinality rationale.
    /// </summary>
    public static readonly Counter<long> MissesByResourceCounter = Meter.CreateCounter<long>(
        name: "cache.misses.by_resource",
        unit: "{miss}",
        description: "Cache misses at the TenantCache wrapper layer, dimensioned by logical resource name.");

    /// <summary>
    /// Record a cache hit. <paramref name="cacheType"/> tags the measurement so dashboards
    /// can break down hit rate by consumer (e.g. "graph", "embedding", "auth-access").
    /// </summary>
    public static void RecordHit(double latencyMs, string cacheType = "graph")
    {
        HitsCounter.Add(1, new KeyValuePair<string, object?>("cache.type", cacheType));
        LatencyHistogram.Record(latencyMs,
            new KeyValuePair<string, object?>("cache.result", "hit"),
            new KeyValuePair<string, object?>("cache.type", cacheType));
    }

    /// <summary>
    /// Record a cache miss. <paramref name="cacheType"/> tags the measurement so dashboards
    /// can break down miss rate by consumer.
    /// </summary>
    public static void RecordMiss(double latencyMs, string cacheType = "graph")
    {
        MissesCounter.Add(1, new KeyValuePair<string, object?>("cache.type", cacheType));
        LatencyHistogram.Record(latencyMs,
            new KeyValuePair<string, object?>("cache.result", "miss"),
            new KeyValuePair<string, object?>("cache.type", cacheType));
    }
}
