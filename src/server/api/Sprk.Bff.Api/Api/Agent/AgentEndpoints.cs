using System.Security.Claims;

namespace Sprk.Bff.Api.Api.Agent;

/// <summary>
/// Agent gateway endpoints for M365 Copilot integration.
///
/// These endpoints are THIN ADAPTERS that bridge the M365 Copilot agent to existing
/// BFF services (chat, search, playbook execution). No new AI orchestration logic lives here —
/// the agent gateway translates between the Copilot message format and existing BFF service calls.
///
/// All endpoints follow ADR-001 (Minimal API), ADR-008 (endpoint filters for authorization),
/// ADR-016 (rate limiting), and ADR-019 (ProblemDetails for errors).
/// </summary>
public static class AgentEndpoints
{
    // TODO: Inject ChatSessionManager for message routing
    // TODO: Inject PlaybookExecutionEngine for playbook operations
    // TODO: Inject PlaybookCatalogService for playbook listing

    /// <summary>
    /// Registers all agent gateway endpoints on the provided route builder.
    /// Called from Program.cs: <c>app.MapAgentEndpoints();</c>
    /// </summary>
    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/agent")
            .RequireAuthorization()
            .WithTags("Agent Gateway");

        // POST /api/agent/message — receive agent message, route to existing services
        group.MapPost("/message", HandleMessageAsync)
            .AddAgentAuthorizationFilter()
            .RequireRateLimiting("ai-stream")
            .WithName("AgentMessage")
            .WithSummary("Process a message from the M365 Copilot agent")
            .WithDescription("Receives a user message via the Copilot agent and routes it to the appropriate existing BFF service (chat, search, or playbook). Returns a response with optional Adaptive Card.")
            .Accepts<AgentMessageRequest>("application/json")
            .Produces<AgentMessageResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // GET /api/agent/playbooks — list available playbooks
        group.MapGet("/playbooks", ListPlaybooksAsync)
            .AddAgentAuthorizationFilter()
            .RequireRateLimiting("dataverse-query")
            .WithName("AgentListPlaybooks")
            .WithSummary("List playbooks available to the Copilot agent")
            .WithDescription("Returns the catalog of playbooks the current user can execute, delegating to the existing playbook catalog service.")
            .Produces<List<PlaybookSummary>>(200)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(500);

