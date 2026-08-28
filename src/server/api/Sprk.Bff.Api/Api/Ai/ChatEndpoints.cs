using Sprk.Bff.Api.Infrastructure.Authentication;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat.SseEventTypes;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Ai.Safety.CrossMatter;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Sprk.Bff.Api.Telemetry;
// Explicit alias to avoid ChatMessage ambiguity between domain model and AI framework.
// Sprk.Bff.Api.Models.Ai.Chat.ChatMessage is the Dataverse persistence record.
// Microsoft.Extensions.AI.ChatMessage is the AI framework conversation message.
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using DvChatMessage = Sprk.Bff.Api.Models.Ai.Chat.ChatMessage;
using Sprk.Bff.Api.Infrastructure.Authentication;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Chat endpoints for the SprkChat feature.
///
/// Implements the session management and SSE streaming API for /api/ai/chat.
///
/// All endpoints follow ADR-001 (Minimal API) and ADR-008 (endpoint filters for authorization).
/// SSE streaming follows the same pattern as <see cref="AnalysisEndpoints"/> for consistency.
///
/// TenantId is extracted from the 'tid' JWT claim per ADR-014 (tenant-scoped cache keys).
/// Tenant comes from the caller's authenticated principal and from nothing else (task 059 — see Infrastructure/Authentication/TenantResolution).
/// </summary>
public static class ChatEndpoints
{
    /// <summary>
    /// Stable errorCode for an unexpected server-side failure of a chat turn
    /// (G-P3 UAT round-1 H3 / ADR-019). The SSE 'error' event carries
    /// <c>[chat.turn-failed]</c> + a safe, stable message; the exception detail
    /// is logged server-side only — upstream-provider internals must never
    /// render in a user transcript.
    /// </summary>
    public const string ChatTurnFailedErrorCode = "chat.turn-failed";

    /// <summary>
    /// The ONE construction site for the SendMessage catch-all SSE error event
    /// (G-P3 UAT round-1 H3 / ADR-019). Takes NO exception input by design —
    /// the safe copy cannot regress into interpolating server internals.
    /// </summary>
    internal static ChatSseEvent BuildTurnFailedErrorEvent() =>
        new("error", $"[{ChatTurnFailedErrorCode}] The assistant hit a problem completing this turn. Please try again.");

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

        // GET /api/ai/chat/sessions — list recent sessions for the current user
        group.MapGet("/sessions", ListRecentSessionsAsync)
            .AddAiAuthorizationFilter()
            .WithName("ListRecentSessions")
            .WithSummary("List recent chat sessions")
            .WithDescription("Returns the most recent sessions for the current tenant, ordered by last activity descending. Use ?limit=N to control count (default 10).")
            .Produces<IReadOnlyList<RecentSessionDto>>()
            .ProducesProblem(401);

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

        // POST /api/ai/chat/sessions/{sessionId}/messages — send message, receive SSE stream.
        // Endpoint filters per ADR-008: AddAiAuthorizationFilter() + RequireRateLimiting("ai-stream").
        // These fire BEFORE the handler runs, so the FR-07 attachment-payload validation added
        // in task 050 (handler-level) executes only on requests that already passed auth + rate
        // limiting. No filter bypass is introduced.
        group.MapPost("/sessions/{sessionId}/messages", SendMessageAsync)
            .AddAiAuthorizationFilter()
            .RequireRateLimiting("ai-stream")
            .WithName("SendChatMessage")
            .WithSummary("Send a message and receive SSE-streamed response")
            .WithDescription("Sends a user message to the agent and streams the response as Server-Sent Events. Events: {type:'token',content:'...'} then {type:'done'}. Optional attachments[] (max 5, FR-07) provide in-memory file context for the SAME single LLM call.")
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

        // PATCH /api/ai/chat/sessions/{sessionId} — rename a session (FR-D4, task 032)
        group.MapMethods("/sessions/{sessionId}", ["PATCH"], RenameSessionAsync)
            .AddAiAuthorizationFilter()
            .WithName("RenameChatSession")
            .WithSummary("Rename a chat session (FR-D4)")
            .WithDescription("Updates the session's stored, human-readable title. Persists across reloads (StoredSession.Title, ADR-040 — no new store). Returns 404 for a genuinely-missing session (same existence-check pattern as GetHistoryAsync/SwitchContextAsync).")
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
            .WithDescription(
                "Erases every copy of the session's uploaded file bytes (durable blob + the 4-hour " +
                "doc-upload caches), removes the session from Redis and Cosmos, and archives it in " +
                "Dataverse. Chat history is retained as an audit trail. Returns 500 with errorCode " +
                "'session.durable-erasure-incomplete' if the file bytes could not be confirmed erased — " +
                "in that case NOTHING was deleted and the request can be retried (FR-B06).")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        // GET /api/ai/chat/sessions/{sessionId}/restore — restore session state for three-pane UI
        group.MapGet("/sessions/{sessionId}/restore", RestoreSessionAsync)
            .AddAiAuthorizationFilter()
            .WithName("RestoreSession")
            .WithSummary("Restore a persisted session for the three-pane UI")
            .WithDescription("Loads session from Cosmos DB, checks entity staleness, reconstructs LLM context, and returns widget states for UI restoration. Target: <500ms p95.")
            .Produces<SessionRestoreResponse>()
            .ProducesProblem(401)
            .ProducesProblem(404);

        // GET /api/ai/chat/sessions/by-analysis/{analysisId} — resolve the session bound to an
        // Analysis for the hub-grid reopen flow (ai-advanced-capabilities-analysis-hub-r1 task 031,
        // spec FR-11). Read-only projection over the task-020 GetSessionsByAnalysisAsync FK query —
        // no new store, no new session-binding write path. Route is a literal 3-segment path
        // ("by-analysis" is not a route parameter) so it cannot collide with the sibling
        // DELETE /sessions/{sessionId} 2-segment route.
        group.MapGet("/sessions/by-analysis/{analysisId:guid}", GetSessionByAnalysisAsync)
            .AddAiAuthorizationFilter()
            .WithName("GetSessionByAnalysis")
            .WithSummary("Resolve the chat session bound to an sprk_analysis record (FR-11 reopen)")
            .WithDescription("Returns the most recently created chat session bound to the given sprk_analysis via the sprk_aichatsummary.sprk_analysis FK (task 020). 404 when no session has ever been bound to this Analysis — callers MUST NOT mint a new session in that case (no silent empty-session creation).")
            .Produces<AnalysisSessionSummary>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(404);

        // PATCH /api/ai/chat/sessions/{sessionId}/tabs — write-through workspace tab persistence (NFR-09, task 065)
        // Endpoint filters match the sibling /messages route (ADR-008): auth + ai-stream rate limit.
        group.MapMethods("/sessions/{sessionId}/tabs", ["PATCH"], SaveTabsAsync)
            .AddAiAuthorizationFilter()
            .RequireRateLimiting("ai-stream")
            .WithName("SaveSessionTabs")
            .WithSummary("Persist workspace tabs and active tab id for a session (NFR-09)")
            .WithDescription("Write-through persistence of non-Home workspace tabs and active selection. Used by SpaarkeAi WorkspacePane on every tab mutation (debounced ~200ms client-side). Home tab is recreated by ensureHomeTab() and is not persisted.")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(404)
            .ProducesProblem(429);

        // GET /api/ai/chat/sessions/{sessionId}/tabs — read persisted workspace tabs (NFR-09, task 065)
        group.MapGet("/sessions/{sessionId}/tabs", GetTabsAsync)
            .AddAiAuthorizationFilter()
            .RequireRateLimiting("ai-stream")
            .WithName("GetSessionTabs")
            .WithSummary("Retrieve persisted workspace tabs and active tab id for a session")
            .WithDescription("Returns the most recently persisted non-Home tabs and active tab id. Empty list for sessions that have never persisted tabs.")
            .Produces<SessionTabsResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(404)
            .ProducesProblem(429);

        // POST /api/ai/chat/sessions/{sessionId}/suggest — proactive follow-on suggestions
        // (spaarkeai-assistant-enhancements-r2 FR-B3/B5, task 022). ONE grounded, contextType-
        // pre-filtered suggestion turn (AssistantSuggestionService) returning ≤3 content-specific
        // chips for the focused tab. PROPOSER only — the chips ride the existing Click path when
        // clicked; this endpoint never dispatches, injects a transcript message, or writes a ledger
        // entry (ADR-039/040). Best-effort: returns an empty chip list (never 5xx) on any
        // upstream failure or when the AI feature is disabled. Same auth as the sibling session
        // endpoints.
        group.MapPost("/sessions/{sessionId}/suggest", SuggestFollowupsAsync)
            .AddAiAuthorizationFilter()
            .RequireRateLimiting("ai-stream")
            .WithName("SuggestFollowups")
            .WithSummary("Proactive follow-on suggestions for the focused workspace tab (FR-B3/B5)")
            .WithDescription("Runs one grounded, context-type-pre-filtered suggestion turn over the focused tab's compact server-derived visible state and returns up to 3 content-specific follow-on chips (targetBindingId + label). A proposer only — chips dispatch via the existing Click path on user click. Returns an empty list when nothing is relevant or the feature is disabled; never injects a transcript message.")
            .Produces<ChatSuggestResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(404)
            .ProducesProblem(429);

        // GET /api/ai/chat/sessions/{sessionId}/compose-outputs — read the session's
        // compose-disposition ledger outputs (spaarkeai-compose-r2 FR-04 render-follows-store;
        // task 016 HOOK #1). Read-only projection of session.Outputs (ADR-040); same auth as
        // the sibling GET session endpoints.
        group.MapGet("/sessions/{sessionId}/compose-outputs", GetComposeOutputsAsync)
            .AddAiAuthorizationFilter()
            .WithName("GetSessionComposeOutputs")
            .WithSummary("Read stored compose-disposition draft outputs for a session (FR-04)")
            .WithDescription("Projects the session ledger's compose-disposition SessionOutputs (ADR-040 store-before-render). ComposeWorkspace re-reads these to materialize AI-drafted content into the editor. Returns an empty list until a compose Binding writes an output (the write half — BindingDisposition.Compose + OutputRouter case — is core spaarke-ai-architecture-redesign-r2 task 010).")
            .Produces<IReadOnlyList<ComposeLedgerOutputDto>>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(404);

        // POST /api/ai/chat/sessions/{sessionId}/compose-outputs/supersede — FR-17 undo/replace
        // (spaarkeai-compose-r2 task 034). Retract a prior compose draft as a LEDGER SUPERSESSION:
        // append a NEW superseding `compose` SessionOutput (ADR-040 "corrections are new entries
        // referencing the superseded key") so the retraction is durable across refresh — NOT a
        // client-only DOM undo (HANDOFF §1 item 5). Consumes the published supersession semantics
        // (ComposeDisposition.ResolveCurrent — the highest-turn compose entry is the head). Does NOT
        // touch OutputRouter.cs / Binding.cs (E-20 frozen files) and adds no disposition/route: it
        // appends to the SAME session.Outputs store via the SAME UpdateSessionCacheAsync seam the
        // OutputRouter uses (ADR-040 append-only). "undo" is a retraction (empty payload → the client
        // re-materializes to nothing); "replace" chains this retraction to a fresh Draft-Alternative
        // dispatch (the client). Same auth as the sibling session-write endpoints (ADR-008).
        group.MapPost("/sessions/{sessionId}/compose-outputs/supersede", SupersedeComposeOutputAsync)
            .AddAiAuthorizationFilter()
            .WithName("SupersedeComposeOutput")
            .WithSummary("Retract/supersede a prior compose draft as a ledger supersession (FR-17)")
            .WithDescription("Appends a NEW superseding compose-disposition SessionOutput that retracts the referenced {bindingId}@t{n} entry (ADR-040 append-only supersession — undo/replace is a durable ledger write, never a client DOM undo). Superseding an already-superseded (or non-existent) ref is an idempotent no-op / honest 404. The client re-materializes the editor from current ledger state (compose-outputs read + the Flow-5 apply signal).")
            .Produces<ComposeSupersedeResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(404);

        // GET /api/ai/chat/sessions/{sessionId}/trace — decision-traceability read surface
        // (AIR2-038 / FR-A1-09, D-F4). NET-NEW read surface: projects the session's stored
        // ADR-040 ledger markers (ToolChain + Gate + ContextEnvelope-fingerprint) into the
        // TraceEvent v1 stream so a decision-traceability view survives a hard refresh (closing
        // the client executionTraceBuffer mount-gap). Read-only (D-F0(b) reads are free); no new
        // store (ADR-040 — the ledger stays source of truth); consumable by satellite CRUD code
        // only via the ISessionTraceReader PublicContracts facade (ADR-013). Same auth + rate
        // limit as the sibling session-read routes (ADR-008): AddAiAuthorizationFilter + ai-stream.
        group.MapGet("/sessions/{sessionId}/trace", GetSessionTraceAsync)
            .AddAiAuthorizationFilter()
            .RequireRateLimiting("ai-stream")
            .WithName("GetSessionTrace")
            .WithSummary("Read the decision-traceability trace for a session (FR-A1-09)")
            .WithDescription("Projects the session ledger's stored ToolChain + Gate + ContextEnvelope-fingerprint markers (ADR-040) into the TraceEvent v1 read stream (request -> context slices -> tools -> gate/approval -> outcome). Read-only projection — no parallel store; the ledger remains the single source of truth. Rehydrates a decision-traceability view after a hard refresh, closing the client trace-buffer mount-gap. Identifiers/counts only (NFR-07). Returns an empty list for an unknown/expired session.")
            .Produces<IReadOnlyList<TraceEvent>>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(429);

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

        // FR-P2-06 (task 035): the Phase-2F plan/approve endpoint is DELETED with the
        // dispatcher stack. Nothing emitted its triggering SSE event after the FR-P2-05
        // cutover (task 034), so no pending plan could ever exist for it to resume.
        // ALL suspend/resume now flows through the unified gate-resolve endpoint below.

        // D12 / FR-P2-02 (spaarke-ai-architecture-redesign-r1 task 031): the R2-052
        // per-action HITL confirm endpoint is DELETED. It was the platform's SECOND
        // confirmation store — a stub with no server emitter for its trigger event and
        // no execution behind it. ALL side-effect confirmation now flows through the ONE
        // gate: PendingPlanManager (suspend/resume/reject + ledger Gate markers, ADR-040).

        // POST /api/ai/chat/sessions/{sessionId}/gates/{gateId}/resolve — resolve a
        // suspended invocation through the ONE gate (FR-P2-03 / task 032). The client
        // ActionConfirmationDialog rewires its confirm/cancel legs here (the presentation
        // of the unified gate). Component Justification (§11): /plan/approve resolves the
        // PLAN-shaped session-singleton entry only; no surface resolved generalized
        // PendingInvocations — without this route the dialog's Confirm has no server path
        // and the loop-boundary suspensions (task 034) have no resume surface.
        group.MapPost("/sessions/{sessionId}/gates/{gateId}/resolve", ResolveGateAsync)
            .AddAiAuthorizationFilter()
            .RequireRateLimiting("ai-stream")
            .WithName("ResolveGate")
            .WithSummary("Confirm or reject a suspended invocation in the unified confirmation gate")
            .WithDescription(
                "Resolves a pending gate entry (PendingPlanManager unified store). approved=true " +
                "resumes the suspended invocation — Binding-backed invocations execute via the " +
                "SessionDispatchOrchestrator dispatch seam (ledger write before render, ADR-040) " +
                "and the result summary is returned as JSON. approved=false rejects it. " +
                "Returns 409 when the gate is expired or already resolved (double-click protection).")
            .Produces<GateResolveResult>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(409)
            .ProducesProblem(422)
            .ProducesProblem(429);

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

