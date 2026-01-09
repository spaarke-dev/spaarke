using System.Text.Json;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Playbook execution endpoints following ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// Provides validation, execution with SSE streaming, and run status operations.
/// </summary>
public static class PlaybookRunEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapPlaybookRunEndpoints(this IEndpointRouteBuilder app)
    {
        var playbookGroup = app.MapGroup("/api/ai/playbooks/{id:guid}")
            .RequireAuthorization()
            .WithTags("AI Playbook Execution");

        var runsGroup = app.MapGroup("/api/ai/playbooks/runs")
            .RequireAuthorization()
            .WithTags("AI Playbook Execution");

        // POST /api/ai/playbooks/{id}/validate - Validate playbook graph
        playbookGroup.MapPost("/validate", ValidatePlaybook)
            .AddPlaybookAccessAuthorizationFilter()
            .WithName("ValidatePlaybook")
            .WithSummary("Validate a playbook's node graph")
            .WithDescription("Checks for cycles, missing dependencies, and invalid configurations before execution.")
            .Produces<PlaybookValidationResult>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        // POST /api/ai/playbooks/{id}/execute - Execute playbook with SSE streaming
        playbookGroup.MapPost("/execute", ExecutePlaybook)
            .AddPlaybookAccessAuthorizationFilter()
            .RequireRateLimiting("ai-stream")
            .WithName("ExecuteNodePlaybook")
            .WithSummary("Execute a playbook with SSE streaming")
            .WithDescription("Executes the playbook's node graph, streaming progress events via Server-Sent Events.")
            .Produces(200, contentType: "text/event-stream")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // GET /api/ai/playbooks/runs/{runId} - Get run status
        runsGroup.MapGet("/{runId:guid}", GetRunStatus)
            .WithName("GetPlaybookRunStatus")
            .WithSummary("Get the status of a playbook run")
            .WithDescription("Returns the current state, progress, and outputs of a playbook execution.")
            .Produces<PlaybookRunStatus>()
            .ProducesProblem(401)
            .ProducesProblem(404);

        // GET /api/ai/playbooks/runs/{runId}/stream - Reconnect to run stream
        runsGroup.MapGet("/{runId:guid}/stream", StreamRunStatus)
            .RequireRateLimiting("ai-stream")
            .WithName("StreamPlaybookRun")
            .WithSummary("Stream events for an active playbook run")
            .WithDescription("Reconnects to an active run's SSE stream. Returns immediately if run is complete.")
            .Produces(200, contentType: "text/event-stream")
            .ProducesProblem(401)
            .ProducesProblem(404);

        // POST /api/ai/playbooks/runs/{runId}/cancel - Cancel a running playbook
        runsGroup.MapPost("/{runId:guid}/cancel", CancelRun)
            .WithName("CancelPlaybookRun")
            .WithSummary("Cancel a running playbook execution")
            .WithDescription("Requests cancellation of an active playbook execution.")
            .Produces<CancelRunResult>()
            .ProducesProblem(401)
            .ProducesProblem(404);

        return app;
    }

    /// <summary>
    /// Validate a playbook's node graph before execution.
    /// POST /api/ai/playbooks/{id}/validate
    /// </summary>
    private static async Task<IResult> ValidatePlaybook(
        Guid id,
        IPlaybookOrchestrationService orchestrationService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("PlaybookRunEndpoints");

        try
        {
            var result = await orchestrationService.ValidateAsync(id, cancellationToken);
            logger.LogDebug("Validated playbook {PlaybookId}: IsValid={IsValid}",
                id, result.IsValid);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to validate playbook {PlaybookId}", id);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to validate playbook");
        }
    }

    /// <summary>
    /// Execute a playbook with SSE streaming.
    /// POST /api/ai/playbooks/{id}/execute
    /// </summary>
    private static async Task ExecutePlaybook(
        Guid id,
        ExecutePlaybookRequest request,
        IPlaybookOrchestrationService orchestrationService,
        HttpContext context,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("PlaybookRunEndpoints");
        var cancellationToken = context.RequestAborted;
        var response = context.Response;

        // Validate request
        if (request.DocumentIds == null || request.DocumentIds.Length == 0)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "DocumentIds is required and must not be empty" }, cancellationToken);
            return;
        }

        // Set SSE headers
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        logger.LogInformation(
            "Starting playbook execution - PlaybookId: {PlaybookId}, Documents: [{DocumentIds}], TraceId: {TraceId}",
            id, string.Join(",", request.DocumentIds), context.TraceIdentifier);

        try
        {
            var runRequest = new PlaybookRunRequest
            {
                PlaybookId = id,
                DocumentIds = request.DocumentIds,
                UserContext = request.UserContext,
                Parameters = request.Parameters
            };

            await foreach (var evt in orchestrationService.ExecuteAsync(runRequest, context, cancellationToken))
            {
                await WriteSSEAsync(response, evt, cancellationToken);
            }

            logger.LogInformation("Playbook execution completed for PlaybookId: {PlaybookId}, TraceId: {TraceId}",
                id, context.TraceIdentifier);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Client disconnected during playbook execution, PlaybookId: {PlaybookId}, TraceId: {TraceId}",
                id, context.TraceIdentifier);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during playbook execution, PlaybookId: {PlaybookId}, TraceId: {TraceId}",
                id, context.TraceIdentifier);

            if (!cancellationToken.IsCancellationRequested)
            {
                var errorEvent = PlaybookStreamEvent.RunFailed(Guid.Empty, id, ex.Message);
                await WriteSSEAsync(response, errorEvent, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Get the status of a playbook run.
    /// GET /api/ai/playbooks/runs/{runId}
    /// </summary>
    private static async Task<IResult> GetRunStatus(
        Guid runId,
        IPlaybookOrchestrationService orchestrationService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("PlaybookRunEndpoints");

        try
        {
            var status = await orchestrationService.GetRunStatusAsync(runId, cancellationToken);
            if (status == null)
            {
                return Results.NotFound(new { error = $"Run {runId} not found" });
            }

            logger.LogDebug("Retrieved status for run {RunId}: {State}", runId, status.State);
            return Results.Ok(status);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get status for run {RunId}", runId);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to get run status");
        }
    }

    /// <summary>
    /// Stream events for an active playbook run (reconnection endpoint).
    /// GET /api/ai/playbooks/runs/{runId}/stream
    /// </summary>
    private static async Task StreamRunStatus(
        Guid runId,
        IPlaybookOrchestrationService orchestrationService,
        HttpContext context,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("PlaybookRunEndpoints");
        var cancellationToken = context.RequestAborted;
        var response = context.Response;

        try
        {
            // Check if run exists
            var status = await orchestrationService.GetRunStatusAsync(runId, cancellationToken);
            if (status == null)
            {
                response.StatusCode = StatusCodes.Status404NotFound;
                await response.WriteAsJsonAsync(new { error = $"Run {runId} not found" }, cancellationToken);
                return;
            }

            // If run is complete, return immediately with final status
            if (status.State is PlaybookRunState.Completed or PlaybookRunState.Failed or PlaybookRunState.Cancelled)
            {
                logger.LogDebug("Run {RunId} already complete ({State}), returning status", runId, status.State);
                response.StatusCode = StatusCodes.Status200OK;
                await response.WriteAsJsonAsync(status, cancellationToken);
                return;
            }

            // Set SSE headers for active run
            response.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            response.Headers.Connection = "keep-alive";

            logger.LogInformation("Client reconnecting to run {RunId}", runId);

            // For Phase 1, we don't have reconnection support
            // Just return the current status as a "snapshot" event
            var snapshotEvent = new PlaybookStreamEvent
            {
                Type = PlaybookEventType.RunStarted,
                RunId = runId,
                PlaybookId = status.PlaybookId,
                Metrics = status.Metrics
            };
            await WriteSSEAsync(response, snapshotEvent, cancellationToken);

            // Note: Full reconnection support would require storing events
            // and replaying from a checkpoint. This is Phase 2 scope.
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Client disconnected from run stream {RunId}", runId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error streaming run {RunId}", runId);
            if (!cancellationToken.IsCancellationRequested)
            {
                response.StatusCode = StatusCodes.Status500InternalServerError;
                await response.WriteAsJsonAsync(new { error = "Stream error" }, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Cancel a running playbook execution.
    /// POST /api/ai/playbooks/runs/{runId}/cancel
    /// </summary>
    private static async Task<IResult> CancelRun(
        Guid runId,
        IPlaybookOrchestrationService orchestrationService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("PlaybookRunEndpoints");

        try
        {
            var cancelled = await orchestrationService.CancelAsync(runId, cancellationToken);

            if (!cancelled)
            {
                // Check if run exists
                var status = await orchestrationService.GetRunStatusAsync(runId, cancellationToken);
                if (status == null)
                {
                    return Results.NotFound(new { error = $"Run {runId} not found" });
                }

                // Run exists but wasn't cancelled (already complete)
                return Results.Ok(new CancelRunResult
                {
                    Cancelled = false,
                    Message = $"Run is not active (current state: {status.State})"
                });
            }

            logger.LogInformation("Cancelled run {RunId}", runId);
            return Results.Ok(new CancelRunResult
            {
                Cancelled = true,
                Message = "Cancellation requested"
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to cancel run {RunId}", runId);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to cancel run");
        }
    }

    /// <summary>
    /// Write a playbook stream event in SSE format.
    /// </summary>
    private static async Task WriteSSEAsync(
        HttpResponse response,
        PlaybookStreamEvent evt,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var sseData = $"data: {json}\n\n";

        await response.WriteAsync(sseData, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}

/// <summary>
/// Request model for executing a playbook.
/// </summary>
public record ExecutePlaybookRequest
{
    /// <summary>
    /// Document IDs to process.
    /// </summary>
    public Guid[]? DocumentIds { get; init; }

    /// <summary>
    /// Optional user-provided context or instructions.
    /// </summary>
    public string? UserContext { get; init; }

    /// <summary>
    /// Optional parameters for template substitution.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }
}

/// <summary>
/// Result of a cancel operation.
/// </summary>
public record CancelRunResult
{
    /// <summary>
    /// Whether cancellation was successfully requested.
    /// </summary>
    public bool Cancelled { get; init; }

    /// <summary>
    /// Message describing the result.
    /// </summary>
    public string? Message { get; init; }
}
