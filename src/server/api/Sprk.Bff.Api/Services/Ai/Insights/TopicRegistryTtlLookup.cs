using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Insights;

namespace Sprk.Bff.Api.Services.Ai.Insights;

/// <summary>
/// In-process registry mirror of <c>sprk_aitopicregistry</c> rows for resolving
/// per-topic cache TTL (spec FR-21 / r1 Insights Widgets). Holds a small map of
/// <c>(playbookName → TimeSpan ttl)</c> populated lazily from Dataverse and refreshed
/// on a short interval (default 5 min).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is NOT a new interface seam</b> (per ADR-010 + audit DR-002): the
/// existing <see cref="IInsightsPlaybookExecutionCache"/> already exposes the public
/// cache contract. <see cref="TopicRegistryTtlLookup"/> is a concrete sealed POCO
/// injected directly into <see cref="InsightsPlaybookExecutionCache"/> as an optional
/// constructor dependency — there is no parallel interface, no Null peer, no DI seam.
/// Tests substitute it with a test-private subclass when needed.
/// </para>
/// <para>
/// <b>ADR-009 metadata-cache exception (documented per ADR-009 §Allowed L1 Exceptions /
/// Metadata)</b>: this is a process-local in-memory cache of registry metadata, NOT a
/// distributed cache. The exception applies because:
/// <list type="bullet">
///   <item>Cardinality is bounded (≤200 topic rows per tenant at r5+ scale per
///   <c>notes/topic-registry-schema-design.md</c> §6).</item>
///   <item>Refresh window is short (default 5 min, configurable) — comparable to the
///   per-instance settings cache used by <see cref="InsightsPlaybookNameMapOptions"/>
///   via <see cref="IOptionsMonitor{TOptions}"/>.</item>
///   <item>Data is metadata only (TTL config), not authorization decisions (ADR-009
///   "MUST NOT cache authorization decisions" is not implicated).</item>
///   <item>The alternative (a Dataverse round-trip per cache.Get) would dwarf the
///   ~50ms p95 the Insights ask endpoint budget targets — see FR-21 acceptance:
///   "verified via cache hit/miss behavior in UAT".</item>
/// </list>
/// </para>
/// <para>
/// <b>Lifetime</b>: Singleton — registered alongside <see cref="InsightsPlaybookExecutionCache"/>
/// in <c>AnalysisServicesModule.AddInsightsCache</c>, which runs only when the compound AI
/// gate is ON (per audit DR-008 Endpoint↔DI Registration Conditionality Symmetry Rule).
/// When the gate is OFF, this class is never instantiated and the cache falls back to
/// <see cref="InsightsPlaybookExecutionCache.DefaultTtl"/>.
/// </para>
/// <para>
/// <b>Concurrency</b>: refresh uses a single <see cref="SemaphoreSlim"/> to serialize
/// concurrent loaders. The TTL map itself is an immutable snapshot
/// (<see cref="IReadOnlyDictionary{TKey, TValue}"/>) swapped atomically via volatile
/// reference write — readers never block.
/// </para>
/// <para>
/// <b>Failure mode</b>: on Dataverse read failure, the lookup logs Warning and surfaces
/// "no entry" — the cache then falls back to <see cref="InsightsPlaybookExecutionCache.DefaultTtl"/>.
/// Never throws to the caller; the cache layer is an optimization, not a hard dependency
/// (ADR-009 graceful-degradation principle).
/// </para>
/// </remarks>
public sealed class TopicRegistryTtlLookup
{
    /// <summary>Default refresh window — short enough to pick up SME edits within 5 minutes,
    /// long enough to keep Dataverse load negligible.</summary>
    public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromMinutes(5);

    /// <summary>Logical name of the topic registry entity (per FR-04).</summary>
    private const string TopicRegistryEntityName = "sprk_aitopicregistry";

    private const string PlaybookNameAttribute = "sprk_playbookname";
    private const string CacheTtlMinutesAttribute = "sprk_cachettlminutes";
    private const string EnabledAttribute = "sprk_enabled";

