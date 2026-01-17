using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Streaming;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// AI Playbook Builder endpoints following ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// Provides conversational AI assistance for building playbooks on the canvas.
/// Uses Server-Sent Events (SSE) for real-time streaming responses.
/// </summary>
public static class AiPlaybookBuilderEndpoints
{

    public static IEndpointRouteBuilder MapAiPlaybookBuilderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/playbook-builder")
            .RequireAuthorization()
            .WithTags("AI Playbook Builder");

        // POST /api/ai/playbook-builder/process - Process a builder message with SSE streaming
        group.MapPost("/process", ProcessBuilderMessage)
            .RequireRateLimiting("ai-stream")
            .WithName("ProcessBuilderMessage")
            .WithSummary("Process a playbook builder message with SSE streaming")
            .WithDescription("""
                Processes a user message in the context of playbook building.
                Classifies intent, generates canvas operations, and streams results.
                Uses proper SSE format with event: and data: lines.

                Event Types:
                - thinking: AI processing status/progress
                - dataverse_operation: Dataverse record changes (scope creation, etc.)
                - canvas_patch: Canvas node/edge changes to apply
                - message: AI response text to display
                - clarification: Request for user clarification
                - plan_preview: Build plan preview for user approval
                - done: Processing complete
                - error: Error occurred
                """)
            .Produces(200, contentType: "text/event-stream")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/ai/playbook-builder/generate-plan - Generate a build plan
        group.MapPost("/generate-plan", GenerateBuildPlan)
            .RequireRateLimiting("ai-batch")
            .WithName("GenerateBuildPlan")
            .WithSummary("Generate a build plan for a playbook goal")
            .WithDescription("Generates a structured build plan with steps to create a playbook for the specified goal.")
            .Produces<BuildPlan>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/ai/playbook-builder/classify-intent - Classify user intent
        group.MapPost("/classify-intent", ClassifyIntent)
            .RequireRateLimiting("ai-batch")
            .WithName("ClassifyBuilderIntent")
            .WithSummary("Classify the intent of a user message")
            .WithDescription("Classifies a user message into one of the builder intent categories and extracts entities.")
            .Produces<IntentClassification>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(429)
            .ProducesProblem(500);

