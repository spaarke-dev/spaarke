using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Resolves standalone SprkChat context for any supported Dataverse entity type.
///
/// Unlike <see cref="AnalysisChatContextResolver"/> (which resolves context for a specific
/// <c>sprk_analysisoutput</c> record), this provider resolves context for any entity
/// (contact, account, opportunity, incident, sprk_matter, etc.) identified by
/// <paramref name="entityType"/> and <paramref name="entityId"/>. It does not require
/// an analysis record to exist.
///
/// Resolution steps:
///   1. Validate <paramref name="entityType"/> against the <see cref="SupportedEntityTypes"/> allowlist.
///      Return null for unsupported types (endpoint maps this to 400 ProblemDetails).
///   2. Check Redis cache (ADR-009 — cache key: <c>chat-context:{tenantId}:standalone:{entityType}:{entityId}</c>).
///   3. On cache miss: build a <see cref="StandaloneChatContextResponse"/> from the static
///      field descriptor catalog for the entity type.
///   4. Cache the result with a 30-minute absolute TTL before returning.
///
/// Field mappings are intentionally static (hardcoded) per ADR-013 — field definitions
/// are version-controlled in code, not schema-queried at runtime. This mirrors the
/// <c>CapabilityToActionMap</c> pattern in <see cref="AnalysisChatContextResolver"/>.
///
/// Caching (ADR-009, ADR-014): Redis-first with 30-minute absolute TTL.
/// Cache key pattern: <c>chat-context:{tenantId}:standalone:{entityType}:{entityId}</c> (tenant-scoped).
///
/// Lifetime: Scoped — matches request lifetime; IDistributedCache is a singleton.
/// </summary>
public class StandaloneChatContextProvider
{
    /// <summary>Absolute TTL for standalone context cache entries (ADR-009 — matches analysis context TTL).</summary>
    internal static readonly TimeSpan ContextCacheTtl = TimeSpan.FromMinutes(30);

    /// <summary>Tenant-cache resource name for standalone context payloads (FR-05).</summary>
    internal const string CacheResource = "standalone-chat-context";

    /// <summary>Tenant-cache schema version for standalone context payloads.</summary>
    internal const int CacheVersion = 1;

    /// <summary>Legacy alias retained for tests that referenced the pre-FR-05 prefix constant.</summary>
    internal const string CacheKeyPrefix = "tenant:";

    /// <summary>
    /// Reproduces the on-wire cache key produced by <see cref="ITenantCache"/> for legacy
    /// test consumers. Format: <c>tenant:{tenantId}:standalone-chat-context:{entityType}:{entityId}:v1</c>.
    /// </summary>
    internal static string BuildCacheKey(string tenantId, string entityType, string entityId)
        => $"tenant:{tenantId}:{CacheResource}:{entityType}:{entityId}:v{CacheVersion}";

