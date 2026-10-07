using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Infrastructure.Authentication;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Knowledge base endpoints following ADR-001 (Minimal API) and ADR-008 (endpoint filters):
/// the index health endpoint returning document counts and last-updated timestamps.
/// </summary>
/// <remarks>
/// <para>Registered under /api/ai/knowledge. Search is scoped to tenantId from claims per ADR-014.
/// Registered in Program.cs as: app.MapKnowledgeBaseEndpoints()</para>
/// <para><b>Four routes DELETED by unified-access-control-r2 task 163</b> (owner round 10 item 1: no caller
/// in the repo and in no published API description): GET /indexes/{indexName}/documents, DELETE
/// /indexes/{indexName}/documents/{documentId}, POST /indexes/reindex/{documentId} and POST /test-search.
/// Each was an evaluation/admin surface behind a bare sign-in (its AiAuthorizationFilter passed through for
/// want of a document id) that listed, deleted, re-indexed app-only, or returned the raw text of any
/// document in the tenant (sweep findings #3, #26, #27, #28). Evidence:
/// projects/unified-access-control-r2/notes/task-163-search-rag-knowledge-insights-authorization.md §2.
/// The surviving health route returns counts only and is unchanged.</para>
/// </remarks>
public static class KnowledgeBaseEndpoints
{
    // The TenantIdHeader constant ("X-Tenant-Id") was removed by task 059 along with its only read.
    // Tenant identity comes from the authenticated principal — see TenantResolution.

    public static IEndpointRouteBuilder MapKnowledgeBaseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/knowledge")
            .RequireAuthorization()
            .WithTags("AI Knowledge Base");

        // GET /api/ai/knowledge/indexes/health — index health: doc counts and last-updated
        group.MapGet("/indexes/health", GetIndexHealth)
            .AddEndpointFilter<AiAuthorizationFilter>()
            .RequireRateLimiting("ai-batch")
            .WithName("KnowledgeBaseHealth")
            .WithSummary("Get knowledge base index health")
            .WithDescription("Returns document counts for knowledge and discovery indexes and last-updated timestamps.")
            .Produces<KnowledgeIndexHealthResult>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(500);

        return app;
    }

    // -------------------------------------------------------------------------
    // Endpoint handlers
    // -------------------------------------------------------------------------

    /// <summary>
    /// GET /api/ai/knowledge/indexes/health
    /// Returns document counts in the knowledge and discovery indexes for the tenant, plus last-updated timestamp.
    /// </summary>
    private static async Task<IResult> GetIndexHealth(
        HttpContext httpContext,
        IRagService ragService,
        ILogger<RagIndexingPipeline> logger,
        CancellationToken cancellationToken)
    {
        var tenantId = ResolveTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "TenantId could not be resolved from claims or header.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        logger.LogDebug("Knowledge base health check requested for tenant {TenantId}", tenantId);

        try
        {
            // B8 refactor (task 011 Phase 1b Tier 3, D-09 §2 B8): delegate to IRagService.
            // Previously this handler injected SearchIndexClient + IOptions<AiSearchOptions> directly;
            // those concerns now live inside RagService.GetIndexHealthAsync. The Null-Object impl
            // (NullRagService) surfaces a FeatureDisabledException caught below.
            var health = await ragService.GetIndexHealthAsync(tenantId, cancellationToken);

            var result = new KnowledgeIndexHealthResult
            {
                KnowledgeDocCount = health.KnowledgeDocCount,
                DiscoveryDocCount = health.DiscoveryDocCount,
                LastUpdated = health.LastUpdated,
                KnowledgeIndexName = health.KnowledgeIndexName,
                DiscoveryIndexName = health.DiscoveryIndexName
            };

            return Results.Ok(result);
        }
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 3 (D-09 §2 B8): NullRagService surfaced.
            logger.LogDebug(
                "Knowledge base health called while AI feature disabled. ErrorCode={ErrorCode}, TenantId={TenantId}",
                ex.ErrorCode, tenantId);
            return ex.AsFeatureDisabled503();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching knowledge base health for tenant {TenantId}", tenantId);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to retrieve knowledge base health.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }


    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves the tenantId from the caller's <c>tid</c> claim (ADR-014). The previous summary said
    /// "tid or oid" — <c>oid</c> is the user's object id, never a tenant, and the code never read it
    /// that way; the comment was simply wrong. The <c>X-Tenant-Id</c> fallback it also described was
    /// removed by task 059.
    /// </summary>
    private static string? ResolveTenantId(HttpContext httpContext)
        => TenantResolution.ResolveTenantId(httpContext.User);

}

// -------------------------------------------------------------------------
// Request / Response models for KnowledgeBaseEndpoints
// -------------------------------------------------------------------------

/// <summary>
/// Health summary for the knowledge base indexes.
/// </summary>
public record KnowledgeIndexHealthResult
{
    /// <summary>Total chunk count in the knowledge index for the requesting tenant.</summary>
    public long KnowledgeDocCount { get; init; }

    /// <summary>Total chunk count in the discovery index for the requesting tenant.</summary>
    public long DiscoveryDocCount { get; init; }

    /// <summary>Timestamp when this health check was performed.</summary>
    public DateTimeOffset LastUpdated { get; init; }

    /// <summary>Name of the knowledge index being queried.</summary>
    public string KnowledgeIndexName { get; init; } = string.Empty;

    /// <summary>Name of the discovery index being queried.</summary>
    public string DiscoveryIndexName { get; init; } = string.Empty;
}

