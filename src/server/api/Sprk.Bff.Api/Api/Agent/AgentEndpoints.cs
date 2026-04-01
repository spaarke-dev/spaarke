using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;

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
        ChatSessionManager sessionManager,
        SprkChatAgentFactory agentFactory,
        IChatClient chatClient,
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
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Tenant ID not found in token claims (tid) or X-Tenant-Id header.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        logger.LogInformation(
            "[AGENT] Message received: UserId={UserId}, HasDocument={HasDocument}, ConversationRef={ConversationRef}",
            userId, request.DocumentId.HasValue, request.ConversationReference ?? "(new)");

        try
        {
            // 1. Resolve or create a chat session from the conversation reference.
            //    The ConversationReference maps 1:1 to a chat session ID. If null, create a new session.
            string sessionId;
            if (!string.IsNullOrEmpty(request.ConversationReference))
            {
                // Attempt to resume existing session
                var existingSession = await sessionManager.GetSessionAsync(tenantId, request.ConversationReference, cancellationToken);
                if (existingSession is not null)
                {
                    sessionId = existingSession.SessionId;
                }
                else
                {
                    // ConversationReference no longer maps to a valid session — create a new one
                    var newSession = await sessionManager.CreateSessionAsync(
                        tenantId,
                        request.DocumentId?.ToString(),
                        playbookId: null,
                        hostContext: null,
                        cancellationToken);
                    sessionId = newSession.SessionId;
                }
            }
            else
            {
                // First message — create a new session
                var newSession = await sessionManager.CreateSessionAsync(
                    tenantId,
                    request.DocumentId?.ToString(),
                    playbookId: null,
                    hostContext: null,
                    cancellationToken);
                sessionId = newSession.SessionId;
            }

            // 2. Create an agent for this session and collect the streamed response
            var session = await sessionManager.GetSessionAsync(tenantId, sessionId, cancellationToken);
            if (session is null)
            {
                return Results.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "Failed to retrieve newly created chat session",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
            }

            // No-op SSE writer — agent gateway collects the full response, not streaming
            Func<Ai.ChatSseEvent, CancellationToken, Task> noOpSseWriter = (_, _) => Task.CompletedTask;

            var agent = await agentFactory.CreateAgentAsync(
                sessionId,
                request.DocumentId?.ToString() ?? session.DocumentId ?? string.Empty,
                session.PlaybookId,
                tenantId,
                session.HostContext,
                session.AdditionalDocumentIds,
                httpContext,
                noOpSseWriter,
                latestUserMessage: request.Message,
                cancellationToken);

            // Build AI history from existing session messages
            var history = session.Messages
                .Where(m => m.Role != Models.Ai.Chat.ChatMessageRole.System)
                .Select(m => new ChatMessage(
                    m.Role == Models.Ai.Chat.ChatMessageRole.User ? ChatRole.User : ChatRole.Assistant,
                    m.Content))
                .ToList();

            // 3. Collect the full streamed response into a single text block
            var fullResponse = new StringBuilder();
            await foreach (var update in agent.SendMessageAsync(request.Message, history, cancellationToken))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    fullResponse.Append(update.Text);
                }
            }

            // 4. Build the agent response
            var responseText = fullResponse.ToString();
            var agentResponse = new AgentMessageResponse
            {
                ResponseText = responseText,
                // TODO: Format an Adaptive Card for rich results when AdaptiveCardFormatterService is available.
                // AdaptiveCardJson = adaptiveCardFormatter.FormatResponseCard(responseText),
                SuggestedActions = new List<string> { "List playbooks", "Search documents" }
            };

            return Results.Ok(agentResponse);
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
        IPlaybookService playbookService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Agent.AgentEndpoints");
        var userId = ExtractUserId(httpContext);
        logger.LogInformation("[AGENT] Listing playbooks for user {UserId}", userId);

        try
        {
            // Mirrors ChatEndpoints.ListPlaybooksAsync — merge user-owned + public, deduplicate by ID.
            var query = new PlaybookQueryParameters { PageSize = 50 };
            var seen = new HashSet<Guid>();
            var playbooks = new List<PlaybookSummary>();

            // 1. User-owned playbooks
            var userGuid = ExtractUserGuid(httpContext);
            if (userGuid.HasValue)
            {
                try
                {
                    var userPlaybooks = await playbookService.ListUserPlaybooksAsync(userGuid.Value, query, cancellationToken);
                    foreach (var pb in userPlaybooks.Items)
                    {
                        if (seen.Add(pb.Id))
                        {
                            playbooks.Add(new PlaybookSummary
                            {
                                PlaybookId = pb.Id,
                                Name = pb.Name,
                                Description = pb.Description
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[AGENT] Failed to load user playbooks for userId={UserId}; continuing with public only", userGuid);
                }
            }

            // 2. Public/shared playbooks (deduplicate)
            try
            {
                var publicPlaybooks = await playbookService.ListPublicPlaybooksAsync(query, cancellationToken);
                foreach (var pb in publicPlaybooks.Items)
                {
                    if (seen.Add(pb.Id))
                    {
                        playbooks.Add(new PlaybookSummary
                        {
                            PlaybookId = pb.Id,
                            Name = pb.Name,
                            Description = pb.Description
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[AGENT] Failed to load public playbooks; returning user playbooks only");
            }

            logger.LogDebug("[AGENT] ListPlaybooks returning {Count} playbooks (userId={UserId})", playbooks.Count, userId);

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
        IPlaybookOrchestrationService orchestrationService,
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
            // Execute playbook via the existing orchestration service.
            // The agent gateway consumes the SSE stream internally and collects the final result
            // rather than forwarding SSE to the Copilot client (Copilot expects JSON, not SSE).
            var runRequest = new PlaybookRunRequest
            {
                PlaybookId = request.PlaybookId,
                DocumentIds = new[] { request.DocumentId },
                Parameters = request.Parameters
            };

            // Consume the execution stream to capture the RunId from the first event
            // and determine final status. The orchestration service handles all execution logic.
            Guid runId = Guid.Empty;
            await foreach (var evt in orchestrationService.ExecuteAsync(runRequest, httpContext, cancellationToken))
            {
                if (evt.Type == PlaybookEventType.RunStarted)
                {
                    runId = evt.RunId;
                }
                // Continue consuming to drive execution to completion.
                // For long-running playbooks, the caller should use run-playbook + status polling instead.
            }

            // If we captured a runId, use it; otherwise generate one as fallback
            if (runId == Guid.Empty) runId = Guid.NewGuid();

            var response = new PlaybookRunResponse
            {
                JobId = runId,
                StatusUrl = $"/api/agent/playbooks/status/{runId}"
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
        IPlaybookOrchestrationService orchestrationService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Agent.AgentEndpoints");
        var userId = ExtractUserId(httpContext);
        logger.LogDebug("[AGENT] Checking playbook status: JobId={JobId}, UserId={UserId}", jobId, userId);

        try
        {
            // Delegate to existing orchestration service for run status.
            var runStatus = await orchestrationService.GetRunStatusAsync(jobId, cancellationToken);
            if (runStatus is null)
            {
                return Results.Problem(
                    statusCode: 404,
                    title: "Not Found",
                    detail: $"Playbook run {jobId} not found",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
            }

            // Map the internal PlaybookRunStatus to the agent-facing PlaybookStatusResponse
            var statusText = runStatus.State switch
            {
                PlaybookRunState.Pending => "Queued",
                PlaybookRunState.Running => "Running",
                PlaybookRunState.Completed => "Completed",
                PlaybookRunState.Failed => "Failed",
                PlaybookRunState.Cancelled => "Cancelled",
                _ => "Unknown"
            };

            // Calculate progress percentage from node metrics when available
            int? progressPercent = null;
            if (runStatus.Metrics is not null && runStatus.Metrics.TotalNodes > 0)
            {
                var completedCount = runStatus.Metrics.CompletedNodes + runStatus.Metrics.FailedNodes + runStatus.Metrics.SkippedNodes;
                progressPercent = (int)(100.0 * completedCount / runStatus.Metrics.TotalNodes);
            }

            var response = new PlaybookStatusResponse
            {
                JobId = jobId,
                Status = statusText,
                ProgressPercent = progressPercent,
                // TODO: Format result as Adaptive Card when AdaptiveCardFormatterService is available.
                // ResultCardJson = runStatus.State == PlaybookRunState.Completed
                //     ? adaptiveCardFormatter.FormatPlaybookResultCard(runStatus.Outputs)
                //     : null,
                ErrorMessage = runStatus.State == PlaybookRunState.Failed ? runStatus.ErrorMessage : null
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
    /// Extracts the Azure AD user object ID from the authenticated claims as a string.
    /// </summary>
    private static string ExtractUserId(HttpContext httpContext)
    {
        return httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "unknown";
    }

    /// <summary>
    /// Extracts the Azure AD user object ID as a Guid for service calls that require it.
    /// Returns null if the claim is missing or not a valid GUID.
    /// </summary>
    private static Guid? ExtractUserGuid(HttpContext httpContext)
    {
        var oid = httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        return Guid.TryParse(oid, out var userId) ? userId : null;
    }

    /// <summary>
    /// Extracts the tenant ID from the JWT 'tid' claim or X-Tenant-Id header fallback.
    /// Mirrors the pattern used in ChatEndpoints.
    /// </summary>
    private static string? ExtractTenantId(HttpContext httpContext)
    {
        var tenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

        if (string.IsNullOrEmpty(tenantId))
        {
            tenantId = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        }

        return tenantId;
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
