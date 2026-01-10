using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.Ai.Visualization;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Document visualization endpoints for relationship discovery.
/// Follows ADR-001 (Minimal API), ADR-008 (endpoint filters), and ADR-013 (AI architecture).
/// </summary>
/// <remarks>
/// <para>
/// Provides endpoints for:
/// - Finding related documents using vector similarity search
/// - Visualizing document relationship graphs
/// </para>
///
/// <para>
/// <strong>Authorization:</strong>
/// Uses VisualizationAuthorizationFilter to verify read access to source document.
/// </para>
///
/// <para>
/// <strong>Rate Limiting:</strong>
/// Applies ai-batch policy for search operations.
/// </para>
/// </remarks>
public static class VisualizationEndpoints
{
    /// <summary>
    /// Maps visualization endpoints to the application.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapVisualizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/visualization")
            .RequireAuthorization()
            .WithTags("AI Visualization");

        // GET /api/ai/visualization/related/{documentId} - Find related documents
        group.MapGet("/related/{documentId:guid}", GetRelatedDocuments)
            .AddVisualizationAuthorizationFilter()
            .RequireRateLimiting("ai-batch")
            .WithName("VisualizationGetRelatedDocuments")
            .WithSummary("Find documents related to a source document")
            .WithDescription("Uses vector similarity search to find semantically related documents and returns a graph structure for visualization.")
            .Produces<DocumentGraphResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        return app;
    }

    /// <summary>
    /// Find documents related to a source document using vector similarity.
    /// </summary>
    /// <param name="documentId">The source document ID (sprk_document GUID).</param>
    /// <param name="query">Query parameters for visualization options.</param>
    /// <param name="visualizationService">The visualization service.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Graph response with nodes, edges, and metadata.</returns>
    private static async Task<IResult> GetRelatedDocuments(
        Guid documentId,
        [AsParameters] VisualizationQueryParameters query,
        IVisualizationService visualizationService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        if (documentId == Guid.Empty)
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "Document ID is required",
                Status = 400
            });
        }

        if (string.IsNullOrWhiteSpace(query.TenantId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "TenantId query parameter is required",
                Status = 400
            });
        }

        // Build visualization options from query parameters
        var options = new VisualizationOptions
        {
            TenantId = query.TenantId,
            Threshold = query.Threshold ?? 0.65f,
            Limit = Math.Clamp(query.Limit ?? 25, 1, 50),
            Depth = Math.Clamp(query.Depth ?? 1, 1, 3),
            IncludeKeywords = query.IncludeKeywords ?? true,
            DocumentTypes = query.DocumentTypes?.ToList(),
            IncludeParentEntity = query.IncludeParentEntity ?? true
        };

        try
        {
            logger.LogDebug(
                "[VISUALIZATION] Getting related documents: DocumentId={DocumentId}, TenantId={TenantId}, Threshold={Threshold}, Limit={Limit}, Depth={Depth}",
                documentId, options.TenantId, options.Threshold, options.Limit, options.Depth);

            var response = await visualizationService.GetRelatedDocumentsAsync(
                documentId,
                options,
                cancellationToken);

            logger.LogInformation(
                "[VISUALIZATION] Found related documents: DocumentId={DocumentId}, NodeCount={NodeCount}, EdgeCount={EdgeCount}, Latency={Latency}ms",
                documentId, response.Nodes.Count, response.Edges.Count, response.Metadata.SearchLatencyMs);

            return Results.Ok(response);
        }
        catch (KeyNotFoundException ex)
        {
            logger.LogWarning(ex,
                "[VISUALIZATION] Source document not found: DocumentId={DocumentId}",
                documentId);

            return Results.NotFound(new ProblemDetails
            {
                Title = "Document Not Found",
                Detail = $"Source document with ID {documentId} was not found or has no embedding",
                Status = 404
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[VISUALIZATION] Failed to get related documents: DocumentId={DocumentId}",
                documentId);

            return Results.Problem(
                title: "Visualization Failed",
                detail: "An error occurred while retrieving related documents",
                statusCode: 500);
        }
    }
}

/// <summary>
/// Query parameters for visualization endpoints.
/// Mapped from URL query string parameters.
/// </summary>
public class VisualizationQueryParameters
{
    /// <summary>
    /// Tenant identifier for multi-tenant routing.
    /// Required for all visualization operations.
    /// </summary>
    [FromQuery(Name = "tenantId")]
    public string? TenantId { get; init; }

    /// <summary>
    /// Minimum similarity score threshold (0.0-1.0).
    /// Default: 0.65
    /// </summary>
    [FromQuery(Name = "threshold")]
    public float? Threshold { get; init; }

    /// <summary>
    /// Maximum number of related documents per level.
    /// Default: 25, Max: 50
    /// </summary>
    [FromQuery(Name = "limit")]
    public int? Limit { get; init; }

    /// <summary>
    /// Relationship depth (1-3 levels).
    /// Default: 1
    /// </summary>
    [FromQuery(Name = "depth")]
    public int? Depth { get; init; }

    /// <summary>
    /// Whether to include shared keywords in edge data.
    /// Default: true
    /// </summary>
    [FromQuery(Name = "includeKeywords")]
    public bool? IncludeKeywords { get; init; }

    /// <summary>
    /// Optional filter by document types.
    /// </summary>
    [FromQuery(Name = "documentTypes")]
    public string[]? DocumentTypes { get; init; }

    /// <summary>
    /// Whether to include parent entity (Matter/Project) information.
    /// Default: true
    /// </summary>
    [FromQuery(Name = "includeParentEntity")]
    public bool? IncludeParentEntity { get; init; }
}
