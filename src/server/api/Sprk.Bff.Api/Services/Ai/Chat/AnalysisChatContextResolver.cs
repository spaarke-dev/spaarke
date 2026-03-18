using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Resolves the analysis-scoped SprkChat context for a given <c>analysisId</c>.
///
/// Resolution steps (R2-020 — replaces the stub from R1):
///   1. Check Redis cache (ADR-009 — cache key: <c>chat-context:{tenantId}:{analysisId}</c>).
///   2. On cache miss: query Dataverse for the <c>sprk_analysisoutput</c> record,
///      the related <c>sprk_analysisplaybook</c>, active scopes, and entity record.
///   3. Map <c>sprk_playbookcapabilities</c> integers to <see cref="InlineActionInfo"/>
///      using the static <see cref="CapabilityToActionMap"/> dictionary.
///   4. Resolve commands via <see cref="DynamicCommandResolver"/> and scope search guidance.
///   5. Build <see cref="AnalysisChatContextResponse"/> and store in Redis with a
///      30-minute absolute TTL before returning.
///
/// Caching (ADR-009, ADR-014): Redis-first with 30-minute absolute TTL.
/// Cache key pattern: <c>chat-context:{tenantId}:{analysisId}</c> (tenant-scoped per ADR-014).
///
/// Lifetime: Scoped — depends on <see cref="IGenericEntityService"/> (singleton).
/// Scoped limits per-request visibility and aligns with ChatContextMappingService lifetime.
/// </summary>
public class AnalysisChatContextResolver
{
    /// <summary>Absolute TTL for analysis context cache entries (ADR-009).</summary>
    internal static readonly TimeSpan ContextCacheTtl = TimeSpan.FromMinutes(30);

    /// <summary>Cache key prefix. Must match the pattern expected by any eviction logic.</summary>
    internal const string CacheKeyPrefix = "chat-context:";

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

    private readonly IGenericEntityService _entityService;
    private readonly IDistributedCache _cache;
    private readonly ILogger<AnalysisChatContextResolver> _logger;

    /// <summary>
    /// Builds the Redis cache key for an analysis context lookup.
    /// Key format: <c>chat-context:{tenantId}:{analysisId}</c> (ADR-014 — tenant-scoped).
    /// </summary>
    internal static string BuildCacheKey(string tenantId, string analysisId)
        => $"{CacheKeyPrefix}{tenantId}:{analysisId}";