        // FR-P2-06 (task 035): the FR-50 /api/ai/playbook-dispatch/execute endpoint is
        // DELETED with the dispatcher stack. Its triggering SSE event stopped being
        // emitted at the FR-P2-05 cutover (task 034), leaving it a dead click leg.
        // Capability execution flows through the loop + the Binding dispatch seam
        // (SessionDispatchOrchestrator) — ADR-039: ONE dispatch protocol.

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
                detail: "Tenant ID not found in token claims (tid).");
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
    /// FR-P2-05 hard cutover (task 034): the chat text path is the agent-turn loop and
    /// nothing else (ADR-039 — one dispatch protocol). Every NL utterance enters
    /// <see cref="ISprkChatAgent.SendMessageAsync"/>; the former compound-intent, single-match
    /// auto-dispatch, and FR-49 file-aware-options pre-passes are DELETED (no chat NL
    /// utterance reaches a legacy dispatch mechanism). Write/communicate side effects gate at the
    /// loop's dispatch seam (FR-P2-02); mid-elicitation answers ride the loop (FR-P2-03);
    /// off-catalog utterances refuse via the no_match_handler Binding (FR-P2-04).
    /// </summary>
    private static async Task SendMessageAsync(
        string sessionId,
        ChatSendMessageRequest request,
        ChatSessionManager sessionManager,
        ChatHistoryManager historyManager,
        SprkChatAgentFactory agentFactory,
        PendingPlanManager pendingPlanManager,
        IChatClient chatClient,
        [FromServices] IMatterContextDetector matterContextDetector,
        [FromServices] IConversationHistorySanitizer conversationHistorySanitizer,
        [FromServices] CrossMatterSafetyTelemetry crossMatterTelemetry,
        [FromServices] AiTelemetry aiTelemetry,
        [FromServices] ISessionPersistenceService? sessionPersistence,
        // spaarkeai-assistant-enhancements-r4 task 021a (FR-04): the grounded follow-on proposer.
        // Runs ONE pass after the response to emit typed capability + question followups (replacing the
        // retired ungrounded free-string generator). Registered AddScoped in AnalysisServicesModule.
        [FromServices] AssistantSuggestionService suggestionService,
        HttpContext httpContext,
        ILogger<SprkChatAgentFactory> logger)
    {
        var cancellationToken = httpContext.RequestAborted;
        var response = httpContext.Response;
        var tenantId = ExtractTenantId(httpContext);

        if (string.IsNullOrEmpty(tenantId))
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "Tenant ID not found in token claims" }, cancellationToken);
            return;
        }

        // === FR-P4-05 per-tenant metering scope (task 054) ===
        // The text entry path's attribution scope: every meterable fact observed inside
        // this turn (loop tokens, executor tokens from in-turn capability dispatch,
        // capability invocations via BindingCapabilityTool) is dimensioned per
        // tenant/user/entry-path=text through the ambient AiMeteringContext.
        // Identifiers only (opaque AAD GUIDs) — NFR-07 / ADR-015.
        var meteringUserId = ExtractUserId(httpContext)?.ToString();
        using var meteringScope = AiMeteringContext.Begin(
            tenantId, meteringUserId, AiMeteringContext.EntryPathText);

        // Retrieve the existing session
        var session = await sessionManager.GetSessionAsync(tenantId, sessionId, cancellationToken);
        if (session is null)
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            await response.WriteAsJsonAsync(new { error = $"Session {sessionId} not found" }, cancellationToken);
            return;
        }

        // === FR-07 attachment validation (task 050) ===
        // Validate BEFORE setting SSE headers so we can return a normal JSON 400 ProblemDetails
        // response (RFC 7807, ADR-019). Endpoint filters (AddAiAuthorizationFilter +
        // RequireRateLimiting per ADR-008) have already fired by the time this handler runs;
        // attachment validation is in-handler payload validation, complementary to those filters.
        var attachmentValidationError = ValidateAttachments(request.Attachments);
        if (attachmentValidationError is { } err)
        {
            response.StatusCode = err.statusCode;
            response.ContentType = "application/problem+json";
            await response.WriteAsJsonAsync(err.payload, cancellationToken);
            return;
        }

        // Compose the effective user-message text passed to the SINGLE LLM call (D-01).
        // When no attachments are present, this is request.Message verbatim. When present,
        // attachment text is appended as structured blocks. Used wherever the agent receives
        // the user message; the ORIGINAL request.Message is still persisted to history (FR-07
        // in-memory-only semantics — attachments are not stored in Dataverse).
        var effectiveMessage = ComposeMessageWithAttachments(request.Message, request.Attachments);

        // Set SSE headers — required for production-quality token-by-token streaming.
        // X-Accel-Buffering: no prevents nginx/YARP reverse proxy from buffering the SSE stream,
        // ensuring each token frame reaches the client immediately (NFR-01: first token < 500ms).
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";

        logger.LogInformation(
            "SendMessage: session={SessionId}, tenant={TenantId}, msgLen={MsgLen}, attachments={AttachmentCount}, attachmentChars={AttachmentChars}, document={DocumentId}, modelTierOverride={ModelTierOverride}",
            sessionId, tenantId, request.Message.Length,
            request.Attachments?.Count ?? 0,
            request.Attachments?.Sum(a => a.TextContent?.Length ?? 0) ?? 0,
            request.DocumentId ?? session.DocumentId,
            request.ModelTierOverride);

        // === AIPU2-028: Cross-Matter Conversation Safety (FR-408) ===
        // Before building the agent or AI history, detect whether the session has pivoted
        // from one matter to another.  If a pivot is detected, strip retrieved document
        // passages from the domain history and emit a matter_context_change SSE event so
        // the user is notified that prior document references are no longer available.
        var incomingMatterId = session.HostContext?.EntityType == "matter"
            ? session.HostContext.EntityId
            : string.Empty;

        var matterChange = matterContextDetector.DetectChange(session.Messages, incomingMatterId);
        if (matterChange is not null)
        {
            var sanitized = conversationHistorySanitizer.StripRetrievedContent(
                session.Messages,
                matterChange.ChangeDetectedAtTurnIndex);

            // Update the session in Redis with the sanitized history so subsequent
            // turns no longer see the stripped content (write-through pattern).
            if (sanitized.WasModified)
            {
                var sanitizedSession = session with { Messages = sanitized.Messages };
                await sessionManager.UpdateSessionCacheAsync(sanitizedSession, cancellationToken);
                session = sanitizedSession;
            }

            // Embed a new matter marker system message so future turns can detect the
            // current matter boundary correctly.
            var markerContent = MatterContextDetector.BuildMatterMarker(matterChange.NewMatterId);
            var markerMessage = new DvChatMessage(
                MessageId: Guid.NewGuid().ToString("N"),
                SessionId: sessionId,
                Role: ChatMessageRole.System,
                Content: markerContent,
                TokenCount: 0,
                CreatedAt: DateTimeOffset.UtcNow,
                SequenceNumber: session.Messages.Count + 1);
            session = await historyManager.AddMessageAsync(session, markerMessage, cancellationToken);

            // Emit matter_context_change SSE event before the turn is processed.
            // The client uses this to notify the user that prior document references are gone.
            var contextChangeData = new ChatSseMatterContextChangeData(
                PreviousMatterId: matterChange.PreviousMatterId,
                NewMatterId: matterChange.NewMatterId,
                Message: sanitized.NotificationMessage);

            await WriteChatSSEAsync(
                response,
                new ChatSseEvent("matter_context_change", null, contextChangeData),
                cancellationToken);

            // Emit OTEL counters (ADR-015: counts only, no matter IDs in metric labels).
            crossMatterTelemetry.RecordPivot(sanitized.WasModified);
            crossMatterTelemetry.RecordContentStripped(sanitized.RemovedDocumentCount);

            logger.LogInformation(
                "CrossMatterSafety: pivot handled for session={SessionId}, strippedMessages={StrippedCount}",
                sessionId, sanitized.RemovedDocumentCount);
        }
        else if (!string.IsNullOrEmpty(incomingMatterId))
        {
            // No pivot — but if no matter marker exists yet, embed one now so future
            // turns have a baseline to compare against.
            var hasMarker = session.Messages.Any(m =>
                m.Role == ChatMessageRole.System &&
                m.Content.Contains(MatterContextDetector.MatterMarkerPrefix, StringComparison.Ordinal));

            if (!hasMarker)
            {
                var markerContent = MatterContextDetector.BuildMatterMarker(incomingMatterId);
                var markerMessage = new DvChatMessage(
                    MessageId: Guid.NewGuid().ToString("N"),
                    SessionId: sessionId,
                    Role: ChatMessageRole.System,
                    Content: markerContent,
                    TokenCount: 0,
                    CreatedAt: DateTimeOffset.UtcNow,
                    SequenceNumber: session.Messages.Count + 1);
                session = await historyManager.AddMessageAsync(session, markerMessage, cancellationToken);
            }
        }
        // === End AIPU2-028 ===

        var fullResponse = new System.Text.StringBuilder();

        // R6 DEF-001 / task 095 Phase 3 — Resolve the per-request context_event SSE relay
        // up-front so the finally clause below can always clear Writer (even on exception
        // paths). The relay forwards typed context.* events emitted by ContextEventEmitter
        // (singleton) to the frontend ExecutionTraceWidget via "context_event" SSE frames.
        // When the AI services module is not registered (e.g., AI-OFF builds), GetService
        // returns null and the attach + finally are no-ops.
        var contextSseRelay = httpContext.RequestServices
            .GetService<Sprk.Bff.Api.Services.Ai.Telemetry.IContextSseRelay>();

        try
        {
            // Create SSE writer delegate for out-of-band events (progress, document_replace)
            var sseWriter = CreateSseWriter(response);

            // R6 DEF-001 / task 095 Phase 3 — Attach the context_event writer. The relay
            // serializes concurrent writes via SemaphoreSlim so frames don't interleave with
            // the token stream + citations/suggestions/done frames written through
            // WriteChatSSEAsync below. JsonNamingPolicy.CamelCase converts the DTO's
            // PascalCase properties to camelCase keys matching IChatSseEventData on the FE.
            if (contextSseRelay is not null)
            {
                contextSseRelay.Writer = (dto, ct) =>
                    WriteChatSSEAsync(response, new ChatSseEvent("context_event", null, dto), ct);
            }

            // === FR-P2-03 (task 032) — mid-elicitation deterministic turn routing =====
            // While an elicitation Gate is pending in the session ledger (ADR-040), an
            // incoming utterance is an ANSWER to the pending invocation unless it is a
            // hard-slash command or an explicit restart — deterministic string checks
            // only, never intent classification (ADR-039; walkthrough steps 10-12).
            // Answer turns ride the loop with a platform elicitation frame prepended to
            // the effective message (same composed-message pattern as attachments); the
            // PERSISTED history keeps request.Message verbatim.
            var elicitationTurn = await ResolveElicitationTurnAsync(
                session, request.Message, effectiveMessage,
                tenantId, sessionId, pendingPlanManager, logger, cancellationToken);
            // FR-P2-05 hard cutover: the ONLY surviving consumer of elicitation-turn
            // routing is the effective-message substitution below — an answer turn rides
            // the loop with the answer frame prepended (persisted history keeps the raw
            // user text). The legacy compound / dispatcher pre-passes that this flag used
            // to gate are deleted (see below); NO chat NL utterance reaches a legacy
            // dispatcher anymore — every turn enters the SprkChatAgent loop.
            var effectiveTurnMessage = elicitationTurn?.FramedMessage ?? effectiveMessage;
            // === End FR-P2-03 routing =================================================

            // === R2: Create the R2 SSE emitter for the six new event types.
            // Available to the response pipeline for duration of this request.
            // R1 events (token, done, error, etc.) continue to be emitted via WriteChatSSEAsync
            // — this emitter is purely additive and does not alter the R1 flow.
            var r2Emitter = CreateR2Emitter(sseWriter, logger);

            // === R3 task 011 (FR-03 re-point) — feed the workspace-state block from the LIVE tabs ===
            // The awareness block's tabs come from StoredSession.Tabs (written by the client via
            // PATCH /sessions/{id}/tabs → ISessionPersistenceService.SaveTabsAsync) — the live
            // source-of-record. This SUPERSEDES the runtime-inert IWorkspaceStateService read whose
            // write path was retired by AIR2-075. We load the same store GetTabs reads, then map the
            // StoredWorkspaceTab shape to the WorkspaceTab shape BuildWorkspaceStateBlock consumes.
            // Best-effort: a load failure degrades to the legacy IWorkspaceStateService path (null).
            IReadOnlyList<WorkspaceTab>? liveTabs = null;
            if (sessionPersistence is not null)
            {
                try
                {
                    var storedForTabs = await sessionPersistence.LoadSessionAsync(tenantId, sessionId, cancellationToken);
                    if (storedForTabs?.Tabs is { Count: > 0 })
                    {
                        liveTabs = MapStoredTabsToWorkspaceTabs(storedForTabs.Tabs, sessionId, tenantId);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "R3 task 011: failed to load live workspace tabs for the awareness block — sessionId={SessionId}, continuing without",
                        sessionId);
                }
            }

            // FR-04 (server half): map the client active-item handle {id,type,label} → the model
            // handle threaded into the single active-item slot. Only mapped when an id is present;
            // ADR-015 id-not-content — no content field is ever carried.
            WorkspaceActiveItemHandle? activeItem = null;
            if (!string.IsNullOrWhiteSpace(request.ActiveItem?.Id))
            {
                activeItem = new WorkspaceActiveItemHandle(
                    Id: request.ActiveItem!.Id!,
                    Type: request.ActiveItem.Type ?? string.Empty,
                    Label: request.ActiveItem.Label ?? string.Empty);
            }

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
                latestUserMessage: effectiveMessage,
                uploadedFiles: session.UploadedFiles,
                // F-1/F-2/F-7 envelope-convergence (D1): forward the session ledger so the provider performs
                // the ONE per-turn ContextEnvelope bind (fingerprint write, ADR-040) and consumes the bound
                // envelope for the interactive prompt. The endpoint's separate bind below is retired.
                ledgerOutputs: session.Outputs,
                // spaarkeai-compose-r2 "summarize this document" reinforcement: forward the active
                // document's session-file id so the Session Files manifest marks the active file and
                // names it the default target when the LLM omits fileIds. Deterministic scoping lives
                // in SessionDispatchOrchestrator.ResolveTargetFiles.
                activeSessionFileId: session.ActiveDocument?.SessionFileId,
                // ai-advanced-capabilities-nda-r1 task 011: the Assistant's runtime tier-picker
                // selection for this turn — forwarded to every projected capability tool so a
                // text-path dispatch composes it with the Binding's own tier override (ADR-039 — the
                // ONE resolver). Null (the default) is a no-op.
                modelTierOverride: request.ModelTierOverride,
                // spaarkeai-assistant-enhancements-r2 FR-A3: the client focus-stamp's tab id. When
                // present, BuildWorkspaceStateBlock labels the matching tab "(active)" in preference
                // to the UpdatedAt-most-recent heuristic. Null = no focus-stamp → UpdatedAt fallback
                // (backward compatible). Only the tab id is forwarded — the compact state is NOT
                // trusted as prompt content (ADR-015: server-derived visible state is authoritative).
                activeContextTabId: request.ActiveContext?.TabId,
                // R3 task 011 (FR-03 re-point): the live open tabs from StoredSession.Tabs — the
                // source-of-record that feeds the (now identity-only) workspace-state block.
                liveTabs: liveTabs,
                // R3 task 011 (FR-04): the {id,type,label} active-item handle → the ONE active-item
                // slot in the block (ADR-015 id-not-content — no content ever).
                activeItem: activeItem,
                cancellationToken: cancellationToken);

            // Convert session history to AI framework messages for context
            var history = BuildAiHistory(session.Messages);

            // === G-P2 UAT round-1 finding 3 (2026-07-06) — surface ledger outputs ====
            // Event/Click outputs (auto-classification, chip-dispatched summaries) live
            // in the session LEDGER (session.Outputs, ADR-040) and render client-side;
            // they never enter session.Messages, so without this block the loop cannot
            // see the summary the user is looking at and a follow-on transform
            // ("provide a more concise summary") degrades to a generic clarifying
            // question. Appended AFTER history, BEFORE the user turn: volatile content
            // rides the tail so the [system]+[history] prefix stays prompt-cache-stable
            // (NFR-04). Recent-window + per-output caps in ChatHistoryManager.
            var ledgerOutputsContext = ConversationContextProducer.BuildLedgerOutputsContext(session.Outputs);
            if (ledgerOutputsContext is not null)
            {
                var augmented = new List<AiChatMessage>(history.Count + 1);
                augmented.AddRange(history);
                augmented.Add(new AiChatMessage(ChatRole.System, ledgerOutputsContext));
                history = augmented;
            }
            // === End finding-3 ledger context =========================================

            // === F-1/F-2/F-7 envelope-convergence (D1) — interactive Context Binder convergence =====
            // The ONE per-turn ContextEnvelope bind now lives INSIDE the context provider
            // (PlaybookChatContextProvider.GetContextAsync, invoked via SprkChatAgentFactory.CreateAgentAsync
            // above), where it CONSUMES the bound envelope for the interactive prompt's host-identity
            // (Business), user-memory (User, F-2 recall), and record-memory sections and RENDERS the
            // environment date suffix from the same envelope — the direct producer-append sites retire on
            // the live path. This relocation keeps the fingerprint write (ADR-040 store-before-render,
            // NFR-07) exactly-once per turn; the earlier endpoint-side bind (which discarded the envelope)
            // is deleted so the turn is never double-bound. Byte-identical for existing sections; the
            // renderer-vs-legacy parity pins prove it before the legacy sites were made fallback-only.
            // === End interactive bind relocation ======================================

            // === FR-P2-05 HARD CUTOVER (task 034) — legacy dispatch pre-passes DELETED ===
            // Before the cutover, three pre-passes ran here between agent creation and the
            // loop stream (compound-intent detection, single-match auto-dispatch, and the
            // FR-49 file-aware options flow), each a route by which a chat NL utterance
            // could reach a legacy dispatch mechanism.
            //
            // ADR-039 mandates ONE dispatch protocol. Task 034 deleted the pre-passes and
            // task 035 (FR-P2-06) deleted the classifier stack behind them plus its click
            // legs (/plan/approve + /playbook-dispatch/execute) outright — no fallback
            // flag, no compat shim (NFR-08 hard-cutover doctrine). The agent-turn loop
            // (SprkChatAgent, FR-P2-01) is the SOLE text-path dispatcher; every chat NL
            // utterance flows straight into agent.SendMessageAsync below.
            //   - Gating: the loop's dispatch seam (SessionDispatchOrchestrator, via the
            //     projected BindingCapabilityTool) rejects non-informational dispositions
            //     pre-run, so no ungated write/communicate side effect is reachable through
            //     the loop (task 030 §Integration); write-shaped capabilities suspend into
            //     the ONE confirmation gate (PendingPlanManager, FR-P2-02) at that seam.
            //   - Elicitation: mid-elicitation answer turns still ride the loop via
            //     effectiveTurnMessage (the answer frame — resolved above, FR-P2-03).
            //   - Refusal: off-catalog utterances are the loop invoking the no_match_handler
            //     Binding (RefusalCapabilityTool, FR-P2-04) — an honest refusal, not a
            //     silent legacy no-match fall-through.

            // Emit typing_start immediately before the first AI token to signal the frontend
            // to show a typing indicator animation (NFR-01: first token < 500ms).
            await WriteChatSSEAsync(response, new ChatSseEvent("typing_start", null), cancellationToken);

            // === FR-P2-01 step 5 — ToolChain ledger persistence (ADR-040) ============
            // The turn's tool-call chain (recorded on the agent's AgentTurnContract by
            // the BudgetedAIFunction wrappers) is written to the session ledger BEFORE
            // the content that follows it renders (storage precedes rendering, D2/D8).
            // Tool phases and text phases can interleave within one turn; each text
            // segment is preceded by a flush of the calls accumulated since the last
            // flush — a turn with interleaved phases appends multiple chain segments
            // under the same turn ordinal (append-only; ADR-040). NFR-07: entries carry
            // identifiers/filters/counts only, enforced at recording time.
            var turnContract = agent.TurnContract;
            var toolChainTurn = (session.ToolChains is { Count: > 0 }
                ? session.ToolChains.Max(tc => tc.Turn) : 0) + 1;

            async Task FlushToolChainLedgerAsync()
            {
                if (turnContract is null || !turnContract.HasUnpersistedCalls)
                {
                    return;
                }

                var segment = turnContract.DrainUnpersistedCalls();
                if (segment.Count == 0)
                {
                    return;
                }

                // FR-P4-05 metering: one ai.metering.tool_calls increment per executed
                // call in the segment (tenant/user/tool.id dimensions; counts only, NFR-07).
                foreach (var meteredCall in segment)
                {
                    aiTelemetry.RecordMeteredToolCall(tenantId, meteringUserId, meteredCall.ToolId);
                }

                await sessionManager.AppendToolChainAsync(
                    tenantId,
                    sessionId,
                    new SessionToolChain
                    {
                        Turn = toolChainTurn,
                        Calls = segment,
                        CreatedAt = DateTimeOffset.UtcNow,
                    },
                    CancellationToken.None); // ledger write completes even if the client disconnects mid-render

                // === FR-P3-07 (task 046) — ExecutionTraceWidget ledger bridge =========
                // Emit the JUST-PERSISTED ToolChain segment as a `context_event` SSE
                // frame (discriminant "tool_chain") so the trace widget renders the
                // REAL ledger records instead of the legacy live-telemetry trace
                // source. Ordering is load-bearing: the AppendToolChainAsync ledger
                // write above completed BEFORE this frame renders (ADR-040 storage-
                // precedes-rendering). NFR-07: identifiers/filters/counts only —
                // citations projected as a count; args summaries were redacted at
                // recording time by AgentTurnContract.SummarizeArguments.
                await WriteChatSSEAsync(
                    response,
                    new ChatSseEvent("context_event", null, new Services.Ai.Telemetry.ContextSseEventDto
                    {
                        ContextEventType = "tool_chain",
                        ContextTimestamp = DateTimeOffset.UtcNow.ToString("o"),
                        ContextTurn = toolChainTurn,
                        ContextToolChainCalls = segment
                            .Select(c => new Services.Ai.Telemetry.ContextToolChainCallDto
                            {
                                ToolId = c.ToolId,
                                ArgsSummary = c.ArgsSummary,
                                ResultCount = c.ResultCount,
                                CitationCount = c.Citations?.Count,
                                DurationMs = c.DurationMs,
                            })
                            .ToArray(),
                    }),
                    cancellationToken);
            }
            // === End FR-P2-01 step 5 setup ============================================

            // Stream the agent response via IAsyncEnumerable<ChatResponseUpdate>.
            // FR-07 (task 050): effectiveMessage contains the user's typed text PLUS any
            // attachment context — this is the SINGLE LLM call that produces the response
            // (D-01 invariant). The agent receives one composed message; no second extraction
            // or summarization LLM call is introduced.
            // FR-P2-03: effectiveTurnMessage carries the elicitation answer frame on
            // mid-elicitation turns; otherwise it IS effectiveMessage.
            // FR-P4-05: harvest model-reported usage (UsageContent rides the final
            // streaming update — the OpenAI streaming pipeline requests include_usage)
            // for the per-tenant ai.metering.tokens counter. Counts only (NFR-07).
            long meteredInputTokens = 0;
            long meteredOutputTokens = 0;
            await foreach (var update in agent.SendMessageAsync(effectiveTurnMessage, history, cancellationToken))
            {
                foreach (var updateContent in update.Contents)
                {
                    if (updateContent is UsageContent usageContent)
                    {
                        meteredInputTokens += usageContent.Details.InputTokenCount ?? 0;
                        meteredOutputTokens += usageContent.Details.OutputTokenCount ?? 0;
                    }
                }

                var content = update.Text;
                if (!string.IsNullOrEmpty(content))
                {
                    // ADR-040: ledger write BEFORE this content renders.
                    await FlushToolChainLedgerAsync();
                    fullResponse.Append(content);
                    await WriteChatSSEAsync(response, new ChatSseEvent("token", content), cancellationToken);
                }
            }

            // Trailing flush: tool calls with no following text (e.g. budget-ended turns)
            // still persist their chain before the citations/done frames render.
            await FlushToolChainLedgerAsync();

            if (turnContract is not null && (turnContract.BudgetSpent > 0 || turnContract.DeniedCalls > 0))
            {
                // ADR-016 per-turn budget telemetry (NFR-09): counts + identifiers only.
                logger.LogInformation(
                    "[ADR-016][agent-turn.summary] session={SessionId} turn={Turn} budgetSpent={BudgetSpent}/{Budget} denied={Denied}",
                    sessionId, toolChainTurn, turnContract.BudgetSpent,
                    turnContract.ToolCallBudget, turnContract.DeniedCalls);
            }

            // === FR-P4-05 per-tenant metering (task 054) =============================
            // One ai.metering.turns increment per completed loop turn, carrying the
            // ADR-016/NFR-09 consumed-vs-cap dimensions; plus the turn's model-reported
            // token usage as ai.metering.tokens (source=loop). Counts only (NFR-07).
            aiTelemetry.RecordMeteredTurn(
                tenantId,
                meteringUserId,
                toolBudgetSpent: turnContract?.BudgetSpent ?? 0,
                toolBudgetCap: turnContract?.ToolCallBudget ?? 0,
                toolBudgetDenied: turnContract?.DeniedCalls ?? 0);
            aiTelemetry.RecordMeteredTokens(
                tenantId, meteringUserId, meteredInputTokens, meteredOutputTokens,
                source: "loop", entryPath: AiMeteringContext.EntryPathText);
            // === End FR-P4-05 metering ===============================================

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

            // === FR-04 (spaarkeai-assistant-enhancements-r4 task 021a): ONE predictable grounded
            // followups pass per turn =================================================================
            // Retires the ungrounded free-string generator (it fed the model NO capability menu — only
            // the last message + 500 chars — so it could not make a backed suggestion even in principle;
            // that is the P2 dead-end) AND the three hidden skips that made cadence feel random
            // (keyword-hijack mutual-exclusion, the <150-char skip, the separate proactive trigger).
            // One pass now merges two typed sources into ONE typed "suggestions" event:
            //   (1) the deterministic missing-context action chips (upload/browse/select) — kept as-is,
            //       now a typed 'action' kind (the "[action:*]" string encoding is retired); and
            //   (2) the grounded proposer's typed two-kind followups — 'capability' chips carrying a
            //       real, model-SELECTED targetBindingId (dispatched via the Click path; a dead-end is
            //       structurally impossible) + 'question' chips that re-enter the grounded loop.
            // Emitted ONLY when non-empty, so absence is meaningful ("nothing relevant"), never a hidden
            // skip. Best-effort (ADR-019): a proposer failure/timeout silently yields no followups and
            // never breaks the turn.
            var effectiveDocumentId = request.DocumentId ?? session.DocumentId;

            // (1) Deterministic missing-context action chips (AIPU-058) — fire only when no document is
            //     loaded AND the AI asked for one. Folding these into the grounded menu is a deferred
            //     phase-2; for now they stay deterministic, re-typed as 'action'.
            var missingContextActionChips = BuildMissingContextActionChips(effectiveDocumentId, fullResponse.ToString());

            // (2) The grounded proposer over the conversation tail + the context-scoped candidate menu +
            //     the active tab's server-derived content. Runs on EVERY turn (no length gate) — cadence
            //     is structural. Bounded by a timeout; a failure/timeout silently yields none.
            //     FR-07 (task 050) note preserved: pass request.Message (NOT the augmented attachment
            //     blob) — the followups reason about the user's conceptual question.
            IReadOnlyList<SuggestedFollowup> grounded = Array.Empty<SuggestedFollowup>();
            try
            {
                using var followupsTimeoutCts = new CancellationTokenSource(FollowupsTimeoutMs);
                using var followupsLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, followupsTimeoutCts.Token);

                var openTabContextTypes = liveTabs is null
                    ? (IReadOnlyCollection<string>)Array.Empty<string>()
                    : WidgetContextTypeResolver.ResolveOpenTabContextTypes(liveTabs);

                grounded = await suggestionService.SuggestForConversationAsync(
                    sessionId,
                    tenantId,
                    request.Message,
                    fullResponse.ToString(),
                    request.ActiveContext?.ContextType,
                    request.ActiveContext?.TabId,
                    openTabContextTypes,
                    followupsLinkedCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    "Grounded followups timed out ({TimeoutMs}ms) for session={SessionId}; skipping",
                    FollowupsTimeoutMs, sessionId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Grounded followups failed for session={SessionId}; skipping", sessionId);
            }

            // Merge the two typed sources into ONE ordered list (action → capability → question; a
            // capability with no binding is dropped) — the §9a wire contract. Extracted to the testable
            // BuildTypedFollowups (task 024 / FR-10, exercised via InternalsVisibleTo) so the "the wire is
            // the typed two-kind shape, never an untyped free string" guarantee has a direct regression guard.
            var followups = BuildTypedFollowups(missingContextActionChips, grounded);

            if (followups.Count > 0)
            {
                await WriteChatSSEAsync(
                    response,
                    new ChatSseEvent("suggestions", null, new ChatSseSuggestionsData(followups)),
                    cancellationToken);
                logger.LogDebug(
                    "Emitted {FollowupCount} typed followups for session={SessionId}",
                    followups.Count, sessionId);
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

            // task 064 (ADR-040 Path A, spec §13.5 / FR-22): the sprk_chathistory per-turn write to
            // sprk_analysis was removed here — task 062/064's hand-trace confirmed the ONLY reader
            // (AnalysisDocumentLoader.GetOrReloadFromDataverseAsync, feeding GET/save/export) no
            // longer reads that column, so this write became provably dead. Cosmos (via
            // historyManager.AddMessageAsync above) is the sole transcript store-of-record; analysis
            // sessions remain discoverable via GET /api/ai/chat/sessions/by-analysis/{id} (task 031).
            // See notes/task-064-chathistory-read-drop.md.
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — clean close without typing_end or error event.
            // The client is already gone so there is no receiver for further frames.
            logger.LogInformation(
                "Client disconnected during SendMessage: session={SessionId}", sessionId);
        }
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 3 (D-09 §2 B2/B3): NullSprkChatAgentFactory or
            // NullPendingPlanManager surfaced. Response is already committed as text/event-stream
            // — emit the error as an SSE 'error' chunk with the stable errorCode so the client
            // can render kill-switch-specific UX. Mirrors the WorkspaceMatterEndpoints HandleAiSummary
            // pattern established in Tier 2.
            logger.LogDebug(
                "SendMessage called while AI chat feature disabled. ErrorCode={ErrorCode}, Session={SessionId}",
                ex.ErrorCode, sessionId);
            if (!cancellationToken.IsCancellationRequested)
            {
                await WriteChatSSEAsync(response, new ChatSseEvent("typing_end", null), CancellationToken.None);
                await WriteChatSSEAsync(
                    response,
                    new ChatSseEvent("error", $"[{ex.ErrorCode}] {ex.Message}"),
                    CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during SendMessage: session={SessionId}", sessionId);

            if (!cancellationToken.IsCancellationRequested)
            {
                // Emit typing_end before the error event so the frontend stops the typing animation.
                await WriteChatSSEAsync(response, new ChatSseEvent("typing_end", null), CancellationToken.None);
                // G-P3 UAT round-1 H3 (ADR-019): server errors map to a STABLE, safe message
                // with a stable errorCode — the raw exception (previously interpolated here)
                // rendered upstream-provider internals (tools[N] indexes, exception type +
                // message) verbatim into the operator's transcript. Detail is logged
                // server-side only (LogError above); the client sees the code + safe copy.
                await WriteChatSSEAsync(response, BuildTurnFailedErrorEvent(), CancellationToken.None);
            }
        }
        finally
        {
            // R6 DEF-001 / task 095 Phase 3 — Detach the context_event writer so any
            // late-arriving emissions from background continuations (or the singleton
            // emitter reused on a parallel request) cannot write to a disposed response.
            if (contextSseRelay is not null)
            {
                contextSseRelay.Writer = null;
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
            await response.WriteAsJsonAsync(new { error = "Tenant ID not found in token claims" }, cancellationToken);
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
            // Uses the typed TextRefinementHandler's shared prompt builder (FR-P2-07 —
            // the legacy chat-tool class was deleted; the handler owns the refine prompt),
            // then streams directly rather than collecting the full response first.
            var messages = Services.Ai.Handlers.TextRefinementHandler.BuildRefineMessages(
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
    ///
    /// FR-D3: returns 404 for a genuinely-missing session so the client's stale-session
    /// recovery fires, instead of a silent blank 200. <see cref="ChatHistoryManager.GetHistoryAsync"/>
    /// itself collapses "missing" and "exists-but-empty" to the same empty array (it exists to serve
    /// the hot message-read path, not existence checks), so the existence check is done here via
    /// <see cref="ChatSessionManager.GetSessionAsync"/> — the same session-load path (Redis hot →
    /// Cosmos warm → Dataverse cold, ADR-040) that <c>DeleteSessionAsync</c>/<c>SwitchContextAsync</c>/
    /// <c>GetComposeOutputsAsync</c> already use for their 404 checks. It returns <c>null</c> ONLY when
    /// the session is absent from all three tiers — an existing session with zero messages still
    /// returns a non-null <see cref="ChatSession"/> (empty <c>Messages</c> list), so it is not
    /// ambiguous with "missing" and correctly stays 200.
    /// </summary>
    private static async Task<IResult> GetHistoryAsync(
        string sessionId,
        ChatHistoryManager historyManager,
        ChatSessionManager sessionManager,
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
                detail: "Tenant ID not found in token claims (tid).");
        }

        logger.LogDebug(
            "GetHistory: session={SessionId}, tenant={TenantId}", sessionId, tenantId);

        // Existence check (FR-D3) — mirrors the 404-on-missing pattern used by
        // DeleteSessionAsync/SwitchContextAsync/GetComposeOutputsAsync.
        var session = await sessionManager.GetSessionAsync(tenantId, sessionId, cancellationToken);
        if (session is null)
        {
            return Results.Problem(
                statusCode: 404,
                title: "Not Found",
                detail: $"Session '{sessionId}' not found.");
        }

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
                detail: "Tenant ID not found in token claims (tid).");
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
    /// Rename a chat session's stored title (FR-D4, task 032).
    /// PATCH /api/ai/chat/sessions/{sessionId}
    ///
    /// Mirrors the 404-on-missing pattern used by GetHistoryAsync/SwitchContextAsync/
    /// DeleteSessionAsync (<see cref="ChatSessionManager.GetSessionAsync"/> — Redis hot ->
    /// Cosmos warm -> Dataverse cold). The new title persists via the same
    /// <see cref="ChatSessionManager.UpdateSessionCacheAsync"/> write-through
    /// <see cref="SwitchContextAsync"/> uses (StoredSession.Title, ADR-040 — no new store).
    /// </summary>
    private static async Task<IResult> RenameSessionAsync(
        string sessionId,
        ChatRenameSessionRequest request,
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
                detail: "Tenant ID not found in token claims (tid).");
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Validation Error",
                detail: "Title must not be empty.");
        }

        var trimmedTitle = request.Title.Trim();
        if (trimmedTitle.Length > ChatRenameSessionRequest.MaxTitleLength)
        {
            return Results.Problem(
                statusCode: 400,
                title: "Validation Error",
                detail: $"Title cannot exceed {ChatRenameSessionRequest.MaxTitleLength} characters. Received {trimmedTitle.Length}.");
        }

        var session = await sessionManager.GetSessionAsync(tenantId, sessionId, cancellationToken);
        if (session is null)
        {
            return Results.Problem(
                statusCode: 404,
                title: "Not Found",
                detail: $"Session '{sessionId}' not found.");
        }

        var updatedSession = session with
        {
            Title = trimmedTitle,
            LastActivity = DateTimeOffset.UtcNow
        };

        await sessionManager.UpdateSessionCacheAsync(updatedSession, cancellationToken);

        logger.LogInformation("Session {SessionId} renamed (tenant={TenantId})", sessionId, tenantId);
        return Results.NoContent();
    }

    /// <summary>
    /// Delete a chat session.
    /// DELETE /api/ai/chat/sessions/{sessionId}
    /// </summary>
    /// <remarks>
    /// <c>internal</c> rather than <c>private</c> so the FR-B06 contract — an unconfirmed durable
    /// erasure is a 500 with a stable errorCode, never a 204 — is asserted against THIS handler rather
    /// than against a re-implementation of its branch. No test host maps this route today, and standing
    /// one up for a two-line decision would cost far more surface than it proves.
    /// </remarks>
    internal static async Task<IResult> DeleteSessionAsync(
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
                detail: "Tenant ID not found in token claims (tid).");
        }

        // Verify session exists before deleting (returns 404 if not found)
        var session = await sessionManager.GetSessionAsync(tenantId, sessionId, cancellationToken);
        if (session is null)
        {
            return Results.NotFound(new { error = $"Session {sessionId} not found" });
        }

        logger.LogInformation(
            "DeleteSession: session={SessionId}, tenant={TenantId}", sessionId, tenantId);

        var erasure = await sessionManager.DeleteSessionAsync(tenantId, sessionId, cancellationToken);

        // spaarkeai-compose-r8 FR-B06 (task 063). A 204 here is a statement that the session and its
        // uploaded files are gone. When the durable byte erasure could not be confirmed, that statement
        // is false — and it is the kind of false that nothing downstream would ever contradict, because
        // the manifest and the UI entry are the very things that would have shown the gap. So the
        // deletion fails closed: the session record is intact (DeleteSessionAsync returned before
        // touching it), the user still sees the conversation, and re-issuing this DELETE completes the
        // erasure — SessionFileEraser enumerates the blob prefix and needs no manifest to find residue.
        if (erasure.State == SessionFileErasureState.Incomplete)
        {
            logger.LogError(
                "DeleteSession REFUSED for session={SessionId}, tenant={TenantId}: durable file bytes " +
                "could not be confirmed erased (reason={Reason}). The session was not deleted.",
                sessionId, tenantId, erasure.Reason);

            // ADR-019: stable errorCode + correlationId so a client can tell THIS 500 from any other on
            // this route, and so the response and the log line can be joined.
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "The session's stored files could not be confirmed deleted, so the session was " +
                        "not deleted. Nothing was partially removed from your history. Please try again.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = DurableErasureIncompleteErrorCode,
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        logger.LogInformation(
            "Session deleted: {SessionId} (durableErasure={State}, blobsDeleted={Deleted})",
            sessionId, erasure.State, erasure.BlobsDeleted);

        return Results.NoContent();
    }

    /// <summary>
    /// ADR-019 stable errorCode for "the session's durable file bytes could not be confirmed erased,
    /// so the session was NOT deleted" (spaarkeai-compose-r8 FR-B06). Distinct from
    /// <c>session.durable-store-failed</c> (task 060, the upload-side write failure): this one tells a
    /// client that nothing was removed and that retrying the same DELETE is both safe and meaningful.
    /// </summary>
    internal const string DurableErasureIncompleteErrorCode = "session.durable-erasure-incomplete";

    /// <summary>
    /// GET /api/ai/chat/sessions/{sessionId}/compose-outputs
    ///
    /// FR-04 render-follows-store READ HALF (spaarkeai-compose-r2 task 016 HOOK #1). Projects the
    /// session ledger's <c>compose</c>-disposition <see cref="SessionOutput"/> entries (ADR-040:
    /// storage precedes rendering — the client re-reads durable ledger state, never a client
    /// buffer). ComposeWorkspace materializes the current draft into the TipTap editor from this
    /// projection. Returns an empty list until a compose Binding writes an output (the WRITE half
    /// — <c>BindingDisposition.Compose</c> + the OutputRouter case — is core
    /// spaarke-ai-architecture-redesign-r2 task 010).
    /// </summary>
    private static async Task<IResult> GetComposeOutputsAsync(
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
                detail: "Tenant ID not found in token claims (tid).");
        }

        var session = await sessionManager.GetSessionAsync(tenantId, sessionId, cancellationToken);
        if (session is null)
        {
            return Results.NotFound(new { error = $"Session {sessionId} not found" });
        }

        var outputs = ProjectComposeOutputs(session.Outputs);

        // NFR-07: identifiers + count only — never payload content.
        logger.LogDebug(
            "GetComposeOutputs: session={SessionId}, tenant={TenantId}, composeOutputs={Count}",
            sessionId, tenantId, outputs.Count);

        return Results.Ok(outputs);
    }

    /// <summary>
    /// Projects a session ledger's <c>compose</c>-disposition outputs to the client DTO, skipping
    /// ADR-040 truncation markers — a truncated compose payload cannot be materialized (the
    /// store-before-render consumer fails loud downstream), so a partial draft is omitted rather
    /// than shipped. Pure + <c>internal</c> for unit testing (task 016 HOOK #1).
    /// </summary>
    internal static IReadOnlyList<ComposeLedgerOutputDto> ProjectComposeOutputs(IReadOnlyList<SessionOutput>? ledger)
    {
        if (ledger is null || ledger.Count == 0)
        {
            return Array.Empty<ComposeLedgerOutputDto>();
        }

        var result = new List<ComposeLedgerOutputDto>();
        foreach (var output in ledger)
        {
            if (!string.Equals(output.Disposition, ComposeDisposition.DispositionValue, StringComparison.Ordinal))
            {
                continue;
            }
            if (SessionLedger.IsTruncationMarker(output.Payload))
            {
                continue;
            }
            result.Add(new ComposeLedgerOutputDto(
                output.Key, output.BindingId, output.Turn, output.Disposition, output.Payload));
        }
        return result;
    }

    /// <summary>
    /// POST /api/ai/chat/sessions/{sessionId}/compose-outputs/supersede
    ///
    /// FR-17 undo/replace via ledger supersession (spaarkeai-compose-r2 task 034). Retracts a prior
    /// <c>compose</c> draft by appending a NEW superseding <c>compose</c> <see cref="SessionOutput"/>
    /// (ADR-040: append-only — corrections are new entries referencing the superseded key). This is
    /// the durable-across-refresh half that a client-only mark-strip (a DOM undo) cannot provide:
    /// the retraction becomes the highest-turn compose entry, so a reload re-materializes from
    /// current ledger state with the prior suggestion gone (HANDOFF §1 item 5). Idempotent — a ref
    /// already superseded (or a non-existent ref) is a no-op / honest 404.
    /// </summary>
    private static async Task<IResult> SupersedeComposeOutputAsync(
        string sessionId,
        ComposeSupersedeRequest request,
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
                detail: "Tenant ID not found in token claims (tid).");
        }

        if (request is null || string.IsNullOrWhiteSpace(request.SupersedesRef))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "supersedesRef is required (the {bindingId}@t{n} key of the compose output to supersede).");
        }

        var session = await sessionManager.GetSessionAsync(tenantId, sessionId, cancellationToken);
        if (session is null)
        {
            return Results.NotFound(new { error = $"Session {sessionId} not found" });
        }

        var result = SupersedeComposeOutput(session.Outputs, request.SupersedesRef);
        switch (result.Outcome)
        {
            case ComposeSupersedeOutcome.NotFound:
                // Honest failure — no compose entry addressable at that ref.
                return Results.NotFound(new
                {
                    error = $"No compose output '{request.SupersedesRef}' found in session {sessionId}.",
                });

            case ComposeSupersedeOutcome.NoOp:
                // Idempotent: the ref was already superseded (or is itself a retraction). No write.
                logger.LogInformation(
                    "SupersedeComposeOutput NOOP (already superseded): session={SessionId} ref={Ref} current={Current}",
                    sessionId, request.SupersedesRef, result.CurrentKey);
                return Results.Ok(new ComposeSupersedeResponse(
                    result.CurrentKey!, request.SupersedesRef, ComposeSupersedeResponse.OutcomeNoop));

            case ComposeSupersedeOutcome.Superseded:
            default:
                // Durable ledger write (same seam OutputRouter uses — ADR-040 store-precedes-render).
                await sessionManager
                    .UpdateSessionCacheAsync(session with { Outputs = result.Outputs }, cancellationToken)
                    .ConfigureAwait(false);
                logger.LogInformation(
                    "SupersedeComposeOutput: session={SessionId} superseded={Ref} newKey={NewKey} turn={Turn}",
                    sessionId, request.SupersedesRef, result.NewEntry!.Key, result.NewEntry.Turn);
                return Results.Ok(new ComposeSupersedeResponse(
                    result.NewEntry!.Key, request.SupersedesRef, ComposeSupersedeResponse.OutcomeSuperseded));
        }
    }

    /// <summary>Outcome of <see cref="SupersedeComposeOutput"/>.</summary>
    internal enum ComposeSupersedeOutcome
    {
        /// <summary>No compose entry is addressable at the supplied ref — honest failure (404).</summary>
        NotFound,
        /// <summary>The ref is already superseded (not the head) or is itself a retraction — idempotent no-op.</summary>
        NoOp,
        /// <summary>A new superseding retraction entry was appended.</summary>
        Superseded,
    }

    /// <summary>Result of the pure supersession computation (no I/O — unit-testable like <see cref="ProjectComposeOutputs"/>).</summary>
    internal readonly record struct ComposeSupersedeResult(
        ComposeSupersedeOutcome Outcome,
        IReadOnlyList<SessionOutput> Outputs,
        SessionOutput? NewEntry,
        string? CurrentKey);

    /// <summary>
    /// Pure supersession computation over a session ledger (FR-17). Given the <c>{bindingId}@t{n}</c>
    /// key of a prior <c>compose</c> output, returns the appended superseding retraction entry (a new
    /// highest-turn <c>compose</c> output whose empty payload re-materializes to NOTHING), OR an
    /// idempotent no-op when the ref is already superseded / itself a retraction, OR not-found.
    /// Consumes the published supersession semantics (<see cref="ComposeDisposition.ResolveCurrent"/> —
    /// the head is the highest-turn compose entry). <c>internal</c> + pure for direct unit testing.
    /// </summary>
    internal static ComposeSupersedeResult SupersedeComposeOutput(
        IReadOnlyList<SessionOutput>? ledger,
        string supersedesRef)
    {
        var outputs = ledger ?? Array.Empty<SessionOutput>();

        // Locate the referenced compose entry (must be an addressable compose output).
        SessionOutput? prior = null;
        foreach (var o in outputs)
        {
            if (string.Equals(o.Key, supersedesRef, StringComparison.Ordinal)
                && string.Equals(o.Disposition, ComposeDisposition.DispositionValue, StringComparison.Ordinal))
            {
                prior = o;
                break;
            }
        }

        if (prior is null)
        {
            return new ComposeSupersedeResult(ComposeSupersedeOutcome.NotFound, outputs, null, null);
        }

        // The CURRENT head for this binding is the highest-turn compose entry (published semantics).
        // prior exists ⇒ ResolveCurrent is non-null.
        var current = ComposeDisposition.ResolveCurrent(outputs, prior.BindingId)!;

        // Idempotent no-op: the ref is no longer the head (already superseded), or it is itself a
        // retraction marker — either way, appending another retraction would be a redundant write.
        if (!string.Equals(current.Key, supersedesRef, StringComparison.Ordinal) || IsComposeRetraction(prior))
        {
            return new ComposeSupersedeResult(ComposeSupersedeOutcome.NoOp, outputs, null, current.Key);
        }

        // Append the superseding retraction entry at turn = max+1 (same key algebra as OutputRouter).
        var turn = outputs.Max(o => o.Turn) + 1;
        var key = SessionLedger.BuildOutputKey(prior.BindingId, turn);
        var payload = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            [ComposeRetractionMarker] = true,
            ["supersedes_ref"] = supersedesRef,
        });
        var entry = new SessionOutput
        {
            Key = key,
            BindingId = prior.BindingId,
            UcId = prior.UcId,
            Turn = turn,
            Disposition = ComposeDisposition.DispositionValue,
            Payload = payload,
            // Provenance — the superseded key this entry corrects (ADR-040).
            SourceRefs = new[] { supersedesRef },
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var updated = new List<SessionOutput>(outputs) { entry };
        return new ComposeSupersedeResult(ComposeSupersedeOutcome.Superseded, updated, entry, key);
    }

    /// <summary>Sentinel property marking a compose output as an FR-17 retraction (empty edit; re-materializes to nothing).</summary>
    internal const string ComposeRetractionMarker = "retracted";

    /// <summary>True when <paramref name="output"/> is an FR-17 retraction entry (payload carries <c>"retracted": true</c>).</summary>
    internal static bool IsComposeRetraction(SessionOutput output) =>
        output.Payload.ValueKind == JsonValueKind.Object
        && output.Payload.TryGetProperty(ComposeRetractionMarker, out var flag)
        && flag.ValueKind == JsonValueKind.True;

    // The R2-052 per-action HITL confirm handler was DELETED by D12 / FR-P2-02 (task 031):
    // it was the second confirmation store's handler. Side-effect confirmation is unified
    // behind PendingPlanManager — see that type's doc for the suspend/resume contract.

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
            catch (FeatureDisabledException ex)
            {
                // Task 011 Phase 1b Tier 2 (D-09 §2 B6): NullPlaybookService surfaced. Fail-fast 503
                // — returning empty playbook list would silently render "no playbooks available"
                // and mask the kill-switch state.
                logger.LogDebug(
                    "Playbook list called while AI feature disabled. ErrorCode={ErrorCode}, UserId={UserId}",
                    ex.ErrorCode, userId);
                return ex.AsFeatureDisabled503();
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
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 2 (D-09 §2 B6): NullPlaybookService surfaced. Fail-fast 503.
            logger.LogDebug(
                "Public playbook list called while AI feature disabled. ErrorCode={ErrorCode}",
                ex.ErrorCode);
            return ex.AsFeatureDisabled503();
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
            // Diagnostic: log claim details for debugging. Task 059 dropped the X-Tenant-Id header from
            // this line — the header no longer participates in resolution, so reporting it here would
            // point whoever reads the log at a value that had no bearing on the outcome.
            var claims = httpContext.User.Claims.Select(c => $"{c.Type}={c.Value}").ToArray();
            logger.LogWarning(
                "GetContextMappings: tenant ID missing — " +
                "claimCount={ClaimCount}, claims=[{Claims}], entityType={EntityType}",
                claims.Length,
                claims.Length > 0 ? string.Join("; ", claims.Take(10)) : "(none)",
                entityType);

            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Tenant ID not found in token claims (tid).");
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
                detail: "Tenant ID not found in token claims (tid).");
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
                detail: "Tenant ID not found in token claims (tid).");
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

        IEnumerable<CommandEntry> commands;
        try
        {
            var resolver = agentFactory.CreateCommandResolver();
            commands = await resolver.ResolveCommandsAsync(tenantId, session.HostContext, cancellationToken);
        }
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 3 (D-09 §2 B2): NullSprkChatAgentFactory surfaced.
            logger.LogDebug(
                "GetCommands called while AI chat feature disabled. ErrorCode={ErrorCode}, Session={SessionId}",
                ex.ErrorCode, sessionId);
            return ex.AsFeatureDisabled503();
        }

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
    // Session Restore DTOs
    // =========================================================================

    /// <summary>
    /// DTO for recent session list items.
    ///
    /// FR-D7 (spaarkeai-assistant-enhancements-r2, DI-01) adds <see cref="Preview"/>,
    /// <see cref="MessageCount"/>, and <see cref="TabSummary"/> — property names chosen to match
    /// the client's `HistoryOverlay.tsx` `mapSession` reads EXACTLY under the default camelCase
    /// wire policy (`Preview` → `preview`, `MessageCount` → `messageCount`,
    /// `TabSummary` → `tabSummary`). All three are optional; the client already renders their
    /// absence gracefully (task 037 forward-compatible mapping).
    /// </summary>
    internal record RecentSessionDto(
        string Id,
        string Title,
        string? EntityType,
        string? EntityName,
        string? PlaybookName,
        DateTimeOffset UpdatedAt,
        string? Preview,
        int? MessageCount,
        string? TabSummary);

    /// <summary>
    /// GET /api/ai/chat/sessions — lists recent sessions for the current tenant.
    /// </summary>
    private static async Task<IResult> ListRecentSessionsAsync(
        HttpContext httpContext,
        ISessionPersistenceService persistenceService,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Tenant ID not found in token claims (tid).");
        }

        // R4-8 (UAT 2026-07-19): list the tenant's most-recent sessions from the Cosmos warm tier.
        // Returns a top-level JSON array (the History dropdown expects `Array.isArray`).
        var recent = await persistenceService.ListRecentSessionsAsync(tenantId, limit, cancellationToken);
        var sessions = recent
            .Select(s => new RecentSessionDto(
                Id: s.SessionId,
                Title: s.Title,
                EntityType: s.EntityType,
                EntityName: s.EntityName,
                PlaybookName: s.PlaybookName,
                UpdatedAt: s.UpdatedAt,
                Preview: s.Preview,
                MessageCount: s.MessageCount,
                TabSummary: s.TabSummary))
            .ToList();

        return Results.Ok(sessions);
    }

    /// <summary>
    /// Response payload for the session restore endpoint.
    /// Maps RestoredSession to a frontend-friendly JSON contract.
    /// </summary>
    internal record SessionRestoreResponse(
        string SessionId,
        Guid? PlaybookId,
        string Stage,
        IReadOnlyDictionary<string, string> WidgetStates,
        string? ConversationSummary,
        IReadOnlyList<SessionRestoreMessageDto> RecentMessages,
        bool HasStaleEntities,
        long RestoreLatencyMs,
        // spaarkeai-assistant-enhancements-r2 FR-D5: minimal uploaded-files manifest so the client
        // rehydrates the attachment chip on restore. Identifier/display fields only (ADR-015 Tier-2).
        IReadOnlyList<SessionRestoreUploadedFileDto> UploadedFiles);

    /// <summary>
    /// A single message in the restore response — minimal projection of SessionMessage.
    /// </summary>
    internal record SessionRestoreMessageDto(
        string Role,
        string Content,
        DateTimeOffset Timestamp);

    /// <summary>
    /// A single uploaded file in the restore response — minimal projection of the session manifest
    /// (FR-D5). Camel-cased on the wire by System.Text.Json
    /// (fileId/fileName/contentType/sizeBytes/contentAvailable), matching the client
    /// <c>SessionRestoreUploadedFile</c> shape in <c>useSessionRestore.ts</c>.
    /// </summary>
    /// <param name="FileId">Stable session-scoped file id.</param>
    /// <param name="FileName">Original upload file name (chip label).</param>
    /// <param name="ContentType">MIME content type as reported on upload.</param>
    /// <param name="SizeBytes">Original (uncompressed) file size in bytes.</param>
    /// <param name="ContentAvailable">
    /// spaarkeai-compose-r8 FR-B05 (task 062) — the server-authoritative availability fact that
    /// REPLACES R7's client-side ~24h heuristic. <c>true</c> = a durable byte copy exists, so the
    /// content lives as long as the session. <c>false</c> = the durable store is configured and holds
    /// no copy. <c>null</c> (omitted-by-shape when unknown) = the server cannot answer, and the client
    /// MUST render "unknown" rather than substituting a guess — two availability sources is the drift
    /// FR-B05 exists to remove. See <see cref="Services.Ai.Sessions.SessionFileAvailability"/>.
    /// </param>
    internal record SessionRestoreUploadedFileDto(
        string FileId,
        string FileName,
        string ContentType,
        long SizeBytes,
        bool? ContentAvailable);

    /// <summary>
    /// GET /api/ai/chat/sessions/{sessionId}/restore
    /// Restores a persisted session for the three-pane SpaarkeAi UI.
    /// </summary>
    private static async Task<IResult> RestoreSessionAsync(
        string sessionId,
        HttpContext httpContext,
        ISessionRestoreService restoreService,
        CancellationToken cancellationToken)
    {
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Tenant ID not found in token claims (tid).");
        }

        var restored = await restoreService.RestoreSessionAsync(tenantId, sessionId, cancellationToken);
        if (restored is null)
        {
            return Results.Problem(
                statusCode: 404,
                title: "Not Found",
                detail: $"Session '{sessionId}' not found.");
        }

        // Recent messages are now included in RestoredSession (single Cosmos read)
        var recentMessages = restored.RecentMessages
            .Select(m => new SessionRestoreMessageDto(m.Role, m.Content, m.Timestamp))
            .ToList();

        var stage = restored.WidgetStates.Count > 0 ? "active-chat" : "loading";

        // FR-D5: project the restored uploaded-files manifest to the wire DTO (already minimal — the
        // restore service dropped enriched fields). Empty list when the session had no attachments.
        var uploadedFiles = restored.UploadedFiles
            .Select(f => new SessionRestoreUploadedFileDto(
                f.FileId, f.FileName, f.ContentType, f.SizeBytes, f.ContentAvailable))
            .ToList();

        var response = new SessionRestoreResponse(
            SessionId: restored.SessionId,
            PlaybookId: restored.PlaybookId,
            Stage: stage,
            WidgetStates: restored.WidgetStates,
            ConversationSummary: restored.WasSummarized ? restored.ReconstructedContext : null,
            RecentMessages: recentMessages,
            HasStaleEntities: restored.StaleEntityRefs.Count > 0,
            RestoreLatencyMs: restored.RestoreLatencyMs,
            UploadedFiles: uploadedFiles);

        return Results.Ok(response);
    }

    // =========================================================================
    // Session-by-Analysis lookup (ai-advanced-capabilities-analysis-hub-r1 task 031, FR-11)
    // =========================================================================
    //
    // PLACEMENT JUSTIFICATION (CLAUDE.md §10 BFF Hygiene + ADR-013):
    //   Extends the EXISTING /api/ai/chat/sessions group in-process — no new DI module, no new
    //   package. Reuses IChatDataverseRepository.GetSessionsByAnalysisAsync (task 020, already
    //   registered by AnalysisServicesModule — symmetric registration, §F.1). Read-only; publish-size
    //   delta is code-only (~a few KB IL).
    //   Project CLAUDE.md MUST rule: "Standardize on ChatEndpoints (Redis→Cosmos); NEVER extend
    //   AnalysisEndpoints in-memory session model" — this lives on ChatEndpoints, not AnalysisEndpoints.

    /// <summary>
    /// GET /api/ai/chat/sessions/by-analysis/{analysisId}
    /// Resolves the chat session bound to an <c>sprk_analysis</c> record for the hub-grid reopen
    /// flow (FR-11). An Analysis may have accrued more than one bound session over its lifetime
    /// (fork-on-analysis, task 021); reopen targets the MOST RECENTLY CREATED one (archived or not
    /// — an archived session still holds its durable transcript, task 022, and reopening it is a
    /// legitimate "view history" action, not a resume-writes action).
    /// </summary>
    private static async Task<IResult> GetSessionByAnalysisAsync(
        Guid analysisId,
        HttpContext httpContext,
        IChatDataverseRepository dataverseRepository,
        CancellationToken cancellationToken)
    {
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Tenant ID not found in token claims (tid).");
        }

        var sessions = await dataverseRepository.GetSessionsByAnalysisAsync(tenantId, analysisId, cancellationToken);
        if (sessions.Count == 0)
        {
            return Results.Problem(
                statusCode: 404,
                title: "Not Found",
                detail: $"No chat session is bound to analysis '{analysisId}'.");
        }

        return Results.Ok(SelectMostRecentSession(sessions));
    }

    /// <summary>
    /// Picks the session to reopen when an <c>sprk_analysis</c> has accrued more than one bound
    /// session over its lifetime (fork-on-analysis, task 021): the MOST RECENTLY CREATED one wins,
    /// archived or not — an archived session still holds its durable transcript (task 022), and
    /// reopening it is a legitimate "view history" action, not a resume-writes action.
    ///
    /// Sessions with no <see cref="AnalysisSessionSummary.CreatedOn"/> (should not occur in
    /// practice — <c>CreateSessionAsync</c> always stamps it — but defensively sorted last so a
    /// missing timestamp never shadows a dated session) never win over a dated one.
    ///
    /// <c>internal</c> (not <c>private</c>) so this branchy selection rule is unit-testable directly
    /// via <c>InternalsVisibleTo</c> — no reflection into a private member (ADR-038 §7 / tests
    /// CLAUDE.md B8 ban).
    /// </summary>
    /// <param name="sessions">Non-empty session list for one Analysis (caller 404s on empty).</param>
    internal static AnalysisSessionSummary SelectMostRecentSession(IReadOnlyList<AnalysisSessionSummary> sessions) =>
        sessions.OrderByDescending(s => s.CreatedOn ?? DateTimeOffset.MinValue).First();

    // =========================================================================
    // Decision-traceability read surface (AIR2-038 / FR-A1-09 — D-F4)
    // =========================================================================
    //
    // PLACEMENT JUSTIFICATION (CLAUDE.md §10 BFF Hygiene + ADR-013 decision criteria):
    //   Q1 latency/TTFB budget against BFF state?         YES — projects the live session
    //      ledger the chat surface just wrote (<500ms restore budget, same as /restore).
    //   Q2 writes BFF session/audit state same lifecycle? read-only, but reads the SAME
    //      ledger/session state the request pipeline owns.
    //   Q3 retroactive annotation of a streaming response? YES — it is the read-half of the
    //      store-before-render trace the /messages SSE stream writes.
    //   Q4 event-driven with no synchronous user wait?     NO — it serves an interactive
    //      "how did you decide?" affordance.
    //   → All BFF answers → stays in the BFF, on the existing /api/ai/chat group.
    //   Facade (ADR-013): satellite/CRUD consumers reach the trace ONLY via
    //      ISessionTraceReader (PublicContracts) — the endpoint injects that facade and never
    //      exposes ledger internals (SessionToolChain/SessionGate/ChatSession) on the wire.
    //   No new store (ADR-040): pure projection over markers already on the session; reads are
    //      free (D-F0(b)); store-before-render is untouched (this is the render-side read).
    //   No new DI module (ADR-010): ISessionTraceReader registered next to ChatSessionManager in
    //      AnalysisServicesModule (both unconditional → symmetric registration, §F.1).
    //   No new package; publish-size delta is code-only (~a few KB IL).

    /// <summary>
    /// GET /api/ai/chat/sessions/{sessionId}/trace
    /// Reads the decision-traceability TraceEvent v1 stream for a session (AIR2-038 / FR-A1-09).
    /// Read-only projection over the ADR-040 ledger; returns an empty list for an unknown session.
    /// </summary>
    private static async Task<IResult> GetSessionTraceAsync(
        string sessionId,
        HttpContext httpContext,
        ISessionTraceReader traceReader,
        CancellationToken cancellationToken)
    {
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Tenant ID not found in token claims (tid).");
        }

        var trace = await traceReader.ReadTraceAsync(tenantId, sessionId, cancellationToken);
        return Results.Ok(trace);
    }

    // =========================================================================
    // Workspace tab persistence (NFR-09 — task 065)
    // =========================================================================
    //
    // PLACEMENT JUSTIFICATION (CLAUDE.md §10 BFF Hygiene + ADR-013):
    //   - Extends the existing /api/ai/chat session endpoint group in-process.
    //   - Uses the same ISessionPersistenceService that handles messages, widget states,
    //     and summaries — no new DI feature module (ADR-010).
    //   - Filter chain (.AddAiAuthorizationFilter().RequireRateLimiting("ai-stream"))
    //     matches the sibling /messages route (ADR-008).
    //   - Cosmos schema change is purely additive (StoredSession.Tabs, ActiveTabId)
    //     with /tenantId partition key unchanged (ADR-015).
    //   - All four BFF decision criteria from ADR-013 answer "BFF" → stays here.

    /// <summary>
    /// Defensive upper bound on incoming tab count. UI cap is MAX_WORKSPACE_TABS = 8 (FR-13),
    /// but we tolerate up to 50 in the payload to absorb FIFO eviction races and future cap
    /// adjustments without forcing a BFF redeploy.
    /// </summary>
    internal const int MaxTabsInRequest = 50;

    /// <summary>
    /// PATCH /api/ai/chat/sessions/{sessionId}/tabs
    /// Write-through workspace tab persistence (NFR-09).
    /// </summary>
    private static async Task<IResult> SaveTabsAsync(
        string sessionId,
        SessionTabsRequest request,
        ISessionPersistenceService persistence,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Ai.ChatEndpoints.SaveTabs");

        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Tenant ID not found in token claims (tid).");
        }

        if (request is null || request.Tabs is null)
        {
            return Results.Problem(
                statusCode: 400,
                title: "Validation Error",
                detail: "Request body must include a 'tabs' array (use [] for no tabs).");
        }

        if (request.Tabs.Count > MaxTabsInRequest)
        {
            return Results.Problem(
                statusCode: 400,
                title: "Validation Error",
                detail: $"Tabs payload cannot exceed {MaxTabsInRequest} entries. Received {request.Tabs.Count}.");
        }

        // Map wire DTOs to persistence DTOs. The wire shape (camelCase JsonElement) and the
        // persistence shape are identical fields-wise — this mapping is the explicit contract
        // boundary between the HTTP layer and the persistence layer.
        var storedTabs = new List<StoredWorkspaceTab>(request.Tabs.Count);
        foreach (var t in request.Tabs)
        {
            if (string.IsNullOrEmpty(t.Id) || string.IsNullOrEmpty(t.WidgetType))
            {
                return Results.Problem(
                    statusCode: 400,
                    title: "Validation Error",
                    detail: "Each tab must have a non-empty 'id' and 'widgetType'.");
            }
            storedTabs.Add(new StoredWorkspaceTab(
                Id: t.Id,
                WidgetType: t.WidgetType,
                WidgetData: t.WidgetData,
                DisplayName: t.DisplayName ?? string.Empty));
        }

        logger.LogDebug(
            "SaveTabs: session={SessionId}, tenant={TenantId}, tabCount={TabCount}, activeTabId={ActiveTabId}",
            sessionId, tenantId, storedTabs.Count, request.ActiveTabId ?? "(null)");

        var updated = await persistence.SaveTabsAsync(
            sessionId,
            tenantId,
            storedTabs,
            request.ActiveTabId,
            cancellationToken);

        if (!updated)
        {
            return Results.Problem(
                statusCode: 404,
                title: "Not Found",
                detail: $"Session '{sessionId}' not found.");
        }

        return Results.NoContent();
    }

    /// <summary>
    /// GET /api/ai/chat/sessions/{sessionId}/tabs
    /// Read persisted workspace tabs and active selection.
    /// </summary>
    private static async Task<IResult> GetTabsAsync(
        string sessionId,
        ISessionPersistenceService persistence,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Ai.ChatEndpoints.GetTabs");

        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Tenant ID not found in token claims (tid).");
        }

        var session = await persistence.LoadSessionAsync(tenantId, sessionId, cancellationToken);
        if (session is null)
        {
            return Results.Problem(
                statusCode: 404,
                title: "Not Found",
                detail: $"Session '{sessionId}' not found.");
        }

        logger.LogDebug(
            "GetTabs: session={SessionId}, tenant={TenantId}, tabCount={TabCount}",
            sessionId, tenantId, session.Tabs.Count);

        // Project persistence DTOs back to the wire shape. Identical field names, so this is
        // effectively a pass-through; we keep the explicit projection to make the wire contract
        // visible at the endpoint boundary (System.Text.Json camelCase via [JsonPropertyName]).
        var wireTabs = session.Tabs
            .Select(t => new SessionTabDto(t.Id, t.WidgetType, t.WidgetData, t.DisplayName))
            .ToList();

        return Results.Ok(new SessionTabsResponse(wireTabs, session.ActiveTabId));
    }

    /// <summary>
    /// R3 task 011 (FR-03 re-point) — map the LIVE persisted tab shape
    /// (<see cref="StoredWorkspaceTab"/>, written by <c>SaveTabsAsync</c>) onto the
    /// <see cref="WorkspaceTab"/> shape <c>SprkChatAgentFactory.BuildWorkspaceStateBlock</c>
    /// consumes for the (now identity-only) workspace-state prompt block.
    ///
    /// <para>
    /// Field mapping: <c>id → Id</c>, <c>widgetType → WidgetType</c>,
    /// <c>displayName → DisplayName</c> (the trimmed block's primary label source),
    /// <c>widgetData (JsonElement) → WidgetData</c> (leniently deserialized against the
    /// polymorphic <see cref="WorkspaceTabWidgetData"/> union — null when the opaque payload
    /// carries no recognized <c>kind</c>, e.g. layout tabs; the derivation tolerates null and
    /// the block still lists the tab by DisplayName). Synthetic ordering timestamps preserve the
    /// client's left-to-right tab order under the block's <c>OrderByDescending(UpdatedAt)</c>.
    /// </para>
    ///
    /// <para>
    /// <b>visibleToAssistant reconciliation (owner-flagged follow-up)</b>:
    /// <see cref="StoredWorkspaceTab"/> carries NO <c>visibleToAssistant</c> field, so live tabs
    /// default to <c>VisibleToAssistant = true</c> — they DO appear (the whole point of the
    /// re-point). Persisting the FR-01/FR-02 per-tab toggle THROUGH this live path would require a
    /// client PATCH-DTO change to carry the flag; that cross-surface change is intentionally NOT
    /// made here and is flagged as a follow-up. ADR-015: all mapped fields are identity/metadata —
    /// no item content crosses this boundary (the block emits <c>{type,label,active}</c> only).
    /// </para>
    ///
    /// <para>
    /// <c>internal</c> (not <c>private</c>) specifically so this mapping — the re-point linchpin
    /// between the write-through tab store and the workspace-state prompt block — is testable
    /// directly via <c>InternalsVisibleTo</c> (see .csproj), matching the precedent set by
    /// <see cref="SelectMostRecentSession"/> (tests CLAUDE.md B8 ban is on reflection into
    /// private members, not on testing an internal member directly).
    /// </para>
    /// </summary>
    internal static IReadOnlyList<WorkspaceTab> MapStoredTabsToWorkspaceTabs(
        IReadOnlyList<StoredWorkspaceTab> storedTabs,
        string sessionId,
        string tenantId)
    {
        var now = DateTimeOffset.UtcNow;
        var result = new List<WorkspaceTab>(storedTabs.Count);
        for (var i = 0; i < storedTabs.Count; i++)
        {
            var t = storedTabs[i];

            // Lenient polymorphic deserialization: the persisted widgetData carries a `kind`
            // discriminator for the typed widgets (Summary/DocumentViewer/Dashboard/Table/Email),
            // but layout/compose tabs persist a `kind`-less shape → deserialization fails → null.
            // TryDeriveVisibleState + the special-cases handle null; the block labels by DisplayName.
            // Hardening fix (r3 task 011 test pass): System.Text.Json's JsonPolymorphic converter
            // throws NotSupportedException (NOT JsonException) when the discriminator property is
            // ABSENT entirely — the exact shape a kind-less layout tab produces. A catch(JsonException)-
            // only clause let this escape per-tab handling and propagate out of the whole batch, which
            // the endpoint's outer catch(Exception) then swallowed by dropping ALL live tabs for the
            // turn (not just the one kind-less tab) — silently defeating the FR-03 re-point for any
            // session with a layout tab open. Both exception shapes are now caught per-tab, matching
            // the documented behavior above.
            WorkspaceTabWidgetData? widgetData = null;
            if (t.WidgetData is { ValueKind: JsonValueKind.Object } we)
            {
                try
                {
                    widgetData = we.Deserialize<WorkspaceTabWidgetData>();
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException)
                {
                    widgetData = null;
                }
            }

            // Ordering: earlier list index → later timestamp so OrderByDescending preserves the
            // client's tab-strip order (the active tab is hoisted separately via the focus-stamp).
            var syntheticUpdatedAt = now.AddSeconds(-i).ToString("O");

            result.Add(new WorkspaceTab
            {
                Id = t.Id,
                WidgetType = t.WidgetType,
                WidgetData = widgetData!,
                DisplayName = t.DisplayName,
                SessionId = sessionId,
                TenantId = tenantId,
                // No visibleToAssistant on StoredWorkspaceTab → default visible (see remarks).
                VisibleToAssistant = true,
                SourceProvenance = new WorkspaceTabSourceProvenance
                {
                    // Deterministic marker id (ADR-015) — never user text.
                    Source = "user",
                    CreatedBy = "workspace-live-tab",
                    CreatedAt = syntheticUpdatedAt,
                },
                MatterContext = new WorkspaceTabMatterContext { MatterId = string.Empty, MatterName = string.Empty },
                IsPinned = false,
                CanEdit = true,
                LastUserEditAt = null,
                CreatedAt = syntheticUpdatedAt,
                UpdatedAt = syntheticUpdatedAt,
            });
        }

        return result;
    }

    /// <summary>
    /// POST /api/ai/chat/sessions/{sessionId}/suggest
    /// spaarkeai-assistant-enhancements-r2 FR-B3/B5 (task 022) — run the ONE grounded proactive-
    /// suggestion turn for the focused workspace tab and return ≤3 content-specific follow-on chips.
    /// Proposer only: the returned chips ride the existing Click path when clicked; this endpoint never
    /// dispatches, injects a transcript message, or writes a ledger entry. Best-effort — returns an
    /// empty chip list (200) rather than an error when the context type is absent, no candidates match,
    /// the model proposes nothing, or the AI feature is disabled.
    /// </summary>
    private static async Task<IResult> SuggestFollowupsAsync(
        string sessionId,
        ChatSuggestRequest request,
        AssistantSuggestionService suggestions,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Ai.ChatEndpoints.Suggest");

        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Tenant ID not found in token claims (tid).");
        }

        // A proactive surface degrades silently: a request with no context type yields no chips
        // (200 empty), not a 400 — the client fires this fire-and-forget on tab open.
        if (request is null || string.IsNullOrWhiteSpace(request.ContextType))
        {
            return Results.Ok(new ChatSuggestResponse(Array.Empty<ChatSuggestChip>()));
        }

        var chips = await suggestions.SuggestAsync(
            sessionId,
            tenantId,
            request.ContextType,
            request.ActiveContext?.TabId,
            cancellationToken);

        logger.LogDebug(
            "Suggest endpoint: session={SessionId}, contextType={ContextType}, chipCount={ChipCount}",
            sessionId, request.ContextType, chips.Count);

        return Results.Ok(new ChatSuggestResponse(
            chips.Select(c => new ChatSuggestChip(c.TargetBindingId, c.Label, c.Reason)).ToList()));
    }

    // =========================================================================
    // Private Helpers
    // =========================================================================

    // FR-P2-05 hard cutover (task 034): BuildDeclaredSideEffectLookupAsync and
    // DerivePlanSideEffectClass were DELETED with the compound-intent pre-pass that was
    // their sole caller. Declared side-effect-class gating now happens loop-native at the
    // dispatch seam (SessionDispatchOrchestrator / PendingPlanManager.RequiresConfirmation,
    // FR-P2-02); ChatEndpoints no longer inspects tool calls before the loop runs. The
    // per-tool-proposing-turn catalog query flagged in task 031-W2 dies here.

    /// <summary>
    /// State of an FR-P2-03 elicitation-answer turn: the framed message the loop
    /// receives (the utterance parsed as answers to the pending invocation) + the
    /// pending gate id (identifiers only).
    /// </summary>
    private sealed record ElicitationTurnState(string FramedMessage, string GateId);

    /// <summary>
    /// FR-P2-03 mid-elicitation deterministic routing (walkthrough steps 10-12):
    /// <list type="bullet">
    ///   <item>No pending <c>elicitation</c> Gate in the ledger → null (normal turn).</item>
    ///   <item>Hard-slash / explicit-restart utterance → the gate is closed
    ///   <c>superseded</c> (deterministic escape) and the turn proceeds normally.</item>
    ///   <item>Resumable payload TTL-expired → the marker is closed <c>expired</c>
    ///   and the turn proceeds normally.</item>
    ///   <item>Otherwise → the utterance IS the answer: return the framed turn message
    ///   instructing the loop to resolve it into the suspended invocation.</item>
    /// </list>
    /// The pending-state check is ledger state (ADR-040), the escapes are exact string
    /// checks (<see cref="ElicitationTurnRouter"/>) — no intent classification (ADR-039).
    /// </summary>
    private static async Task<ElicitationTurnState?> ResolveElicitationTurnAsync(
        ChatSession session,
        string userMessage,
        string effectiveMessage,
        string tenantId,
        string sessionId,
        PendingPlanManager pendingPlanManager,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var pending = PendingPlanManager.FindPendingGate(
            session.Gates, PendingPlanManager.GateKindElicitation);
        if (pending is null)
        {
            return null;
        }

        if (ElicitationTurnRouter.IsHardSlash(userMessage) ||
            ElicitationTurnRouter.IsExplicitRestart(userMessage))
        {
            var closed = await pendingPlanManager.CloseInvocationAsync(
                tenantId, sessionId, pending.GateId,
                PendingPlanManager.GateStatusSuperseded, cancellationToken);
            if (!closed)
            {
                // Payload already TTL-expired — marker-only close (append-only resolution).
                await pendingPlanManager.WriteGateMarkerAsync(
                    tenantId, sessionId, pending.GateId,
                    PendingPlanManager.GateKindElicitation, PendingPlanManager.GateStatusSuperseded,
                    bindingId: pending.BindingId, sideEffectClass: pending.SideEffectClass,
                    missingFields: pending.MissingFields, turn: pending.Turn, ct: cancellationToken);
            }

            logger.LogInformation(
                "[FR-P2-03] elicitation superseded (deterministic escape) — gateId={GateId}, session={SessionId}",
                pending.GateId, sessionId);
            return null;
        }

        var invocation = await pendingPlanManager.GetInvocationAsync(
            tenantId, sessionId, pending.GateId, cancellationToken);
        if (invocation is null)
        {
            // Resumable payload lapsed (30-min TTL) — the walk-away expired cleanly.
            await pendingPlanManager.WriteGateMarkerAsync(
                tenantId, sessionId, pending.GateId,
                PendingPlanManager.GateKindElicitation, PendingPlanManager.GateStatusExpired,
                bindingId: pending.BindingId, sideEffectClass: pending.SideEffectClass,
                missingFields: pending.MissingFields, turn: pending.Turn, ct: cancellationToken);

            logger.LogInformation(
                "[FR-P2-03] elicitation expired — gateId={GateId}, session={SessionId}",
                pending.GateId, sessionId);
            return null;
        }

        logger.LogInformation(
            "[FR-P2-03] mid-elicitation answer turn — gateId={GateId}, tool={ToolId}, " +
            "missingFieldCount={MissingFieldCount}, session={SessionId}",
            pending.GateId, invocation.ToolId, invocation.MissingFields?.Count ?? 0, sessionId);

        return new ElicitationTurnState(
            ElicitationTurnRouter.BuildAnswerFrame(invocation, effectiveMessage),
            pending.GateId);
    }

    /// <summary>
    /// POST /api/ai/chat/sessions/{sessionId}/gates/{gateId}/resolve — the unified-gate
    /// resolution surface (FR-P2-03 / task 032; presentation contract for
    /// ActionConfirmationDialog and future loop-boundary suspensions per task 034).
    /// </summary>
    private static async Task<IResult> ResolveGateAsync(
        string sessionId,
        string gateId,
        GateResolveRequest request,
        PendingPlanManager pendingPlanManager,
        SessionDispatchOrchestrator dispatchOrchestrator,
        HttpContext httpContext,
        ILogger<SprkChatAgentFactory> logger)
    {
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                detail: "Tenant ID not found in token claims",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request");
        }

        var cancellationToken = httpContext.RequestAborted;

        if (!request.Approved)
        {
            var rejected = await pendingPlanManager.RejectInvocationAsync(
                tenantId, sessionId, gateId, cancellationToken);
            return rejected
                ? Results.Ok(new GateResolveResult("rejected", null))
                : GateNotPendingProblem();
        }

        // Confirm — G-P2 UAT round-1 finding 6 (2026-07-06): PEEK the invocation FIRST
        // so a typed-handler (non-Binding) confirm closes the gate with the HONEST
        // terminal status instead of the pre-fix sequence (ResumeInvocationAsync wrote a
        // `confirmed` marker, then the 422 left the ledger claiming an execution that
        // never happened). Binding-backed invocations proceed through the unchanged
        // get-then-delete resume below (double-confirm → 409).
        var peeked = await pendingPlanManager.GetInvocationAsync(
            tenantId, sessionId, gateId, cancellationToken);
        if (peeked is null)
        {
            return GateNotPendingProblem();
        }

        if (!Guid.TryParse(peeked.BindingId, out var bindingId) || bindingId == Guid.Empty)
        {
            // FR-P3-03 (task 042): typed-handler confirm-RESUME. Non-Binding invocations
            // (tools suspended by SideEffectGateAIFunction) resolve back to their catalog
            // row + registered handler and EXECUTE under the confirming user's OBO scope,
            // with SessionOutput + ToolChain ledger writes before the result renders
            // (TypedHandlerResumeExecutor). Resolution is peek-only FIRST so genuinely
            // unsupported invocations (no chat-available row / no handler / compound AI
            // off) keep the honest `confirmed-unexecutable` + 422 `gate.no-binding-target`
            // interim path from the G-P2 UAT finding-6 fix (ADR-019 stable errorCode).
            var resumeExecutor = TypedHandlerResumeExecutor.TryCreate(httpContext.RequestServices, logger);
            var resolution = resumeExecutor is null
                ? null
                : await resumeExecutor.TryResolveAsync(peeked.ToolId, cancellationToken);

            if (resolution is null)
            {
                var closed = await pendingPlanManager.CloseInvocationAsync(
                    tenantId, sessionId, gateId,
                    PendingPlanManager.GateStatusConfirmedUnexecutable, cancellationToken);
                if (!closed)
                {
                    // Raced by a concurrent resolve/expiry between peek and close.
                    return GateNotPendingProblem();
                }

                return Results.Problem(
                    detail: "The confirmed invocation has no executable target from this surface.",
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "Unprocessable Entity",
                    extensions: new Dictionary<string, object?> { ["errorCode"] = "gate.no-binding-target" });
            }

            // Supported: take the payload (double-confirm → 409) + `confirmed` marker
            // (the user's approval — same marker contract as the Binding leg), then
            // execute through the typed-handler stack (ValidateChat → ExecuteChatAsync —
            // the same handler contract the loop's adapter drives).
            var typedInvocation = await pendingPlanManager.ResumeInvocationAsync(
                tenantId, sessionId, gateId, cancellationToken);
            if (typedInvocation is null)
            {
                return GateNotPendingProblem();
            }

            var oid = ExtractUserId(httpContext);
            var outcome = await resumeExecutor!.ExecuteAsync(
                resolution, typedInvocation, oid?.ToString("D"), cancellationToken);
            if (!outcome.Success)
            {
                // G-P3 UAT round-2 R2-A/R2-C (2026-07-07): a failed confirm previously
                // vanished — the 502 was toast-only client-side and NOTHING recorded the
                // failure for the ledger or the next turn's model, which kept "guessing"
                // the record existed. Now: (a) a `dispatch-failed` gate marker lands
                // (append-only, after the `confirmed` approval marker), and (b) the
                // honest failure is persisted as an assistant transcript message so the
                // next turn's history carries the real outcome.
                var failureText = GateOutcomeProducer.BuildGateOutcomeMessage(
                    success: false, resolution.Tool.Name, outcome.Error, ledgerKey: null);
                await pendingPlanManager.WriteGateMarkerAsync(
                    tenantId, sessionId, gateId,
                    typedInvocation.Kind, PendingPlanManager.GateStatusDispatchFailed,
                    bindingId: typedInvocation.BindingId, sideEffectClass: typedInvocation.SideEffectClass,
                    missingFields: null, turn: typedInvocation.Turn, ct: cancellationToken);
                await PersistGateOutcomeMessageAsync(
                    httpContext, tenantId, sessionId, failureText, logger, cancellationToken);

                return BuildGateDispatchFailedProblem(outcome.Error);
            }

            // R2-C: the executed outcome must reach the NEXT turn's model. The
            // SessionOutput (loop@t{n}) already rides the ledger-outputs context block;
            // this transcript message additionally puts the confirmation event itself
            // into conversation history (and survives a page reload, unlike the
            // client-local rendering).
            // R4-6 (2026-07-07): the transcript prefers the handler's USER-facing outcome
            // sentence — the model-facing Summary leaked instruction text verbatim.
            // R4-3: the server-composed MDA record link rides both the persisted message
            // (markdown link, durable across reloads, real truth for the model to relay)
            // and the response fields (client chip / local rendering).
            var userFacingSummary = outcome.UserSummary ?? outcome.Summary;
            await PersistGateOutcomeMessageAsync(
                httpContext, tenantId, sessionId,
                GateOutcomeProducer.BuildGateOutcomeMessage(
                    success: true, resolution.Tool.Name, userFacingSummary, outcome.LedgerKey, outcome.RecordUrl),
                logger, cancellationToken);

            return Results.Ok(new GateResolveResult(
                "confirmed", userFacingSummary,
                RecordUrl: outcome.RecordUrl,
                RecordEntityLogicalName: outcome.RecordEntityLogicalName,
                RecordId: outcome.RecordId?.ToString("D"),
                // task 035 / FR-A1-06: carry the Completion Engine's OutcomeCard so the client
                // renders the structured card (server-composed link chip + next-step chips)
                // instead of parsing the markdown "[Open record]" link. Composed from the stored
                // ledger output (store-before-render — ADR-040); null only if the ledger write
                // degraded, in which case the client falls back to the summary/record fields.
                Outcome: outcome.Outcome));
        }

        // Binding-backed: get-then-delete (double-confirm → 409) + confirmed ledger marker.
        var invocation = await pendingPlanManager.ResumeInvocationAsync(
            tenantId, sessionId, gateId, cancellationToken);
        if (invocation is null)
        {
            return GateNotPendingProblem();
        }

        JsonElement? args = null;
        if (!string.IsNullOrWhiteSpace(invocation.ArgsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(invocation.ArgsJson);
                args = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Store-only payload should always be valid JSON; degrade to no args.
                logger.LogWarning(
                    "ResolveGate: malformed ArgsJson on gate {GateId} — dispatching without args. session={SessionId}",
                    gateId, sessionId);
            }
        }

        try
        {
            // Execute via THE dispatch seam (ADR-039). The orchestrator ledger-writes the
            // output BEFORE the terminal chunk (ADR-040); this handler drains the chunks
            // and returns the terminal summary as JSON (the dialog presentation is a
            // toast, not a stream — streamed resume presentation arrives with task 034's
            // loop-boundary wiring).
            string? summary = null;
            string? error = null;
            await foreach (var chunk in dispatchOrchestrator.DispatchAsync(
                new SessionDispatchRequest(tenantId, sessionId, bindingId, args), cancellationToken))
            {
                if (chunk.Done)
                {
                    error = chunk.Error;
                    summary = chunk.Summary ?? chunk.Content;
                }
            }

            if (error is not null)
            {
                // R2-A/R2-C symmetry with the typed-handler leg (2026-07-07): failure
                // marker + honest transcript message so the outcome is never silent.
                await pendingPlanManager.WriteGateMarkerAsync(
                    tenantId, sessionId, gateId,
                    invocation.Kind, PendingPlanManager.GateStatusDispatchFailed,
                    bindingId: invocation.BindingId, sideEffectClass: invocation.SideEffectClass,
                    missingFields: null, turn: invocation.Turn, ct: cancellationToken);
                await PersistGateOutcomeMessageAsync(
                    httpContext, tenantId, sessionId,
                    GateOutcomeProducer.BuildGateOutcomeMessage(success: false, invocation.Title ?? invocation.ToolId, error, ledgerKey: null),
                    logger, cancellationToken);

                return BuildGateDispatchFailedProblem(error);
            }

            await PersistGateOutcomeMessageAsync(
                httpContext, tenantId, sessionId,
                GateOutcomeProducer.BuildGateOutcomeMessage(success: true, invocation.Title ?? invocation.ToolId, summary, ledgerKey: null),
                logger, cancellationToken);

            return Results.Ok(new GateResolveResult("confirmed", summary));
        }
        catch (DispatchRejectedException ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: ex.StatusCode,
                title: "Dispatch Rejected",
                extensions: new Dictionary<string, object?> { ["errorCode"] = ex.ErrorCode });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            // Session vanished between resume and dispatch — same 404 mapping contract
            // as DispatchSessionEndpoint (the sibling dispatch surface).
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "gate.session-not-found" });
        }

        static IResult GateNotPendingProblem() => Results.Problem(
            detail: "The gate is expired or already resolved.",
            statusCode: StatusCodes.Status409Conflict,
            title: "Conflict",
            extensions: new Dictionary<string, object?> { ["errorCode"] = "gate.not-pending" });
    }

    /// <summary>
    /// Builds the gate-resolve failure response — the single construction site for
    /// BOTH resolve legs (typed-handler resume + Binding dispatch).
    /// G-P3 UAT round-3 R3-2 (2026-07-07): handler-reported dispatch failures
    /// (write-mapper validation, Dataverse 400s) are REQUEST-CONTENT problems —
    /// <b>422</b> ProblemDetails carrying the stable <c>gate.dispatch-failed</c>
    /// errorCode (ADR-019) and the handler's instructive detail. The previous 502
    /// falsely signaled a gateway fault for what is a correctable payload problem;
    /// 5xx is reserved for genuinely unexpected exceptions (which propagate to the
    /// global exception handler).
    /// </summary>
    internal static IResult BuildGateDispatchFailedProblem(string? detail) => Results.Problem(
        detail: detail ?? "The confirmed action failed.",
        statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "Dispatch Failed",
        extensions: new Dictionary<string, object?> { ["errorCode"] = "gate.dispatch-failed" });

    // Task 053 (FR-B-04): BuildGateOutcomeMessage + MaxGateOutcomeMessageChars (the gate-outcome
    // transcript primitive) moved to ContextSliceProducers.GateOutcomeProducer — the single production
    // home for this Memory.Conversation primitive. Persistence (PersistGateOutcomeMessageAsync below)
    // stays here: it is session plumbing, not string production.

    /// <summary>
    /// Persists a gate-resolution outcome as an Assistant transcript message so
    /// (a) the operator sees the outcome IN the conversation (it survives reload,
    /// unlike the client-local rendering) and (b) the NEXT turn's model history
    /// carries the real result instead of guessing (G-P3 UAT round-2 R2-C: the model
    /// oscillated between "created" and "not found" because no gate outcome ever
    /// entered <c>session.Messages</c>). Best-effort: history services unavailable
    /// (kill switch) or a write failure degrade to a loud log — the gate resolution
    /// itself already succeeded/failed and must not be masked by persistence errors.
    /// </summary>
    private static async Task PersistGateOutcomeMessageAsync(
        HttpContext httpContext,
        string tenantId,
        string sessionId,
        string content,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var historyManager = httpContext.RequestServices.GetService<ChatHistoryManager>();
            var sessionManager = httpContext.RequestServices.GetService<ChatSessionManager>();
            if (historyManager is null || sessionManager is null)
            {
                logger.LogWarning(
                    "[gate-outcome] history services unavailable — gate outcome not persisted to transcript. session={SessionId}",
                    sessionId);
                return;
            }

            var session = await sessionManager.GetSessionAsync(tenantId, sessionId, cancellationToken);
            if (session is null)
            {
                logger.LogWarning(
                    "[gate-outcome] session not found — gate outcome not persisted. session={SessionId}",
                    sessionId);
                return;
            }

            var message = new DvChatMessage(
                MessageId: Guid.NewGuid().ToString("N"),
                SessionId: sessionId,
                Role: ChatMessageRole.Assistant,
                Content: content,
                TokenCount: 0,
                CreatedAt: DateTimeOffset.UtcNow,
                SequenceNumber: session.Messages.Count + 1);

            await historyManager.AddMessageAsync(session, message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // NFR-07: identifiers only. The resolution outcome already returned to the
            // client; a transcript-persistence failure must degrade loudly, not 5xx.
            logger.LogError(ex,
                "[gate-outcome] failed to persist gate outcome message — session={SessionId}",
                sessionId);
        }
    }

    /// <summary>
    /// Extracts the tenant ID from the JWT 'tid' claim (ADR-014).
    /// Tenant comes from the caller's authenticated principal and from nothing else (task 059 — see Infrastructure/Authentication/TenantResolution).
    /// </summary>
    private static string? ExtractTenantId(HttpContext httpContext)
    {
        // Primary: 'tid' claim from Azure AD JWT token
        // Microsoft.Identity.Web may map 'tid' to the long-form URI claim
        var tenantId = TenantResolution.ResolveTenantId(httpContext.User);

        return tenantId;
    }

    /// <summary>
    /// Extracts the user's object ID from the JWT 'oid' claim (Azure AD).
    /// Returns null if the claim is missing or not a valid GUID.
    /// </summary>
    private static Guid? ExtractUserId(HttpContext httpContext)
    {
        var oid = CallerResolution.ResolveObjectId(httpContext.User);
        return Guid.TryParse(oid, out var userId) ? userId : null;
    }

    // =========================================================================
    // FR-07 Multi-file attachment validation + composition (R3 task 050)
    // R4 task 050 (A-4): client-side binary cap raised 10 → 25 MB; server text
    // caps unchanged (operate on extracted text, not binary — see policy doc).
    // =========================================================================
    //
    // PLACEMENT JUSTIFICATION (per CLAUDE.md §10 BFF Hygiene + ADR-013):
    // - In-process extension on an existing endpoint (POST /sessions/{id}/messages).
    // - NO new DI feature module, NO new service, NO new NuGet packages.
    // - Latency-coupled to the existing single LLM call (D-01 invariant preserved).
    // - Transactional coupling with session/history persistence in same request lifecycle.
    // - All four BFF decision criteria (ADR-013 §"Decision Criteria") answer "BFF" → stays here.
    // - All four CRUD→AI facade boundary rules satisfied: this is AI-internal code in
    //   Api/Ai/, not CRUD code consuming AI — no IBffAiPublicContracts facade needed.
    //
    // See docs/standards/CHAT-ATTACHMENT-POLICY.md for the policy, MIME allow-list,
    // total-text cap rationale, PDF page cap, and upgrade path.

    /// <summary>Maximum attachments per chat message (NFR-04, FR-07).</summary>
    internal const int MaxAttachmentsPerMessage = 5;

    /// <summary>
    /// Maximum extracted text length per attachment, in characters.
    /// ~2.5M chars ≈ 10 MB UTF-16 in memory; bounds LLM prompt growth per attachment.
    ///
    /// R4 task 050 (A-4) raised the CLIENT-side binary cap from 10 MB → 25 MB to
    /// align with DocumentUploadWizard + OfficeService. This char-cap is NOT scaled
    /// proportionally because it operates on EXTRACTED TEXT, not raw binary. A 25 MB
    /// PDF typically extracts to &lt;1M chars (image-heavy PDFs even less); a 25 MB
    /// DOCX often extracts to &lt;500K chars. Keeping this cap at 2.5M chars preserves
    /// the LLM-prompt envelope without artificially limiting binary file size.
    ///
    /// See <c>docs/standards/CHAT-ATTACHMENT-POLICY.md</c> for the full policy + rationale.
    /// </summary>
    internal const int MaxAttachmentTextCharsPerFile = 2_500_000;

    /// <summary>
    /// Maximum sum of all attachment <c>TextContent</c> lengths in a single message.
    /// Bounds the LLM prompt size so 5 × 2.5M = 12.5M does not balloon context.
    ///
    /// R4 task 050 (A-4): NOT scaled with the 25 MB binary cap — char-cap is the
    /// LLM-prompt envelope, independent of binary file size. See policy doc.
    /// </summary>
    internal const int MaxAttachmentTextCharsTotal = 5_000_000;

    /// <summary>
    /// Allowed MIME types for attachments (NFR-04, FR-07). Matches the client-side extractor surface.
    /// </summary>
    private static readonly HashSet<string> AllowedAttachmentContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain",
        "text/markdown",
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    };

    /// <summary>
    /// Validates the <c>Attachments</c> list per NFR-04 and FR-07. Returns null on success;
    /// returns an RFC 7807 ProblemDetails-shaped error payload (with HTTP 400) on failure.
    /// Caller is responsible for writing the payload + status code to the response when not null.
    /// </summary>
    /// <returns>(statusCode, payload) on rejection; null on accept.</returns>
    private static (int statusCode, object payload)? ValidateAttachments(
        IReadOnlyList<ChatMessageAttachment>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
        {
            return null;
        }

        // Rule 1: max 5 attachments per message (NFR-04, FR-07)
        if (attachments.Count > MaxAttachmentsPerMessage)
        {
            return (400, BuildProblemDetails(
                title: "Too many attachments",
                detail: $"Attachments cannot exceed {MaxAttachmentsPerMessage} entries. Received {attachments.Count}.",
                status: 400));
        }

        long totalChars = 0;

        for (var i = 0; i < attachments.Count; i++)
        {
            var att = attachments[i];

            // Defensive: null entry from a deserializer edge case
            if (att is null || att.ContentType is null || att.TextContent is null || att.Filename is null)
            {
                return (400, BuildProblemDetails(
                    title: "Invalid attachment",
                    detail: $"Attachment at index {i} has missing required fields (filename, contentType, textContent).",
                    status: 400));
            }

            // Rule 2: MIME type in allow-list (NFR-04, FR-07)
            if (!AllowedAttachmentContentTypes.Contains(att.ContentType))
            {
                return (400, BuildProblemDetails(
                    title: "Unsupported attachment content type",
                    detail: $"Attachment '{att.Filename}' has unsupported contentType '{att.ContentType}'. " +
                            $"Allowed types: {string.Join(", ", AllowedAttachmentContentTypes)}.",
                    status: 400));
            }

            // Rule 3: per-file textContent cap (LLM prompt envelope; independent of
            // the 25 MB client-side binary cap raised in R4 A-4 — see policy doc).
            if (att.TextContent.Length > MaxAttachmentTextCharsPerFile)
            {
                return (400, BuildProblemDetails(
                    title: "Attachment too large",
                    detail: $"Attachment '{att.Filename}' textContent length ({att.TextContent.Length}) " +
                            $"exceeds the per-file cap of {MaxAttachmentTextCharsPerFile} characters.",
                    status: 400));
            }

            totalChars += att.TextContent.Length;

            // Rule 4: sum-of-all-attachments size cap (bounds LLM prompt)
            if (totalChars > MaxAttachmentTextCharsTotal)
            {
                return (400, BuildProblemDetails(
                    title: "Attachments exceed total size limit",
                    detail: $"Sum of attachment textContent lengths exceeds the total cap of " +
                            $"{MaxAttachmentTextCharsTotal} characters.",
                    status: 400));
            }
        }

        return null;
    }

    /// <summary>
    /// Builds an RFC 7807 ProblemDetails-shaped object suitable for direct JSON serialization
    /// in the SSE response (the handler returns <see cref="Task"/>, not <see cref="IResult"/>,
    /// so <c>Results.Problem(...)</c> cannot be used directly — we emit the same shape inline).
    /// </summary>
    private static object BuildProblemDetails(string title, string detail, int status)
    {
        return new
        {
            type = "https://tools.ietf.org/html/rfc7807",
            title,
            status,
            detail,
        };
    }

    /// <summary>
    /// Composes the effective user-message text passed to the SINGLE LLM call (D-01 invariant).
    /// When attachments are present, their filenames + extracted text are appended as structured
    /// blocks so the model has clear file boundaries; the user's typed message is preserved at
    /// the top so the question remains the dominant signal. When no attachments are present,
    /// the original message is returned verbatim (zero overhead, backwards compatible).
    /// </summary>
    /// <remarks>
    /// CRITICAL: this is the ONLY place where attachment text reaches the agent. There is no
    /// separate extraction or summarization LLM call — the single existing <c>agent.SendMessageAsync</c>
    /// receives one message that contains everything. This preserves D-01.
    /// </remarks>
    private static string ComposeMessageWithAttachments(
        string message,
        IReadOnlyList<ChatMessageAttachment>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
        {
            return message;
        }

        var sb = new System.Text.StringBuilder(message.Length + 256);

        sb.Append("User message: ").AppendLine(message);
        sb.AppendLine();
        sb.Append("[Attached files: ")
          .Append(string.Join(", ", attachments.Select(a => a.Filename)))
          .AppendLine("]");
        sb.AppendLine();

        for (var i = 0; i < attachments.Count; i++)
        {
            var att = attachments[i];
            sb.Append("--- Attachment: ").Append(att.Filename).Append(" (").Append(att.ContentType).AppendLine(") ---");
            sb.AppendLine(att.TextContent);
            sb.AppendLine();
        }

        return sb.ToString();
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
    /// spaarkeai-assistant-enhancements-r4 task 021a (FR-04) — maximum time the grounded follow-on
    /// proposer may run after a chat response before it is silently skipped (ADR-019 — followups are
    /// optional and must never delay or break the turn). A little more headroom than the retired
    /// ~100-token free-string generator's 2s, because the grounded pass resolves the Action + the
    /// candidate menu (both cached) then runs ONE Fast-tier selection turn.
    /// </summary>
    private const int FollowupsTimeoutMs = 4000;

    /// <summary>
    /// Keyword phrases that indicate the AI is asking the user to provide a document.
    /// Matched case-insensitively against the full assistant response text.
    ///
    /// AIPU-058: When the AI signals missing document/entity context AND no document is
    /// currently loaded in the session, action chips are emitted so the user can resolve
    /// the gap without typing a follow-up message.
    /// </summary>
    private static readonly string[] MissingContextKeywords =
    [
        "upload a document",
        "upload a file",
        "upload the document",
        "upload the file",
        "provide a document",
        "provide the document",
        "share a document",
        "share the document",
        "attach a document",
        "attach a file",
        "please upload",
        // UAT 2026-07-22: the bare "please provide" / "please share" / "you can provide" /
        // "you need to provide" / "please send" keywords were OVERBROAD — they matched
        // non-document replies (e.g. a create-task clarification: "could you please provide
        // some details"), firing the document/matter action chips in the wrong context. The
        // document-SPECIFIC variants below ("provide a/the document", "share a/the document",
        // "send me the document/file") already cover the genuine document-missing cases, so the
        // bare forms are removed / tightened to require a document|file object.
        "no document",
        "no file",
        "don't have a document",
        "don't have access to a document",
        "haven't provided a document",
        "haven't shared a document",
        "you need to upload",
        "you need to provide a document",
        "you need to share a document",
        "to analyze a document",
        "to review a document",
        "to compare documents",
        "to summarize a document",
        "could not find a document",
        "couldn't find a document",
        "you can upload",
        "you can provide a document",
        "send me the document",
        "send me the file",
        "i need a document",
        "i need the document",
        "i need a file",
    ];

    /// <summary>
    /// AIPU-058 — builds the deterministic "missing document context" action chips as TYPED followup
    /// items, or an empty list when no missing-context signal is present. Returns the chips rather than
    /// emitting them (task 021a: the caller merges them with the grounded proposer's followups into ONE
    /// typed "suggestions" event, so the two are no longer mutually exclusive — the former keyword
    /// "hijack" that suppressed suggestions is gone).
    ///
    /// The three chips carry a typed <c>action</c> kind with an <c>actionId</c> the frontend
    /// (SprkChat.tsx) special-routes — <c>upload</c> → the file-input flow, <c>search</c> → the document
    /// search pane, <c>select</c> → the matter picker. This replaces the legacy <c>"[action:&lt;id&gt;]
    /// &lt;label&gt;"</c> string-prefix encoding; the behavior is unchanged (folding these into the
    /// grounded candidate menu is a deferred phase-2).
    ///
    /// Preconditions (both required):
    ///   1. The effective document ID is null or empty (no document loaded in this session).
    ///   2. The AI response text contains at least one keyword from <see cref="MissingContextKeywords"/>.
    /// </summary>
    /// <param name="effectiveDocumentId">The active document ID (null or empty when no document is loaded).</param>
    /// <param name="responseText">The full AI response text to inspect for missing-context keywords.</param>
    /// <returns>The three typed action chips when missing-context is detected; otherwise an empty list.</returns>
    /// <summary>
    /// task 024 (FR-04 / FR-10): assemble the ONE typed "suggestions" wire payload from the two typed
    /// sources — the deterministic missing-context ACTION chips and the grounded proposer's two-kind
    /// output — in the §9a wire order (ACTION first, then CAPABILITY = what you can DO, then QUESTION =
    /// what you can ASK). A CAPABILITY whose <c>TargetBindingId</c> is null is DROPPED (a capability with
    /// no binding is a dead-end and must never reach the wire — the structural death of the P2 free-string
    /// dead-end); a QUESTION carries only its label. Extracted from the <c>/messages</c> emit path so the
    /// "the wire shape is the typed two-kind <see cref="ChatSseFollowupItem"/>[], never an untyped string"
    /// guarantee has a direct regression guard, exercised via <c>InternalsVisibleTo</c> (the same testing
    /// precedent as this file's other internals). Behavior-preserving refactor of the prior inline block.
    /// </summary>
    /// <param name="missingContextActionChips">The deterministic, already-typed <c>action</c> chips (may be empty).</param>
    /// <param name="grounded">The grounded proposer's typed two-kind followups (empty on a proposer failure/timeout).</param>
    /// <returns>The ordered, typed followups list; a capability with a null binding id is excluded.</returns>
    internal static List<ChatSseFollowupItem> BuildTypedFollowups(
        IReadOnlyList<ChatSseFollowupItem> missingContextActionChips,
        IReadOnlyList<SuggestedFollowup> grounded)
    {
        var followups = new List<ChatSseFollowupItem>(missingContextActionChips);
        // Capabilities first (what you can DO), then questions (what you can ASK) — the design's
        // deterministic §5a order; the client renders the single arrow/affordance distinction.
        followups.AddRange(grounded
            .Where(f => f.Kind == SuggestedFollowupKind.Capability && f.TargetBindingId is not null)
            .Select(f => new ChatSseFollowupItem("capability", f.Label, TargetBindingId: f.TargetBindingId)));
        followups.AddRange(grounded
            .Where(f => f.Kind == SuggestedFollowupKind.Question)
            .Select(f => new ChatSseFollowupItem("question", f.Label)));
        return followups;
    }

    private static IReadOnlyList<ChatSseFollowupItem> BuildMissingContextActionChips(
        string? effectiveDocumentId,
        string responseText)
    {
        // Precondition 1: No document loaded.
        if (!string.IsNullOrWhiteSpace(effectiveDocumentId))
        {
            return Array.Empty<ChatSseFollowupItem>();
        }

        // Precondition 2: Response contains missing-context keywords.
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return Array.Empty<ChatSseFollowupItem>();
        }

        var lowerResponse = responseText.ToLowerInvariant();
        var keywordFound = false;
        foreach (var keyword in MissingContextKeywords)
        {
            if (lowerResponse.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                keywordFound = true;
                break;
            }
        }

        if (!keywordFound)
        {
            return Array.Empty<ChatSseFollowupItem>();
        }

        return new[]
        {
            new ChatSseFollowupItem("action", "Upload File", ActionId: "upload"),
            new ChatSseFollowupItem("action", "Browse Matter Documents", ActionId: "search"),
            new ChatSseFollowupItem("action", "Select a Matter", ActionId: "select"),
        };
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
    /// Used by catalog-projected handlers (e.g. the analysis-rerun handler) to emit
    /// out-of-band events (progress, document_replace) during long-running tool execution
    /// without coupling them to HttpResponse directly.
    /// </summary>
    internal static Func<ChatSseEvent, CancellationToken, Task> CreateSseWriter(HttpResponse response)
    {
        return (evt, ct) => WriteChatSSEAsync(response, evt, ct);
    }

    /// <summary>
    /// Creates an <see cref="R2SseEventEmitter"/> scoped to the current HTTP response.
    ///
    /// The emitter wraps the SSE writer delegate produced by <see cref="CreateSseWriter"/>
    /// and exposes typed emit methods for the six R2 event types (workspace_widget,
    /// context_update, context_highlight, workspace_action, capability_change,
    /// safety_annotation). All R1 event types remain unchanged and are NOT routed through
    /// this emitter.
    ///
    /// Callers inject the emitter into services or tool handlers that need to push R2 events
    /// during a streaming turn without holding a direct reference to <see cref="HttpResponse"/>.
    /// </summary>
    /// <param name="sseWriter">The SSE writer delegate from <see cref="CreateSseWriter"/>.</param>
    /// <param name="logger">Logger used for validation failure warnings (ADR-015: payload content is never logged).</param>
    internal static R2SseEventEmitter CreateR2Emitter(
        Func<ChatSseEvent, CancellationToken, Task> sseWriter,
        ILogger logger)
    {
        return new R2SseEventEmitter(sseWriter, logger);
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
    /// Forwarded to the typed working-document handler via <see cref="SprkChatAgentFactory"/>
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
/// <param name="Attachments">
/// Optional in-memory file attachments with client-side-extracted text (FR-07). Max 5 entries.
/// Each entry's <see cref="ChatMessageAttachment.TextContent"/> is appended to the same
/// single LLM call's user-message context — there is exactly ONE LLM call per user turn
/// (D-01 single-LLM-call invariant). NOT persisted as Dataverse Document entities (FR-07
/// in-memory only). Default null preserves backwards compatibility for clients that omit
/// the field. See <see cref="ValidateAttachments"/> for validation rules (NFR-04).
/// </param>
/// <param name="ModelTierOverride">
/// ai-advanced-capabilities-nda-r1 task 011: the Assistant's runtime model-tier picker selection for
/// THIS turn (raw <c>sprk_aimodeltier</c> option-set value — the SAME wire vocabulary the maker-facing
/// catalog editor already uses, e.g. <c>100000002</c> = Reasoning). Forwarded to
/// <see cref="Sprk.Bff.Api.Services.Ai.Chat.SprkChatAgentFactory.CreateAgentAsync"/> and, from there, to
/// every projected capability tool for the turn — it composes with (does not replace) the dispatched
/// Binding's own <c>sprk_modeltieroverride</c> / <see cref="Sprk.Bff.Api.Services.Ai.PublicContracts.Binding.EffectiveModelTier"/>
/// through the ONE tier→deployment resolver (ADR-039). Default <c>null</c> = no override; the Action's
/// own tier governs unchanged (pre-task-011 behavior).
/// </param>
/// <param name="ActiveContext">
/// spaarkeai-assistant-enhancements-r2 FR-A2/A3 (Active-tab awareness / focus-stamp): the
/// identity + compact state of the workspace tab the user has explicitly focused, captured
/// client-side from the <c>workspace.active_widget_changed</c> signal and attached to the
/// outbound body via the existing <c>onDecorateOutboundBody</c> seam (FR-A1/A2 — no
/// <c>SprkChat</c> change). Server-side, <see cref="ChatActiveContext.TabId"/> is the ONLY
/// load-bearing field: it is forwarded to
/// <see cref="Sprk.Bff.Api.Services.Ai.Chat.SprkChatAgentFactory.CreateAgentAsync"/> so
/// <c>BuildWorkspaceStateBlock</c> labels the matching tab "(active)" in preference to the
/// legacy <c>UpdatedAt</c>-most-recent heuristic (FR-A3). The active tab then contributes its
/// COMPACT content shape while background tabs stay metadata-only (FR-A4, ADR-015 Path A
/// exception). Default <c>null</c> = no focus-stamp → the <c>UpdatedAt</c> fallback is used
/// unchanged (backward compatible). ADR-015 note: the server does NOT trust the client-supplied
/// <see cref="ChatActiveContext.CompactState"/> as prompt content — the agent-visible content is
/// derived server-side from persisted tab state, so this field cannot widen visibility beyond the
/// bounded compact shape.
/// </param>
/// <remarks>
/// FR-P2-05 hard cutover (task 034): the former soft-slash bias field was RETIRED
/// end-to-end (NFR-08). There is no longer any client-to-server intent-bias hint —
/// every chat NL utterance enters the agent-turn loop unbiased; the four retained soft
/// slashes invoke deterministically through the Click path, not via a wire hint.
/// </remarks>
/// <param name="ActiveItem">
/// spaarkeai-assistant-enhancements-r3 task 011 (FR-04): the client-supplied active-item handle —
/// the <c>{id,type,label}</c> of the single item the user is currently acting on WITHIN a tab
/// (e.g. a selected email in an email list, the document open in a viewer). Published client-side
/// by the task-001 active-item conduit (a generalization of the Compose <c>composeActionBridge</c>)
/// and forwarded to <see cref="Sprk.Bff.Api.Services.Ai.Chat.SprkChatAgentFactory.CreateAgentAsync"/>,
/// where it becomes the ONE active-item slot in the workspace-state prompt block. Distinct from
/// <see cref="ActiveContext"/> (the active TAB focus-stamp). ADR-015 id-not-content BINDING: only
/// id/type/label are carried — NEVER item content; all content is tool-fetched by id. Default
/// <c>null</c> = no active item published → the slot is empty.
/// </param>
public record ChatSendMessageRequest(
    string Message,
    string? DocumentId = null,
    IReadOnlyList<ChatMessageAttachment>? Attachments = null,
    AiModelTier? ModelTierOverride = null,
    ChatActiveContext? ActiveContext = null,
    ChatActiveItem? ActiveItem = null);

/// <summary>
/// spaarkeai-assistant-enhancements-r3 task 011 (FR-04) — the client active-item handle carried on
/// the chat request. Mirrors the widget-agnostic <c>{id,type,label}</c> shape published by the
/// task-001 conduit. Mapped to <see cref="Sprk.Bff.Api.Models.Workspace.WorkspaceActiveItemHandle"/>
/// at the send-message call site.
///
/// <para>
/// <b>ADR-015 (id-not-content) BINDING</b>: this record has EXACTLY three fields — id, type, label —
/// and MUST NOT be extended with any content field (body, snippet, selection text, row/chart data).
/// A content field here is a governance defect. All real content is tool-fetched by <see cref="Id"/>.
/// </para>
/// </summary>
/// <param name="Id">Opaque, deterministic fetch key for the active item (never user text). The Assistant keys per-item actions to this id.</param>
/// <param name="Type">The item kind (e.g. <c>"email"</c>, <c>"document"</c>) — an identity discriminator, not content.</param>
/// <param name="Label">Human-readable item title — a thin identity slice, never a body/snippet.</param>
public record ChatActiveItem(
    string? Id = null,
    string? Type = null,
    string? Label = null);

/// <summary>
/// spaarkeai-assistant-enhancements-r2 FR-A2 — the client "focus-stamp": the identity and
/// compact state of the currently focused workspace tab, mirrored from the client
/// <c>{ widgetType, contextType, tabId, displayName, compactState }</c> shape (FR-A1/A2).
///
/// <para>
/// Server-side contract: <see cref="TabId"/> matches <c>WorkspaceTab.id</c> and is used ONLY to
/// prefer the explicit focus-stamp over the <c>UpdatedAt</c>-most-recent heuristic when labeling
/// the "(active)" tab (FR-A3). The remaining fields are carried for wire-fidelity with the client
/// stamp; <see cref="CompactState"/> is deliberately NOT injected into the agent prompt — the
/// agent-visible content is derived server-side from persisted tab state (ADR-015: the server is
/// the authority on what content is visible, never client-supplied bytes). This keeps the field
/// a labeling preference, not a new content channel or intent-classifier (ADR-039).
/// </para>
/// </summary>
/// <param name="WidgetType">The focused tab's widget-type discriminator (e.g. "DocumentViewer", "Summary", "email"). Carried for fidelity; not load-bearing server-side.</param>
/// <param name="ContextType">The focused widget's context type from the closed FR-B1 set (email | document | compose-doc | matter-grid | dashboard | calendar). Carried for fidelity.</param>
/// <param name="TabId">Stable tab identity matching <c>WorkspaceTab.id</c>. The ONLY load-bearing field — drives the FR-A3 active-tab labeling preference.</param>
/// <param name="DisplayName">Human-readable tab label. Carried for fidelity; not load-bearing server-side.</param>
/// <param name="CompactState">Client-computed compact visible-state payload. Accepted for wire-fidelity but NOT emitted into the agent prompt (ADR-015 — server-derived visible state is authoritative).</param>
public record ChatActiveContext(
    string? WidgetType = null,
    string? ContextType = null,
    string? TabId = null,
    string? DisplayName = null,
    JsonElement? CompactState = null);

/// <summary>
/// Request body for <c>POST /sessions/{id}/suggest</c> (spaarkeai-assistant-enhancements-r2 FR-B3/B5, task 022).
/// </summary>
/// <param name="ContextType">The focused tab's context type (closed FR-B1 set: email | document | compose-doc | matter-grid | dashboard | calendar). Required — a blank value yields an empty chip list. Deterministically pre-filters the candidate capabilities (task 021 <c>Binding.ContextTypeTags</c>).</param>
/// <param name="ActiveContext">The focused tab's identity/compact stamp (reuses <see cref="ChatActiveContext"/>). Only <see cref="ChatActiveContext.TabId"/> is load-bearing server-side — the active tab's compact content is derived SERVER-SIDE from persisted state (ADR-015), never from the client-supplied <see cref="ChatActiveContext.CompactState"/>.</param>
public record ChatSuggestRequest(
    string ContextType,
    ChatActiveContext? ActiveContext = null);

/// <summary>Response body for <c>POST /sessions/{id}/suggest</c> — up to 3 proactive follow-on chips (empty when nothing fits).</summary>
/// <param name="Chips">The proposed chips (≤3), ranked most-useful first.</param>
public record ChatSuggestResponse(
    [property: JsonPropertyName("chips")] IReadOnlyList<ChatSuggestChip> Chips);

/// <summary>
/// One proactive follow-on chip. Field names match the client <c>parseConsumerChips</c> contract
/// (<c>targetBindingId</c> + <c>label</c>) so the client feeds the array straight to
/// <c>useConsumerChips.acceptChips</c>. The chip dispatches via the existing deterministic Click path
/// (<c>invoke(targetBindingId, args)</c>) on user click — this record is a proposal, never a dispatch.
/// </summary>
/// <param name="TargetBindingId">The proposed capability's <c>sprk_playbookconsumer</c> id (always one of the pre-filtered candidates).</param>
/// <param name="Label">Short, content-specific chip label.</param>
/// <param name="Reason">Developer-facing selection-trace rationale (FR-B6/task 024); not shown to the end user.</param>
public record ChatSuggestChip(
    [property: JsonPropertyName("targetBindingId")] string TargetBindingId,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("reason")] string? Reason);

/// <summary>
/// In-memory chat-message attachment with client-extracted text content (FR-07).
///
/// Text extraction happens client-side (PDF.js, mammoth.js, raw read) before the message
/// is sent. The BFF receives only the extracted text — never the original binary file.
/// Attachments are NOT persisted as Dataverse Document entities; they live for exactly
/// one user turn and are passed into the same single LLM call as the user message
/// (D-01 invariant, ADR-013 in-process AI extension).
/// </summary>
/// <param name="Filename">Original filename for display in the prompt context (e.g., "contract-a.pdf").</param>
/// <param name="ContentType">Original MIME type. MUST be in the allow-list (see <see cref="ValidateAttachments"/>).</param>
/// <param name="TextContent">Client-extracted text content. Length capped per <see cref="MaxAttachmentTextCharsPerFile"/>.</param>
public record ChatMessageAttachment(
    string Filename,
    string ContentType,
    string TextContent);

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

/// <summary>Request body for PATCH /sessions/{id} (FR-D4, task 032 — rename).</summary>
/// <param name="Title">The new session title. Required, non-empty after trim, capped at <see cref="MaxTitleLength"/> characters.</param>
public record ChatRenameSessionRequest(string Title)
{
    /// <summary>
    /// Safety ceiling for a user-supplied rename. Deliberately more generous than
    /// <c>ChatHistoryManager.TitleMaxLength</c> (60) — that constant caps the CHEAP
    /// auto-generated/fallback title (3-6 words), whereas a user manually renaming a session
    /// may reasonably want a longer descriptive label. This cap exists only to bound storage/
    /// display, not to enforce the auto-gen word-count target.
    /// </summary>
    public const int MaxTitleLength = 200;
}

// The R2-052 per-action confirm request/response records were DELETED by D12 / FR-P2-02
// (task 031) together with their endpoint — the second confirmation store.
// The ONE gate is PendingPlanManager.

/// <summary>
/// Data payload for the <c>matter_context_change</c> SSE event (AIPU2-028, FR-408).
///
/// Emitted by <see cref="ChatEndpoints.SendMessageAsync"/> when the session pivots from one
/// legal matter to another.  The client uses this event to notify the user that retrieved
/// document references from the previous matter are no longer available in this conversation.
///
/// Wire format (camelCase JSON inside the SSE <c>data</c> field):
/// <code>
/// {
///   "type": "matter_context_change",
///   "content": null,
///   "data": {
///     "previousMatterId": "matter-a-guid",
///     "newMatterId": "matter-b-guid",
///     "message": "Matter context changed. Prior document details cleared from context for privilege protection."
///   }
/// }
/// </code>
/// </summary>
/// <param name="PreviousMatterId">The matter ID from the previous conversation context.</param>
/// <param name="NewMatterId">The matter ID of the new (incoming) context.</param>
/// <param name="Message">Human-readable notification for the user.</param>
public record ChatSseMatterContextChangeData(
    string PreviousMatterId,
    string NewMatterId,
    string Message);

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
/// Request body for PATCH /api/ai/chat/sessions/{sessionId}/tabs (NFR-09, task 065).
///
/// Field names use System.Text.Json camelCase by default ([JsonPropertyName] on the per-tab
/// record for explicitness). The frontend agent and BFF agent share this exact contract —
/// do not rename without coordinated update.
/// </summary>
/// <param name="Tabs">Non-Home workspace tabs in display order. Empty list clears the persisted tabs.</param>
/// <param name="ActiveTabId">Active tab id at save time. May be "home" or one of the Tabs entries' Id. Null = no active selection persisted.</param>
public record SessionTabsRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("tabs")] IReadOnlyList<SessionTabDto> Tabs,
    [property: System.Text.Json.Serialization.JsonPropertyName("activeTabId")] string? ActiveTabId);

