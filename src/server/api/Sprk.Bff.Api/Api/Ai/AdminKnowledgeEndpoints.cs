using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Admin endpoints for indexing golden reference knowledge sources into the
/// <c>spaarke-rag-references</c> AI Search index.
/// </summary>
/// <remarks>
/// <para>
/// All endpoints require authentication and <see cref="AiAuthorizationFilter"/> (ADR-008).
/// Registered under <c>/api/admin/knowledge</c>.
/// </para>
/// <para>
/// Endpoints:
/// <list type="bullet">
///   <item>POST /api/admin/knowledge/index-references — index all knowledge sources from Dataverse</item>
///   <item>POST /api/admin/knowledge/index-reference/{knowledgeSourceId} — index a single source</item>
///   <item>DELETE /api/admin/knowledge/index-reference/{knowledgeSourceId} — delete chunks for a source</item>
/// </list>
/// </para>
/// </remarks>
public static class AdminKnowledgeEndpoints
{
    public static IEndpointRouteBuilder MapAdminKnowledgeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/knowledge")
            .RequireAuthorization()
            .WithTags("Admin Knowledge");

        // POST /api/admin/knowledge/index-references — bulk index all knowledge sources
        group.MapPost("/index-references", IndexAllReferences)
            .AddEndpointFilter<AiAuthorizationFilter>()
            .WithName("AdminIndexAllReferences")
            .WithSummary("Index all reference knowledge sources")
            .WithDescription("Queries Dataverse for all system knowledge sources and indexes each into the spaarke-rag-references AI Search index. Idempotent: deletes existing chunks before re-indexing.")
            .Produces<ReferenceIndexingBatchResult>(StatusCodes.Status200OK)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(500);

        // POST /api/admin/knowledge/index-reference/{knowledgeSourceId} — index a single source
        group.MapPost("/index-reference/{knowledgeSourceId}", IndexSingleReference)
            .AddEndpointFilter<AiAuthorizationFilter>()
            .WithName("AdminIndexSingleReference")
            .WithSummary("Index a single reference knowledge source")
            .WithDescription("Indexes a single knowledge source into the spaarke-rag-references index. Content can be provided in the request body or fetched from Dataverse.")
            .Produces<ReferenceIndexingResult>(StatusCodes.Status200OK)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        // DELETE /api/admin/knowledge/index-reference/{knowledgeSourceId} — delete chunks
        group.MapDelete("/index-reference/{knowledgeSourceId}", DeleteReference)
            .AddEndpointFilter<AiAuthorizationFilter>()
            .WithName("AdminDeleteReference")
            .WithSummary("Delete a reference knowledge source from the index")
            .WithDescription("Removes all chunks for the specified knowledge source from the spaarke-rag-references index.")
            .Produces<ReferenceDeleteResult>(StatusCodes.Status200OK)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(500);

