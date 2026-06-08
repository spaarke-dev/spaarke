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
}