/// <summary>
/// Response body for GET /api/ai/chat/sessions/{sessionId}/tabs (NFR-09, task 065).
/// </summary>
/// <param name="Tabs">Persisted non-Home tabs in display order. Empty for sessions that never persisted tabs.</param>
/// <param name="ActiveTabId">Persisted active tab id. May be "home", one of the Tabs entries' Id, or null.</param>
public record SessionTabsResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("tabs")] IReadOnlyList<SessionTabDto> Tabs,
    [property: System.Text.Json.Serialization.JsonPropertyName("activeTabId")] string? ActiveTabId);

/// <summary>
/// Wire DTO for a single persisted workspace tab — shared by PATCH request and GET response.
///
/// Identical field shape to <see cref="Sprk.Bff.Api.Services.Ai.Sessions.StoredWorkspaceTab"/>;
/// the endpoint handler maps between the two explicitly to make the wire contract visible at
/// the HTTP boundary.
/// </summary>
/// <param name="Id">Tab identifier (client-generated, stable across persist/restore).</param>
/// <param name="WidgetType">Widget kind to re-resolve via the client widget registry on restore.</param>
/// <param name="WidgetData">Opaque widget payload pass-through. Null if the widget has no state.</param>
/// <param name="DisplayName">Tab title displayed in the workspace tab strip.</param>
public record SessionTabDto(
    [property: System.Text.Json.Serialization.JsonPropertyName("id")] string Id,
    [property: System.Text.Json.Serialization.JsonPropertyName("widgetType")] string WidgetType,
    [property: System.Text.Json.Serialization.JsonPropertyName("widgetData")] System.Text.Json.JsonElement? WidgetData,
    [property: System.Text.Json.Serialization.JsonPropertyName("displayName")] string DisplayName);