        return app;
    }

    // -------------------------------------------------------------------------
    // Endpoint handlers
    // -------------------------------------------------------------------------

    /// <summary>
    /// POST /api/admin/knowledge/index-references
    /// Indexes all knowledge sources from Dataverse into the references index.
    /// </summary>
    private static async Task<IResult> IndexAllReferences(
        ReferenceIndexingService referenceIndexingService,
        ILogger<ReferenceIndexingService> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Admin triggered bulk reference knowledge indexing");

        try
        {
            var result = await referenceIndexingService.IndexAllReferencesAsync(cancellationToken);
            return Results.Ok(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Bulk reference indexing failed");
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to index reference knowledge sources.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// POST /api/admin/knowledge/index-reference/{knowledgeSourceId}
    /// Indexes a single knowledge source. If request body provides content, uses that;
    /// otherwise fetches from Dataverse via IScopeResolverService.
    /// </summary>
    private static async Task<IResult> IndexSingleReference(
        string knowledgeSourceId,
        IndexReferenceRequest? request,
        ReferenceIndexingService referenceIndexingService,
        IScopeResolverService scopeResolverService,
        ILogger<ReferenceIndexingService> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(knowledgeSourceId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "knowledgeSourceId is required.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        logger.LogInformation("Admin triggered single reference indexing for {KnowledgeSourceId}", knowledgeSourceId);

        try
        {
            string content;
            string name;
            string domain;
            string[] tags;

            if (!string.IsNullOrWhiteSpace(request?.Content))
            {
                // Use content from request body.
                content = request.Content;
                name = request.Name ?? knowledgeSourceId;
                domain = request.Domain ?? string.Empty;
                tags = request.Tags ?? [];
            }
            else
            {
                // Fetch from Dataverse.
                if (!Guid.TryParse(knowledgeSourceId, out var sourceGuid))
                {
                    return Results.Problem(
                        statusCode: 400,
                        title: "Bad Request",
                        detail: "knowledgeSourceId must be a valid GUID when fetching from Dataverse.",
                        type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
                }

                var knowledge = await scopeResolverService.GetKnowledgeAsync(sourceGuid, cancellationToken);
                if (knowledge == null)
                {
                    return Results.Problem(
                        statusCode: 404,
                        title: "Not Found",
                        detail: $"Knowledge source '{knowledgeSourceId}' not found in Dataverse.",
                        type: "https://tools.ietf.org/html/rfc7235#section-3.1");
                }

                if (string.IsNullOrWhiteSpace(knowledge.Content))
                {
                    return Results.Problem(
                        statusCode: 400,
                        title: "Bad Request",
                        detail: $"Knowledge source '{knowledgeSourceId}' has no content to index.",
                        type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
                }

                content = knowledge.Content;
                name = knowledge.Name;
                domain = knowledge.Type.ToString();
                tags = [];
            }

            var result = await referenceIndexingService.IndexKnowledgeSourceAsync(
                knowledgeSourceId, content, name, domain, tags, cancellationToken);

            return Results.Ok(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "Failed to index reference knowledge source {KnowledgeSourceId}",
                knowledgeSourceId);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to index reference knowledge source.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// DELETE /api/admin/knowledge/index-reference/{knowledgeSourceId}
    /// Deletes all chunks for the specified knowledge source from the references index.
    /// </summary>
    private static async Task<IResult> DeleteReference(
        string knowledgeSourceId,
        ReferenceIndexingService referenceIndexingService,
        ILogger<ReferenceIndexingService> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(knowledgeSourceId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "knowledgeSourceId is required.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        logger.LogInformation("Admin triggered delete for knowledge source {KnowledgeSourceId} from references index",
            knowledgeSourceId);

        try
        {
            var chunksDeleted = await referenceIndexingService.DeleteKnowledgeSourceAsync(
                knowledgeSourceId, cancellationToken);

            return Results.Ok(new ReferenceDeleteResult
            {
                KnowledgeSourceId = knowledgeSourceId,
                ChunksDeleted = chunksDeleted,
                Message = chunksDeleted > 0
                    ? $"Deleted {chunksDeleted} chunk(s) for knowledge source '{knowledgeSourceId}'."
                    : $"No chunks found for knowledge source '{knowledgeSourceId}'."
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "Failed to delete reference knowledge source {KnowledgeSourceId}",
                knowledgeSourceId);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to delete reference knowledge source from index.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }
}

// -------------------------------------------------------------------------
// Request / Response models for AdminKnowledgeEndpoints
// -------------------------------------------------------------------------

/// <summary>
/// Request body for POST /api/admin/knowledge/index-reference/{knowledgeSourceId}.
/// All fields are optional — when content is omitted, the source is fetched from Dataverse.
/// </summary>
public record IndexReferenceRequest
{
    /// <summary>Text content to index. If omitted, content is fetched from Dataverse.</summary>
    public string? Content { get; init; }

    /// <summary>Display name of the knowledge source. Defaults to knowledgeSourceId if omitted.</summary>
    public string? Name { get; init; }

    /// <summary>Domain classification (e.g., "legal", "finance"). Defaults to empty.</summary>
    public string? Domain { get; init; }

    /// <summary>Tags for categorization and filtering.</summary>
    public string[]? Tags { get; init; }
}

/// <summary>
/// Result of deleting a knowledge source from the references index.
/// </summary>
public record ReferenceDeleteResult
{
    /// <summary>The knowledge source ID that was targeted.</summary>
    public string KnowledgeSourceId { get; init; } = string.Empty;

    /// <summary>Number of chunks deleted from the index.</summary>
    public int ChunksDeleted { get; init; }

    /// <summary>Human-readable summary message.</summary>
    public string Message { get; init; } = string.Empty;
}
