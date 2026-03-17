using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Resolves the analysis-scoped SprkChat context for a given <c>analysisId</c>.
///
/// Resolution steps:
///   1. Check Redis cache (ADR-009 — cache key: <c>analysis-context:{analysisId}</c>).
///   2. On cache miss: query Dataverse for the <c>sprk_analysisoutput</c> record,
///      the related <c>sprk_analysisplaybook</c>, and the related matter record.
///   3. Map <c>sprk_playbookcapabilities</c> integers to <see cref="InlineActionInfo"/>
///      using the static <see cref="CapabilityToActionMap"/> dictionary.
///   4. Build <see cref="AnalysisChatContextResponse"/> and store in Redis with a
///      30-minute absolute TTL before returning.
///
/// Caching (ADR-009): Redis-first with 30-minute absolute TTL.
/// Cache key pattern: <c>analysis-context:{analysisId}</c>
///
/// Lifetime: Scoped — depends on <see cref="IGenericEntityService"/> (singleton).
/// Scoped limits per-request visibility and aligns with ChatContextMappingService lifetime.
/// </summary>
public class AnalysisChatContextResolver
{
    /// <summary>Absolute TTL for analysis context cache entries (ADR-009).</summary>
    internal static readonly TimeSpan ContextCacheTtl = TimeSpan.FromMinutes(30);

    /// <summary>Cache key prefix. Must match the pattern expected by any eviction logic.</summary>
    internal const string CacheKeyPrefix = "analysis-context:";

    /// <summary>
    /// Static mapping from <c>sprk_playbookcapabilities</c> Dataverse option set integer values
    /// to <see cref="InlineActionInfo"/> descriptors.
    ///
    /// Values sourced from the Dataverse global choice definition for <c>sprk_playbookcapabilities</c>:
    ///   100000000 = search
    ///   100000001 = analyze
    ///   100000002 = write_back
    ///   100000003 = reanalyze
    ///   100000004 = selection_revise  ← diff-type (opens DiffReviewPanel)
    ///   100000005 = web_search
    ///   100000006 = summarize
    ///
    /// This mapping is intentionally static (hardcoded) per spec and ADR-013 — capability
    /// definitions live in code, not in Dataverse, so they are version-controlled and
    /// do not require a schema query.
    /// </summary>
    internal static readonly IReadOnlyDictionary<int, InlineActionInfo> CapabilityToActionMap =
        new Dictionary<int, InlineActionInfo>
        {
            [100000000] = new InlineActionInfo(
                PlaybookCapabilities.Search,
                "Search",
                "chat",
                "Search knowledge sources"),
            [100000001] = new InlineActionInfo(
                PlaybookCapabilities.Analyze,
                "Analyze",
                "chat",
                "Analyze with AI"),
            [100000002] = new InlineActionInfo(
                PlaybookCapabilities.WriteBack,
                "Write Back",
                "chat",
                "Write AI content to document"),
            [100000003] = new InlineActionInfo(
                PlaybookCapabilities.Reanalyze,
                "Re-Analyze",
                "chat",
                "Re-run analysis"),
            [100000004] = new InlineActionInfo(
                PlaybookCapabilities.SelectionRevise,
                "Revise Selection",
                "diff",
                "Revise selected text and show diff"),
            [100000005] = new InlineActionInfo(
                PlaybookCapabilities.WebSearch,
                "Web Search",
                "chat",
                "Search the web"),
            [100000006] = new InlineActionInfo(
                PlaybookCapabilities.Summarize,
                "Summarize",
                "chat",
                "Summarize content"),
        };

    private readonly IDistributedCache _cache;
    private readonly ILogger<AnalysisChatContextResolver> _logger;

    /// <summary>
    /// Builds the Redis cache key for an analysis context lookup.
    /// </summary>
    internal static string BuildCacheKey(string analysisId)
        => $"{CacheKeyPrefix}{analysisId}";