/// <summary>
/// SSE event payload for chat streaming.
/// Serializes as: <c>{"type":"token","content":"..."}</c> or <c>{"type":"done"}</c>.
///
/// For richer event types (progress, document_replace), use the derived records below
/// which carry structured <c>Data</c> payloads. All event types are serialized through the
/// same <see cref="ChatEndpoints.WriteChatSSEAsync"/> method and share the SSE wire format.
/// </summary>
/// <param name="Type">Event type: "token", "done", "error", "typing_start", "typing_end", "suggestions", "citations", "progress", or "document_replace".</param>
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
/// Data payload for "suggestions" SSE events emitted after the main response completes
/// (spaarkeai-assistant-enhancements-r4 task 021a — FR-04). Contains up to a few TYPED follow-on
/// items — capability chips (a real selected <c>targetBindingId</c>), question chips (text that
/// re-enters the grounded loop), and the deterministic missing-context action chips. The frontend
/// (021b) renders these via <c>SprkChatSuggestions</c>, branching on <see cref="ChatSseFollowupItem.Kind"/>.
/// This REPLACES the retired untyped <c>string[]</c> payload the ungrounded free-string generator emitted.
/// </summary>
/// <param name="Suggestions">The ordered typed follow-on items (actions first, then capabilities, then questions). Empty events are not emitted.</param>
public record ChatSseSuggestionsData(IReadOnlyList<ChatSseFollowupItem> Suggestions);

