using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Sprk.Bff.Api.Infrastructure.Cache;
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

    /// <summary>
    /// Conventional name of the insufficient-evidence terminal node in an Insights-mode
    /// playbook (D-P12) that emits a structured <see cref="DeclineResponse"/> per D-49.
    /// Matches the predict-matter-cost playbook (task 060) where the DeclineToFindNode
    /// instance is named <c>declineInsufficient</c>.
    /// </summary>
    /// <remarks>
    /// Added by task 071 (Wave 8.5) so the cache surfaces real declines through the
    /// engine stream rather than the orchestrator returning a scaffold "no-artifact-produced"
    /// fallback. The drain matches both by exact name AND by structural fingerprint
    /// (deserialising StructuredData as <see cref="DeclineResponse"/> with all 5 required
    /// fields present) so future playbooks can rename the node without breaking decline
    /// propagation.
    /// </remarks>
    public const string DeclineToFindNodeName = "declineInsufficient";

    private readonly ITenantCache _cache;
    private readonly ILogger<InsightsPlaybookExecutionCache> _logger;
    private readonly InsightsCacheMetrics? _metrics;
    private readonly TopicRegistryTtlLookup? _registryTtl;

    // ITenantCache resource name (FR-05 redis remediation r1). Final on-wire key:
    // spaarke:tenant:{tenantId}:insights-playbook:{hashKey}:v1
    private const string CacheResource = "insights-playbook";
    private const int CacheVersion = 1;

    /// <summary>
    /// Per-key semaphores for in-process concurrent-invocation dedup (r1 Insights Widgets task 053
    /// / FR-22). When two requests arrive concurrently for the same
    /// <c>(playbookId, subject, parameters, accessibleScopeHash)</c> key, the second waits on the
    /// first to populate the cache, then re-reads Redis and returns the cached artifact instead of
    /// invoking the engine a second time. This satisfies FR-22 within a single BFF instance; cross-
    /// instance dedup would require a Redis-side distributed lock which is explicitly out of scope
    /// per task 053 (audit DR-002 "do NOT add new cache abstraction" + ADR-010 DI minimalism).
    ///
    /// The dictionary is concurrent-safe and self-cleaning via TryRemove on the last waiter
    /// (semaphore CurrentCount == 1 after release). Bounded by distinct hot-key cardinality
    /// during the engine invocation window — typically &lt;100 entries even under load. Each
    /// SemaphoreSlim is ~200 bytes; aggregate footprint negligible vs. the rest of the cache hot
    /// path.
    ///
    /// NOT a new abstraction: this is private state on the existing concrete singleton; the
    /// <see cref="IInsightsPlaybookExecutionCache"/> contract is unchanged.
    /// </summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _perKeyLocks = new();

    public InsightsPlaybookExecutionCache(
        ITenantCache cache,
        ILogger<InsightsPlaybookExecutionCache> logger,
        InsightsCacheMetrics? metrics = null,
        TopicRegistryTtlLookup? registryTtl = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics; // Optional — null in test scenarios that don't care about metrics

        // Optional per-topic TTL lookup (r1 Insights Widgets task 052 / FR-21). When non-null,
        // GetOrExecuteAsync resolves per-topic TTL from sprk_aitopicregistry.sprk_cachettlminutes
        // for the playbook (in-process mirror, ≤5 min refresh window). When null OR the
        // playbook has no registry entry, the cache falls back to DefaultTtl. The lookup is
        // a sealed POCO (NOT a new interface seam — ADR-010 + audit DR-002 "do NOT add new
        // cache abstraction"). It is registered alongside this cache in
        // AnalysisServicesModule.AddInsightsCache, which runs only when the compound AI gate
        // is ON (Endpoint↔DI Symmetry preserved per audit DR-008).
        _registryTtl = registryTtl;
    }

    /// <inheritdoc />
    public async Task<InsightsEngineRunResult> GetOrExecuteAsync(
        InsightsPlaybookExecutionRequest request,
        Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>> engineInvocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(engineInvocation);

        var key = InsightsPlaybookCacheKey.Compose(
            request.PlaybookId, request.Subject, request.Parameters, request.AccessibleScopeHash);

        // TTL resolution precedence (per FR-21 / r1 Insights Widgets task 052):
        //   1. Per-call override on the request (request.Ttl) — power-user / test path
        //   2. Per-topic TTL from sprk_aitopicregistry.sprk_cachettlminutes via the
        //      in-process mirror (TopicRegistryTtlLookup) — registry-driven default
        //   3. DefaultTtl (5 min) — cache-level fallback when neither override applies
        // The in-process mirror keeps this hot path Dataverse-free per task 052's
        // "avoid per-call Dataverse lookup" constraint.
        TimeSpan ttl;
        if (request.Ttl.HasValue)
        {
            ttl = request.Ttl.Value;
        }
        else if (_registryTtl is not null)
        {
            var (found, registryTtl) = await _registryTtl
                .TryGetTtlForPlaybookIdAsync(request.PlaybookId, cancellationToken)
                .ConfigureAwait(false);
            ttl = found ? registryTtl : DefaultTtl;
        }
        else
        {
            ttl = DefaultTtl;
        }

        var ttlSeconds = (int)ttl.TotalSeconds;

        // ---- HOT PATH: try Redis first (ADR-009 + FR-05 via ITenantCache wrapper) ----
        // Note: declines are NEVER cached (task 071), so any cached bytes deserialise to
        // an InsightArtifact only. A cache HIT therefore always means sufficient-evidence path.
        var sw = Stopwatch.StartNew();
        InsightArtifact? cached = null;
        try
        {
            cached = await _cache.GetAsync<InsightArtifact>(
                request.TenantId, CacheResource, key, CacheVersion, ct: cancellationToken);
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

        if (cached is not null)
        {
            _metrics?.RecordHit(request.PlaybookId, request.TenantId, ttlSeconds, sw.Elapsed.TotalMilliseconds);
            _logger.LogDebug(
                "Insights playbook cache HIT for playbook {PlaybookId}, subject {Subject}, key {Key}",
                request.PlaybookId, request.Subject, key);
            return InsightsEngineRunResult.FromArtifact(cached);
        }

        // ---- CACHE MISS: acquire per-key semaphore to dedup concurrent invocations (FR-22) ----
        // Per task 053: simultaneous requests for the same (playbookId, subject, parameters,
        // accessibleScopeHash) tuple MUST collapse to a single engine execution; the second
        // observer must see the cached envelope produced by the first. We achieve this with a
        // process-local SemaphoreSlim keyed on the same cache key — cheap, no new abstraction
        // (audit DR-002), no new interface seam (ADR-010). Cross-instance dedup is explicitly
        // out of scope for r1; short playbook TTLs + sticky-ish AppService routing make
        // per-instance dedup the dominant win.
        var perKeyLock = _perKeyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await perKeyLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        InsightsEngineRunResult runResult;
        try
        {
            // DOUBLE-CHECK: while we waited on the lock, the first invoker may have populated
            // the cache. Re-read Redis; if HIT, return the cached artifact WITHOUT invoking the
            // engine. This is the dedup payoff path for FR-22.
            try
            {
                var raceCached = await _cache.GetAsync<InsightArtifact>(
                    request.TenantId, CacheResource, key, CacheVersion, ct: cancellationToken).ConfigureAwait(false);
                if (raceCached is not null)
                {
                    _metrics?.RecordHit(request.PlaybookId, request.TenantId, ttlSeconds, 0);
                    _logger.LogDebug(
                        "Insights playbook cache HIT after per-key lock acquisition (concurrent-dedup, FR-22) for playbook {PlaybookId}, subject {Subject}, key {Key}",
                        request.PlaybookId, request.Subject, key);
                    return InsightsEngineRunResult.FromArtifact(raceCached);
                }
            }
            catch (Exception ex)
            {
                // Same graceful-degradation policy as the primary GET — fall through to engine.
                _logger.LogWarning(ex,
                    "Insights playbook cache double-check GET failed for playbook {PlaybookId}; falling back to engine",
                    request.PlaybookId);
            }

            // ---- TRUE CACHE MISS: invoke the playbook engine ----
            _metrics?.RecordMiss(request.PlaybookId, request.TenantId, ttlSeconds, sw.Elapsed.TotalMilliseconds);
            _logger.LogDebug(
                "Insights playbook cache MISS for playbook {PlaybookId}, subject {Subject}; invoking engine",
                request.PlaybookId, request.Subject);

            runResult = await DrainEngineStreamAsync(engineInvocation, cancellationToken);
        }
        finally
        {
            perKeyLock.Release();
            // Best-effort cleanup: remove the entry if no other waiters. Race-safe because
            // TryRemove is no-op when another thread has already acquired the semaphore for
            // a new request. Bounded growth of _perKeyLocks even under adversarial keys.
            if (perKeyLock.CurrentCount == 1)
            {
                _perKeyLocks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(key, perKeyLock));
            }
        }

        // Decline path: never cache (task 071). Evidence sufficiency depends on the current
        // state of the index; a cached decline becomes stale the moment a new Observation
        // lands. Return the decline directly so the orchestrator can surface it through
        // InsightsAgentResult.Declined with real MinimumEvidenceNeeded gap analysis.
        if (runResult.HasDecline)
        {
            _logger.LogDebug(
                "Insights playbook {PlaybookId} produced DeclineResponse (reason={Reason}); not caching (declines are state-dependent)",
                request.PlaybookId, runResult.Decline!.Reason);
            return runResult;
        }

        // Empty path: engine produced neither artifact nor decline (malformed playbook).
        // Don't cache; orchestrator logs Warning and surfaces a scaffold decline so the
        // facade contract's "exactly one of artifact/decline" invariant holds for Zone B.
        if (runResult.IsEmpty)
        {
            _logger.LogDebug(
                "Insights playbook {PlaybookId} produced no InsightArtifact and no DeclineResponse; not caching",
                request.PlaybookId);
            return runResult;
        }

        // ---- WRITE-THROUGH: cache the artifact (best-effort) ----
        var artifact = runResult.Artifact!;
        try
        {
            await _cache.SetAsync(
                request.TenantId, CacheResource, key, CacheVersion,
                artifact, ttl, ct: cancellationToken);

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

        return runResult;
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
            await _cache.RemoveAsync(tenantId, CacheResource, key, CacheVersion, ct: cancellationToken);
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
    /// extract <b>both</b> the <see cref="InsightArtifact"/> from a
    /// <see cref="ReturnInsightArtifactNodeName"/> NodeCompleted event (sufficient-evidence
    /// path) and the <see cref="DeclineResponse"/> from a <see cref="DeclineToFindNodeName"/>
    /// NodeCompleted event (insufficient-evidence path).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Task 071 (Wave 8.5)</b>: previously this method only extracted the artifact and
    /// returned null on decline, forcing the orchestrator into a scaffold "no-artifact-produced"
    /// fallback that lost all the structured gap analysis from the EvidenceSufficiencyNode
    /// upstream. Now it returns an <see cref="InsightsEngineRunResult"/> carrying whichever
    /// path the playbook took, so callers see real DeclineResponse with populated
    /// MinimumEvidenceNeeded.
    /// </para>
    /// <para>
    /// Iterates the full stream (rather than short-circuiting on first match) because:
    /// (a) the engine relies on the consumer draining the stream to allow downstream nodes
    /// to flush; (b) we want the LAST artifact-emitting event in case multiple
    /// ReturnInsightArtifactNode instances exist in a playbook graph (unusual but not forbidden);
    /// (c) the same applies for DeclineToFindNode events.
    /// </para>
    /// <para>
    /// <b>Discrimination strategy</b>: matches the artifact node by exact name
    /// (<see cref="ReturnInsightArtifactNodeName"/> — the documented convention since task 023)
    /// and the decline node by exact name (<see cref="DeclineToFindNodeName"/> — predict-matter-cost
    /// task 060 convention) PLUS structural fingerprint (StructuredData deserialises into
    /// DeclineResponse with all 5 required fields). The structural fallback lets future playbooks
    /// rename the decline node without breaking propagation.
    /// </para>
    /// <para>
    /// <b>Both-paths defensive case</b>: if the stream contains both event types (shouldn't
    /// happen with EvidenceSufficiencyNode's branch routing, but possible if a playbook is
    /// authored without proper branching), <see cref="InsightsEngineRunResult"/> is constructed
    /// with both populated. The orchestrator's contract (see InsightsOrchestrator) prefers
    /// Artifact in that case ("sufficient path wins" defensive policy).
    /// </para>
    /// </remarks>
    private async Task<InsightsEngineRunResult> DrainEngineStreamAsync(
        Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>> engineInvocation,
        CancellationToken cancellationToken)
    {
        InsightArtifact? artifact = null;
        DeclineResponse? decline = null;

        await foreach (var ev in engineInvocation(cancellationToken).WithCancellation(cancellationToken))
        {
            if (ev.Type != PlaybookEventType.NodeCompleted)
                continue;

            if (ev.NodeOutput is not { Success: true, StructuredData: { } structuredData })
                continue;

            // Path 1: artifact-emitting node (sufficient-evidence path)
            if (string.Equals(ev.NodeName, ReturnInsightArtifactNodeName, StringComparison.Ordinal))
            {
                try
                {
                    var candidate = structuredData.Deserialize<InsightArtifact>();
                    if (candidate is not null)
                    {
                        artifact = candidate; // capture last; keep iterating to drain
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex,
                        "ReturnInsightArtifactNode StructuredData failed to deserialise as InsightArtifact; skipping event");
                }
                continue;
            }

            // Path 2: decline-emitting node (insufficient-evidence path).
            // Match by conventional name OR by structural fingerprint so future playbooks can
            // rename the node without breaking decline propagation (per task 071 design).
            if (TryExtractDecline(ev.NodeName, structuredData, out var candidateDecline))
            {
                decline = candidateDecline; // capture last; keep iterating to drain
            }
        }

        // Construct the result. The "both populated" defensive case is preserved exactly
        // as-is so the orchestrator can apply its "sufficient path wins" policy explicitly.
        return new InsightsEngineRunResult(artifact, decline);
    }

    /// <summary>
    /// Try to extract a DeclineResponse from a NodeCompleted event's StructuredData.
    /// Matches the conventional <see cref="DeclineToFindNodeName"/> by exact name, and also
    /// falls back to structural deserialisation (a JSON object with all 5 required
    /// <see cref="DeclineResponse"/> fields populated). The structural match makes the cache
    /// robust against future playbooks renaming the decline node.
    /// </summary>
    private bool TryExtractDecline(string? nodeName, System.Text.Json.JsonElement structuredData, out DeclineResponse? decline)
    {
        decline = null;

        // Fast path: exact name match (the documented predict-matter-cost convention).
        bool nameMatch = string.Equals(nodeName, DeclineToFindNodeName, StringComparison.Ordinal);

        // Structural prefilter: must be a JSON object with the 5 required DeclineResponse fields.
        // Cheap to check before attempting full deserialisation; avoids spurious deserialisation
        // attempts on every NodeCompleted event in long-running playbooks.
        if (!nameMatch && !LooksLikeDeclineResponse(structuredData))
        {
            return false;
        }

        try
        {
            var candidate = structuredData.Deserialize<DeclineResponse>();
            if (candidate is not null)
            {
                decline = candidate;
                return true;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Decline node {NodeName} StructuredData failed to deserialise as DeclineResponse; skipping event",
                nodeName);
        }

        return false;
    }

    /// <summary>
    /// Structural fingerprint: true when the element has all 5 required DeclineResponse
    /// field names. Cheap; avoids invoking the JSON deserialiser unless the shape matches.
    /// </summary>
    private static bool LooksLikeDeclineResponse(System.Text.Json.JsonElement element)
    {
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object) return false;

        return element.TryGetProperty("reason", out _)
            && element.TryGetProperty("explanation", out _)
            && element.TryGetProperty("minimumEvidenceNeeded", out _)
            && element.TryGetProperty("suggestedActions", out _)
            && element.TryGetProperty("confidenceInDecline", out _);
    }
}
