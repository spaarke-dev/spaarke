using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Invocation context for tool handlers invoked from the chat-agent path
/// (LLM function-calling).
/// </summary>
/// <remarks>
/// <para>
/// Introduced by R6 Pillar 2 task D-A-09 (FR-09). Sister type of
/// <see cref="ToolExecutionContext"/>; both derive from
/// <see cref="ToolInvocationContextBase"/> for shared identity / LLM-parameter fields.
/// </para>
/// <para>
/// The chat path builds this context inside the
/// <c>ToolHandlerToAIFunctionAdapter</c> (task D-A-10): when the LLM invokes a wrapped
/// <see cref="IToolHandler"/>, the adapter constructs <see cref="ChatInvocationContext"/>
/// from the active chat session + the LLM's tool-call arguments, then dispatches via
/// the handler's chat-context overload.
/// </para>
/// <para>
/// <strong>ADR-015 binding</strong>: this context exposes only IDs and handle
/// references. It MUST NOT carry raw user message text, full conversation transcript,
/// or any content that crosses the data-governance boundary. Telemetry from handlers
/// that consume this context emits tool name + decision + timestamp only; deterministic
/// IDs (tenantId, sessionId, matterId, decisionId) are acceptable.
/// </para>
/// <para>
/// Per ADR-013, this type is AI-internal — not exposed through the
/// <c>Services/Ai/PublicContracts/</c> facade.
/// </para>
/// </remarks>
public record ChatInvocationContext : ToolInvocationContextBase
{
    /// <summary>
    /// The chat session this tool invocation belongs to. Forwards to the base
    /// <see cref="ToolInvocationContextBase.InvocationId"/> storage so existing
    /// correlation pipelines see a consistent identifier — symmetric to
    /// <see cref="ToolExecutionContext.AnalysisId"/>.
    /// </summary>
    /// <remarks>
    /// Required at construction; this is the chat session identifier (not the per-call
    /// decision identifier — see <see cref="DecisionId"/>).
    /// </remarks>
    public required Guid ChatSessionId
    {
        get => InvocationId;
        init => InvocationId = value;
    }

    /// <summary>
    /// Per-tool-call decision id used for telemetry correlation. Distinct from
    /// <see cref="ChatSessionId"/> because one chat session may dispatch multiple
    /// tool calls. Auto-generated if not supplied.
    /// </summary>
    public Guid DecisionId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Opaque handle (NOT content) referencing the conversation history available in the
    /// chat-session store. Handlers that need conversation context retrieve it through
    /// the chat-session-aware service surface using this handle.
    /// </summary>
    /// <remarks>
    /// ADR-015: storing only a handle (not text) keeps user-message content out of
    /// the cross-pillar invocation pathway. Null when no conversation history is
    /// relevant (e.g., first-turn invocations).
    /// </remarks>
    public string? ConversationHistoryRef { get; init; }

    /// <summary>
    /// The Dataverse-stored AnalysisTool name the LLM invoked (echoes
    /// <c>AnalysisTool.Name</c>). Used for telemetry + dispatch routing.
    /// </summary>
    public string? RequestedToolName { get; init; }

    /// <summary>
    /// Raw tool-call arguments provided by the LLM, as JSON.
    /// </summary>
    /// <remarks>
    /// Validated against the <c>AnalysisTool.JsonSchema</c> at the adapter boundary
    /// (task D-A-10). The schema constrains argument structure so the LLM cannot inject
    /// arbitrary user text into a tool argument and bypass ADR-015 governance — the
    /// adapter rejects arguments not matching the declared schema.
    /// </remarks>
    public string? ToolArgumentsJson { get; init; }

    /// <summary>
    /// Optional matter / workspace scope id for tenant-isolated routing within chat.
    /// Deterministic id only (no user content). Null when chat invocation is not
    /// matter-scoped.
    /// </summary>
    public Guid? MatterId { get; init; }

