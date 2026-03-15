using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Resolves which playbook(s) to show for a given entityType + pageType combination
/// using the <c>sprk_aichatcontextmapping</c> Dataverse entity.
///
/// Resolution precedence (highest → lowest):
///   1. Exact match: entityType + pageType
///   2. Entity + any: entityType + "any"
///   3. Wildcard + pageType: "*" + pageType
///   4. Global fallback: "*" + "any"
///
/// Caching (ADR-009): Redis-first with 30-minute sliding TTL.
/// Cache key pattern: <c>"chat:ctx-mapping:{entityType}:{pageType}"</c>
///
/// Lifetime: Scoped — depends on <see cref="IGenericEntityService"/> (singleton)
/// and <see cref="IDistributedCache"/> (singleton). Scoped limits per-request visibility.
/// </summary>
public class ChatContextMappingService
{
    /// <summary>Sliding TTL for context mapping cache entries (ADR-009).</summary>
    internal static readonly TimeSpan MappingCacheTtl = TimeSpan.FromMinutes(30);

    private const string MappingEntityName = "sprk_aichatcontextmapping";

    private readonly IDistributedCache _cache;
    private readonly IGenericEntityService _genericEntityService;
    private readonly ILogger<ChatContextMappingService> _logger;

    /// <summary>
    /// Builds the Redis cache key for a context mapping lookup.
    /// </summary>
    internal static string BuildCacheKey(string entityType, string? pageType)
        => $"chat:ctx-mapping:{entityType}:{pageType ?? "any"}";

    public ChatContextMappingService(
        IDistributedCache cache,
        IGenericEntityService genericEntityService,
        ILogger<ChatContextMappingService> logger)
    {
        _cache = cache;
        _genericEntityService = genericEntityService;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the playbook(s) available for the given entity type and page type.
    ///
    /// Checks Redis first (ADR-009). On cache miss, queries Dataverse using the
    /// four-tier resolution precedence, caches the result, and returns it.
    /// </summary>
    /// <param name="entityType">Dataverse entity logical name (e.g., "sprk_matter").</param>
    /// <param name="pageType">Page type identifier (e.g., "main", "quick") or null for "any".</param>
    /// <param name="tenantId">Tenant ID for logging context (cache key is not tenant-scoped because mappings are environment-global).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Resolved playbook mapping with default and available playbooks.</returns>
    public async Task<ChatContextMappingResponse> ResolveAsync(
        string entityType,
        string? pageType,
        string tenantId,
        CancellationToken ct = default)
    {
        var cacheKey = BuildCacheKey(entityType, pageType);

        // Hot path: Redis cache (ADR-009 — Redis first)
        var cachedBytes = await _cache.GetAsync(cacheKey, ct);
        if (cachedBytes is not null)
        {
            _logger.LogDebug(
                "Cache HIT for context mapping {EntityType}:{PageType} (tenant={TenantId})",
                entityType, pageType ?? "any", tenantId);

            var cached = JsonSerializer.Deserialize<ChatContextMappingResponse>(cachedBytes);
            if (cached is not null)
            {
                await _cache.RefreshAsync(cacheKey, ct);
                return cached;
            }
        }

        // Cold path: query Dataverse with four-tier resolution
        _logger.LogDebug(
            "Cache MISS for context mapping {EntityType}:{PageType} — querying Dataverse (tenant={TenantId})",
            entityType, pageType ?? "any", tenantId);

        var response = await ResolveFromDataverseAsync(entityType, pageType, ct);

        // Cache the result (including empty responses to avoid repeated misses)
        await CacheMappingAsync(cacheKey, response, ct);

        return response;
    }

    /// <summary>
    /// Queries Dataverse using the four-tier resolution precedence.
    /// Returns the first tier that yields results.
    /// </summary>
    private async Task<ChatContextMappingResponse> ResolveFromDataverseAsync(
        string entityType,
        string? pageType,
        CancellationToken ct)
    {
        var effectivePageType = pageType ?? "any";

        // Tier 1: Exact match — entityType + pageType
        var mappings = await QueryMappingsAsync(entityType, effectivePageType, ct);

        // Tier 2: Entity + any — entityType + "any"
        if (mappings.Count == 0 && effectivePageType != "any")
        {
            mappings = await QueryMappingsAsync(entityType, "any", ct);
        }

        // Tier 3: Wildcard + pageType — "*" + pageType
        if (mappings.Count == 0 && effectivePageType != "any")
        {
            mappings = await QueryMappingsAsync("*", effectivePageType, ct);
        }

        // Tier 4: Global fallback — "*" + "any"
        if (mappings.Count == 0)
        {
            mappings = await QueryMappingsAsync("*", "any", ct);
        }

        if (mappings.Count == 0)
        {
            _logger.LogInformation(
                "No context mapping found for {EntityType}:{PageType} (all tiers exhausted)",
                entityType, pageType ?? "any");

            return new ChatContextMappingResponse(null, []);
        }

        // Determine default: first record with sprk_isdefault=true, or first record
        var defaultPlaybook = mappings.FirstOrDefault(m => m.IsDefault) ?? mappings[0];

        var availablePlaybooks = mappings
            .Select(m => m.Playbook)
            .ToList()
            .AsReadOnly();

        _logger.LogInformation(
            "Resolved context mapping for {EntityType}:{PageType}: default={DefaultPlaybook}, available={Count}",
            entityType, pageType ?? "any", defaultPlaybook.Playbook.Name, mappings.Count);

        return new ChatContextMappingResponse(defaultPlaybook.Playbook, availablePlaybooks);
    }

    /// <summary>
    /// Queries <c>sprk_aichatcontextmapping</c> for records matching the given entity type
    /// and page type, ordered by <c>sprk_sortorder ASC</c>.
    ///
    /// Each mapping record has a lookup to <c>sprk_analysisplaybook</c> — the playbook ID,
    /// name, and description are read from the mapping record's denormalized columns
    /// (sprk_playbookid lookup + sprk_name, sprk_description on the playbook).
    /// </summary>
    private async Task<List<MappingRecord>> QueryMappingsAsync(
        string entityType,
        string pageType,
        CancellationToken ct)
    {
        // TODO: Replace with actual Dataverse query once sprk_aichatcontextmapping entity is deployed.
        // The query should filter by:
        //   sprk_entitytype = entityType
        //   sprk_pagetype = pageType
        //   statecode = 0 (Active)
        // And order by sprk_sortorder ASC.
        // Each record includes:
        //   sprk_aichatcontextmappingid (GUID)
        //   sprk_playbookid (EntityReference → sprk_analysisplaybook)
        //   sprk_isdefault (bool)
        //   sprk_sortorder (int)
        // The playbook name and description come from the related playbook entity via
        // a linked entity or separate lookup.

        try
        {
            var query = new QueryExpression(MappingEntityName)
            {
                ColumnSet = new ColumnSet(
                    "sprk_aichatcontextmappingid",
                    "sprk_playbookid",
                    "sprk_isdefault",
                    "sprk_sortorder"),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression("sprk_entitytype", ConditionOperator.Equal, entityType),
                        new ConditionExpression("sprk_pagetype", ConditionOperator.Equal, pageType),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0) // Active only
                    }
                },
                Orders = { new OrderExpression("sprk_sortorder", OrderType.Ascending) }
            };

