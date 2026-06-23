using Sprk.Bff.Api.Services.Ai;
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
                "Returns 400 ProblemDetails with `missingFields` extension when the playbook is missing " +
                "FR-12 required fields (description / documentTypes / destinationHint). " +
                "Called by Dataverse plugin on playbook create/update.")
            .Produces(202)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(429)
            .ProducesProblem(503);

        return app;
    }

    /// <summary>
    /// Loads the playbook, validates FR-12 required fields, and enqueues for embedding
    /// indexing. Returns 202 Accepted on success; 400 with <c>missingFields</c> extension
    /// when validation fails; 404 when playbook does not exist; 503 when the indexing
    /// background service is not available.
    /// </summary>
    /// <param name="playbookId">Playbook GUID to index.</param>
    /// <param name="playbookService">Dataverse playbook fetch service (injected).</param>
    /// <param name="validator">FR-12 validation gate (injected).</param>
    /// <param name="loggerFactory">Logger factory for structured logging.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>
    /// 202 Accepted if enqueued; 400 if validation failed; 404 if playbook not found;
    /// 503 if background service not available.
    /// </returns>
    private static async Task<IResult> IndexPlaybook(
        Guid playbookId,
        IPlaybookService playbookService,
        PlaybookIndexInputValidator validator,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("PlaybookEmbeddingEndpoints");

        // Step 1: Load the playbook from Dataverse to validate required fields (FR-12).
        var playbook = await playbookService.GetPlaybookAsync(playbookId, cancellationToken);
        if (playbook is null)
        {
            logger.LogWarning(
                "Playbook {PlaybookId} not found in Dataverse — cannot index",
                playbookId);
            return Results.Problem(
                statusCode: 404,
                title: "Playbook not found",
                detail: $"No playbook exists with id {playbookId}.");
        }

        // Step 2: FR-12 validation gate — reject if required fields are missing.
        var missingFields = validator.Validate(playbook);
        if (missingFields.Count > 0)
        {
            // ADR-015: log only the field names + playbook ID; never log JPS JSON content.
            logger.LogWarning(
                "Playbook {PlaybookId} failed FR-12 validation — missing fields: {MissingFields}",
                playbookId, string.Join(",", missingFields));

            return Results.Problem(
                statusCode: 400,
                title: "Playbook validation failed",
                detail: "Playbook is missing one or more required fields for indexing per FR-12",
                extensions: new Dictionary<string, object?>
                {
                    ["missingFields"] = missingFields
                });
        }

        // Step 3: Enqueue for background indexing.
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