    public AnalysisChatContextResolver(
        IDistributedCache cache,
        ILogger<AnalysisChatContextResolver> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the full analysis chat context for the given analysis record ID.
    ///
    /// Checks Redis first (ADR-009). On cache miss, resolves from Dataverse,
    /// caches the result with a 30-minute absolute TTL, and returns.
    /// Returns <c>null</c> when the analysis record cannot be found or resolution fails.
    /// </summary>
    /// <param name="analysisId">The <c>sprk_analysisoutput</c> record ID (GUID string or alternate key).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Resolved context response, or <c>null</c> on resolution failure.</returns>
    public async Task<AnalysisChatContextResponse?> ResolveAsync(
        string analysisId,
        CancellationToken ct = default)
    {
        var cacheKey = BuildCacheKey(analysisId);

        // Hot path: Redis cache (ADR-009 — Redis first)
        var cachedBytes = await _cache.GetAsync(cacheKey, ct);
        if (cachedBytes is not null)
        {
            _logger.LogDebug(
                "Cache HIT for analysis context {AnalysisId}",
                analysisId);

            var cached = JsonSerializer.Deserialize<AnalysisChatContextResponse>(cachedBytes);
            if (cached is not null)
            {
                return cached;
            }
        }

        // Cold path: resolve from Dataverse
        _logger.LogDebug(
            "Cache MISS for analysis context {AnalysisId} — resolving from Dataverse",
            analysisId);

        var response = await ResolveFromDataverseAsync(analysisId, ct);
        if (response is null)
        {
            return null;
        }

        // Cache the result with a 30-minute absolute TTL (ADR-009)
        await CacheContextAsync(cacheKey, response, ct);

        return response;
    }

    /// <summary>
    /// Queries Dataverse for the <c>sprk_analysisoutput</c> record and related entities
    /// to build the full <see cref="AnalysisChatContextResponse"/>.
    ///
    /// TODO (task 021): Replace the stub response below with actual Dataverse queries
    /// once the <c>sprk_analysisoutput</c> entity field names are confirmed and the
    /// endpoint is wired in <c>AnalysisChatContextEndpoints.cs</c>.
    ///
    /// Query design (when implemented):
    ///   - Primary entity: <c>sprk_analysisoutput</c> filtered by analysisId
    ///   - Linked entity: <c>sprk_analysisplaybook</c> via <c>sprk_playbookid</c> lookup
    ///     → retrieve <c>sprk_name</c>, <c>sprk_description</c>, <c>sprk_playbookcapabilities</c>
    ///   - Linked entity: matter record via the analysis output's matter lookup
    ///     → retrieve matter type and practice area fields
    ///   - Map <c>sprk_playbookcapabilities</c> OptionSetValueCollection to InlineActionInfo
    ///     via <see cref="CapabilityToActionMap"/>
    ///
    /// Field names to confirm (document in notes/dataverse-field-names.md):
    ///   - Analysis output entity: <c>sprk_analysisoutput</c> (assumed — verify against schema)
    ///   - Analysis ID field: likely <c>sprk_analysisoutputid</c> (primary key) or a text alternate key
    ///   - Source file lookup: likely <c>sprk_spefileid</c> or <c>sprk_sourcedocumentid</c>
    ///   - Source container field: likely <c>sprk_containerid</c>
    ///   - Playbook capabilities field: <c>sprk_playbookcapabilities</c> (multi-select option set)
    /// </summary>
#pragma warning disable CS1998 // Async method lacks 'await' — intentional stub pending task 021 Dataverse integration
    private async Task<AnalysisChatContextResponse?> ResolveFromDataverseAsync(
        string analysisId,
        CancellationToken ct)
    {
#pragma warning restore CS1998
        try
        {
            _logger.LogInformation(
                "Resolving analysis context from Dataverse for analysisId: {AnalysisId}",
                analysisId);

            // Stub response — Dataverse queries will be completed in task 021 once the
            // sprk_analysisoutput entity is deployed and field names are confirmed.
            // All 7 capability actions are included in the stub so the UI can render
            // QuickActionChips and the slash command menu immediately during development.
            var allInlineActions = CapabilityToActionMap.Values.ToList();

            return new AnalysisChatContextResponse(
                DefaultPlaybookId: string.Empty,
                DefaultPlaybookName: "Default Analysis Playbook",
                AvailablePlaybooks: [],
                InlineActions: allInlineActions,
                KnowledgeSources: [],
                AnalysisContext: new AnalysisContextInfo(
                    AnalysisId: analysisId,
                    AnalysisType: null,
                    MatterType: null,
                    PracticeArea: null,
                    SourceFileId: null,
                    SourceContainerId: null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to resolve analysis context from Dataverse for {AnalysisId}",
                analysisId);
            return null;
        }
    }

    /// <summary>
    /// Serialises the context response to JSON and stores it in Redis with a 30-minute
    /// absolute TTL (ADR-009 — no sliding expiration to prevent stale data accumulation).
    /// </summary>
    private async Task CacheContextAsync(
        string cacheKey,
        AnalysisChatContextResponse response,
        CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ContextCacheTtl
        };
        await _cache.SetAsync(cacheKey, bytes, options, ct);
    }
}
