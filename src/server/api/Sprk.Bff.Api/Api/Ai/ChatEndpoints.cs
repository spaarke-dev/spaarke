using System.Text.Json;
using Microsoft.Extensions.AI;
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
    /// </summary>
    private static async Task SendMessageAsync(
        string sessionId,
        ChatSendMessageRequest request,
        ChatSessionManager sessionManager,
        ChatHistoryManager historyManager,
        SprkChatAgentFactory agentFactory,
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

        // Set SSE headers (matches AnalysisEndpoints.cs pattern exactly)
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        logger.LogInformation(
            "SendMessage: session={SessionId}, tenant={TenantId}, msgLen={MsgLen}, document={DocumentId}",
            sessionId, tenantId, request.Message.Length, request.DocumentId ?? session.DocumentId);

        var fullResponse = new System.Text.StringBuilder();

        try
        {
            // Create SSE writer delegate for out-of-band events (progress, document_replace)
            var sseWriter = CreateSseWriter(response);

            // Create agent for this session
            var agent = await agentFactory.CreateAgentAsync(
                sessionId,
                request.DocumentId ?? session.DocumentId ?? string.Empty,
                session.PlaybookId,
                tenantId,
                session.HostContext,
                session.AdditionalDocumentIds,
                httpContext,
                sseWriter,
                cancellationToken);

            // Convert session history to AI framework messages for context
            var history = BuildAiHistory(session.Messages);

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

            // Emit citation metadata (if any) BEFORE the done event.
            // Citations are accumulated by search tools during tool execution via CitationContext.
            // The frontend parses this event to map [N] markers in the response text to source details.
            if (agent.Citations is { Count: > 0 })
            {
                var citations = agent.Citations.GetCitations()
                    .Select(c => new ChatSseCitationItem(c.CitationId, c.SourceName, c.PageNumber, c.Excerpt, c.ChunkId))
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
            logger.LogInformation(
                "Client disconnected during SendMessage: session={SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during SendMessage: session={SessionId}", sessionId);

            if (!cancellationToken.IsCancellationRequested)
            {
                await WriteChatSSEAsync(response, new ChatSseEvent("error", ex.Message), CancellationToken.None);
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

        // Set SSE headers
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        logger.LogInformation(
            "RefineText: session={SessionId}, textLen={TextLen}, instruction={Instruction}",
            sessionId, request.SelectedText.Length, request.Instruction);

        var fullResponse = new System.Text.StringBuilder();

        try
        {
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

            // Send done event
            await WriteChatSSEAsync(response, new ChatSseEvent("done", null), cancellationToken);

            // If the refinement produced no output, send an informational message
            if (fullResponse.Length == 0)
            {
                await WriteChatSSEAsync(response, new ChatSseEvent("token", "No changes suggested."), cancellationToken);
                await WriteChatSSEAsync(response, new ChatSseEvent("done", null), cancellationToken);
            }

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
                await WriteChatSSEAsync(response, new ChatSseEvent("error", ex.Message), CancellationToken.None);
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
        var tenantId = httpContext.User.FindFirst("tid")?.Value;

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
}

// =============================================================================
// Request / Response Records
// =============================================================================

/// <summary>Request body for POST /sessions.</summary>
/// <param name="DocumentId">Optional document ID for the session context.</param>
/// <param name="PlaybookId">Playbook that governs the agent's system prompt and tools.</param>
/// <param name="HostContext">Optional host context describing where SprkChat is embedded (entity type, entity ID, workspace).</param>
public record ChatCreateSessionRequest(string? DocumentId, Guid PlaybookId, ChatHostContext? HostContext = null);

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
/// TODO: PH-112-A — Full context-aware refinement wired when editor exposes surrounding
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
/// <param name="Type">Event type: "token", "done", "error", "progress", or "document_replace".</param>
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
public record ChatSseCitationItem(int Id, string SourceName, int? Page, string Excerpt, string ChunkId);

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

/// <summary>
/// Playbook summary for the SprkChat playbook selector UI.
/// </summary>
/// <param name="Id">Playbook ID (GUID string).</param>
/// <param name="Name">Playbook display name.</param>
/// <param name="Description">Optional playbook description.</param>
/// <param name="IsPublic">Whether the playbook is public/shared.</param>
public record ChatPlaybookInfo(string Id, string Name, string? Description, bool IsPublic);
