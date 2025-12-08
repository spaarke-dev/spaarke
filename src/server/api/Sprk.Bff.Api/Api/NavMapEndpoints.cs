using Microsoft.Extensions.Caching.Memory;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// API endpoints for navigation property metadata discovery (Phase 7).
///
/// Provides case-sensitive navigation property names for @odata.bind operations,
/// eliminating manual PowerShell validation and enabling dynamic multi-entity support.
///
/// Architecture: 3-layer fallback (Server → Cache → Hardcoded)
/// - Layer 1 (Server): Query Dataverse EntityDefinitions metadata
/// - Layer 2 (L1 Cache): In-memory cache (15-minute TTL)
/// - Layer 3 (Hardcoded): Fallback values for known entities (reliability)
///
/// ADR Compliance:
/// - ADR-001: Minimal API (no controllers)
/// - ADR-008: Endpoint-level authorization
/// - ADR-009: Redis-first caching (with justified L1 exception for metadata hotspot)
/// - ADR-010: DI minimalism (use injected IDataverseService)
/// </summary>
public static class NavMapEndpoints
{
    // Cache TTL: 15 minutes (metadata changes are rare)
    private static readonly TimeSpan CacheTTL = TimeSpan.FromMinutes(15);

    // Cache key prefix
    private const string CacheKeyPrefix = "navmap:";

    /// <summary>
    /// Registers navigation metadata endpoints with the application.
    /// </summary>
    public static void MapNavMapEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/navmap")
            .WithTags("Navigation Metadata")
            .RequireRateLimiting("metadata-query")
            .RequireAuthorization(); // All endpoints require authentication

        // GET /api/navmap/{entityLogicalName}/entityset
        group.MapGet("{entityLogicalName}/entityset", GetEntitySetNameAsync)
            .WithName("GetEntitySetName")
            .WithSummary("Get plural entity set name for an entity")
            .WithDescription("Returns the EntitySetName (plural collection name) for a given entity logical name. Example: sprk_document → sprk_documents")
            .Produces<EntitySetNameResponse>(200)
            .Produces(400) // Bad request
            .Produces(401) // Unauthorized
            .Produces(404) // Entity not found
            .Produces(500); // Server error

        // GET /api/navmap/{childEntity}/{relationship}/lookup
        group.MapGet("{childEntity}/{relationship}/lookup", GetLookupNavigationAsync)
            .WithName("GetLookupNavigation")
            .WithSummary("Get lookup navigation property metadata (CRITICAL for @odata.bind)")
            .WithDescription("Returns case-sensitive navigation property metadata for a child → parent relationship. This is the MOST CRITICAL endpoint for solving the sprk_Matter vs sprk_matter case-sensitivity issue.")
            .Produces<LookupNavigationResponse>(200)
            .Produces(400) // Bad request
            .Produces(401) // Unauthorized
            .Produces(404) // Entity or relationship not found
            .Produces(500); // Server error