/// <summary>
/// One TYPED follow-on item on the "suggestions" SSE event (task 021a). The <see cref="Kind"/> is
/// STRUCTURAL — the client renders and routes by it, never by a keyword heuristic on the label:
/// <list type="bullet">
///   <item><c>capability</c> — carries <see cref="TargetBindingId"/> (a real, model-selected Binding id);
///     clicking dispatches that Binding via the existing Click path. Guaranteed to work.</item>
///   <item><c>question</c> — carries only <see cref="Label"/> (a question); clicking re-enters the
///     grounded chat loop (safe by construction). No id.</item>
///   <item><c>action</c> — carries <see cref="ActionId"/> (<c>upload</c> | <c>search</c> | <c>select</c>);
///     the client special-routes it (file-input / document-search pane / matter picker). The deterministic
///     missing-context chips, re-typed from the legacy <c>"[action:*]"</c> string encoding.</item>
/// </list>
/// </summary>
/// <param name="Kind">The structural kind: <c>capability</c>, <c>question</c>, or <c>action</c>.</param>
/// <param name="Label">The chip text (imperative for capability/action; interrogative for question).</param>
/// <param name="TargetBindingId">The selected Binding id for a <c>capability</c>; null otherwise.</param>
/// <param name="ActionId">The deterministic action route (<c>upload</c>|<c>search</c>|<c>select</c>) for an <c>action</c>; null otherwise.</param>
public record ChatSseFollowupItem(
    string Kind,
    string Label,
    string? TargetBindingId = null,
    string? ActionId = null);