        // POST /api/agent/run-playbook — execute a playbook
        group.MapPost("/run-playbook", RunPlaybookAsync)
            .AddAgentAuthorizationFilter()
            .RequireRateLimiting("ai-batch")
            .WithName("AgentRunPlaybook")
            .WithSummary("Execute a playbook via the Copilot agent")
            .WithDescription("Enqueues a playbook execution against a document, delegating to the existing playbook execution engine. Returns a job ID for status polling.")
            .Accepts<AgentPlaybookRequest>("application/json")
            .Produces<PlaybookRunResponse>(202)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // GET /api/agent/playbooks/status/{jobId} — poll playbook execution status
        group.MapGet("/playbooks/status/{jobId:guid}", GetPlaybookStatusAsync)
            .AddAgentAuthorizationFilter()
            .RequireRateLimiting("dataverse-query")
            .WithName("AgentPlaybookStatus")
            .WithSummary("Get playbook execution status")
            .WithDescription("Returns the current status and progress of an asynchronous playbook execution. When complete, includes the result Adaptive Card.")
            .Produces<PlaybookStatusResponse>(200)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        return app;
    }

    // ────────────────────────────────────────────────────────────────
    // Endpoint Handlers
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/agent/message
    /// Routes the agent message to the appropriate existing BFF service.
    /// </summary>
    private static async Task<IResult> HandleMessageAsync(
        AgentMessageRequest request,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Agent.AgentEndpoints");

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Message text is required",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        var userId = ExtractUserId(httpContext);
        logger.LogInformation(
            "[AGENT] Message received: UserId={UserId}, HasDocument={HasDocument}, ConversationRef={ConversationRef}",
            userId, request.DocumentId.HasValue, request.ConversationReference ?? "(new)");

        try
        {
            // TODO: Inject ChatSessionManager and route message to existing chat service.
            // The routing logic will:
            //   1. Resolve or create a chat session from the conversation reference
            //   2. Forward the message to ChatSessionManager.SendMessageAsync()
            //   3. Collect the streamed response into a single AgentMessageResponse
            //   4. Optionally format an Adaptive Card for rich results

            // Placeholder response until service wiring is implemented.
            var response = new AgentMessageResponse
            {
                ResponseText = "Agent gateway received your message. Service wiring pending.",
                SuggestedActions = new List<string> { "List playbooks", "Search documents" }
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AGENT] Failed to process message for user {UserId}", userId);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to process agent message",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// GET /api/agent/playbooks
    /// Returns the list of playbooks available to the current user.
    /// </summary>
    private static async Task<IResult> ListPlaybooksAsync(
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Agent.AgentEndpoints");
        var userId = ExtractUserId(httpContext);
        logger.LogInformation("[AGENT] Listing playbooks for user {UserId}", userId);

        try
        {
            // TODO: Inject PlaybookCatalogService and delegate to existing listing.
            // Will call PlaybookCatalogService.GetAvailablePlaybooksAsync(userId, cancellationToken)
            // and map the results to PlaybookSummary records for the agent.

            // Placeholder response until service wiring is implemented.
            var playbooks = new List<PlaybookSummary>();

            return Results.Ok(playbooks);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AGENT] Failed to list playbooks for user {UserId}", userId);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to retrieve playbook catalog",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// POST /api/agent/run-playbook
    /// Enqueues a playbook execution and returns the job ID.
    /// </summary>
    private static async Task<IResult> RunPlaybookAsync(
        AgentPlaybookRequest request,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Agent.AgentEndpoints");

        if (request.PlaybookId == Guid.Empty)
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "PlaybookId is required",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        if (request.DocumentId == Guid.Empty)
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "DocumentId is required",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        var userId = ExtractUserId(httpContext);
        logger.LogInformation(
            "[AGENT] Running playbook: UserId={UserId}, PlaybookId={PlaybookId}, DocumentId={DocumentId}",
            userId, request.PlaybookId, request.DocumentId);

        try
        {
            // TODO: Inject PlaybookExecutionEngine and delegate to existing execution.
            // Will call PlaybookExecutionEngine.EnqueueAsync(request.PlaybookId, request.DocumentId, request.Parameters, userId, cancellationToken)
            // and return the job ID for status polling.

            // Placeholder response until service wiring is implemented.
            var jobId = Guid.NewGuid();
            var response = new PlaybookRunResponse
            {
                JobId = jobId,
                StatusUrl = $"/api/agent/playbooks/status/{jobId}"
            };

            return Results.Json(response, statusCode: 202);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AGENT] Failed to enqueue playbook {PlaybookId} for user {UserId}", request.PlaybookId, userId);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to enqueue playbook execution",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// GET /api/agent/playbooks/status/{jobId}
    /// Returns the current execution status of a playbook job.
    /// </summary>
    private static async Task<IResult> GetPlaybookStatusAsync(
        Guid jobId,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Agent.AgentEndpoints");
        var userId = ExtractUserId(httpContext);
        logger.LogDebug("[AGENT] Checking playbook status: JobId={JobId}, UserId={UserId}", jobId, userId);

        try
        {
            // TODO: Inject PlaybookExecutionEngine and delegate to existing status query.
            // Will call PlaybookExecutionEngine.GetStatusAsync(jobId, userId, cancellationToken)
            // and map the result to PlaybookStatusResponse.
            // Must verify job ownership (userId matches the job's creator) before returning status.

            // Placeholder response until service wiring is implemented.
            var response = new PlaybookStatusResponse
            {
                JobId = jobId,
                Status = "Queued",
                ProgressPercent = 0
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AGENT] Failed to get playbook status for job {JobId}", jobId);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to retrieve playbook execution status",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    // ────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the Azure AD user object ID from the authenticated claims.
    /// </summary>
    private static string ExtractUserId(HttpContext httpContext)
    {
        return httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "unknown";
    }

    // ────────────────────────────────────────────────────────────────
    // Supporting Types (lightweight, co-located with endpoints)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Summary record for a playbook in the agent catalog listing.
    /// </summary>
    public sealed record PlaybookSummary
    {
        /// <summary>Playbook definition ID.</summary>
        public required Guid PlaybookId { get; init; }

        /// <summary>Display name for the Copilot agent to present.</summary>
        public required string Name { get; init; }

        /// <summary>Brief description of what the playbook does.</summary>
        public required string Description { get; init; }
    }

    /// <summary>
    /// Response returned when a playbook execution is enqueued.
    /// </summary>
    public sealed record PlaybookRunResponse
    {
        /// <summary>The job ID to poll for status.</summary>
        public required Guid JobId { get; init; }

        /// <summary>Relative URL for status polling.</summary>
        public required string StatusUrl { get; init; }
    }
}