        // GET /api/navmap/{parentEntity}/{relationship}/collection
        group.MapGet("{parentEntity}/{relationship}/collection", GetCollectionNavigationAsync)
            .WithName("GetCollectionNavigation")
            .WithSummary("Get collection navigation property name")
            .WithDescription("Returns the collection navigation property name for a parent → child relationship. Used for relationship URL creation.")
            .Produces<CollectionNavigationResponse>(200)
            .Produces(400) // Bad request
            .Produces(401) // Unauthorized
            .Produces(404) // Entity or relationship not found
            .Produces(500); // Server error
    }

    /// <summary>
    /// Gets the EntitySetName (plural collection name) for an entity.
    ///
    /// Example: sprk_document → sprk_documents
    ///
    /// Uses 3-layer fallback:
    /// 1. Query Dataverse metadata
    /// 2. L1 cache (15-minute TTL)
    /// 3. Hardcoded fallback for known entities
    /// </summary>
    private static async Task<IResult> GetEntitySetNameAsync(
        string entityLogicalName,
        IDataverseService dataverseService,
        IMemoryCache cache,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            return TypedResults.BadRequest(new { error = "entityLogicalName cannot be empty" });
        }

        var cacheKey = $"{CacheKeyPrefix}entityset:{entityLogicalName}";

        try
        {
            // Layer 2: Check cache first
            if (cache.TryGetValue<string>(cacheKey, out var cachedValue))
            {
                logger.LogDebug("EntitySetName for {Entity} retrieved from cache", entityLogicalName);
                return TypedResults.Ok(new EntitySetNameResponse
                {
                    EntityLogicalName = entityLogicalName,
                    EntitySetName = cachedValue,
                    Source = "cache"
                });
            }

            // Layer 1: Query Dataverse metadata
            logger.LogDebug("Querying Dataverse metadata for EntitySetName: {Entity}", entityLogicalName);
            var entitySetName = await dataverseService.GetEntitySetNameAsync(entityLogicalName, ct);

            // Cache the result
            cache.Set(cacheKey, entitySetName, CacheTTL);

            logger.LogInformation("EntitySetName retrieved: {Entity} → {EntitySetName}", entityLogicalName, entitySetName);

            return TypedResults.Ok(new EntitySetNameResponse
            {
                EntityLogicalName = entityLogicalName,
                EntitySetName = entitySetName,
                Source = "dataverse"
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            logger.LogWarning("Entity '{Entity}' not found in Dataverse metadata", entityLogicalName);

            // Layer 3: Try hardcoded fallback
            var fallback = GetHardcodedEntitySetName(entityLogicalName);
            if (fallback != null)
            {
                logger.LogInformation("Using hardcoded fallback for {Entity} → {EntitySetName}", entityLogicalName, fallback);
                cache.Set(cacheKey, fallback, CacheTTL);

                return TypedResults.Ok(new EntitySetNameResponse
                {
                    EntityLogicalName = entityLogicalName,
                    EntitySetName = fallback,
                    Source = "hardcoded"
                });
            }

            return TypedResults.NotFound(new { error = $"Entity '{entityLogicalName}' not found in Dataverse metadata" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving EntitySetName for {Entity}", entityLogicalName);
            return TypedResults.Problem(
                title: "Error retrieving entity set name",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Gets lookup navigation property metadata for a child → parent relationship.
    ///
    /// This is the MOST CRITICAL endpoint - it returns the exact case-sensitive
    /// navigation property name required for @odata.bind operations.
    ///
    /// Example: sprk_document + sprk_matter_document → sprk_Matter (capital M)
    ///
    /// Uses 3-layer fallback:
    /// 1. Query Dataverse metadata
    /// 2. L1 cache (15-minute TTL)
    /// 3. Hardcoded fallback for known relationships
    /// </summary>
    private static async Task<IResult> GetLookupNavigationAsync(
        string childEntity,
        string relationship,
        IDataverseService dataverseService,
        IMemoryCache cache,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(childEntity))
        {
            return TypedResults.BadRequest(new { error = "childEntity cannot be empty" });
        }

        if (string.IsNullOrWhiteSpace(relationship))
        {
            return TypedResults.BadRequest(new { error = "relationship cannot be empty" });
        }

        var cacheKey = $"{CacheKeyPrefix}lookup:{childEntity}:{relationship}";

        try
        {
            // Layer 2: Check cache first
            if (cache.TryGetValue<LookupNavigationMetadata>(cacheKey, out var cachedMetadata))
            {
                logger.LogDebug("Lookup navigation for {Child}.{Relationship} retrieved from cache", childEntity, relationship);
                return TypedResults.Ok(new LookupNavigationResponse
                {
                    ChildEntity = childEntity,
                    Relationship = relationship,
                    LogicalName = cachedMetadata.LogicalName,
                    SchemaName = cachedMetadata.SchemaName,
                    NavigationPropertyName = cachedMetadata.NavigationPropertyName,
                    TargetEntity = cachedMetadata.TargetEntityLogicalName,
                    Source = "cache"
                });
            }

            // Layer 1: Query Dataverse metadata
            logger.LogDebug("Querying Dataverse metadata for lookup navigation: {Child}.{Relationship}", childEntity, relationship);
            var metadata = await dataverseService.GetLookupNavigationAsync(childEntity, relationship, ct);

            // Cache the result
            cache.Set(cacheKey, metadata, CacheTTL);

            logger.LogInformation(
                "Lookup navigation retrieved: {Child}.{Relationship} → {NavProperty} (target: {Target})",
                childEntity, relationship, metadata.NavigationPropertyName, metadata.TargetEntityLogicalName);

            return TypedResults.Ok(new LookupNavigationResponse
            {
                ChildEntity = childEntity,
                Relationship = relationship,
                LogicalName = metadata.LogicalName,
                SchemaName = metadata.SchemaName,
                NavigationPropertyName = metadata.NavigationPropertyName,
                TargetEntity = metadata.TargetEntityLogicalName,
                Source = "dataverse"
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            logger.LogWarning("Lookup navigation not found: {Child}.{Relationship}", childEntity, relationship);

            // Layer 3: Try hardcoded fallback
            var fallback = GetHardcodedLookupNavigation(childEntity, relationship);
            if (fallback != null)
            {
                logger.LogInformation("Using hardcoded fallback for {Child}.{Relationship} → {NavProperty}",
                    childEntity, relationship, fallback.NavigationPropertyName);
                cache.Set(cacheKey, fallback, CacheTTL);

                return TypedResults.Ok(new LookupNavigationResponse
                {
                    ChildEntity = childEntity,
                    Relationship = relationship,
                    LogicalName = fallback.LogicalName,
                    SchemaName = fallback.SchemaName,
                    NavigationPropertyName = fallback.NavigationPropertyName,
                    TargetEntity = fallback.TargetEntityLogicalName,
                    Source = "hardcoded"
                });
            }

            return TypedResults.NotFound(new { error = $"Lookup navigation not found for {childEntity}.{relationship}" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving lookup navigation for {Child}.{Relationship}", childEntity, relationship);
            return TypedResults.Problem(
                title: "Error retrieving lookup navigation metadata",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Gets collection navigation property name for a parent → child relationship.
    ///
    /// Example: sprk_matter + sprk_matter_document → sprk_matter_document
    ///
    /// Uses 3-layer fallback:
    /// 1. Query Dataverse metadata
    /// 2. L1 cache (15-minute TTL)
    /// 3. Hardcoded fallback for known relationships
    /// </summary>
    private static async Task<IResult> GetCollectionNavigationAsync(
        string parentEntity,
        string relationship,
        IDataverseService dataverseService,
        IMemoryCache cache,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(parentEntity))
        {
            return TypedResults.BadRequest(new { error = "parentEntity cannot be empty" });
        }

        if (string.IsNullOrWhiteSpace(relationship))
        {
            return TypedResults.BadRequest(new { error = "relationship cannot be empty" });
        }

        var cacheKey = $"{CacheKeyPrefix}collection:{parentEntity}:{relationship}";

        try
        {
            // Layer 2: Check cache first
            if (cache.TryGetValue<string>(cacheKey, out var cachedValue))
            {
                logger.LogDebug("Collection navigation for {Parent}.{Relationship} retrieved from cache", parentEntity, relationship);
                return TypedResults.Ok(new CollectionNavigationResponse
                {
                    ParentEntity = parentEntity,
                    Relationship = relationship,
                    CollectionPropertyName = cachedValue,
                    Source = "cache"
                });
            }

            // Layer 1: Query Dataverse metadata
            logger.LogDebug("Querying Dataverse metadata for collection navigation: {Parent}.{Relationship}", parentEntity, relationship);
            var collectionPropertyName = await dataverseService.GetCollectionNavigationAsync(parentEntity, relationship, ct);

            // Cache the result
            cache.Set(cacheKey, collectionPropertyName, CacheTTL);

            logger.LogInformation(
                "Collection navigation retrieved: {Parent}.{Relationship} → {CollectionProperty}",
                parentEntity, relationship, collectionPropertyName);

            return TypedResults.Ok(new CollectionNavigationResponse
            {
                ParentEntity = parentEntity,
                Relationship = relationship,
                CollectionPropertyName = collectionPropertyName,
                Source = "dataverse"
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            logger.LogWarning("Collection navigation not found: {Parent}.{Relationship}", parentEntity, relationship);

            // Layer 3: Try hardcoded fallback
            var fallback = GetHardcodedCollectionNavigation(parentEntity, relationship);
            if (fallback != null)
            {
                logger.LogInformation("Using hardcoded fallback for {Parent}.{Relationship} → {CollectionProperty}",
                    parentEntity, relationship, fallback);
                cache.Set(cacheKey, fallback, CacheTTL);

                return TypedResults.Ok(new CollectionNavigationResponse
                {
                    ParentEntity = parentEntity,
                    Relationship = relationship,
                    CollectionPropertyName = fallback,
                    Source = "hardcoded"
                });
            }

            return TypedResults.NotFound(new { error = $"Collection navigation not found for {parentEntity}.{relationship}" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving collection navigation for {Parent}.{Relationship}", parentEntity, relationship);
            return TypedResults.Problem(
                title: "Error retrieving collection navigation metadata",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    // ============================================================================================
    // Layer 3: Hardcoded Fallbacks (Reliability)
    // ============================================================================================

    /// <summary>
    /// Hardcoded fallback for entity set names (Layer 3).
    /// Used when Dataverse metadata query fails.
    /// </summary>
    private static string? GetHardcodedEntitySetName(string entityLogicalName)
    {
        return entityLogicalName switch
        {
            "sprk_document" => "sprk_documents",
            "sprk_matter" => "sprk_matters",
            "sprk_client" => "sprk_clients",
            _ => null
        };
    }

    /// <summary>
    /// Hardcoded fallback for lookup navigation metadata (Layer 3).
    /// Used when Dataverse metadata query fails.
    ///
    /// CRITICAL: These values are case-sensitive! They must match the exact
    /// navigation property names from Dataverse metadata.
    /// </summary>
    private static LookupNavigationMetadata? GetHardcodedLookupNavigation(string childEntity, string relationship)
    {
        // Known relationships based on Phase 6 findings
        var key = $"{childEntity}:{relationship}";

        return key switch
        {
            // sprk_document → sprk_matter (CONFIRMED: capital M)
            "sprk_document:sprk_matter_document" => new LookupNavigationMetadata
            {
                LogicalName = "sprk_matter",
                SchemaName = "sprk_Matter",
                NavigationPropertyName = "sprk_Matter", // Capital M - CRITICAL!
                TargetEntityLogicalName = "sprk_matter"
            },

            // sprk_document → sprk_client
            "sprk_document:sprk_client_document" => new LookupNavigationMetadata
            {
                LogicalName = "sprk_client",
                SchemaName = "sprk_Client",
                NavigationPropertyName = "sprk_Client", // Capital C (assumption)
                TargetEntityLogicalName = "sprk_client"
            },

            _ => null
        };
    }

    /// <summary>
    /// Hardcoded fallback for collection navigation property names (Layer 3).
    /// Used when Dataverse metadata query fails.
    /// </summary>
    private static string? GetHardcodedCollectionNavigation(string parentEntity, string relationship)
    {
        var key = $"{parentEntity}:{relationship}";

        return key switch
        {
            "sprk_matter:sprk_matter_document" => "sprk_matter_document",
            "sprk_client:sprk_client_document" => "sprk_client_document",
            _ => null
        };
    }
}