        return app;
    }

    /// <summary>
    /// Process a builder message with SSE streaming.
    /// POST /api/ai/playbook-builder/process
    /// </summary>
    /// <remarks>
    /// Streams events in SSE format with proper event: and data: lines.
    /// Event types: thinking, dataverse_operation, canvas_patch, message, done, error, clarification, plan_preview
    /// </remarks>
    private static async Task ProcessBuilderMessage(
        BuilderRequest request,
        IAiPlaybookBuilderService builderService,
        HttpContext context,
        ILogger<AiPlaybookBuilderService> logger)
    {
        var cancellationToken = context.RequestAborted;
        var response = context.Response;

        // Validate request
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "Message is required" }, cancellationToken);
            return;
        }

        // Set SSE headers using utility
        ServerSentEventWriter.SetSseHeaders(response);

        logger.LogInformation(
            "Processing builder message, PlaybookId={PlaybookId}, SessionId={SessionId}, TraceId={TraceId}",
            request.PlaybookId, request.SessionId, context.TraceIdentifier);

        var operationCount = 0;

        try
        {
            // Stream initial thinking event
            await ServerSentEventWriter.WriteThinkingAsync(
                response, "Processing your request...", "Classifying intent", cancellationToken);

            await foreach (var chunk in builderService.ProcessMessageAsync(request, cancellationToken))
            {
                if (await WriteChunkAsSseEventAsync(response, chunk, cancellationToken))
                {
                    operationCount++;
                }
            }

            // Stream done event
            await ServerSentEventWriter.WriteDoneAsync(
                response, operationCount, "Processing complete", cancellationToken: cancellationToken);

            logger.LogInformation(
                "Builder processing completed, OperationCount={OperationCount}, TraceId={TraceId}",
                operationCount, context.TraceIdentifier);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Client disconnected during builder processing, TraceId={TraceId}",
                context.TraceIdentifier);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during builder processing, TraceId={TraceId}", context.TraceIdentifier);

            if (!cancellationToken.IsCancellationRequested)
            {
                await ServerSentEventWriter.WriteErrorAsync(
                    response,
                    "An error occurred while processing your request",
                    code: "PROCESSING_ERROR",
                    isRecoverable: true,
                    suggestedAction: "Please try again",
                    cancellationToken: CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Map a BuilderStreamChunk to the appropriate SSE event type and write it.
    /// </summary>
    /// <returns>True if a canvas operation was written, false otherwise.</returns>
    private static async Task<bool> WriteChunkAsSseEventAsync(
        HttpResponse response,
        BuilderStreamChunk chunk,
        CancellationToken cancellationToken)
    {
        var isCanvasOperation = false;

        switch (chunk.Type)
        {
            case BuilderChunkType.Message:
                await ServerSentEventWriter.WriteMessageAsync(
                    response, chunk.Text ?? string.Empty, isPartial: false, cancellationToken);
                break;

            case BuilderChunkType.CanvasOperation:
                if (chunk.Patch is not null)
                {
                    await ServerSentEventWriter.WriteCanvasPatchAsync(
                        response, chunk.Patch, description: null, cancellationToken);
                    isCanvasOperation = true;
                }
                break;

            case BuilderChunkType.Clarification:
                await ServerSentEventWriter.WriteClarificationAsync(
                    response, chunk.Text ?? "Please clarify your request", options: null, cancellationToken);
                break;

            case BuilderChunkType.PlanPreview:
                // Plan preview requires a BuildPlan object - extract from metadata if available
                if (chunk.Metadata?.TryGetValue("plan", out var planObj) == true &&
                    planObj is BuildPlan plan)
                {
                    await ServerSentEventWriter.WritePlanPreviewAsync(response, plan, cancellationToken);
                }
                break;

            case BuilderChunkType.Complete:
                // Done is handled after the loop; skip here
                break;

            case BuilderChunkType.Error:
                await ServerSentEventWriter.WriteErrorAsync(
                    response, chunk.Error ?? "Unknown error", cancellationToken: cancellationToken);
                break;

            default:
                // Unknown chunk type - log and skip
                break;
        }

        return isCanvasOperation;
    }

    /// <summary>
    /// Generate a build plan for a playbook goal.
    /// POST /api/ai/playbook-builder/generate-plan
    /// </summary>
    private static async Task<IResult> GenerateBuildPlan(
        BuildPlanRequest request,
        IAiPlaybookBuilderService builderService,
        ILogger<AiPlaybookBuilderService> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Goal))
        {
            return Results.BadRequest(new { error = "Goal is required" });
        }

        logger.LogInformation("Generating build plan for goal: {Goal}", request.Goal);

        try
        {
            var plan = await builderService.GenerateBuildPlanAsync(request, cancellationToken);
            return Results.Ok(plan);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating build plan");
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to generate build plan");
        }
    }

    /// <summary>
    /// Classify the intent of a user message.
    /// POST /api/ai/playbook-builder/classify-intent
    /// </summary>
    private static async Task<IResult> ClassifyIntent(
        ClassifyIntentRequest request,
        IAiPlaybookBuilderService builderService,
        ILogger<AiPlaybookBuilderService> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.BadRequest(new { error = "Message is required" });
        }

        logger.LogDebug("Classifying intent for message: {Message}", request.Message);

        try
        {
            var classification = await builderService.ClassifyIntentAsync(
                request.Message, request.CanvasContext, cancellationToken);
            return Results.Ok(classification);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error classifying intent");
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to classify intent");
        }
    }

}

/// <summary>
/// Request for classifying user intent.
/// </summary>
public record ClassifyIntentRequest
{
    /// <summary>The user message to classify.</summary>
    public required string Message { get; init; }

    /// <summary>Optional canvas context for disambiguation.</summary>
    public CanvasContext? CanvasContext { get; init; }
}
