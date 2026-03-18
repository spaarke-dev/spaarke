using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat.Tools;

// Explicit alias to avoid ChatMessage ambiguity between domain model and AI framework.
// Sprk.Bff.Api.Models.Ai.Chat.ChatMessage is the Dataverse persistence record.
// Microsoft.Extensions.AI.ChatMessage is the AI framework conversation message.
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using DvChatMessage = Sprk.Bff.Api.Models.Ai.Chat.ChatMessage;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Chat endpoints for the SprkChat feature.
///
/// Implements the session management and SSE streaming API for /api/ai/chat.
///
/// All endpoints follow ADR-001 (Minimal API) and ADR-008 (endpoint filters for authorization).
/// SSE streaming follows the same pattern as <see cref="AnalysisEndpoints"/> for consistency.
///
/// TenantId is extracted from the 'tid' JWT claim per ADR-014 (tenant-scoped cache keys),
/// with X-Tenant-Id header as a fallback for service-to-service calls.
/// </summary>
public static class ChatEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Registers all chat session endpoints on the provided route builder.
    /// Called from Program.cs: <c>app.MapChatEndpoints();</c>
    /// </summary>
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/chat")
            .RequireAuthorization()
            .WithTags("AI Chat");

        // POST /api/ai/chat/sessions — create a new chat session
        group.MapPost("/sessions", CreateSessionAsync)
            .AddAiAuthorizationFilter()
            .WithName("CreateChatSession")
            .WithSummary("Create a new SprkChat session")
            .WithDescription("Creates a new chat session for a document/playbook context. Returns the session ID and creation timestamp.")
            .Produces<ChatSessionCreatedResponse>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403);

        // POST /api/ai/chat/sessions/{sessionId}/messages — send message, receive SSE stream
        group.MapPost("/sessions/{sessionId}/messages", SendMessageAsync)
            .AddAiAuthorizationFilter()
            .RequireRateLimiting("ai-stream")
            .WithName("SendChatMessage")
            .WithSummary("Send a message and receive SSE-streamed response")
            .WithDescription("Sends a user message to the agent and streams the response as Server-Sent Events. Events: {type:'token',content:'...'} then {type:'done'}.")
            .Produces(200, contentType: "text/event-stream")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/ai/chat/sessions/{sessionId}/refine — SSE-streamed text refinement
        group.MapPost("/sessions/{sessionId}/refine", RefineTextAsync)
            .AddAiAuthorizationFilter()
            .RequireRateLimiting("ai-stream")
            .WithName("RefineText")
            .WithSummary("Refine selected text with SSE-streamed response")
            .WithDescription("Applies a refinement instruction to selected text and streams the result as Server-Sent Events.")
            .Produces(200, contentType: "text/event-stream")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // GET /api/ai/chat/sessions/{sessionId}/history — retrieve message history
        group.MapGet("/sessions/{sessionId}/history", GetHistoryAsync)
            .AddAiAuthorizationFilter()
            .WithName("GetChatHistory")
            .WithSummary("Get chat message history for a session")
            .WithDescription("Returns the ordered message list for a session. Falls back to Dataverse if Redis hot cache has expired.")
            .Produces<ChatHistoryResponse>()
            .ProducesProblem(401)
            .ProducesProblem(404);

        // PATCH /api/ai/chat/sessions/{sessionId}/context — switch document/playbook context
        group.MapMethods("/sessions/{sessionId}/context", ["PATCH"], SwitchContextAsync)
            .AddAiAuthorizationFilter()
            .WithName("SwitchChatContext")
            .WithSummary("Switch the document and/or playbook context for an existing session")
            .WithDescription("Updates the active document and playbook for a session without losing chat history.")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        // DELETE /api/ai/chat/sessions/{sessionId} — delete a session
        group.MapDelete("/sessions/{sessionId}", DeleteSessionAsync)
            .AddAiAuthorizationFilter()
            .WithName("DeleteChatSession")
            .WithSummary("Delete a chat session")
            .WithDescription("Removes the session from Redis and archives it in Dataverse. Chat history is retained as an audit trail.")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        // GET /api/ai/chat/playbooks — discover available playbooks (no session required)
        group.MapGet("/playbooks", ListPlaybooksAsync)
            .AddAiAuthorizationFilter()
            .WithName("ListChatPlaybooks")
            .WithSummary("Discover available playbooks for SprkChat")
            .WithDescription("Returns available playbooks for the current user. Merges user-owned and public playbooks, deduplicates by ID. Called before session creation to populate playbook selector UI.")
            .Produces<ChatPlaybookListResponse>()
            .ProducesProblem(401);

        // GET /api/ai/chat/context-mappings — resolve playbook mappings for entity/page context
        group.MapGet("/context-mappings", GetContextMappingsAsync)
            .AddAiAuthorizationFilter()
            .WithName("GetChatContextMappings")
            .WithSummary("Resolve playbook context mappings for a given entity type and page type")
            .WithDescription("Queries the sprk_aichatcontextmap table (with Redis caching) to resolve which playbook(s) apply for the given entityType + pageType context. Returns defaultPlaybook and availablePlaybooks. Returns 200 with empty results when no mapping exists (never 404).")
            .Produces<ChatContextMappingResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401);

        // DELETE /api/ai/chat/context-mappings/cache — evict all cached context mappings
        group.MapDelete("/context-mappings/cache", EvictContextMappingsCacheAsync)
            .AddAiAuthorizationFilter()
            .WithName("EvictContextMappingsCache")
            .WithSummary("Evict all cached context mappings from Redis")
            .WithDescription("Removes all chat:ctx-mapping:* keys from Redis. Use after updating sprk_aichatcontextmapping records in Dataverse to force fresh resolution on next request.")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401);

        // POST /api/ai/chat/sessions/{sessionId}/plan/approve — approve and execute a pending plan
        group.MapPost("/sessions/{sessionId}/plan/approve", ApprovePlanAsync)
            .AddAiAuthorizationFilter()
            .RequireRateLimiting("ai-stream")
            .WithName("ApprovePlan")
            .WithSummary("Approve a pending plan and execute it via SSE stream")
            .WithDescription(
                "Atomically retrieves and deletes the pending plan from Redis, then executes each step " +
                "in order while streaming progress as Server-Sent Events. " +
                "Events: plan_step_start, token (per step), plan_step_complete, done. " +
                "Returns 404 if the plan does not exist (expired or never created). " +
                "Returns 409 if the plan was already approved (double-click protection).")
            .Produces(200, contentType: "text/event-stream")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/ai/chat/sessions/{sessionId}/actions/{actionId}/confirm — confirm and execute a pending HITL action (Task R2-052)
        group.MapPost("/sessions/{sessionId}/actions/{actionId}/confirm", ConfirmActionAsync)
            .AddAiAuthorizationFilter()
            .WithName("ConfirmAction")
            .WithSummary("Confirm and execute a pending HITL action")
            .WithDescription(
                "Called after the user clicks Confirm in the ActionConfirmationDialog. " +
                "Dispatches the confirmed action to the PlaybookOutputHandler for execution. " +
                "Returns 200 with a result message on success. " +
                "Returns 404 if the session or action does not exist.")
            .Produces<ActionConfirmResult>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(404);

        // GET /api/ai/chat/sessions/{sessionId}/commands — resolve dynamic command catalog
        group.MapGet("/sessions/{sessionId}/commands", GetCommandsAsync)
            .AddAiAuthorizationFilter()
            .WithName("GetChatCommands")
            .WithSummary("Resolve available slash commands for a chat session")
            .WithDescription(
                "Returns the dynamic command catalog assembled from system commands, " +
                "playbook-contributed commands (filtered by entity type), and scope " +
                "capability commands. Results are cached in Redis with a 5-minute TTL " +
                "(ADR-009, ADR-014). The catalog is tenant-scoped, not user-scoped.")
            .Produces<IReadOnlyList<CommandEntry>>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(404);

        return app;
    }

    // =========================================================================
    // Session Management Endpoints
    // =========================================================================

    /// <summary>
    /// Create a new chat session.
    /// POST /api/ai/chat/sessions
    /// </summary>
    private static async Task<IResult> CreateSessionAsync(
        ChatCreateSessionRequest request,
        ChatSessionManager sessionManager,
        HttpContext httpContext,
        ILogger<ChatSessionManager> logger,
        CancellationToken cancellationToken)
    {
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Tenant ID not found in token claims (tid) or X-Tenant-Id header.");
        }

        logger.LogInformation(
            "Creating chat session for tenant={TenantId}, document={DocumentId}, playbook={PlaybookId}",
            tenantId, request.DocumentId, request.PlaybookId);

        var session = await sessionManager.CreateSessionAsync(
            tenantId,
            request.DocumentId,
            request.PlaybookId,
            request.HostContext,
            cancellationToken);

        logger.LogInformation("Chat session created: {SessionId}", session.SessionId);

        return Results.Created(
            $"/api/ai/chat/sessions/{session.SessionId}",
            new ChatSessionCreatedResponse(session.SessionId, session.CreatedAt));
    }

    /// <summary>
    /// Send a user message and receive SSE-streamed agent response.
    /// POST /api/ai/chat/sessions/{sessionId}/messages
    ///
    /// Phase 2F (task 071): Before streaming the agent response, this endpoint performs compound
    /// intent detection via <see cref="ISprkChatAgent.DetectToolCallsAsync"/>. If compound intent is
    /// detected (2+ tools, write-back, or external action), a <c>plan_preview</c> SSE event is emitted
    /// and execution halts until the user approves via POST /plan/approve (task 072).
    /// </summary>
    private static async Task SendMessageAsync(
        string sessionId,
        ChatSendMessageRequest request,
        ChatSessionManager sessionManager,
        ChatHistoryManager historyManager,
        SprkChatAgentFactory agentFactory,
        PendingPlanManager pendingPlanManager,
        IChatClient chatClient,
        HttpContext httpContext,
        ILogger<SprkChatAgentFactory> logger)
    {
        var cancellationToken = httpContext.RequestAborted;
        var response = httpContext.Response;
        var tenantId = ExtractTenantId(httpContext);

        if (string.IsNullOrEmpty(tenantId))
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "Tenant ID not found in token claims or X-Tenant-Id header" }, cancellationToken);
            return;
        }

        // Retrieve the existing session
        var session = await sessionManager.GetSessionAsync(tenantId, sessionId, cancellationToken);
        if (session is null)
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            await response.WriteAsJsonAsync(new { error = $"Session {sessionId} not found" }, cancellationToken);
            return;
        }

        // Set SSE headers — required for production-quality token-by-token streaming.
        // X-Accel-Buffering: no prevents nginx/YARP reverse proxy from buffering the SSE stream,
        // ensuring each token frame reaches the client immediately (NFR-01: first token < 500ms).
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";

        logger.LogInformation(
            "SendMessage: session={SessionId}, tenant={TenantId}, msgLen={MsgLen}, document={DocumentId}",
            sessionId, tenantId, request.Message.Length, request.DocumentId ?? session.DocumentId);

        var fullResponse = new System.Text.StringBuilder();

        try
        {
            // Create SSE writer delegate for out-of-band events (progress, document_replace)
            var sseWriter = CreateSseWriter(response);

            // Create agent for this session — pass the user's message for conversation-aware
            // document chunk re-selection (FR-03, R2-054). When a document exceeds the 30K
            // token budget, this enables the DocumentContextService to select chunks most
            // relevant to the user's current question rather than defaulting to position-based.
            var agent = await agentFactory.CreateAgentAsync(
                sessionId,
                request.DocumentId ?? session.DocumentId ?? string.Empty,
                session.PlaybookId,
                tenantId,
                session.HostContext,
                session.AdditionalDocumentIds,
                httpContext,
                sseWriter,
                latestUserMessage: request.Message,
                cancellationToken);

            // Convert session history to AI framework messages for context
            var history = BuildAiHistory(session.Messages);

            // === Phase 2F: Compound Intent Detection (task 071) ===
            // Perform a pre-execution tool call inspection to detect compound intent before
            // any tools run. If compound intent is detected, emit a plan_preview SSE event
            // and halt execution — the user must approve via POST /plan/approve (task 072).
            //
            // Compound intent triggers:
            //   - 2 or more tool calls in the proposed plan
            //   - Any write-back tool (EditWorkingDocument, AppendSection, etc.)
            //   - Any external action tool (SendEmail, CreateTask, etc.)
            //
            // This satisfies spec constraint FR-11: "No write-back executes without user Proceed."
            var toolCalls = await agent.DetectToolCallsAsync(request.Message, history, cancellationToken);
            var intentDetector = new CompoundIntentDetector(logger);

            if (intentDetector.IsCompoundIntent(toolCalls))
            {
                // Build the pending plan from the detected tool calls
                var pendingPlan = intentDetector.BuildPlan(
                    toolCalls,
                    sessionId,
                    tenantId,
                    agent.Context);

                // Store in Redis — 30-minute TTL per task 070 design
                await pendingPlanManager.StoreAsync(pendingPlan, cancellationToken);

                // Emit plan_preview SSE event with the plan steps
                var planPreviewData = new ChatSsePlanPreviewData(
                    PlanId: pendingPlan.PlanId,
                    PlanTitle: pendingPlan.PlanTitle,
                    Steps: pendingPlan.Steps.Select(s => new ChatSsePlanStep(s.Id, s.Description, "pending")).ToArray(),
                    AnalysisId: pendingPlan.AnalysisId,
                    WriteBackTarget: pendingPlan.WriteBackTarget);

                await WriteChatSSEAsync(
                    response,
                    new ChatSseEvent("plan_preview", null, planPreviewData),
                    cancellationToken);

                // Emit done event to close the SSE stream
                await WriteChatSSEAsync(response, new ChatSseEvent("done", null), cancellationToken);

                logger.LogInformation(
                    "Compound intent detected — plan_preview emitted and execution halted: session={SessionId}, planId={PlanId}, steps={StepCount}",
                    sessionId, pendingPlan.PlanId, pendingPlan.Steps.Length);

                // Persist the user message to history so the conversation record is intact.
                // The assistant message will be stored when the plan is approved/executed (task 072).
                var seqBaseForPlan = session.Messages.Count;
                var planUserMessage = new DvChatMessage(
                    MessageId: Guid.NewGuid().ToString("N"),
                    SessionId: sessionId,
                    Role: ChatMessageRole.User,
                    Content: request.Message,
                    TokenCount: 0,
                    CreatedAt: DateTimeOffset.UtcNow,
                    SequenceNumber: seqBaseForPlan + 1);
                await historyManager.AddMessageAsync(session, planUserMessage, CancellationToken.None);

                return;
            }
            // === End Phase 2F ===

            // === R2-018: Playbook Output Routing ===
            // Before streaming the standard chat response, check if the user's message matches
            // a playbook via PlaybookDispatcher. If a match is found, route through
            // PlaybookOutputHandler for typed output handling (dialog, navigation, download, insert).
            // Text output falls through to the standard streaming flow below.
            var dispatcher = await agentFactory.CreatePlaybookDispatcherAsync(tenantId, cancellationToken);
            var dispatchResult = await dispatcher.DispatchAsync(request.Message, session.HostContext, cancellationToken);

            if (dispatchResult is { Matched: true, OutputType: not OutputType.Text })
            {
                var outputHandler = agentFactory.CreatePlaybookOutputHandler();
                var handled = await outputHandler.HandleOutputAsync(
                    dispatchResult,
                    (evt, ct) => WriteChatSSEAsync(response, evt, ct),
                    session.HostContext,
                    cancellationToken);

                if (handled)
                {
                    // Emit done event and persist the user message
                    await WriteChatSSEAsync(response, new ChatSseEvent("done", null), cancellationToken);

                    var seqBaseForDispatch = session.Messages.Count;
                    var dispatchUserMessage = new DvChatMessage(
                        MessageId: Guid.NewGuid().ToString("N"),
                        SessionId: sessionId,
                        Role: ChatMessageRole.User,
                        Content: request.Message,
                        TokenCount: 0,
                        CreatedAt: DateTimeOffset.UtcNow,
                        SequenceNumber: seqBaseForDispatch + 1);
                    await historyManager.AddMessageAsync(session, dispatchUserMessage, CancellationToken.None);

                    logger.LogInformation(
                        "PlaybookDispatch: output handled — session={SessionId}, playbook={PlaybookId}, " +
                        "outputType={OutputType}",
                        sessionId, dispatchResult.PlaybookId, dispatchResult.OutputType);
                    return;
                }
            }
            // === End R2-018 ===

            // Emit typing_start immediately before the first AI token to signal the frontend
            // to show a typing indicator animation (NFR-01: first token < 500ms).
            await WriteChatSSEAsync(response, new ChatSseEvent("typing_start", null), cancellationToken);

            // Stream the agent response via IAsyncEnumerable<ChatResponseUpdate>
            await foreach (var update in agent.SendMessageAsync(request.Message, history, cancellationToken))
            {
                var content = update.Text;
                if (!string.IsNullOrEmpty(content))
                {
                    fullResponse.Append(content);
                    await WriteChatSSEAsync(response, new ChatSseEvent("token", content), cancellationToken);
                }
            }

            // Emit typing_end to signal that token generation is complete.
            // Placed before citations/suggestions/done so the frontend can hide the typing
            // animation as soon as the last token has been rendered.
            await WriteChatSSEAsync(response, new ChatSseEvent("typing_end", null), cancellationToken);

            // Emit citation metadata (if any) BEFORE the done event.
            // Citations are accumulated by search tools during tool execution via CitationContext.
            // The frontend parses this event to map [N] markers in the response text to source details.
            if (agent.Citations is { Count: > 0 })
            {
                var citations = agent.Citations.GetCitations()
                    .Select(c => new ChatSseCitationItem(
                        c.CitationId, c.SourceName, c.PageNumber, c.Excerpt, c.ChunkId,
                        c.SourceType, c.Url, c.Snippet))
                    .ToArray();

                await WriteChatSSEAsync(
                    response,
                    new ChatSseEvent("citations", null, new ChatSseCitationsData(citations)),
                    cancellationToken);

                logger.LogDebug(
                    "Emitted {CitationCount} citations for session={SessionId}",
                    citations.Length, sessionId);
            }

            // Generate follow-up suggestions via a focused LLM call (~100 tokens).
            // Runs after the main response completes so it doesn't delay perceived response time.
            // Bounded by a 2-second timeout — if generation fails or exceeds the timeout,
            // suggestions are silently skipped (ADR-019: suggestions are optional, no error emitted).
            await GenerateAndEmitSuggestionsAsync(
                chatClient, response, request.Message, fullResponse.ToString(), logger, sessionId, cancellationToken);

            // Write done event
            await WriteChatSSEAsync(response, new ChatSseEvent("done", null), cancellationToken);

            logger.LogInformation(
                "SendMessage completed: session={SessionId}, responseLen={ResponseLen}",
                sessionId, fullResponse.Length);

            // Persist user message then assistant response to history (outside SSE stream)
            var seqBase = session.Messages.Count;

            var userMessage = new DvChatMessage(
                MessageId: Guid.NewGuid().ToString("N"),
                SessionId: sessionId,
                Role: ChatMessageRole.User,
                Content: request.Message,
                TokenCount: 0,
                CreatedAt: DateTimeOffset.UtcNow,
                SequenceNumber: seqBase + 1);

            var updatedSession = await historyManager.AddMessageAsync(session, userMessage, CancellationToken.None);

            var assistantMessage = new DvChatMessage(
                MessageId: Guid.NewGuid().ToString("N"),
                SessionId: sessionId,
                Role: ChatMessageRole.Assistant,
                Content: fullResponse.ToString(),
                TokenCount: 0,
                CreatedAt: DateTimeOffset.UtcNow,
                SequenceNumber: seqBase + 2);

            await historyManager.AddMessageAsync(updatedSession, assistantMessage, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — clean close without typing_end or error event.
            // The client is already gone so there is no receiver for further frames.
            logger.LogInformation(
                "Client disconnected during SendMessage: session={SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during SendMessage: session={SessionId}", sessionId);

            if (!cancellationToken.IsCancellationRequested)
            {
                // Emit typing_end before the error event so the frontend stops the typing animation.
                await WriteChatSSEAsync(response, new ChatSseEvent("typing_end", null), CancellationToken.None);
                await WriteChatSSEAsync(
                    response,
                    new ChatSseEvent("error", "An error occurred while generating a response."),
                    CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Refine selected text with SSE-streamed response.
    /// POST /api/ai/chat/sessions/{sessionId}/refine
    ///
    /// Streams tokens incrementally as they are generated by the AI model,
    /// enabling real-time display in the client. Uses document_stream_* event
    /// convention for consistency with the Analysis Workspace streaming pipeline.
    /// </summary>
    private static async Task RefineTextAsync(
        string sessionId,
        ChatRefineRequest request,
        ChatSessionManager sessionManager,
        IChatClient chatClient,
        HttpContext httpContext,
        ILogger<ChatHistoryManager> logger)
    {
        var cancellationToken = httpContext.RequestAborted;
        var response = httpContext.Response;
        var tenantId = ExtractTenantId(httpContext);

        if (string.IsNullOrEmpty(tenantId))
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "Tenant ID not found in token claims or X-Tenant-Id header" }, cancellationToken);
            return;
        }

        // Verify session exists (tenant-scoped authorization check — ADR-014)
        var session = await sessionManager.GetSessionAsync(tenantId, sessionId, cancellationToken);
        if (session is null)
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            await response.WriteAsJsonAsync(new { error = $"Session {sessionId} not found" }, cancellationToken);
            return;
        }

        // Set SSE headers — X-Accel-Buffering prevents reverse proxy buffering (NFR-01).
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";

        logger.LogInformation(
            "RefineText: session={SessionId}, textLen={TextLen}, instruction={Instruction}",
            sessionId, request.SelectedText.Length, request.Instruction);

        var fullResponse = new System.Text.StringBuilder();

        try
        {
            // Emit typing_start before AI generation begins.
            await WriteChatSSEAsync(response, new ChatSseEvent("typing_start", null), cancellationToken);

            // Stream tokens incrementally via IChatClient.GetStreamingResponseAsync.
            // Uses TextRefinementTools to build the prompt messages, then streams
            // directly rather than collecting the full response first.
            var refinementTools = new TextRefinementTools(chatClient);
            var messages = refinementTools.BuildRefineMessages(
                request.SelectedText,
                request.Instruction,
                request.SurroundingContext);

            await foreach (var update in chatClient.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken))
            {
                var content = update.Text;
                if (!string.IsNullOrEmpty(content))
                {
                    fullResponse.Append(content);
                    await WriteChatSSEAsync(response, new ChatSseEvent("token", content), cancellationToken);
                }
            }

            // If the refinement produced no output, send an informational message
            if (fullResponse.Length == 0)
            {
                await WriteChatSSEAsync(response, new ChatSseEvent("token", "No changes suggested."), cancellationToken);
            }

            // Emit typing_end before done to signal the frontend to hide the typing animation.
            await WriteChatSSEAsync(response, new ChatSseEvent("typing_end", null), cancellationToken);

            // Send done event
            await WriteChatSSEAsync(response, new ChatSseEvent("done", null), cancellationToken);

            logger.LogInformation(
                "RefineText completed: session={SessionId}, resultLen={ResultLen}",
                sessionId, fullResponse.Length);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Client disconnected during RefineText: session={SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during RefineText: session={SessionId}", sessionId);

            if (!cancellationToken.IsCancellationRequested)
            {
                await WriteChatSSEAsync(response, new ChatSseEvent("typing_end", null), CancellationToken.None);
                await WriteChatSSEAsync(
                    response,
                    new ChatSseEvent("error", "An error occurred during text refinement."),
                    CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Get chat history for a session.
    /// GET /api/ai/chat/sessions/{sessionId}/history
    /// </summary>
    private static async Task<IResult> GetHistoryAsync(
        string sessionId,
        ChatHistoryManager historyManager,
        HttpContext httpContext,
        ILogger<ChatHistoryManager> logger,
        CancellationToken cancellationToken)
    {
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Tenant ID not found in token claims (tid) or X-Tenant-Id header.");
        }

        logger.LogDebug(
            "GetHistory: session={SessionId}, tenant={TenantId}", sessionId, tenantId);

        var messages = await historyManager.GetHistoryAsync(tenantId, sessionId, ct: cancellationToken);

        return Results.Ok(new ChatHistoryResponse(sessionId, messages.Select(MapToMessageInfo).ToArray()));
    }

    /// <summary>
    /// Switch the document/playbook context for an existing session.
    /// PATCH /api/ai/chat/sessions/{sessionId}/context
    /// </summary>
    private static async Task<IResult> SwitchContextAsync(
        string sessionId,
        ChatSwitchContextRequest request,
        ChatSessionManager sessionManager,
        HttpContext httpContext,
        ILogger<ChatSessionManager> logger,
        CancellationToken cancellationToken)
    {
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Tenant ID not found in token claims (tid) or X-Tenant-Id header.");
        }

        var session = await sessionManager.GetSessionAsync(tenantId, sessionId, cancellationToken);
        if (session is null)
        {
            return Results.NotFound(new { error = $"Session {sessionId} not found" });
        }

        // Validate additional document IDs cap (max 5)
        if (request.AdditionalDocumentIds is { Count: > ChatKnowledgeScope.MaxAdditionalDocuments })
        {
            return Results.Problem(
                statusCode: 400,
                title: "Validation Error",
                detail: $"AdditionalDocumentIds cannot exceed {ChatKnowledgeScope.MaxAdditionalDocuments} entries. Received {request.AdditionalDocumentIds.Count}.");
        }

        logger.LogInformation(
            "SwitchContext: session={SessionId}, newDocument={DocumentId}, newPlaybook={PlaybookId}, additionalDocs={AdditionalDocCount}",
            sessionId, request.DocumentId, request.PlaybookId, request.AdditionalDocumentIds?.Count ?? 0);

        // The agent is created fresh on each SendMessage call via SprkChatAgentFactory,
        // so context switching only requires updating the cached session's document/playbook fields.
        // The factory will pick up the new context on the next SendMessage call automatically.
        var updatedSession = session with
        {
            DocumentId = request.DocumentId ?? session.DocumentId,
            PlaybookId = request.PlaybookId ?? session.PlaybookId,
            HostContext = request.HostContext ?? session.HostContext,
            AdditionalDocumentIds = request.AdditionalDocumentIds ?? session.AdditionalDocumentIds,
            LastActivity = DateTimeOffset.UtcNow
        };

        await sessionManager.UpdateSessionCacheAsync(updatedSession, cancellationToken);

        logger.LogInformation("Context switched for session {SessionId}", sessionId);
        return Results.NoContent();
    }

    /// <summary>
    /// Delete a chat session.
    /// DELETE /api/ai/chat/sessions/{sessionId}
    /// </summary>
    private static async Task<IResult> DeleteSessionAsync(
        string sessionId,
        ChatSessionManager sessionManager,
        HttpContext httpContext,
        ILogger<ChatSessionManager> logger,
        CancellationToken cancellationToken)
    {
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Tenant ID not found in token claims (tid) or X-Tenant-Id header.");
        }

        // Verify session exists before deleting (returns 404 if not found)
        var session = await sessionManager.GetSessionAsync(tenantId, sessionId, cancellationToken);
        if (session is null)
        {
            return Results.NotFound(new { error = $"Session {sessionId} not found" });
        }

        logger.LogInformation(
            "DeleteSession: session={SessionId}, tenant={TenantId}", sessionId, tenantId);

        await sessionManager.DeleteSessionAsync(tenantId, sessionId, cancellationToken);

        logger.LogInformation("Session deleted: {SessionId}", sessionId);
        return Results.NoContent();
    }

    /// <summary>
    /// Confirm and execute a pending HITL action (Task R2-052).
    /// POST /api/ai/chat/sessions/{sessionId}/actions/{actionId}/confirm
    ///
    /// Called after the user clicks Confirm in the ActionConfirmationDialog.
    /// Currently a stub that returns success — future iterations will execute
    /// the action tool associated with the playbook.
    /// </summary>
    private static async Task<IResult> ConfirmActionAsync(
        string sessionId,
        string actionId,
        [Microsoft.AspNetCore.Mvc.FromBody] ActionConfirmRequest request,
        ChatSessionManager sessionManager,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Ai.ChatEndpoints");
        var tenantId = ExtractTenantId(httpContext);
        if (tenantId is null)
        {
            return Results.Problem(
                detail: "Unable to determine tenant. Ensure the 'tid' claim is present in the token.",
                statusCode: 403);
        }

        // Verify the session exists
        var session = await sessionManager.GetSessionAsync(tenantId, sessionId, cancellationToken);
        if (session is null)
        {
            return Results.Problem(
                detail: $"Session {sessionId} not found.",
                statusCode: 404,
                title: "Session Not Found");
        }

        logger.LogInformation(
            "Action confirmed: sessionId={SessionId}, actionId={ActionId}, paramCount={ParamCount}",
            sessionId, actionId, request.Parameters?.Count ?? 0);

        // TODO: In future iterations, execute the action tool here.
        // For now, return a success stub indicating the action was acknowledged.
        return Results.Ok(new ActionConfirmResult(
            Success: true,
            Message: $"Action {actionId} confirmed and executed successfully.",
            ActionId: actionId));
    }

    /// <summary>
    /// Approve a pending plan and execute it as an SSE stream.
    /// POST /api/ai/chat/sessions/{sessionId}/plan/approve
    ///
    /// Phase 2F (task 072): Called when the user clicks "Proceed" on a PlanPreviewCard.
    ///
    /// Protocol (task 070 design doc, Section 4):
    ///   1. Atomically get-and-delete the pending plan from Redis.
    ///      - Returns 404 if the plan was not found (expired or never created).
    ///      - Returns 409 if the plan was already deleted (concurrent approval / double-click).
    ///   2. Validates the <see cref="PlanApprovalRequest.PlanId"/> matches the stored plan.
    ///   3. Opens an SSE stream and executes each step in order, emitting per-step events.
    ///   4. Step execution uses <see cref="SprkChatAgentFactory"/> to build an agent with the
    ///      session context, then calls <see cref="ISprkChatAgent.SendMessageAsync"/> per step
    ///      using a synthetic step-execution message derived from the step's ToolName and ParametersJson.
    ///   5. After all steps complete, emits "done".
    ///
    /// SSE event sequence:
    ///   plan_step_start → token (streaming output) → plan_step_complete → ... → done
    ///   On step failure: plan_step_complete (status:"failed") → error → (stream ends)
    ///
    /// Idempotency: The get-and-delete on the Redis key ensures only one concurrent approval
    /// can proceed. The second request finds no key and receives 409 Conflict.
    /// </summary>
    private static async Task ApprovePlanAsync(
        string sessionId,
        PlanApprovalRequest request,
        ChatSessionManager sessionManager,
        ChatHistoryManager historyManager,
        SprkChatAgentFactory agentFactory,
        PendingPlanManager pendingPlanManager,
        HttpContext httpContext,
        ILogger<SprkChatAgentFactory> logger)
    {
        var cancellationToken = httpContext.RequestAborted;
        var response = httpContext.Response;
        var tenantId = ExtractTenantId(httpContext);

        if (string.IsNullOrEmpty(tenantId))
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "Tenant ID not found in token claims or X-Tenant-Id header" }, cancellationToken);
            return;
        }

        // Validate request body
        if (string.IsNullOrWhiteSpace(request.PlanId))
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "PlanId is required." }, cancellationToken);
            return;
        }

        // Verify session exists (tenant-scoped authorization per ADR-014)
        var session = await sessionManager.GetSessionAsync(tenantId, sessionId, cancellationToken);
        if (session is null)
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            await response.WriteAsJsonAsync(new { error = $"Session {sessionId} not found." }, cancellationToken);
            return;
        }

        // Atomic get-and-delete: prevents double-execution (task 070 design doc, Risk 2)
        // First approval: finds the key, deletes it, returns the plan → proceed
        // Second approval: key already gone → returns null → 409 Conflict
        var pendingPlan = await pendingPlanManager.GetAndDeleteAsync(tenantId, sessionId, cancellationToken);

        if (pendingPlan is null)
        {
            // Plan expired (30-min TTL) or was already approved/cancelled
            response.StatusCode = StatusCodes.Status409Conflict;
            await response.WriteAsJsonAsync(new
            {
                error = "Plan no longer available. It may have been approved already or expired. Please resend your request."
            }, cancellationToken);
            return;
        }

        // Validate planId matches — defence-in-depth against replay or mismatch
        if (!string.Equals(pendingPlan.PlanId, request.PlanId, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "ApprovePlan: planId mismatch — expected={ExpectedPlanId}, received={ReceivedPlanId}, session={SessionId}",
                pendingPlan.PlanId, request.PlanId, sessionId);

            response.StatusCode = StatusCodes.Status409Conflict;
            await response.WriteAsJsonAsync(new { error = "Plan ID does not match the pending plan. Please resend your request." }, cancellationToken);
            return;
        }

        // All validation passed — open SSE stream (matches pattern from SendMessageAsync).
        // X-Accel-Buffering prevents reverse proxy buffering (NFR-01).
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";

        logger.LogInformation(
            "ApprovePlan: executing plan — planId={PlanId}, session={SessionId}, tenant={TenantId}, steps={StepCount}",
            pendingPlan.PlanId, sessionId, tenantId, pendingPlan.Steps.Length);

        var sseWriter = CreateSseWriter(response);
        var stepResultsSb = new System.Text.StringBuilder();

        try
        {
            // Emit typing_start before plan execution begins.
            await WriteChatSSEAsync(response, new ChatSseEvent("typing_start", null), cancellationToken);

            // Build agent for this session (same factory call as SendMessageAsync).
            // Extract the last user message from session history for conversation-aware
            // document chunk re-selection (FR-03, R2-054).
            var lastUserMessage = session.Messages?
                .LastOrDefault(m => m.Role == ChatMessageRole.User)?.Content;

            var agent = await agentFactory.CreateAgentAsync(
                sessionId,
                session.DocumentId ?? string.Empty,
                session.PlaybookId,
                tenantId,
                session.HostContext,
                session.AdditionalDocumentIds,
                httpContext,
                sseWriter,
                latestUserMessage: lastUserMessage,
                cancellationToken);

            var history = BuildAiHistory(session.Messages);

            for (int stepIndex = 0; stepIndex < pendingPlan.Steps.Length; stepIndex++)
            {
                var step = pendingPlan.Steps[stepIndex];

                // Emit plan_step_start
                await WriteChatSSEAsync(
                    response,
                    new ChatSseEvent("plan_step_start", null, new ChatSsePlanStepStartData(step.Id, stepIndex)),
                    cancellationToken);

                logger.LogInformation(
                    "ApprovePlan: starting step {StepIndex}/{StepCount} — stepId={StepId}, tool={ToolName}, session={SessionId}",
                    stepIndex + 1, pendingPlan.Steps.Length, step.Id, step.ToolName, sessionId);

                // Build step execution message from the stored tool name and parameters
                // This synthetic message tells the agent what to execute for this step.
                // The agent will re-route it through the tool pipeline with its tools registered.
                var stepMessage = BuildStepExecutionMessage(step);

                var stepResponseSb = new System.Text.StringBuilder();
                var stepFailed = false;
                string? stepError = null;

                try
                {
                    // Stream this step's agent response
                    await foreach (var update in agent.SendMessageAsync(stepMessage, history, cancellationToken))
                    {
                        var content = update.Text;
                        if (!string.IsNullOrEmpty(content))
                        {
                            stepResponseSb.Append(content);
                            await WriteChatSSEAsync(response, new ChatSseEvent("token", content), cancellationToken);
                        }
                    }
                }
                catch (Exception stepEx) when (stepEx is not OperationCanceledException)
                {
                    stepFailed = true;
                    stepError = stepEx.Message;
                    logger.LogError(stepEx,
                        "ApprovePlan: step {StepIndex} failed — stepId={StepId}, session={SessionId}",
                        stepIndex + 1, step.Id, sessionId);
                }

                var stepResult = stepFailed
                    ? null
                    : (stepResponseSb.Length > 0 ? stepResponseSb.ToString() : "Step completed.");

                // Emit plan_step_complete
                await WriteChatSSEAsync(
                    response,
                    new ChatSseEvent("plan_step_complete", null, new ChatSsePlanStepCompleteData(
                        StepId: step.Id,
                        Status: stepFailed ? "failed" : "completed",
                        Result: stepResult,
                        ErrorCode: stepFailed ? "TOOL_EXECUTION_FAILED" : null,
                        ErrorMessage: stepError)),
                    cancellationToken);

                if (stepFailed)
                {
                    // Halt on first step failure (partial writes left in place — task 070 design doc, Risk 3)
                    await WriteChatSSEAsync(response, new ChatSseEvent("typing_end", null), CancellationToken.None);
                    await WriteChatSSEAsync(
                        response,
                        new ChatSseEvent("error", $"Plan execution halted at step {stepIndex + 1}."),
                        CancellationToken.None);

                    logger.LogWarning(
                        "ApprovePlan: execution halted at step {StepIndex} — planId={PlanId}, session={SessionId}",
                        stepIndex + 1, pendingPlan.PlanId, sessionId);

                    // Persist assistant message recording the partial execution
                    var partialResultMessage = new DvChatMessage(
                        MessageId: Guid.NewGuid().ToString("N"),
                        SessionId: sessionId,
                        Role: ChatMessageRole.Assistant,
                        Content: $"Plan execution halted at step {stepIndex + 1}: {step.Description}. Error: {stepError}",
                        TokenCount: 0,
                        CreatedAt: DateTimeOffset.UtcNow,
                        SequenceNumber: session.Messages.Count + 1);

                    await historyManager.AddMessageAsync(session, partialResultMessage, CancellationToken.None);
                    return;
                }

                stepResultsSb.AppendLine(stepResult);

                // Update history with step result so subsequent steps have context
                // (agent builds history fresh from session on each call; appending is sufficient)
                var stepAssistantMsg = new DvChatMessage(
                    MessageId: Guid.NewGuid().ToString("N"),
                    SessionId: sessionId,
                    Role: ChatMessageRole.Assistant,
                    Content: stepResult ?? string.Empty,
                    TokenCount: 0,
                    CreatedAt: DateTimeOffset.UtcNow,
                    SequenceNumber: session.Messages.Count + stepIndex + 1);

                session = await historyManager.AddMessageAsync(session, stepAssistantMsg, CancellationToken.None);
            }

            // All steps completed successfully.
            // If this plan has a write-back target, persist the accumulated step results to
            // sprk_analysisoutput.sprk_workingdocument in Dataverse.
            //
            // SAFETY CONSTRAINT (spec FR-12, ADR-013):
            //   - ONLY writes to sprk_analysisoutput.sprk_workingdocument via IWorkingDocumentService.
            //   - MUST NOT call SpeFileStore, GraphServiceClient write methods, or any SPE write.
            //   - WriteBackTarget == "sprk_analysisoutput.sprk_workingdocument" guards this path.
            //
            // IWorkingDocumentService is resolved via RequestServices because it is conditionally
            // registered (requires Analysis:Enabled + DocumentIntelligence:Enabled). When not
            // available (e.g., dev environments with analysis disabled), write-back is skipped
            // with a warning log — steps already executed and SSE done event follows regardless.
            // Diagnostic: log write-back evaluation
            logger.LogInformation(
                "ApprovePlan: write-back evaluation — " +
                "WriteBackTarget={WriteBackTarget}, AnalysisId={AnalysisId}, " +
                "accumulatedContentLen={AccumulatedContentLen}, planId={PlanId}",
                pendingPlan.WriteBackTarget ?? "(null)",
                pendingPlan.AnalysisId ?? "(null)",
                stepResultsSb.Length,
                pendingPlan.PlanId);

            if (!string.IsNullOrWhiteSpace(pendingPlan.WriteBackTarget) &&
                pendingPlan.WriteBackTarget == "sprk_analysisoutput.sprk_workingdocument" &&
                !string.IsNullOrWhiteSpace(pendingPlan.AnalysisId) &&
                Guid.TryParse(pendingPlan.AnalysisId, out var analysisGuid))
            {
                var accumulatedContent = stepResultsSb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(accumulatedContent))
                {
                    // Resolve IWorkingDocumentService from the scoped request services.
                    // GetService<T> returns null when not registered (safe optional resolution).
                    var workingDocumentService = httpContext.RequestServices
                        .GetService<IWorkingDocumentService>();

                    if (workingDocumentService != null)
                    {
                        logger.LogInformation(
                            "ApprovePlan: writing back to sprk_analysisoutput.sprk_workingdocument — " +
                            "analysisId={AnalysisId}, contentLen={ContentLen}, planId={PlanId}, session={SessionId}",
                            pendingPlan.AnalysisId, accumulatedContent.Length, pendingPlan.PlanId, sessionId);

                        try
                        {
                            // SAFETY: IWorkingDocumentService.UpdateWorkingDocumentAsync writes ONLY to
                            // Dataverse (sprk_analysisoutput.sprk_workingdocument via IGenericEntityService).
                            // No SPE/SharePoint Embedded write operations are performed here.
                            await workingDocumentService.UpdateWorkingDocumentAsync(
                                analysisGuid,
                                accumulatedContent,
                                CancellationToken.None); // Use None to ensure write completes even if client disconnects

                            logger.LogInformation(
                                "ApprovePlan: write-back completed — analysisId={AnalysisId}, planId={PlanId}",
                                pendingPlan.AnalysisId, pendingPlan.PlanId);
                        }
                        catch (Exception writeEx)
                        {
                            // Write-back failure is logged but does NOT prevent the done event from being emitted.
                            // The plan steps already executed successfully; the write-back is a best-effort persistence.
                            // The next plan execution or manual refresh will reflect the correct state.
                            logger.LogError(writeEx,
                                "ApprovePlan: write-back to sprk_analysisoutput.sprk_workingdocument failed — " +
                                "analysisId={AnalysisId}, planId={PlanId}, session={SessionId}",
                                pendingPlan.AnalysisId, pendingPlan.PlanId, sessionId);
                        }
                    }
                    else
                    {
                        logger.LogWarning(
                            "ApprovePlan: IWorkingDocumentService not available — write-back skipped. " +
                            "analysisId={AnalysisId}, planId={PlanId}",
                            pendingPlan.AnalysisId, pendingPlan.PlanId);
                    }
                }
                else
                {
                    logger.LogDebug(
                        "ApprovePlan: write-back target set but no accumulated content to write — " +
                        "analysisId={AnalysisId}, planId={PlanId}",
                        pendingPlan.AnalysisId, pendingPlan.PlanId);
                }
            }

            // Emit typing_end before done to signal plan execution is complete.
            await WriteChatSSEAsync(response, new ChatSseEvent("typing_end", null), cancellationToken);

            await WriteChatSSEAsync(response, new ChatSseEvent("done", null), cancellationToken);

            logger.LogInformation(
                "ApprovePlan: all steps completed — planId={PlanId}, session={SessionId}, steps={StepCount}",
                pendingPlan.PlanId, sessionId, pendingPlan.Steps.Length);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Client disconnected during ApprovePlan: session={SessionId}, planId={PlanId}",
                sessionId, pendingPlan.PlanId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error during ApprovePlan: session={SessionId}, planId={PlanId}",
                sessionId, pendingPlan.PlanId);

            if (!cancellationToken.IsCancellationRequested)
            {
                await WriteChatSSEAsync(response, new ChatSseEvent("typing_end", null), CancellationToken.None);
                await WriteChatSSEAsync(
                    response,
                    new ChatSseEvent("error", "An error occurred during plan execution."),
                    CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Builds a step-execution message from a <see cref="PendingPlanStep"/>.
    ///
    /// The message text is a synthetic instruction sent to the agent to execute the step's
    /// tool with the stored parameters. The agent sees this as a user turn and uses its
    /// registered tools to fulfill it — the LLM then invokes the correct tool based on
    /// the instruction.
    ///
    /// Format: "[Execute step: {ToolName}] {ParametersJson}"
    ///   - The bracketed prefix identifies this as a plan execution step (not a user query).
    ///   - The parameters JSON provides the tool call arguments.
    /// </summary>
    private static string BuildStepExecutionMessage(PendingPlanStep step)
    {
        if (string.IsNullOrWhiteSpace(step.ParametersJson) || step.ParametersJson == "{}")
        {
            return $"[Execute step: {step.ToolName}] {step.Description}";
        }
        return $"[Execute step: {step.ToolName}] Parameters: {step.ParametersJson}. {step.Description}";
    }

    /// <summary>
    /// Discover available playbooks for SprkChat.
    /// GET /api/ai/chat/playbooks
    ///
    /// Pre-session endpoint — called before the user starts chatting to populate
    /// the playbook selector UI with quick-action chips.
    /// Merges user-owned and public playbooks, deduplicates by ID.
    /// </summary>
    private static async Task<IResult> ListPlaybooksAsync(
        IPlaybookService playbookService,
        HttpContext httpContext,
        ILogger<ChatSessionManager> logger,
        string? nameFilter = null,
        CancellationToken cancellationToken = default)
    {
        var userId = ExtractUserId(httpContext);

        var query = new PlaybookQueryParameters
        {
            NameFilter = nameFilter,
            PageSize = 50
        };

        var seen = new HashSet<Guid>();
        var playbooks = new List<ChatPlaybookInfo>();

        // 1. Load user's own playbooks (if user ID is available)
        if (userId.HasValue)
        {
            try
            {
                var userPlaybooks = await playbookService.ListUserPlaybooksAsync(userId.Value, query, cancellationToken);
                foreach (var pb in userPlaybooks.Items)
                {
                    if (seen.Add(pb.Id))
                    {
                        playbooks.Add(new ChatPlaybookInfo(pb.Id.ToString(), pb.Name, pb.Description, pb.IsPublic));
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load user playbooks for userId={UserId}; continuing with public only", userId);
            }
        }

        // 2. Load public/shared playbooks and merge (deduplicate by ID)
        try
        {
            var publicPlaybooks = await playbookService.ListPublicPlaybooksAsync(query, cancellationToken);
            foreach (var pb in publicPlaybooks.Items)
            {
                if (seen.Add(pb.Id))
                {
                    playbooks.Add(new ChatPlaybookInfo(pb.Id.ToString(), pb.Name, pb.Description, pb.IsPublic));
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load public playbooks; returning user playbooks only");
        }

        logger.LogDebug("ListPlaybooks returning {Count} playbooks (userId={UserId})", playbooks.Count, userId);

        return Results.Ok(new ChatPlaybookListResponse(playbooks.ToArray()));
    }

    /// <summary>
    /// Resolve playbook context mappings for a given entity type and page type.
    /// GET /api/ai/chat/context-mappings?entityType=...&amp;pageType=...
    ///
    /// Pre-session endpoint — called by the frontend to determine which playbook(s)
    /// to offer based on where SprkChat is embedded (entity type + page type).
    /// Returns 200 with empty results when no mapping exists (never 404).
    /// </summary>
    private static async Task<IResult> GetContextMappingsAsync(
        HttpContext httpContext,
        ChatContextMappingService mappingService,
        ILogger<ChatContextMappingService> logger,
        string entityType,
        string? pageType = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            // Diagnostic: log claim details for debugging
            var claims = httpContext.User.Claims.Select(c => $"{c.Type}={c.Value}").ToArray();
            var xTenantHeader = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
            logger.LogWarning(
                "GetContextMappings: tenant ID missing — " +
                "claimCount={ClaimCount}, claims=[{Claims}], X-Tenant-Id={XTenantId}, entityType={EntityType}",
                claims.Length,
                claims.Length > 0 ? string.Join("; ", claims.Take(10)) : "(none)",
                xTenantHeader ?? "(none)",
                entityType);

            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Tenant ID not found in token claims (tid) or X-Tenant-Id header.");
        }

        logger.LogDebug(
            "GetContextMappings: entityType={EntityType}, pageType={PageType}, tenant={TenantId}",
            entityType, pageType, tenantId);

        var result = await mappingService.ResolveAsync(entityType, pageType, tenantId, cancellationToken);

        return Results.Ok(result);
    }

    /// <summary>
    /// Evict all cached context mappings from Redis.
    /// DELETE /api/ai/chat/context-mappings/cache
    ///
    /// Administrative endpoint — removes all <c>chat:ctx-mapping:*</c> keys from Redis
    /// so that subsequent <see cref="GetContextMappingsAsync"/> calls re-query Dataverse.
    /// Use after bulk-updating <c>sprk_aichatcontextmapping</c> records.
    /// </summary>
    private static async Task<IResult> EvictContextMappingsCacheAsync(
        HttpContext httpContext,
        ChatContextMappingService mappingService,
        ILogger<ChatContextMappingService> logger,
        CancellationToken cancellationToken)
    {
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Tenant ID not found in token claims (tid) or X-Tenant-Id header.");
        }

        logger.LogInformation(
            "EvictContextMappingsCache: evicting all context mapping cache entries (tenant={TenantId})",
            tenantId);

        var evictedCount = await mappingService.EvictAllCachedMappingsAsync(cancellationToken);

        logger.LogInformation(
            "EvictContextMappingsCache: evicted {Count} cache entries (tenant={TenantId})",
            evictedCount, tenantId);

        return Results.NoContent();
    }

    // =========================================================================
    // Command Resolution
    // =========================================================================

    /// <summary>
    /// Resolve the dynamic command catalog for a session's context.
    /// GET /api/ai/chat/sessions/{sessionId}/commands
    ///
    /// Returns commands partitioned into <c>systemCommands</c> (always present) and
    /// <c>dynamicCommands</c> (playbook + scope, context-specific). Each item carries
    /// a <c>source</c> discriminator ("system", "playbook", "scope") so the frontend
    /// SlashCommandMenu can group commands by origin category (R2-036, R2-053).
    ///
    /// Requires the session to exist in the session manager to obtain the host context
    /// (entity type) for playbook filtering.
    /// </summary>
    private static async Task<IResult> GetCommandsAsync(
        string sessionId,
        ChatSessionManager sessionManager,
        SprkChatAgentFactory agentFactory,
        HttpContext httpContext,
        ILogger<SprkChatAgentFactory> logger,
        CancellationToken cancellationToken)
    {
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Tenant ID not found in token claims (tid) or X-Tenant-Id header.");
        }

        // Retrieve the session to obtain host context (entity type for playbook filtering)
        var session = await sessionManager.GetSessionAsync(tenantId, sessionId, cancellationToken);
        if (session is null)
        {
            return Results.Problem(
                statusCode: 404,
                title: "Not Found",
                detail: $"Chat session '{sessionId}' not found or has expired.");
        }

        logger.LogDebug(
            "Resolving commands for session={SessionId}, tenant={TenantId}, entityType={EntityType}",
            sessionId, tenantId, session.HostContext?.EntityType ?? "(none)");

        var resolver = agentFactory.CreateCommandResolver();
        var commands = await resolver.ResolveCommandsAsync(tenantId, session.HostContext, cancellationToken);

        // Partition into system vs. dynamic and project to CommandResponseItem with
        // explicit source discriminator for frontend SlashCommandMenu grouping (R2-053).
        var systemCommands = new List<CommandResponseItem>();
        var dynamicCommands = new List<CommandResponseItem>();

        foreach (var cmd in commands)
        {
            var sourceType = DeriveSourceType(cmd.Category);
            var sourceName = sourceType switch
            {
                // System commands have no source name subtitle
                "system" => (string?)null,
                // Scope commands: Category carries the scope-qualified label (e.g., "Legal Research -- Search")
                "scope" => cmd.Category,
                // Playbook commands: use the label as source name (playbook name is in Label)
                _ => cmd.Label,
            };

            var item = new CommandResponseItem(
                cmd.Id,
                cmd.Label,
                cmd.Description,
                cmd.Trigger,
                Category: sourceType,
                Source: sourceType,
                SourceName: sourceName);

            if (sourceType == "system")
            {
                systemCommands.Add(item);
            }
            else
            {
                dynamicCommands.Add(item);
            }
        }

        return Results.Ok(new CommandsResponse(systemCommands, dynamicCommands));
    }

    /// <summary>
    /// Derives the frontend <c>SlashCommandSource</c> discriminator from the internal
    /// <see cref="CommandEntry.Category"/> value.
    ///
    /// The <see cref="DynamicCommandResolver"/> uses "system" and "playbook" as literal
    /// category values, but scope commands get a scope-qualified category label
    /// (e.g., "Legal Research -- Search"). Any category that is not "system" or "playbook"
    /// is treated as a scope command.
    /// </summary>
    private static string DeriveSourceType(string category)
    {
        if (string.Equals(category, "system", StringComparison.OrdinalIgnoreCase))
            return "system";
        if (string.Equals(category, "playbook", StringComparison.OrdinalIgnoreCase))
            return "playbook";
        return "scope";
    }

    // =========================================================================
    // Private Helpers
    // =========================================================================

    /// <summary>
    /// Extracts the tenant ID from the JWT 'tid' claim (ADR-014) with X-Tenant-Id header fallback
    /// for service-to-service calls that don't carry a user JWT.
    /// </summary>
    private static string? ExtractTenantId(HttpContext httpContext)
    {
        // Primary: 'tid' claim from Azure AD JWT token
        // Microsoft.Identity.Web may map 'tid' to the long-form URI claim
        var tenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

        // Fallback: X-Tenant-Id request header (service-to-service calls)
        if (string.IsNullOrEmpty(tenantId))
        {
            tenantId = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        }

        return tenantId;
    }

    /// <summary>
    /// Extracts the user's object ID from the JWT 'oid' claim (Azure AD).
    /// Returns null if the claim is missing or not a valid GUID.
    /// </summary>
    private static Guid? ExtractUserId(HttpContext httpContext)
    {
        var oid = httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        return Guid.TryParse(oid, out var userId) ? userId : null;
    }

    /// <summary>
    /// Converts the session's domain <see cref="DvChatMessage"/> history to the AI framework
    /// <see cref="AiChatMessage"/> format required by <see cref="SprkChatAgent.SendMessageAsync"/>.
    /// System messages are excluded — SprkChatAgent prepends the system prompt on every call.
    /// </summary>
    private static IReadOnlyList<AiChatMessage> BuildAiHistory(IReadOnlyList<DvChatMessage> messages)
    {
        return messages
            .Where(m => m.Role != ChatMessageRole.System)
            .Select(m => new AiChatMessage(
                m.Role == ChatMessageRole.User ? ChatRole.User : ChatRole.Assistant,
                m.Content))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Maps a domain <see cref="DvChatMessage"/> to the API response DTO.
    /// </summary>
    private static ChatSessionMessageInfo MapToMessageInfo(DvChatMessage m) =>
        new(m.Role.ToString(), m.Content, m.CreatedAt);

    /// <summary>
    /// Maximum time allowed for suggestion generation before it is silently skipped.
    /// </summary>
    private const int SuggestionsTimeoutMs = 2000;

    /// <summary>
    /// Generates 2-3 contextual follow-up suggestions using a focused LLM call (~100 tokens)
    /// and emits them as a "suggestions" SSE event.
    ///
    /// This is intentionally cheap and runs after the main response completes, so it does not
    /// add latency to the perceived response time. If the call fails or exceeds the 2-second
    /// timeout, suggestions are silently skipped (ADR-019: suggestions are optional).
    /// </summary>
    private static async Task GenerateAndEmitSuggestionsAsync(
        IChatClient chatClient,
        HttpResponse response,
        string userMessage,
        string assistantResponse,
        ILogger logger,
        string sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(SuggestionsTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            var suggestionsPrompt = new List<AiChatMessage>
            {
                new(ChatRole.System,
                    "You generate follow-up suggestions for a chat conversation. " +
                    "Based on the user's last message and the assistant's response, suggest 2-3 brief follow-up " +
                    "questions or actions the user might want to take next. " +
                    "Return ONLY a JSON array of strings. Each suggestion must be under 80 characters. " +
                    "Be specific and actionable. Do not include numbering or bullet points. " +
                    "Example: [\"Summarize the key risks\",\"Compare with the previous version\",\"What are the next steps?\"]"),
                new(ChatRole.User, $"User message: {userMessage}\n\nAssistant response (truncated): {Truncate(assistantResponse, 500)}")
            };

            var options = new ChatOptions { MaxOutputTokens = 100 };
            var result = await chatClient.GetResponseAsync(suggestionsPrompt, options, linkedCts.Token);
            var text = result.Text?.Trim();

            if (string.IsNullOrEmpty(text))
            {
                logger.LogDebug("Suggestion generation returned empty response for session={SessionId}", sessionId);
                return;
            }

            // Parse the JSON array of suggestions
            var suggestions = JsonSerializer.Deserialize<string[]>(text, JsonOptions);
            if (suggestions is null || suggestions.Length == 0)
            {
                logger.LogDebug("Suggestion generation returned no valid suggestions for session={SessionId}", sessionId);
                return;
            }

            // Validate: 1-3 suggestions, each under 80 characters
            var validSuggestions = suggestions
                .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length <= 80)
                .Take(3)
                .ToArray();

            if (validSuggestions.Length == 0)
            {
                logger.LogDebug("No valid suggestions after filtering for session={SessionId}", sessionId);
                return;
            }

            // Emit the suggestions SSE event
            await WriteChatSSEAsync(
                response,
                new ChatSseEvent("suggestions", null, new ChatSseSuggestionsData(validSuggestions)),
                cancellationToken);

            logger.LogDebug(
                "Emitted {SuggestionCount} suggestions for session={SessionId}",
                validSuggestions.Length, sessionId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Suggestion generation timed out — skip silently (ADR-019)
            logger.LogWarning(
                "Suggestion generation timed out ({TimeoutMs}ms) for session={SessionId}; skipping",
                SuggestionsTimeoutMs, sessionId);
        }
        catch (Exception ex)
        {
            // Suggestion generation failed — log warning but do not emit error event (ADR-019)
            logger.LogWarning(ex,
                "Suggestion generation failed for session={SessionId}; skipping", sessionId);
        }
    }

    /// <summary>
    /// Truncates a string to the specified maximum length, appending "..." if truncated.
    /// Used to limit the assistant response text sent to the suggestion generation prompt.
    /// </summary>
    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        return text[..maxLength] + "...";
    }

    /// <summary>
    /// Writes a single Server-Sent Event in the format:
    /// <c>data: {"type":"token","content":"..."}\n\n</c>
    ///
    /// Matches the SSE pattern from <see cref="AnalysisEndpoints"/> exactly.
    /// Supports structured <see cref="ChatSseEvent.Data"/> payloads for rich event types
    /// (progress, document_replace).
    /// </summary>
    private static async Task WriteChatSSEAsync(
        HttpResponse response,
        ChatSseEvent evt,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var sseData = $"data: {json}\n\n";

        await response.WriteAsync(sseData, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a delegate that writes SSE events to the given HTTP response.
    /// Used by tool classes (e.g. <see cref="Tools.AnalysisExecutionTools"/>) to emit
    /// out-of-band events (progress, document_replace) during long-running tool execution
    /// without coupling them to HttpResponse directly.
    /// </summary>
    internal static Func<ChatSseEvent, CancellationToken, Task> CreateSseWriter(HttpResponse response)
    {
        return (evt, ct) => WriteChatSSEAsync(response, evt, ct);
    }

    /// <summary>
    /// Writes a single <see cref="DocumentStreamEvent"/> as an SSE frame.
    ///
    /// The event type discriminator (<c>document_stream_start</c>, <c>document_stream_token</c>,
    /// <c>document_stream_end</c>) is embedded in the JSON payload via the <c>type</c> property,
    /// consistent with the <see cref="ChatSseEvent"/> pattern.
    ///
    /// ADR-015: Document content in <see cref="DocumentStreamTokenEvent"/> MUST NOT be logged.
    /// ADR-014: Streaming tokens are transient and MUST NOT be cached.
    /// </summary>
    internal static async Task WriteDocumentStreamSSEAsync(
        HttpResponse response,
        DocumentStreamEvent evt,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize<object>(evt, JsonOptions);
        var sseData = $"data: {json}\n\n";

        await response.WriteAsync(sseData, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a delegate that writes <see cref="DocumentStreamEvent"/> objects to the SSE response.
    /// Injected into <see cref="WorkingDocumentTools"/> via <see cref="SprkChatAgentFactory"/>
    /// to enable streaming write-back content to the client (spec FR-04).
    ///
    /// Replaces the no-op delegate that was used before task R2-023.
    /// </summary>
    internal static Func<DocumentStreamEvent, CancellationToken, Task> CreateDocumentStreamSseWriter(HttpResponse response)
    {
        return (evt, ct) => WriteDocumentStreamSSEAsync(response, evt, ct);
    }
}

// =============================================================================
// Request / Response Records
// =============================================================================

/// <summary>Request body for POST /sessions.</summary>
/// <param name="DocumentId">Optional document ID for the session context.</param>
/// <param name="PlaybookId">Playbook that governs the agent's system prompt and tools.</param>
/// <param name="HostContext">Optional host context describing where SprkChat is embedded (entity type, entity ID, workspace).</param>
public record ChatCreateSessionRequest(string? DocumentId, Guid? PlaybookId = null, ChatHostContext? HostContext = null);

/// <summary>Response body for POST /sessions (201 Created).</summary>
/// <param name="SessionId">The newly created session identifier.</param>
/// <param name="CreatedAt">UTC timestamp of session creation.</param>
public record ChatSessionCreatedResponse(string SessionId, DateTimeOffset CreatedAt);

/// <summary>Request body for POST /sessions/{id}/messages.</summary>
/// <param name="Message">The user's message text.</param>
/// <param name="DocumentId">Optional document ID override (uses session's document if omitted).</param>
public record ChatSendMessageRequest(string Message, string? DocumentId = null);

/// <summary>Request body for POST /sessions/{id}/refine.</summary>
/// <param name="SelectedText">The text passage to refine.</param>
/// <param name="Instruction">The refinement instruction (e.g., "simplify", "make formal").</param>
/// <param name="SurroundingContext">
/// Optional surrounding paragraphs for AI context. When provided, the AI model
/// receives context about where the selected text appears in the document, improving
/// refinement quality.
/// TRACKED: GitHub #233 - PH-112-A: Full context-aware refinement (editor surrounding context)
/// paragraph extraction. For now, this field is optional and the backend proceeds without it.
/// </param>
public record ChatRefineRequest(string SelectedText, string Instruction, string? SurroundingContext = null);

/// <summary>Request body for PATCH /sessions/{id}/context.</summary>
/// <param name="DocumentId">New document ID (optional — null keeps current).</param>
/// <param name="PlaybookId">New playbook ID (optional — null keeps current).</param>
/// <param name="HostContext">Optional host context override (null keeps current session's host context).</param>
/// <param name="AdditionalDocumentIds">
/// Optional list of additional document IDs (max 5) to pin to the conversation for
/// cross-referencing. Pass an empty list to clear. Pass null to keep the current set.
/// Exceeding 5 entries returns a 400 ProblemDetails validation error.
/// </param>
public record ChatSwitchContextRequest(
    string? DocumentId,
    Guid? PlaybookId,
    ChatHostContext? HostContext = null,
    IReadOnlyList<string>? AdditionalDocumentIds = null);

/// <summary>Request body for POST /sessions/{id}/actions/{actionId}/confirm (Task R2-052).</summary>
/// <param name="ActionId">The action identifier to confirm (matches URL parameter for validation).</param>
/// <param name="Parameters">Extracted parameters submitted with the confirmation.</param>
public record ActionConfirmRequest(string ActionId, Dictionary<string, string>? Parameters = null);

/// <summary>Response body for POST /sessions/{id}/actions/{actionId}/confirm (Task R2-052).</summary>
/// <param name="Success">Whether the action was executed successfully.</param>
/// <param name="Message">Human-readable result message.</param>
/// <param name="ActionId">The action identifier that was confirmed.</param>
public record ActionConfirmResult(bool Success, string Message, string ActionId);

/// <summary>Response body for GET /sessions/{id}/history.</summary>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Messages">Ordered message list (oldest first).</param>
public record ChatHistoryResponse(string SessionId, ChatSessionMessageInfo[] Messages);

/// <summary>
/// Chat message DTO for history responses.
/// Named ChatSessionMessageInfo to avoid collision with AnalysisEndpoints.ChatMessageInfo.
/// </summary>
/// <param name="Role">Message role (User, Assistant, System).</param>
/// <param name="Content">Message text content.</param>
/// <param name="Timestamp">UTC timestamp when the message was created.</param>
public record ChatSessionMessageInfo(string Role, string Content, DateTimeOffset Timestamp);

/// <summary>
/// SSE event payload for chat streaming.
/// Serializes as: <c>{"type":"token","content":"..."}</c> or <c>{"type":"done"}</c>.
///
/// For richer event types (progress, document_replace), use the derived records below
/// which carry structured <c>Data</c> payloads. All event types are serialized through the
/// same <see cref="ChatEndpoints.WriteChatSSEAsync"/> method and share the SSE wire format.
/// </summary>
/// <param name="Type">Event type: "token", "done", "error", "typing_start", "typing_end", "suggestions", "citations", "plan_preview", "plan_step_start", "plan_step_complete", "progress", "document_replace", "dialog_open", or "navigate".</param>
/// <param name="Content">Text content for token events; error message for error events; null for done/progress/document_replace.</param>
/// <param name="Data">Optional structured payload for rich event types (progress, document_replace). Null for token/done/error.</param>
public record ChatSseEvent(string Type, string? Content, object? Data = null);

/// <summary>
/// Progress data payload for "progress" SSE events emitted during long-running re-analysis.
/// Serialized as the <c>data</c> field inside a <see cref="ChatSseEvent"/>.
/// </summary>
/// <param name="Percent">Progress percentage (0-100). Indicates approximate completion of the re-analysis pipeline.</param>
/// <param name="Message">Human-readable progress message (e.g., "Extracting document text...", "Running analysis tools...").</param>
public record ChatSseProgressData(int Percent, string Message);

/// <summary>
/// Document replacement metadata for "document_replace" SSE events.
/// </summary>
/// <param name="PlaybookId">The playbook ID that produced this analysis.</param>
/// <param name="Timestamp">UTC ISO-8601 timestamp of when the analysis completed.</param>
public record ChatSseDocumentReplaceMetadata(string PlaybookId, string Timestamp);

/// <summary>
/// Document replacement data payload for "document_replace" SSE events.
/// Emitted when a re-analysis completes, carrying the full new analysis HTML for the client
/// to replace the current document pane content. The previous version MUST be pushed to the
/// undo stack by the client before applying the replacement.
/// </summary>
/// <param name="Html">Full analysis HTML output to replace the current document content.</param>
/// <param name="Metadata">Metadata about the replacement (playbook ID, timestamp).</param>
public record ChatSseDocumentReplaceData(string Html, ChatSseDocumentReplaceMetadata Metadata);

/// <summary>
/// Individual citation item in a "citations" SSE event payload.
/// Maps to the frontend <c>ICitation</c> type.
/// </summary>
/// <param name="Id">1-based citation number matching [N] markers in the response text.</param>
/// <param name="SourceName">Display name of the source document or knowledge article.</param>
/// <param name="Page">Page number in the source document (null when not available).</param>
/// <param name="Excerpt">Short excerpt (max 200 chars) from the matched content.</param>
/// <param name="ChunkId">Chunk ID from the search index for traceability.</param>
/// <param name="SourceType">Citation source type: null/"document" for internal SPE, "web" for external web search results.</param>
/// <param name="Url">Full URL of the web search result. Present when SourceType is "web".</param>
/// <param name="Snippet">Short text snippet from the web search result. Present when SourceType is "web".</param>
public record ChatSseCitationItem(
    int Id,
    string SourceName,
    int? Page,
    string Excerpt,
    string ChunkId,
    string? SourceType = null,
    string? Url = null,
    string? Snippet = null);

/// <summary>
/// Data payload for "citations" SSE events emitted after the agent response stream completes.
/// Contains all citation metadata accumulated by search tools during tool execution.
/// The frontend uses this to map [N] markers in the response text to source details.
/// </summary>
/// <param name="Citations">Ordered list of citation items (by citation ID).</param>
public record ChatSseCitationsData(ChatSseCitationItem[] Citations);

/// <summary>
/// Data payload for "suggestions" SSE events emitted after the main response completes.
/// Contains 1-3 contextual follow-up questions or actions generated by a focused LLM call.
/// The frontend renders these as clickable chips via <c>SprkChatSuggestions</c>.
/// </summary>
/// <param name="Suggestions">Array of 1-3 follow-up suggestion strings (each max 80 chars).</param>
public record ChatSseSuggestionsData(string[] Suggestions);

/// <summary>Response body for GET /playbooks — playbook discovery.</summary>
/// <param name="Playbooks">Available playbooks (user-owned + public, deduplicated).</param>
public record ChatPlaybookListResponse(ChatPlaybookInfo[] Playbooks);

// =============================================================================
// Plan Preview SSE Records (task 071, Phase 2F)
// =============================================================================

/// <summary>
/// A single step in a plan_preview SSE event.
/// Maps to the <see cref="PendingPlanStep"/> model but with a simplified shape for the frontend.
/// </summary>
/// <param name="Id">Step identifier (e.g., "step-1").</param>
/// <param name="Description">Human-readable description shown in the PlanPreviewCard step list.</param>
/// <param name="Status">Initial status: always "pending" at plan_preview time.</param>
public record ChatSsePlanStep(string Id, string Description, string Status);

/// <summary>
/// Data payload for "plan_preview" SSE events (task 071, Phase 2F).
///
/// Emitted when compound intent is detected (2+ tools, write-back, or external action)
/// to present the plan to the user for approval before execution.
///
/// The frontend renders this as a <c>PlanPreviewCard</c> with step list and Proceed/Cancel buttons.
/// On "Proceed": frontend calls POST /api/ai/chat/sessions/{sessionId}/plan/approve with { planId }.
/// On "Cancel": plan expires naturally (30-min Redis TTL) or user sends a new message.
///
/// Shape (matches task 070 design doc):
/// <code>
/// {
///   "type": "plan_preview",
///   "content": null,
///   "data": {
///     "planId": "a1b2c3d4...",
///     "planTitle": "Analyze and save findings",
///     "steps": [{ "id": "step-1", "description": "...", "status": "pending" }],
///     "analysisId": "uuid",
///     "writeBackTarget": "sprk_analysisoutput.sprk_workingdocument"
///   }
/// }
/// </code>
/// </summary>
/// <param name="PlanId">
/// Unique ID for this pending plan. Frontend echoes this back on POST /plan/approve
/// to prevent double-execution.
/// </param>
/// <param name="PlanTitle">Display title for the PlanPreviewCard header.</param>
/// <param name="Steps">Ordered list of plan steps shown to the user.</param>
/// <param name="AnalysisId">
/// Optional GUID of the <c>sprk_analysisoutput</c> record. Present for write-back plans.
/// </param>
/// <param name="WriteBackTarget">
/// Optional canonical field path for write-back steps (e.g., "sprk_analysisoutput.sprk_workingdocument").
/// </param>
public record ChatSsePlanPreviewData(
    string PlanId,
    string PlanTitle,
    ChatSsePlanStep[] Steps,
    string? AnalysisId,
    string? WriteBackTarget);

// =============================================================================
// Plan Approval Execution SSE Records (task 072, Phase 2F)
// =============================================================================

/// <summary>
/// Data payload for "plan_step_start" SSE events emitted during plan execution.
///
/// Emitted by POST /api/ai/chat/sessions/{sessionId}/plan/approve immediately
/// before each step begins. The frontend uses this to update the corresponding
/// step's status indicator in the PlanPreviewCard to "running".
///
/// Shape:
/// <code>
/// { "type": "plan_step_start", "content": null, "data": { "stepId": "step-1", "stepIndex": 0 } }
/// </code>
/// </summary>
/// <param name="StepId">The step identifier matching a step in the plan_preview data.</param>
/// <param name="StepIndex">0-based index of this step in the plan's step list.</param>
public record ChatSsePlanStepStartData(string StepId, int StepIndex);

/// <summary>
/// Data payload for "plan_step_complete" SSE events emitted after each step finishes.
///
/// Emitted by POST /api/ai/chat/sessions/{sessionId}/plan/approve after each step completes
/// (either successfully or with an error). The frontend uses this to update the step's
/// status and optionally show a result snippet below the step description.
///
/// Shape (success):
/// <code>
/// { "type": "plan_step_complete", "content": null,
///   "data": { "stepId": "step-1", "status": "completed", "result": "Analysis complete: 3 risks identified" } }
/// </code>
///
/// Shape (failure):
/// <code>
/// { "type": "plan_step_complete", "content": null,
///   "data": { "stepId": "step-1", "status": "failed",
///              "errorCode": "TOOL_EXECUTION_FAILED",
///              "errorMessage": "Analysis action timed out" } }
/// </code>
/// </summary>
/// <param name="StepId">The step identifier matching a step in the plan_preview data.</param>
/// <param name="Status">Execution status: "completed" or "failed".</param>
/// <param name="Result">
/// Optional brief result snippet (max 500 chars) shown below the step description on success.
/// Null on failure.
/// </param>
/// <param name="ErrorCode">Machine-readable error code on failure (e.g., "TOOL_EXECUTION_FAILED"). Null on success.</param>
/// <param name="ErrorMessage">Human-readable error message on failure. Null on success.</param>
public record ChatSsePlanStepCompleteData(
    string StepId,
    string Status,
    string? Result = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

// =============================================================================
// Playbook Output Handler SSE Records (R2-018, Phase 2B)
// =============================================================================

/// <summary>
/// Data payload for "dialog_open" SSE events emitted by <see cref="Services.Ai.Chat.PlaybookOutputHandler"/>
/// when a playbook's output type is <see cref="Models.Ai.OutputType.Dialog"/> and
/// <see cref="Models.Ai.Chat.DispatchResult.RequiresConfirmation"/> is true.
///
/// The frontend (SprkChatPane) receives this event and opens the Code Page dialog via
/// <c>Xrm.Navigation.navigateTo</c> with the specified web resource name and pre-populated fields.
///
/// Shape (camelCase serialization matches frontend IChatSseEventData contract):
/// <code>
/// {
///   "type": "dialog_open",
///   "content": null,
///   "data": {
///     "targetPage": "sprk_emailcomposer",
///     "prePopulateFields": { "recipient": "john@example.com", "subject": "RE: Contract Review" },
///     "playbookId": "a1b2c3d4-...",
///     "playbookName": "Draft Email"
///   }
/// }
/// </code>
///
/// ADR-006: The <see cref="TargetPage"/> MUST reference a Code Page web resource name
/// (not a model-driven app dialog or JavaScript alert).
/// ADR-014: Pre-populated field values are ephemeral and MUST NOT be cached.
/// </summary>
/// <param name="TargetPage">Code Page web resource name (e.g., "sprk_emailcomposer"). Serializes as "targetPage".</param>
/// <param name="PrePopulateFields">AI-extracted field values for pre-populating the Code Page dialog. Serializes as "prePopulateFields".</param>
/// <param name="PlaybookId">The matched playbook's ID (GUID string).</param>
/// <param name="PlaybookName">Display name of the matched playbook.</param>
public record ChatSseDialogOpenData(
    string TargetPage,
    Dictionary<string, string> PrePopulateFields,
    string PlaybookId,
    string? PlaybookName);

/// <summary>
/// Data payload for "navigate" SSE events emitted by <see cref="Services.Ai.Chat.PlaybookOutputHandler"/>
/// when a playbook's output type is <see cref="Models.Ai.OutputType.Navigation"/>.
///
/// The frontend uses this to navigate the user to a Dataverse record, external URL,
/// or another page within the application.
///
/// Shape:
/// <code>
/// {
///   "type": "navigate",
///   "content": null,
///   "data": {
///     "url": "https://org.crm.dynamics.com/main.aspx?...",
///     "targetPage": "sprk_matterdetail",
///     "parameters": { "matterId": "abc-123" },
///     "playbookId": "a1b2c3d4-..."
///   }
/// }
/// </code>
/// </summary>
/// <param name="Url">
/// Fully constructed navigation URL. Null when <see cref="TargetPage"/> is provided instead.
/// </param>
/// <param name="TargetPage">
/// Code Page web resource name for internal navigation. Null when <see cref="Url"/> is provided.
/// </param>
/// <param name="Parameters">Extracted parameters for building the navigation target.</param>
/// <param name="PlaybookId">The matched playbook's ID (GUID string).</param>
public record ChatSseNavigateData(
    string? Url,
    string? TargetPage,
    Dictionary<string, string> Parameters,
    string? PlaybookId);

/// <summary>
/// Playbook summary for the SprkChat playbook selector UI.
/// </summary>
/// <param name="Id">Playbook ID (GUID string).</param>
/// <param name="Name">Playbook display name.</param>
/// <param name="Description">Optional playbook description.</param>
/// <param name="IsPublic">Whether the playbook is public/shared.</param>
public record ChatPlaybookInfo(string Id, string Name, string? Description, bool IsPublic);