            // Link to playbook entity to get name and description
            var playbookLink = query.AddLink(
                "sprk_analysisplaybook",
                "sprk_playbookid",
                "sprk_analysisplaybookid",
                JoinOperator.Inner);
            playbookLink.EntityAlias = "pb";
            playbookLink.Columns = new ColumnSet("sprk_name", "sprk_description");

            var results = await _genericEntityService.RetrieveMultipleAsync(query, ct);

            return results.Entities
                .Select(MapToMappingRecord)
                .ToList();
        }
        catch (Exception ex)
        {
            // Non-fatal: if the entity doesn't exist yet (pre-deployment), return empty.
            // This allows the service to start without the Dataverse entity being deployed.
            _logger.LogWarning(ex,
                "Failed to query context mappings for {EntityType}:{PageType} — " +
                "entity may not be deployed yet. Returning empty result.",
                entityType, pageType);

            return [];
        }
    }

    /// <summary>
    /// Maps a Dataverse entity record (with linked playbook alias) to an internal mapping record.
    /// </summary>
    private static MappingRecord MapToMappingRecord(Entity entity)
    {
        var playbookRef = entity.GetAttributeValue<EntityReference>("sprk_playbookid");
        var isDefault = entity.GetAttributeValue<bool?>("sprk_isdefault") ?? false;

        // Linked entity fields are returned as AliasedValue
        var playbookName = GetAliasedValue<string>(entity, "pb.sprk_name") ?? playbookRef?.Name ?? "Unknown";
        var playbookDescription = GetAliasedValue<string>(entity, "pb.sprk_description");

        var playbook = new ChatPlaybookInfo(
            Id: playbookRef?.Id ?? Guid.Empty,
            Name: playbookName,
            Description: playbookDescription);

        return new MappingRecord(playbook, isDefault);
    }

    /// <summary>
    /// Extracts a typed value from an <see cref="AliasedValue"/> attribute.
    /// </summary>
    private static T? GetAliasedValue<T>(Entity entity, string aliasedKey)
    {
        if (entity.Attributes.TryGetValue(aliasedKey, out var attr) && attr is AliasedValue aliased)
        {
            return (T)aliased.Value;
        }
        return default;
    }

    /// <summary>
    /// Serialises the mapping response to JSON and stores it in Redis with a 30-minute sliding TTL.
    /// </summary>
    private async Task CacheMappingAsync(string cacheKey, ChatContextMappingResponse response, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response);
        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = MappingCacheTtl
        };
        await _cache.SetAsync(cacheKey, bytes, options, ct);
    }

    /// <summary>
    /// Internal record for holding mapping + default flag during resolution.
    /// </summary>
    private record MappingRecord(ChatPlaybookInfo Playbook, bool IsDefault);
}