    /// <summary>
    /// Allowlist of supported Dataverse entity logical names.
    ///
    /// Any entity type NOT in this set returns null from <see cref="ResolveAsync"/>,
    /// which the endpoint maps to a 400 ProblemDetails response.
    ///
    /// Adding new entity types here requires:
    ///   1. An entry in <see cref="EntityFieldCatalog"/>.
    ///   2. An entry in <see cref="EntityDisplayNames"/>.
    /// </summary>
    public static readonly IReadOnlySet<string> SupportedEntityTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "contact",
            "account",
            "opportunity",
            "incident",
            "sprk_matter",
        };

    /// <summary>
    /// Static mapping from entity type to human-readable display name.
    /// Used to populate <see cref="StandaloneChatContextResponse.DisplayName"/>.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> EntityDisplayNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["contact"] = "Contact",
            ["account"] = "Account",
            ["opportunity"] = "Opportunity",
            ["incident"] = "Case",
            ["sprk_matter"] = "Matter",
        };

    /// <summary>
    /// Static catalog of context field descriptors per entity type.
    ///
    /// Each entry lists the Dataverse attributes that are surfaced as AI context
    /// for the entity type. Fields are ordered by relevance (most important first).
    ///
    /// Adding new entity types requires a corresponding entry here, in
    /// <see cref="SupportedEntityTypes"/>, and in <see cref="EntityDisplayNames"/>.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<StandaloneContextField>> EntityFieldCatalog =
        new Dictionary<string, IReadOnlyList<StandaloneContextField>>(StringComparer.OrdinalIgnoreCase)
        {
            ["contact"] =
            [
                new StandaloneContextField("fullname",        "Full Name",     "text",      IsRequired: true),
                new StandaloneContextField("emailaddress1",   "Email",         "text"),
                new StandaloneContextField("jobtitle",        "Job Title",     "text"),
                new StandaloneContextField("parentcustomerid","Account",       "lookup"),
                new StandaloneContextField("telephone1",      "Phone",         "text"),
            ],

            ["account"] =
            [
                new StandaloneContextField("name",            "Account Name",  "text",      IsRequired: true),
                new StandaloneContextField("accountnumber",   "Account Number","text"),
                new StandaloneContextField("telephone1",      "Phone",         "text"),
                new StandaloneContextField("websiteurl",      "Website",       "text"),
                new StandaloneContextField("industrycode",    "Industry",      "optionset"),
            ],

            ["opportunity"] =
            [
                new StandaloneContextField("name",            "Opportunity Name",   "text",     IsRequired: true),
                new StandaloneContextField("parentaccountid", "Account",            "lookup"),
                new StandaloneContextField("estimatedvalue",  "Estimated Value",    "number"),
                new StandaloneContextField("closedatetime",   "Estimated Close Date","datetime"),
                new StandaloneContextField("statuscode",      "Status",             "optionset"),
            ],

            ["incident"] =
            [
                new StandaloneContextField("title",           "Case Title",    "text",      IsRequired: true),
                new StandaloneContextField("customerid",      "Customer",      "lookup"),
                new StandaloneContextField("prioritycode",    "Priority",      "optionset"),
                new StandaloneContextField("statuscode",      "Status",        "optionset"),
                new StandaloneContextField("description",     "Description",   "text"),
            ],

            ["sprk_matter"] =
            [
                new StandaloneContextField("sprk_mattername",   "Matter Name",   "text",    IsRequired: true),
                new StandaloneContextField("sprk_mattertype",   "Matter Type",   "optionset"),
                new StandaloneContextField("sprk_practicearea", "Practice Area", "optionset"),
                new StandaloneContextField("sprk_clientid",     "Client",        "lookup"),
                new StandaloneContextField("statuscode",        "Status",        "optionset"),
            ],
        };

    private readonly ITenantCache _cache;
    private readonly ILogger<StandaloneChatContextProvider> _logger;

    /// <summary>
    /// Builds the tenant-cache <c>id</c> component for a standalone context lookup.
    /// Combined with the tenant and resource by <see cref="ITenantCache"/>.
    /// </summary>
    internal static string BuildCacheId(string entityType, string entityId)
        => $"{entityType}:{entityId}";

    public StandaloneChatContextProvider(
        ITenantCache cache,
        ILogger<StandaloneChatContextProvider> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the standalone chat context for the given entity type and record ID.
    ///
    /// Returns <c>null</c> when the <paramref name="entityType"/> is not in the supported
    /// allowlist (<see cref="SupportedEntityTypes"/>). The endpoint maps null to 400.
    ///
    /// Checks Redis first (ADR-009). On cache miss, builds the context from the static
    /// field catalog, caches the result with a 30-minute absolute TTL, and returns.
    /// </summary>
    /// <param name="entityType">Dataverse entity logical name (must be in <see cref="SupportedEntityTypes"/>).</param>
    /// <param name="entityId">Entity record ID (GUID string). Must be pre-validated as a valid Guid by the endpoint.</param>
    /// <param name="tenantId">Tenant ID for cache key scoping (ADR-014).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Resolved context response, or <c>null</c> when <paramref name="entityType"/> is not supported.
    /// </returns>
    public async Task<StandaloneChatContextResponse?> ResolveAsync(
        string entityType,
        string entityId,
        string tenantId,
        CancellationToken ct = default)
    {
        // Allowlist check: unsupported entity types → null → endpoint returns 400 (project constraint)
        if (!SupportedEntityTypes.Contains(entityType))
        {
            _logger.LogWarning(
                "Unsupported standalone entity type requested: {EntityType}. Supported types: {Supported}",
                entityType,
                string.Join(", ", SupportedEntityTypes));
            return null;
        }

        var cacheId = BuildCacheId(entityType, entityId);

        // Hot path: Redis cache (ADR-009 — Redis first). Tenant-scoped via ITenantCache (FR-05).
        try
        {
            var cached = await _cache.GetAsync<StandaloneChatContextResponse>(
                tenantId, CacheResource, cacheId, CacheVersion, ct: ct);
            if (cached is not null)
            {
                _logger.LogDebug(
                    "Cache HIT for standalone context (tenant={TenantId}, entityType={EntityType}, entityId={EntityId})",
                    tenantId, entityType, entityId);
                return cached;
            }
        }
        catch (Exception ex)
        {
            // Redis failure should not block context resolution — degrade gracefully (ADR-009)
            _logger.LogWarning(ex,
                "Redis cache read failed for standalone context (tenant={TenantId}, entityType={EntityType}, entityId={EntityId}); falling through to in-memory catalog",
                tenantId, entityType, entityId);
        }

        // Cold path: build from static field catalog (ADR-013 — field defs live in code)
        _logger.LogDebug(
            "Cache MISS for standalone context (tenant={TenantId}, entityType={EntityType}, entityId={EntityId}) — building from field catalog",
            tenantId, entityType, entityId);

        var response = BuildFromCatalog(entityType, entityId);

        // Cache the result with a 30-minute absolute TTL (ADR-009)
        await CacheContextAsync(tenantId, cacheId, response, ct);

        return response;
    }

    // =========================================================================
    // Private: Catalog Lookup
    // =========================================================================

    /// <summary>
    /// Builds a <see cref="StandaloneChatContextResponse"/> from the static field catalog.
    ///
    /// Falls back to an empty field list when the entity type has no catalog entry
    /// (this should not occur in practice because SupportedEntityTypes and EntityFieldCatalog
    /// are maintained in sync).
    /// </summary>
    private static StandaloneChatContextResponse BuildFromCatalog(string entityType, string entityId)
    {
        var displayName = EntityDisplayNames.TryGetValue(entityType, out var dn)
            ? dn
            : entityType;

        var fields = EntityFieldCatalog.TryGetValue(entityType, out var catalogFields)
            ? catalogFields
            : Array.Empty<StandaloneContextField>();

        return new StandaloneChatContextResponse(
            EntityType: entityType,
            EntityId: entityId,
            DisplayName: displayName,
            ContextFields: fields);
    }

    // =========================================================================
    // Private: Caching
    // =========================================================================

    /// <summary>
    /// Serialises the context response to JSON and stores it in Redis with a 30-minute
    /// absolute TTL (ADR-009 — no sliding expiration to prevent stale data accumulation).
    /// Tenant-scoped via <see cref="ITenantCache"/> per FR-05.
    /// </summary>
    private async Task CacheContextAsync(
        string tenantId,
        string cacheId,
        StandaloneChatContextResponse response,
        CancellationToken ct)
    {
        try
        {
            await _cache.SetAsync(
                tenantId,
                CacheResource,
                cacheId,
                CacheVersion,
                response,
                ContextCacheTtl,
                ct: ct);
        }
        catch (Exception ex)
        {
            // Redis failure should not block context resolution — degrade gracefully
            _logger.LogWarning(ex,
                "Redis cache write failed for standalone context (tenant={TenantId}, id={CacheId}); result will not be cached",
                tenantId, cacheId);
        }
    }
}