/// <summary>Response body for GET /playbooks — playbook discovery.</summary>
/// <param name="Playbooks">Available playbooks (user-owned + public, deduplicated).</param>
public record ChatPlaybookListResponse(ChatPlaybookInfo[] Playbooks);

// NOTE (FR-P2-07, task 036): the R2-018 "dialog_open" / "navigate" SSE payload records were
// DELETED with their sole emitter (the legacy typed-playbook-output router removed by the
// P2 hard cutover). Typed capability outputs now render via the loop's disposition
// vocabulary (Binding rows), not per-output-type SSE side channels.

/// <summary>
/// Playbook summary for the SprkChat playbook selector UI.
/// </summary>
/// <param name="Id">Playbook ID (GUID string).</param>
/// <param name="Name">Playbook display name.</param>
/// <param name="Description">Optional playbook description.</param>
/// <param name="IsPublic">Whether the playbook is public/shared.</param>
public record ChatPlaybookInfo(string Id, string Name, string? Description, bool IsPublic);

// =============================================================================
// FR-P2-03 loop-native elicitation records (task 032)
// =============================================================================

/// <summary>
/// One missing input field on an <c>elicitation_modal</c> SSE event. Name/prompt/type
/// come EXCLUSIVELY from the Binding's declared input schema (ADR-039 grounded
/// outputs — never invented fields).
/// </summary>
/// <param name="Name">Declared schema field name.</param>
/// <param name="Prompt">Maker-authored elicitation prompt (<c>elicitation_prompt</c> ?? <c>description</c>); null when undeclared.</param>
/// <param name="Type">Declared JSON-schema type token, when present.</param>
public record ChatSseElicitationFieldData(string Name, string? Prompt, string? Type);