    private readonly IDataverseService _dataverseService;
    private readonly IOptionsMonitor<InsightsPlaybookNameMapOptions> _playbookNameMap;
    private readonly ILogger<TopicRegistryTtlLookup> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _refreshInterval;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    // Snapshot of the registry — atomic swap on refresh. OrdinalIgnoreCase matches the
    // case-insensitive lookup pattern used by InsightsPlaybookNameMapOptions.
    private volatile IReadOnlyDictionary<string, TimeSpan> _ttlsByPlaybookName =
        new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastLoadedUtc = DateTimeOffset.MinValue;

    public TopicRegistryTtlLookup(
        IDataverseService dataverseService,
        IOptionsMonitor<InsightsPlaybookNameMapOptions> playbookNameMap,
        ILogger<TopicRegistryTtlLookup> logger,
        TimeProvider? timeProvider = null,
        TimeSpan? refreshInterval = null)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _playbookNameMap = playbookNameMap ?? throw new ArgumentNullException(nameof(playbookNameMap));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _refreshInterval = refreshInterval ?? DefaultRefreshInterval;
    }

    /// <summary>
    /// Try to resolve a per-topic TTL for the supplied playbook Guid by reverse-mapping
    /// to a canonical playbook name via <see cref="InsightsPlaybookNameMapOptions"/> and
    /// looking that name up in the registry mirror.
    /// </summary>
    /// <param name="playbookId">The Dataverse <c>sprk_analysisplaybook</c> row Guid the
    /// cache key was built from.</param>
    /// <param name="ttl">When the method returns <c>true</c>, contains the per-topic TTL
    /// from <c>sprk_aitopicregistry.sprk_cachettlminutes</c>; otherwise <see cref="TimeSpan.Zero"/>.</param>
    /// <param name="cancellationToken">Cancellation token for the (best-effort) refresh.</param>
    /// <returns><c>true</c> when a registry entry exists for the playbook; <c>false</c> when
    /// either the Guid is not registered in the name map, the playbook name is not present
    /// in the registry, or the registry could not be loaded.</returns>
    public async Task<(bool Found, TimeSpan Ttl)> TryGetTtlForPlaybookIdAsync(
        Guid playbookId,
        CancellationToken cancellationToken = default)
    {
        if (playbookId == Guid.Empty)
        {
            return (false, TimeSpan.Zero);
        }

        // Reverse-resolve the canonical name from the playbook Guid. The forward map is
        // (name → Guid); we scan it for the matching Guid. Map sizes are tiny (a handful
        // of playbooks per environment), so a linear scan is negligible and avoids
        // maintaining a parallel reverse map.
        var nameMap = _playbookNameMap.CurrentValue;
        string? playbookName = null;
        foreach (var kvp in nameMap.Map)
        {
            if (kvp.Value == playbookId)
            {
                playbookName = kvp.Key;
                break;
            }
        }

        if (playbookName is null)
        {
            // No canonical name registered — caller used a direct-Guid path that bypasses
            // the topic registry. Cache falls back to DefaultTtl.
            return (false, TimeSpan.Zero);
        }

        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.TryGetValue(playbookName, out var ttl))
        {
            return (true, ttl);
        }

        return (false, TimeSpan.Zero);
    }

    /// <summary>
    /// Try to resolve a per-topic TTL for the supplied canonical playbook name. Same as
    /// <see cref="TryGetTtlForPlaybookIdAsync"/> but skips the Guid → name reverse step
    /// for callers that already have the name in hand (test entry point + future hot
    /// paths if name-keyed lookup is added).
    /// </summary>
    public async Task<(bool Found, TimeSpan Ttl)> TryGetTtlForPlaybookNameAsync(
        string playbookName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playbookName))
        {
            return (false, TimeSpan.Zero);
        }

        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.TryGetValue(playbookName, out var ttl)
            ? (true, ttl)
            : (false, TimeSpan.Zero);
    }

    /// <summary>
    /// Return the current registry snapshot, refreshing from Dataverse if the cache age
    /// exceeds <see cref="_refreshInterval"/>. Concurrent calls during a refresh share
    /// the in-flight load via the refresh gate; readers outside the refresh see the
    /// previous snapshot (atomic volatile-reference swap).
    /// </summary>
    private async Task<IReadOnlyDictionary<string, TimeSpan>> GetSnapshotAsync(
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow();
        if (nowUtc - _lastLoadedUtc < _refreshInterval && _ttlsByPlaybookName.Count > 0)
        {
            return _ttlsByPlaybookName;
        }

        // Refresh window expired (or first call) — try to acquire the gate. If another
        // caller is already loading, await its result by re-reading the snapshot after
        // gate acquisition (double-check pattern).
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check after acquiring the gate — another thread may have just refreshed.
            nowUtc = _timeProvider.GetUtcNow();
            if (nowUtc - _lastLoadedUtc < _refreshInterval && _ttlsByPlaybookName.Count > 0)
            {
                return _ttlsByPlaybookName;
            }

            var loaded = await LoadFromDataverseAsync(cancellationToken).ConfigureAwait(false);
            if (loaded is not null)
            {
                _ttlsByPlaybookName = loaded;
                _lastLoadedUtc = nowUtc;
            }

            return _ttlsByPlaybookName;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Read enabled rows from <c>sprk_aitopicregistry</c> via the singleton
    /// <see cref="IDataverseService"/>. On failure, returns <c>null</c> — the caller
    /// keeps the previous snapshot (or empty on first load failure), and the cache
    /// falls back to <see cref="InsightsPlaybookExecutionCache.DefaultTtl"/>.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, TimeSpan>?> LoadFromDataverseAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            // Query only enabled rows. statecode = 0 is the Dataverse "Active" state;
            // sprk_enabled is the soft on/off SMEs toggle from the form per FR-09.
            var query = new QueryExpression(TopicRegistryEntityName)
            {
                ColumnSet = new ColumnSet(PlaybookNameAttribute, CacheTtlMinutesAttribute),
                NoLock = true,
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition(EnabledAttribute, ConditionOperator.Equal, true);

            var result = await _dataverseService.RetrieveMultipleAsync(query, cancellationToken)
                .ConfigureAwait(false);

            var map = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
            foreach (var entity in result.Entities)
            {
                var playbookName = entity.GetAttributeValue<string?>(PlaybookNameAttribute);
                var ttlMinutes = entity.GetAttributeValue<int?>(CacheTtlMinutesAttribute);

                if (string.IsNullOrWhiteSpace(playbookName))
                {
                    continue;
                }

                // Defensive bounds: schema enforces 1..1440 per §3.1 row 8, but a stale
                // row might violate that if the constraint was added after a row was
                // hand-edited. Clamp to schema bounds; log Warning when the row is
                // out-of-range so SMEs can fix the source data.
                var minutes = ttlMinutes ?? 60;
                if (minutes < 1 || minutes > 1440)
                {
                    _logger.LogWarning(
                        "TopicRegistryTtlLookup: row {PlaybookName} has out-of-bounds sprk_cachettlminutes={Minutes}; clamping to schema bounds [1..1440].",
                        playbookName, minutes);
                    minutes = Math.Clamp(minutes, 1, 1440);
                }

                map[playbookName] = TimeSpan.FromMinutes(minutes);
            }

            _logger.LogInformation(
                "TopicRegistryTtlLookup: refreshed {Count} enabled topic-registry rows from Dataverse.",
                map.Count);

            return map;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "TopicRegistryTtlLookup: failed to refresh from Dataverse; keeping previous snapshot ({Count} entries). Cache will fall back to DefaultTtl for unmapped playbooks.",
                _ttlsByPlaybookName.Count);
            return null;
        }
    }
}
