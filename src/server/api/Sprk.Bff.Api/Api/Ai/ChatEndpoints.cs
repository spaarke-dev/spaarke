using System.Text.Json;
using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai.Chat;
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
            // Create agent for this session
            var agent = await agentFactory.CreateAgentAsync(
                sessionId,
                request.DocumentId ?? session.DocumentId ?? string.Empty,
                session.PlaybookId,
                tenantId,
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

        try
        {
            // Use TextRefinementTools directly (tool calling without full agent overhead)
            var refinementTools = new TextRefinementTools(chatClient);
            var refinedText = await refinementTools.RefineTextAsync(
                request.SelectedText,
                request.Instruction,
                cancellationToken);

            // Stream result as SSE token event then done event
            await WriteChatSSEAsync(response, new ChatSseEvent("token", refinedText), cancellationToken);
            await WriteChatSSEAsync(response, new ChatSseEvent("done", null), cancellationToken);

            logger.LogInformation(
                "RefineText completed: session={SessionId}, resultLen={ResultLen}",
                sessionId, refinedText.Length);
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

        logger.LogInformation(
            "SwitchContext: session={SessionId}, newDocument={DocumentId}, newPlaybook={PlaybookId}",
            sessionId, request.DocumentId, request.PlaybookId);

        // The agent is created fresh on each SendMessage call via SprkChatAgentFactory,
        // so context switching only requires updating the cached session's document/playbook fields.
        // The factory will pick up the new context on the next SendMessage call automatically.
        var updatedSession = session with
        {
            DocumentId = request.DocumentId ?? session.DocumentId,
            PlaybookId = request.PlaybookId ?? session.PlaybookId,
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
    /// Writes a single Server-Sent Event in the format:
    /// <c>data: {"type":"token","content":"..."}\n\n</c>
    ///
    /// Matches the SSE pattern from <see cref="AnalysisEndpoints"/> exactly.
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
}

// =============================================================================
// Request / Response Records
// =============================================================================

/// <summary>Request body for POST /sessions.</summary>
/// <param name="DocumentId">Optional document ID for the session context.</param>
/// <param name="PlaybookId">Playbook that governs the agent's system prompt and tools.</param>
public record ChatCreateSessionRequest(string? DocumentId, Guid PlaybookId);

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
public record ChatRefineRequest(string SelectedText, string Instruction);

/// <summary>Request body for PATCH /sessions/{id}/context.</summary>
/// <param name="DocumentId">New document ID (optional — null keeps current).</param>
/// <param name="PlaybookId">New playbook ID (optional — null keeps current).</param>
public record ChatSwitchContextRequest(string? DocumentId, Guid? PlaybookId);

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
/// </summary>
/// <param name="Type">Event type: "token", "done", or "error".</param>
/// <param name="Content">Text content for token events; error message for error events; null for done.</param>
public record ChatSseEvent(string Type, string? Content);