/// <summary>
/// Data payload for the <c>elicitation_modal</c> SSE event (FR-P2-03): a capability
/// invocation with missing required args whose Binding declares
/// <c>capture_mode: modal</c> routes to the wizard surface instead of conversational
/// elicitation. The pending <c>elicitation</c> Gate marker is in the session ledger
/// BEFORE this event renders (ADR-040). The host wizard collects
/// <see cref="MissingFields"/> (pre-filling <see cref="ProvidedArgs"/>) and completes
/// by invoking the task-023 client dispatch helper —
/// <c>dispatchConsumer(bindingId, {slots})</c> — which resolves the gate at the
/// dispatch seam.
/// </summary>
/// <param name="GateId">Pending elicitation gate id (ledger correlation key).</param>
/// <param name="BindingId">Target <c>sprk_playbookconsumer</c> row GUID — the ONLY routing datum (ADR-039).</param>
/// <param name="ConsumerType">Stable consumer-type code for presentation (e.g. wizard title fallback).</param>
/// <param name="Title">The Binding's maker-authored tool description, when present.</param>
/// <param name="MissingFields">Declared required fields still missing.</param>
/// <param name="ProvidedArgs">Arguments the model already supplied (wizard pre-fill); null when none.</param>
public record ChatSseElicitationModalData(
    string GateId,
    string BindingId,
    string ConsumerType,
    string? Title,
    IReadOnlyList<ChatSseElicitationFieldData> MissingFields,
    JsonElement? ProvidedArgs);

