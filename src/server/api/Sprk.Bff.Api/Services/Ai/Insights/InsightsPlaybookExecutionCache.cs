using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai.Insights;

/// <summary>
/// Redis-backed cache wrapping <see cref="IPlaybookExecutionEngine.ExecuteBatchAsync"/>
/// for Insights-mode playbooks (D-P13, SPEC §3.1). Returns the cached
/// <see cref="InsightArtifact"/> when an identical
/// <c>(playbookId, subject, parameters, accessibleScopeHash)</c> tuple is presented within
/// the TTL window; otherwise drains the engine stream, extracts the
/// <c>ReturnInsightArtifactNode</c> output, caches it, and returns it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this lives in Zone A</b> (per SPEC §3.5): this class imports
/// <see cref="IPlaybookExecutionEngine"/> via the engine factory delegate (Zone A internal),
/// and references <see cref="PlaybookStreamEvent"/> + <see cref="NodeOutput"/>. The
/// <see cref="InsightArtifact"/> it returns is a Zone B POCO, so Zone B consumers
/// (D-P15 endpoint, D-P11 review surface) never have to import this class — they go
/// through the <c>IInsightsAi</c> facade (task 042) which calls into this cache.
/// </para>
/// <para>
/// <b>ADR-009 compliance</b>:
/// <list type="bullet">
/// <item>Uses <see cref="IDistributedCache"/> abstraction only — no direct Redis client</item>
/// <item>Per-playbook TTL with <see cref="DistributedCacheEntryOptions.AbsoluteExpirationRelativeToNow"/></item>
/// <item>Graceful degradation on cache failure — engine is still invoked, cache write is best-effort</item>
/// <item>JSON serialisation via <see cref="JsonSerializer"/> (handles polymorphic <see cref="InsightArtifact"/>)</item>
/// </list>
/// </para>
/// <para>
/// <b>Cache key</b>: composed by <see cref="InsightsPlaybookCacheKey.Compose"/>. The key
/// includes <c>accessibleScopeHash</c> so within-tenant access changes (DEP-3) naturally
/// produce different keys — old cached entries simply expire on their TTL.
/// </para>
/// <para>
/// <b>Artifact extraction</b>: scans <see cref="PlaybookStreamEvent"/>s for the last
/// <see cref="PlaybookEventType.NodeCompleted"/> event whose <see cref="PlaybookStreamEvent.NodeName"/>
/// equals <see cref="ReturnInsightArtifactNodeName"/> ("ReturnInsightArtifactNode") and whose
/// <see cref="NodeOutput.StructuredData"/> deserialises to an <see cref="InsightArtifact"/>.
/// If no such event is found, the cache returns <c>null</c> and does NOT write to cache
/// (no point caching a failed run; the user can retry).
/// </para>
/// </remarks>
public class InsightsPlaybookExecutionCache : IInsightsPlaybookExecutionCache
{
    /// <summary>Default TTL when the playbook supplies none (5 min per D-P13).</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Conventional name of the terminal node in an Insights-mode playbook (D-P12) that
    /// emits the final <see cref="InsightArtifact"/>. The cache identifies which
    /// <see cref="PlaybookStreamEvent"/> carries the artifact by matching this name.
    /// </summary>
    public const string ReturnInsightArtifactNodeName = "ReturnInsightArtifactNode";

    private readonly IDistributedCache _cache;
    private readonly ILogger<InsightsPlaybookExecutionCache> _logger;
    private readonly InsightsCacheMetrics? _metrics;

    public InsightsPlaybookExecutionCache(
        IDistributedCache cache,
        ILogger<InsightsPlaybookExecutionCache> logger,
        InsightsCacheMetrics? metrics = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics; // Optional — null in test scenarios that don't care about metrics
    }

    /// <inheritdoc />
    public async Task<InsightArtifact?> GetOrExecuteAsync(
        InsightsPlaybookExecutionRequest request,
        Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>> engineInvocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(engineInvocation);

        var key = InsightsPlaybookCacheKey.Compose(
            request.PlaybookId, request.Subject, request.Parameters, request.AccessibleScopeHash);

        var ttl = request.Ttl ?? DefaultTtl;
        var ttlSeconds = (int)ttl.TotalSeconds;

        // ---- HOT PATH: try Redis first (ADR-009) ----
        var sw = Stopwatch.StartNew();
        byte[]? cachedBytes = null;
        try
        {
            cachedBytes = await _cache.GetAsync(key, cancellationToken);
        }
        catch (Exception ex)
        {
            // Redis transient failure — degrade to engine invocation. ADR-009 mandates
            // graceful failure: cache is an optimisation, not a hard dependency.
            _logger.LogWarning(ex,
                "Insights playbook cache GET failed for playbook {PlaybookId}; falling back to engine",
                request.PlaybookId);
        }
        sw.Stop();

        if (cachedBytes is not null)
        {
            try
            {
                var cached = JsonSerializer.Deserialize<InsightArtifact>(cachedBytes);
                if (cached is not null)
                {
                    _metrics?.RecordHit(request.PlaybookId, request.TenantId, ttlSeconds, sw.Elapsed.TotalMilliseconds);
                    _logger.LogDebug(
                        "Insights playbook cache HIT for playbook {PlaybookId}, subject {Subject}, key {Key}",
                        request.PlaybookId, request.Subject, key);
                    return cached;
                }

                // Corrupt entry — log + treat as miss. Don't throw; we have a recovery path (re-run engine).
                _logger.LogWarning(
                    "Insights playbook cache returned non-null bytes but deserialisation produced null for key {Key}; treating as miss",
                    key);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "Insights playbook cache entry for key {Key} could not be deserialised; treating as miss and overwriting",
                    key);
            }
        }

