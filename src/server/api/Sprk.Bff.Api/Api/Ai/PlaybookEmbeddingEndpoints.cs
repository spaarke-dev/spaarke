using Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Playbook embedding pipeline trigger endpoints following ADR-001 (Minimal API).
/// Provides fire-and-forget indexing of playbooks into the playbook-embeddings AI Search index.
/// </summary>
/// <remarks>
/// <para>
/// Trigger: <c>POST /api/ai/playbooks/{playbookId}/index</c>
/// Called by Dataverse plugin (post-create/post-update on sprk_analysisplaybook) or admin tooling.
/// Returns 202 Accepted immediately; actual embedding generation runs in background.
/// </para>
/// <para>
/// The endpoint enqueues the playbookId into the <see cref="PlaybookIndexingBackgroundService"/>
/// channel. No DI registration required — accesses the static <see cref="PlaybookIndexingBackgroundService.Instance"/>.
/// </para>
/// </remarks>
public static class PlaybookEmbeddingEndpoints
{
    /// <summary>
    /// Maps playbook embedding pipeline endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapPlaybookEmbeddingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/playbooks")
            .RequireAuthorization()
            .WithTags("AI Playbook Embeddings");

        // POST /api/ai/playbooks/{playbookId}/index — trigger embedding indexing
        group.MapPost("/{playbookId:guid}/index", IndexPlaybook)
            .RequireRateLimiting("ai-indexing")
            .WithName("IndexPlaybookEmbedding")
            .WithSummary("Trigger playbook embedding indexing")
            .WithDescription(
                "Enqueues a playbook for embedding generation and AI Search index upsert. " +
                "Returns 202 Accepted immediately — indexing runs asynchronously in the background. " +
                "Called by Dataverse plugin on playbook create/update.")
            .Produces(202)
            .ProducesProblem(429)
            .ProducesProblem(503);

        return app;
    }

    /// <summary>
    /// Enqueues a playbook for embedding indexing and returns 202 Accepted immediately.
    /// </summary>
    /// <param name="playbookId">Playbook GUID to index.</param>
    /// <param name="loggerFactory">Logger factory for structured logging.</param>
    /// <returns>202 Accepted if enqueued; 503 if background service not available.</returns>
    private static IResult IndexPlaybook(
        Guid playbookId,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("PlaybookEmbeddingEndpoints");

        var indexingService = PlaybookIndexingBackgroundService.Instance;
        if (indexingService is null)
        {
            logger.LogWarning(
                "Playbook indexing service not available — background service may not have started. PlaybookId={PlaybookId}",
                playbookId);
            return Results.Problem(
                statusCode: 503,
                title: "Service Unavailable",
                detail: "Playbook indexing background service is not available");
        }

        indexingService.EnqueueIndexing(playbookId.ToString());

        logger.LogInformation(
            "Playbook {PlaybookId} enqueued for embedding indexing",
            playbookId);

        return Results.Accepted();
    }
}