/// <summary>
/// Data payload for the <c>surface_launch</c> SSE event (spaarkeai-assistant-enhancements-r1
/// P0(b) — the typed-path create-flow fix, 2026-07-17). A capability tool-call the agent turn
/// selected resolved to a Binding whose disposition is <c>surface_launch</c> — a client-owned
/// PASS-THROUGH: the server drafts + grounds a payload; the CLIENT opens the pre-seeded surface
/// (matter/event/task wizard, list-tasks grid, …). The Click path already branches on
/// <see cref="AnalysisChunk.Disposition"/> == <c>surface_launch</c> (task 013); this event is the
/// TEXT/agent-path analogue — <see cref="Sprk.Bff.Api.Services.Ai.Chat.BindingCapabilityTool"/>
/// emits it (mirroring <c>elicitation_modal</c>) so the SpaarkeAi chat client calls
/// <c>launchSurface(consumerType, payload)</c> with the SAME static registry the chip path uses
/// (surface-launch-mechanism §3; §10 keeps deployment-specific web-resource names client-side).
/// The enriched draft payload (draft slot values + task-013 <c>resolvedLookups</c> + <c>fileIds</c>)
/// is carried VERBATIM so the surface pre-fills without a second server round-trip. The
/// SessionOutput ledger entry was written BEFORE this event renders (ADR-040).
/// </summary>
/// <param name="BindingId">The dispatched <c>sprk_playbookconsumer</c> row GUID (audit/trace).</param>
/// <param name="ConsumerType">The Binding's <c>sprk_consumertype</c> — the client maps it to a concrete launch surface via its static registry.</param>
/// <param name="Payload">The enriched draft payload to pre-seed the launched surface; null when the dispatch produced no structured payload.</param>
public record ChatSseSurfaceLaunchData(
    string BindingId,
    string ConsumerType,
    JsonElement? Payload);

// =============================================================================
// FR-P2-02 loop-boundary confirmation-gate records (task 037 / FR-P2-08)
// =============================================================================

/// <summary>
/// Data payload for the <c>action_confirmation</c> SSE event: a side-effecting
/// typed-handler tool invocation was SUSPENDED into the unified confirmation gate
/// (<see cref="Sprk.Bff.Api.Services.Ai.Chat.SideEffectGateAIFunction"/>) instead of
/// executing. The pending <c>SessionGate</c> ledger marker is written BEFORE this
/// event renders (ADR-040). The client ActionConfirmationDialog resolves it via
/// <c>POST /sessions/{sessionId}/gates/{gateId}/resolve</c> (task 032) — reject
/// closes the gate; confirm on a non-Binding invocation closes the gate
/// <c>confirmed-unexecutable</c> (approval recorded, execution honestly unavailable)
/// and returns 422 <c>gate.no-binding-target</c>, which the client renders as an
/// honest transcript message, until the P3 typed-handler resume seam (FR-P3-03) lands.
/// Field names mirror the client <c>IActionConfirmationPayload</c> contract.
/// </summary>
/// <param name="ActionId">The gate id (ledger correlation key + resolve-route parameter).</param>
/// <param name="ActionName">The suspended tool's LLM-facing function name (identifier only).</param>
/// <param name="Summary">User-renderable one-liner (NFR-07-safe argument summary; free text redacted).</param>
/// <param name="Parameters">Reserved presentation slot (client contract compatibility); values stay in the Tier-3 store.</param>
public record ChatSseActionConfirmationData(
    string ActionId,
    string ActionName,
    string Summary,
    IReadOnlyDictionary<string, string> Parameters);

/// <summary>
/// SSE presentation payload for an INLINE AUTO-EXECUTED side effect (spaarke-ai-architecture-redesign-r2
/// task 044, gate G-R2-A). Emitted by <see cref="Sprk.Bff.Api.Services.Ai.Chat.SideEffectGateAIFunction"/>
/// AFTER the deterministic Confirmation Policy v2 engine resolved the invocation to
/// <c>Execute</c> / <c>ExecuteWithUndo</c> and the tool ran WITHOUT a confirmation dialog — the
/// counterpart to <see cref="ChatSseActionConfirmationData"/> for the no-dialog path. It carries the
/// task-035 <see cref="Sprk.Bff.Api.Services.Ai.PublicContracts.OutcomeCard"/>'s user-facing projection:
/// the audience-split user sentence, the SERVER-composed record link (the Undo target for a reversible
/// Tier 2a/2b create, or the review-and-send record for a Tier-1 email draft — never a model-invented
/// URL, ADR-040/NFR-07), and the declared affordance chips (e.g. Undo). Store-before-render: the
/// referenced <c>loop@t{n}</c> ledger output is persisted BEFORE this event is emitted. Field names
/// mirror <see cref="Sprk.Bff.Api.Services.Ai.PublicContracts.OutcomeCardView"/> so Compose r2 consumes
/// the same shape it already renders on the gate-resolve HTTP response.
/// </summary>
/// <param name="ActionName">The executed tool's LLM-facing function name (identifier only).</param>
/// <param name="Status">Terminal disposition token (<c>succeeded</c> / <c>partial</c> / <c>failed</c>).</param>
/// <param name="UserSummary">User-facing outcome sentence (rendered verbatim; never the internal detail).</param>
/// <param name="LinkUrl">Server-composed record/deep link (Undo target / email review-and-send record); null when none.</param>
/// <param name="LinkLabel">Human-readable link label; null when no link.</param>
/// <param name="NextSteps">Declared affordance chip labels (e.g. <c>Undo</c>) the surface may render.</param>
/// <param name="LedgerOutputKey">The stored <c>loop@t{n}</c> ledger key this card renders (ADR-040 store-before-render).</param>
public record ChatSseActionOutcomeData(
    string ActionName,
    string Status,
    string UserSummary,
    string? LinkUrl,
    string? LinkLabel,
    IReadOnlyList<string> NextSteps,
    string LedgerOutputKey);

/// <summary>Request body for <c>POST /sessions/{sessionId}/gates/{gateId}/resolve</c>.</summary>
/// <param name="Approved">True = confirm and execute the suspended invocation; false = reject it.</param>
public record GateResolveRequest(bool Approved);

/// <summary>Result of a unified-gate resolution.</summary>
/// <param name="Status"><c>confirmed</c> or <c>rejected</c>.</param>
/// <param name="Summary">
/// Terminal output summary (already ledger-written per ADR-040); null on reject.
/// For typed-handler confirms this is the handler's USER-facing outcome sentence
/// (R4-6, 2026-07-07) — safe to render verbatim in the transcript.
/// </param>
/// <param name="RecordUrl">
/// Server-composed MDA deep link to the created/updated record (R4-3, 2026-07-07;
/// additive — null when the handler reported no record or the environment URL is
/// unknown). See <c>TypedHandlerResumeExecutor.ResumeOutcome.RecordUrl</c> for the
/// seam decision (server-composed, no appid).
/// </param>
/// <param name="RecordEntityLogicalName">Created/updated record's table logical name (additive, R4-3).</param>
/// <param name="RecordId">Created/updated record's GUID as <c>D</c>-format string (additive, R4-3).</param>
/// <param name="Outcome">
/// The Completion Engine's structured <see cref="OutcomeCard"/> for a confirmed side-effect
/// (spaarke-ai-architecture-redesign-r2 task 035 / FR-A1-06; additive — null on reject, on the
/// Binding-dispatch leg, and when the ledger write degraded). The client renders this card
/// (server-composed link chip + next-step chips) in place of the markdown "[Open record]" link;
/// the <see cref="RecordUrl"/>/<see cref="Summary"/> fields remain as the fallback.
/// </param>
public record GateResolveResult(
    string Status,
    string? Summary,
    string? RecordUrl = null,
    string? RecordEntityLogicalName = null,
    string? RecordId = null,
    OutcomeCard? Outcome = null);