    public AnalysisChatContextResolver(
        IGenericEntityService entityService,
        IDistributedCache cache,
        ILogger<AnalysisChatContextResolver> logger)
    {
        _entityService = entityService;
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
    /// <param name="tenantId">Tenant ID for cache key scoping (ADR-014).</param>
    /// <param name="hostContext">Optional host context for entity-scoped queries (ADR-013).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Resolved context response, or <c>null</c> on resolution failure.</returns>
    public async Task<AnalysisChatContextResponse?> ResolveAsync(
        string analysisId,
        string tenantId,
        ChatHostContext? hostContext = null,
        CancellationToken ct = default)
    {
        var cacheKey = BuildCacheKey(tenantId, analysisId);

        // Hot path: Redis cache (ADR-009 — Redis first)
        try
        {
            var cachedBytes = await _cache.GetAsync(cacheKey, ct);
            if (cachedBytes is not null)
            {
                _logger.LogDebug(
                    "Cache HIT for analysis context (tenant={TenantId}, analysis={AnalysisId})",
                    tenantId, analysisId);

                var cached = JsonSerializer.Deserialize<AnalysisChatContextResponse>(cachedBytes);
                if (cached is not null)
                {
                    return cached;
                }
            }
        }
        catch (Exception ex)
        {
            // Redis failure should not block context resolution — degrade gracefully
            _logger.LogWarning(ex,
                "Redis cache read failed for analysis context (tenant={TenantId}, analysis={AnalysisId}); falling through to Dataverse",
                tenantId, analysisId);
        }

        // Cold path: resolve from Dataverse
        _logger.LogDebug(
            "Cache MISS for analysis context (tenant={TenantId}, analysis={AnalysisId}) — resolving from Dataverse",
            tenantId, analysisId);

        var response = await ResolveFromDataverseAsync(analysisId, tenantId, hostContext, ct);
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
    /// Resolution pipeline (R2-020):
    ///   Step 1: Query <c>sprk_analysisoutput</c> → get playbook lookup + source file + container
    ///   Step 2: Query <c>sprk_analysisplaybook</c> → get capabilities, name, description
    ///   Step 3: Query entity/matter record for entity-specific context
    ///   Step 4: Query active scopes for capabilities, search guidance, and metadata
    ///   Step 5: Resolve dynamic command catalog via DynamicCommandResolver
    ///   Step 6: Assemble the full AnalysisChatContextResponse
    /// </summary>
    private async Task<AnalysisChatContextResponse?> ResolveFromDataverseAsync(
        string analysisId,
        string tenantId,
        ChatHostContext? hostContext,
        CancellationToken ct)
    {
        try
        {
            // ADR-015: Log only entity type, record ID, and cache metadata — NOT entity field values
            _logger.LogInformation(
                "Resolving analysis context from Dataverse (analysis={AnalysisId}, tenant={TenantId})",
                analysisId, tenantId);

            // =================================================================
            // Step 1: Query sprk_analysisoutput to get the playbook lookup
            // =================================================================
            if (!Guid.TryParse(analysisId, out var analysisGuid))
            {
                _logger.LogWarning(
                    "Invalid analysisId format (not a GUID): {AnalysisId}", analysisId);
                return null;
            }

            Entity analysisOutput;
            try
            {
                analysisOutput = await _entityService.RetrieveAsync(
                    "sprk_analysisoutput",
                    analysisGuid,
                    new[]
                    {
                        "sprk_analysisplaybookid",  // Lookup to sprk_analysisplaybook
                        "sprk_analysistype",        // Analysis type (option set or string)
                        "sprk_spefileid",           // Source file lookup
                        "sprk_containerid",         // SPE container ID
                    },
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to retrieve sprk_analysisoutput record {AnalysisId}", analysisId);
                return null;
            }

            // Extract playbook reference from the analysis output
            var playbookRef = analysisOutput.GetAttributeValue<EntityReference>("sprk_analysisplaybookid");
            var analysisType = analysisOutput.GetAttributeValue<string>("sprk_analysistype");
            var sourceFileRef = analysisOutput.GetAttributeValue<EntityReference>("sprk_spefileid");
            var sourceContainerId = analysisOutput.GetAttributeValue<string>("sprk_containerid");

            // =================================================================
            // Step 2: Query sprk_analysisplaybook for capabilities and metadata
            // =================================================================
            string playbookId = string.Empty;
            string playbookName = "Default Analysis Playbook";
            string? playbookDescription = null;
            var inlineActions = new List<InlineActionInfo>();

            if (playbookRef is not null)
            {
                playbookId = playbookRef.Id.ToString();

                try
                {
                    var playbook = await _entityService.RetrieveAsync(
                        "sprk_analysisplaybook",
                        playbookRef.Id,
                        new[]
                        {
                            "sprk_name",
                            "sprk_description",
                            "sprk_playbookcapabilities",
                            "sprk_recordtype",
                            "sprk_entitytype",
                            "sprk_tags",
                        },
                        ct);

                    playbookName = playbook.GetAttributeValue<string>("sprk_name") ?? playbookName;
                    playbookDescription = playbook.GetAttributeValue<string>("sprk_description");

                    // Map playbook capabilities (multi-select option set) to InlineActionInfo
                    inlineActions = MapCapabilitiesToActions(playbook);

                    _logger.LogInformation(
                        "Resolved playbook for analysis {AnalysisId}: playbookId={PlaybookId}, capabilities={CapabilityCount}",
                        analysisId, playbookId, inlineActions.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to retrieve playbook {PlaybookId} for analysis {AnalysisId}; using defaults",
                        playbookId, analysisId);
                }
            }
            else
            {
                _logger.LogInformation(
                    "Analysis {AnalysisId} has no playbook reference; using defaults with all capabilities",
                    analysisId);
                // No playbook reference — fall back to all capabilities (stub-compatible behaviour)
                inlineActions = CapabilityToActionMap.Values.ToList();
            }

            // =================================================================
            // Step 3: Query entity/matter record for entity-specific context
            // =================================================================
            string? matterType = null;
            string? practiceArea = null;

            if (hostContext is not null &&
                !string.IsNullOrWhiteSpace(hostContext.EntityType) &&
                !string.IsNullOrWhiteSpace(hostContext.EntityId) &&
                Guid.TryParse(hostContext.EntityId, out var entityGuid))
            {
                try
                {
                    // Query the entity record for context-specific fields
                    // For matters: sprk_mattertype, sprk_practicearea
                    // Entity type determines which columns to query
                    var entityColumns = GetEntityContextColumns(hostContext.EntityType);

                    if (entityColumns.Length > 0)
                    {
                        var entityRecord = await _entityService.RetrieveAsync(
                            hostContext.EntityType,
                            entityGuid,
                            entityColumns,
                            ct);

                        matterType = entityRecord.GetAttributeValue<string>("sprk_mattertype");
                        practiceArea = entityRecord.GetAttributeValue<string>("sprk_practicearea");

                        _logger.LogDebug(
                            "Resolved entity context for {EntityType}/{EntityId} on analysis {AnalysisId}",
                            hostContext.EntityType, hostContext.EntityId, analysisId);
                    }
                }
                catch (Exception ex)
                {
                    // Soft failure — entity context is enhancing, not required
                    _logger.LogWarning(ex,
                        "Failed to retrieve entity context ({EntityType}/{EntityId}) for analysis {AnalysisId}; continuing without entity metadata",
                        hostContext.EntityType, hostContext.EntityId, analysisId);
                }
            }

            // =================================================================
            // Step 4: Query active scopes for capabilities, search guidance, metadata
            // =================================================================
            string? searchGuidance = null;
            AnalysisScopeMetadata? scopeMetadata = null;

            try
            {
                var scopeResult = await QueryActiveScopesAsync(ct);
                searchGuidance = scopeResult.SearchGuidance;
                scopeMetadata = scopeResult.Metadata;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to query active scopes for analysis {AnalysisId}; continuing without scope data",
                    analysisId);
            }

            // =================================================================
            // Step 5: Resolve dynamic command catalog
            // =================================================================
            IReadOnlyList<CommandEntry>? commands = null;

            try
            {
                var commandResolver = new DynamicCommandResolver(
                    _entityService,
                    _cache,
                    _logger as ILogger<DynamicCommandResolver>
                    ?? LoggerFactory.Create(b => { }).CreateLogger<DynamicCommandResolver>());

                commands = await commandResolver.ResolveCommandsAsync(tenantId, hostContext, ct);

                _logger.LogDebug(
                    "Resolved {CommandCount} commands for analysis {AnalysisId}",
                    commands.Count, analysisId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to resolve commands for analysis {AnalysisId}; command catalog will be empty",
                    analysisId);
            }

            // =================================================================
            // Step 6: Assemble the full response
            // =================================================================
            var analysisContext = new AnalysisContextInfo(
                AnalysisId: analysisId,
                AnalysisType: analysisType,
                MatterType: matterType,
                PracticeArea: practiceArea,
                SourceFileId: sourceFileRef?.Id.ToString(),
                SourceContainerId: sourceContainerId);

            var response = new AnalysisChatContextResponse(
                DefaultPlaybookId: playbookId,
                DefaultPlaybookName: playbookName,
                AvailablePlaybooks: playbookRef is not null
                    ? [new AnalysisPlaybookInfo(playbookId, playbookName, playbookDescription)]
                    : [],
                InlineActions: inlineActions,
                KnowledgeSources: [],
                AnalysisContext: analysisContext,
                Commands: commands,
                SearchGuidance: searchGuidance,
                ScopeMetadata: scopeMetadata);

            _logger.LogInformation(
                "Analysis context resolved for {AnalysisId}: playbook={PlaybookName}, actions={ActionCount}, commands={CommandCount}, hasSearchGuidance={HasSearchGuidance}",
                analysisId, playbookName, inlineActions.Count,
                commands?.Count ?? 0, searchGuidance is not null);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to resolve analysis context from Dataverse for {AnalysisId}",
                analysisId);
            return null;
        }
    }

    // =========================================================================
    // Private: Capability Mapping
    // =========================================================================

    /// <summary>
    /// Maps the <c>sprk_playbookcapabilities</c> multi-select option set on a playbook
    /// entity to a list of <see cref="InlineActionInfo"/> descriptors.
    ///
    /// Handles two runtime shapes for the option set:
    ///   - <see cref="OptionSetValueCollection"/> (SDK typed query)
    ///   - <c>string</c> (comma-delimited integers from OData JSON)
    /// </summary>
    private static List<InlineActionInfo> MapCapabilitiesToActions(Entity playbookEntity)
    {
        var actions = new List<InlineActionInfo>();
        var capabilityValues = ExtractCapabilityValues(playbookEntity, "sprk_playbookcapabilities");

        foreach (var capValue in capabilityValues)
        {
            if (CapabilityToActionMap.TryGetValue(capValue, out var action))
            {
                actions.Add(action);
            }
        }

        return actions;
    }

    /// <summary>
    /// Extracts integer values from a multi-select option set attribute.
    ///
    /// Handles two runtime shapes:
    ///   - <see cref="OptionSetValueCollection"/> (SDK typed query)
    ///   - <c>string</c> (comma-delimited integers from OData JSON)
    /// </summary>
    private static IReadOnlyList<int> ExtractCapabilityValues(Entity entity, string attributeName)
    {
        if (!entity.Contains(attributeName))
        {
            return [];
        }

        var raw = entity[attributeName];

        // SDK typed: OptionSetValueCollection
        if (raw is OptionSetValueCollection collection)
        {
            return collection.Select(osv => osv.Value).ToList();
        }

        // OData fallback: comma-delimited string (e.g., "100000000,100000002,100000004")
        if (raw is string csvString && !string.IsNullOrWhiteSpace(csvString))
        {
            var values = new List<int>();
            foreach (var part in csvString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out var intVal))
                {
                    values.Add(intVal);
                }
            }
            return values;
        }