        // ---- CACHE MISS: invoke the playbook engine ----
        _metrics?.RecordMiss(request.PlaybookId, request.TenantId, ttlSeconds, sw.Elapsed.TotalMilliseconds);
        _logger.LogDebug(
            "Insights playbook cache MISS for playbook {PlaybookId}, subject {Subject}; invoking engine",
            request.PlaybookId, request.Subject);

        var artifact = await DrainEngineStreamAsync(engineInvocation, cancellationToken);
        if (artifact is null)
        {
            // Engine completed but no ReturnInsightArtifactNode output (e.g., decline path
            // emitted a DeclineResponse instead — that's a different code path, the facade
            // handles it). We don't cache nulls because the next invocation might produce
            // an artifact if upstream data has appeared.
            _logger.LogDebug(
                "Insights playbook {PlaybookId} produced no InsightArtifact; not caching",
                request.PlaybookId);
            return null;
        }

        // ---- WRITE-THROUGH: cache the artifact (best-effort) ----
        try
        {
            var serialised = JsonSerializer.SerializeToUtf8Bytes(artifact);
            await _cache.SetAsync(
                key,
                serialised,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
                cancellationToken);

            _logger.LogDebug(
                "Cached InsightArtifact for playbook {PlaybookId}, subject {Subject}, TTL={TtlSeconds}s",
                request.PlaybookId, request.Subject, ttlSeconds);
        }
        catch (Exception ex)
        {
            // Write failure is non-fatal — we already have the artifact to return.
            _logger.LogWarning(ex,
                "Insights playbook cache SET failed for playbook {PlaybookId}; returning artifact uncached",
                request.PlaybookId);
        }

        return artifact;
    }

    /// <inheritdoc />
    public async Task EvictAsync(
        Guid playbookId,
        string subject,
        IReadOnlyDictionary<string, string>? parameters,
        string accessibleScopeHash,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var key = InsightsPlaybookCacheKey.Compose(playbookId, subject, parameters, accessibleScopeHash);
        try
        {
            await _cache.RemoveAsync(key, cancellationToken);
            _metrics?.RecordEviction(playbookId, tenantId);
            _logger.LogDebug(
                "Evicted Insights playbook cache entry for playbook {PlaybookId}, subject {Subject}, key {Key}",
                playbookId, subject, key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Insights playbook cache REMOVE failed for playbook {PlaybookId}; entry will expire on TTL",
                playbookId);
        }
    }

    /// <summary>
    /// Drain the engine's <see cref="IAsyncEnumerable{PlaybookStreamEvent}"/> stream and
    /// extract the final <see cref="InsightArtifact"/> from the
    /// <see cref="ReturnInsightArtifactNodeName"/> NodeCompleted event.
    /// </summary>
    /// <remarks>
    /// Iterates the full stream (rather than short-circuiting on first match) because:
    /// (a) the engine relies on the consumer draining the stream to allow downstream nodes
    /// to flush; (b) we want the LAST artifact-emitting event in case multiple ReturnInsightArtifactNode
    /// instances exist in a playbook graph (which would be unusual but not forbidden).
    /// </remarks>
    private async Task<InsightArtifact?> DrainEngineStreamAsync(
        Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>> engineInvocation,
        CancellationToken cancellationToken)
    {
        InsightArtifact? artifact = null;

        await foreach (var ev in engineInvocation(cancellationToken).WithCancellation(cancellationToken))
        {
            if (ev.Type != PlaybookEventType.NodeCompleted)
                continue;

            if (!string.Equals(ev.NodeName, ReturnInsightArtifactNodeName, StringComparison.Ordinal))
                continue;

            if (ev.NodeOutput is not { Success: true, StructuredData: { } structuredData })
                continue;

            try
            {
                var candidate = structuredData.Deserialize<InsightArtifact>();
                if (candidate is not null)
                {
                    artifact = candidate; // keep iterating to drain stream; capture last
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "ReturnInsightArtifactNode StructuredData failed to deserialise as InsightArtifact; skipping event");
            }
        }

        return artifact;
    }
}