    /// <summary>
    /// Optional owning user identifier for user-curated chat affordances (R6 Pillar 7 /
    /// task 069 / FR-47). Sourced from the request principal's <c>oid</c> claim at
    /// <see cref="Chat.SprkChatAgentFactory"/> resolution time and forwarded into the per-call
    /// context by <see cref="Chat.ToolHandlerToAIFunctionAdapter"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Carries a deterministic principal identifier only (the Azure AD <c>oid</c> GUID rendered
    /// as a string). ADR-015 compliant — never user message text. Null when standalone chat
    /// (no authenticated user) or when the <c>oid</c> claim is unavailable.
    /// </para>
    /// <para>
    /// Consumed by chat-side handlers that read or write user-scoped data (currently
    /// <c>ManagePinnedContextHandler</c> for the "remember / forget / always" affordance —
    /// the <see cref="Memory.IPinnedContextRepository"/> contract requires a non-empty
    /// <c>userId</c> on pin creation and partitions reads by user). Handlers that REQUIRE this
    /// field MUST short-circuit with a clear diagnostic (<see cref="ToolResult.Error"/>) when
    /// null — never throw, and never fall back to a synthetic sentinel id.
    /// </para>
    /// <para>
    /// Mirrors the shape of <see cref="MatterId"/> + <see cref="AnalysisId"/>: optional,
    /// init-only, deterministic identifier.
    /// </para>
    /// </remarks>
    public string? UserId { get; init; }

    /// <summary>
    /// Optional analysis id from the active chat session (R6 Wave 9 / ADR-033 Stage 4).
    /// Carries the deterministic <c>sprk_analysisoutput</c> row id when the chat session is
    /// bound to an active analysis. Read by chat-side handlers that fetch or persist
    /// working-document content (currently <c>WorkingDocumentHandler</c>) via the existing
    /// <c>IAnalysisOrchestrationService</c> / <c>IWorkingDocumentService</c> surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mirrors the shape of <see cref="MatterId"/>: optional, init-only, Guid identifier
    /// (no user content). Sourced from <c>ChatContext.AnalysisMetadata["analysisId"]</c>
    /// at <see cref="Chat.SprkChatAgentFactory"/> resolution time; forwarded into the
    /// per-call context by <see cref="Chat.ToolHandlerToAIFunctionAdapter"/> when wrapping
    /// a chat-tool handler.
    /// </para>
    /// <para>
    /// Null when standalone chat (no analysis bound) or when AnalysisMetadata doesn't carry
    /// the id. Handlers that REQUIRE it MUST short-circuit with a clear diagnostic
    /// (<c>ToolResult.Failure</c>) — never throw or fall back to a sentinel guid.
    /// </para>
    /// </remarks>
    public Guid? AnalysisId { get; init; }

    /// <summary>
    /// Optional playbook knowledge scope (R6 Wave 7c). When the chat session is bound to a
    /// playbook with knowledge sources, the data-driven block of
    /// <see cref="Chat.SprkChatAgentFactory"/> forwards the resolved
    /// <see cref="ChatKnowledgeScope"/> here so chat-side handlers (KnowledgeRetrieval,
    /// DocumentSearch, etc.) can filter their queries to the playbook's knowledge sources
    /// without taking a separate DI dependency.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Carries deterministic identifiers (knowledge-source IDs, document IDs, parent-entity
    /// refs) + the playbook's inline-content / skill-instructions text. ADR-015 binding:
    /// handlers reading this MUST NOT log the inline text. Telemetry remains IDs + counts only.
    /// </para>
    /// <para>
    /// Null when no playbook is bound (standalone chat) or when the playbook has no knowledge
    /// scopes configured — handlers SHOULD interpret null as "no scope filter" and fall back to
    /// tenant-wide retrieval (subject to their own contract).
    /// </para>
    /// </remarks>
    public ChatKnowledgeScope? KnowledgeScope { get; init; }

    /// <summary>
    /// Optional per-request writer for document-stream SSE side-channel events.
    /// Bound by ChatEndpoints when the active session/playbook is write-back-capable.
    /// Handlers that emit DocumentStreamEvent (currently WorkingDocumentHandler) read this
    /// field and emit directly during streaming. Null when document streaming is not wired
    /// for the current request — handlers MUST check for null and degrade gracefully.
    /// </summary>
    /// <remarks>
    /// ADR-033 binding pattern. ADR-015: this delegate is a side-effect emitter; it MUST
    /// NOT be invoked from a logging context (the delegate writes content to the SSE pipe,
    /// not to the structured-log sink).
    /// </remarks>
    public Func<Models.Ai.Chat.DocumentStreamEvent, CancellationToken, Task>? DocumentStreamWriter { get; init; }
}