        return [];
    }

    // =========================================================================
    // Private: Entity Context Helpers
    // =========================================================================

    /// <summary>
    /// Returns the Dataverse columns to query for entity-specific context
    /// based on the entity type. Uses <see cref="ChatHostContext.EntityType"/>
    /// to determine which fields are relevant (ADR-013 — no hardcoded entity names).
    /// </summary>
    private static string[] GetEntityContextColumns(string entityType)
    {
        return entityType.ToLowerInvariant() switch
        {
            "sprk_matter" => new[] { "sprk_mattertype", "sprk_practicearea" },
            "sprk_project" => new[] { "sprk_projecttype" },
            "sprk_invoice" => new[] { "sprk_invoicetype" },
            _ => [] // Unknown entity type — no context columns to query
        };
    }

    // =========================================================================
    // Private: Scope Queries
    // =========================================================================

    /// <summary>
    /// Queries active <c>sprk_scope</c> records for search guidance and metadata.
    ///
    /// Returns the first non-empty <c>sprk_searchguidance</c> value found and
    /// the metadata from the first active scope. Only active scopes (statecode = 0)
    /// are included.
    /// </summary>
    private async Task<(string? SearchGuidance, AnalysisScopeMetadata? Metadata)> QueryActiveScopesAsync(
        CancellationToken ct)
    {
        var query = new QueryExpression("sprk_scope")
        {
            ColumnSet = new ColumnSet(
                "sprk_name",
                "sprk_description",
                "sprk_searchguidance",
                "sprk_focusarea",
                "sprk_capabilities"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("statecode", ConditionOperator.Equal, 0) // Active only
                }
            }
        };

        var results = await _entityService.RetrieveMultipleAsync(query, ct);

        string? searchGuidance = null;
        AnalysisScopeMetadata? metadata = null;

        foreach (var entity in results.Entities)
        {
            var scopeName = entity.GetAttributeValue<string>("sprk_name");
            var scopeDescription = entity.GetAttributeValue<string>("sprk_description");
            var scopeGuidance = entity.GetAttributeValue<string>("sprk_searchguidance");
            var scopeFocusArea = entity.GetAttributeValue<string>("sprk_focusarea");

            // Take the first non-empty search guidance
            if (searchGuidance is null && !string.IsNullOrWhiteSpace(scopeGuidance))
            {
                searchGuidance = scopeGuidance;
            }

            // Take the first scope as primary metadata
            if (metadata is null && !string.IsNullOrWhiteSpace(scopeName))
            {
                metadata = new AnalysisScopeMetadata(
                    ScopeId: entity.Id.ToString(),
                    ScopeName: scopeName,
                    Description: scopeDescription,
                    FocusArea: scopeFocusArea);
            }

            // If we have both, no need to continue iterating
            if (searchGuidance is not null && metadata is not null)
            {
                break;
            }
        }

        _logger.LogDebug(
            "Queried {ScopeCount} active scopes: hasSearchGuidance={HasGuidance}, hasMetadata={HasMetadata}",
            results.Entities.Count,
            searchGuidance is not null,
            metadata is not null);

        return (searchGuidance, metadata);
    }

    // =========================================================================
    // Private: Caching
    // =========================================================================

    /// <summary>
    /// Serialises the context response to JSON and stores it in Redis with a 30-minute
    /// absolute TTL (ADR-009 — no sliding expiration to prevent stale data accumulation).
    /// </summary>
    private async Task CacheContextAsync(
        string cacheKey,
        AnalysisChatContextResponse response,
        CancellationToken ct)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(response);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ContextCacheTtl
            };
            await _cache.SetAsync(cacheKey, bytes, options, ct);
        }
        catch (Exception ex)
        {
            // Redis failure should not block context resolution — degrade gracefully
            _logger.LogWarning(ex,
                "Redis cache write failed for analysis context (key={CacheKey}); result will not be cached",
                cacheKey);
        }
    }
}
