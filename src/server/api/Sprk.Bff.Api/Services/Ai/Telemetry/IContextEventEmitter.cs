using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Services.Ai.Telemetry;

/// <summary>
/// R6 Pillar 6c (FR-37 / task 063) — emits the six `context.*` execution-trace events
/// from BFF telemetry sites to feed the ExecutionTraceWidget (Context pane, task 061).
///
/// Event categories (matches <c>PaneEventTypes.ts</c> ContextPaneEvent discriminants):
/// <list type="bullet">
///   <item><c>tool_call_started</c> — chat agent has invoked a registered tool.</item>
///   <item><c>tool_call_completed</c> — tool invocation has returned.</item>
///   <item><c>knowledge_retrieved</c> — knowledge retrieval has produced one result.</item>
///   <item><c>playbook_node_executing</c> — a playbook node has started executing.</item>
///   <item><c>playbook_node_completed</c> — a playbook node has finished.</item>
///   <item><c>decision_made</c> — the agent made an enumerated decision.</item>
/// </list>
///
/// <para>
/// <b>ADR-015 BINDING (non-negotiable)</b>: implementations MUST only carry deterministic
/// IDs, numeric metrics, enum-like short strings, and ISO-8601 timestamps in event payloads
/// and meter tags. Implementations MUST NEVER carry user-message text, tool input/output
/// bodies, retrieved chunk text, LLM response text, or any other Tier 3 content. The method
/// signatures below are structurally constrained to enforce this — none accept
/// <c>object</c>, <c>string content</c>, or <c>JsonElement payload</c> parameters.
/// </para>
///
/// <para>
/// <b>ADR-030</b>: events are emitted on the existing 4-channel PaneEventBus model. The
/// `context` channel discriminants are additive (task 059); no 5th channel is introduced.
/// Wire transport (SSE → PaneEventBus dispatch) is handled by the chat SSE pipe consuming
/// the meter + log surface this emitter populates. This emitter is the BFF-side write
/// surface; subscription happens downstream.
/// </para>
///
/// <para>
/// <b>Lifetime</b>: singleton. Meter + Logger are thread-safe; implementations should
/// share counters across calls.
/// </para>
/// </summary>
public interface IContextEventEmitter
{
    /// <summary>
    /// Emits <c>context.tool_call_started</c>. Called by
    /// <see cref="Chat.ToolHandlerToAIFunctionAdapter"/> BEFORE handler dispatch.
    /// </summary>
    /// <param name="toolName">Tool name (sourced from <c>sprk_analysistool.sprk_name</c>).</param>
    /// <param name="decisionId">Per-invocation correlation GUID.</param>
    /// <param name="sessionId">Chat session GUID (may be null when emitter is invoked outside chat).</param>
    /// <param name="tenantId">Opaque tenant identifier (ADR-014 cache-key partition).</param>
    void ToolCallStarted(string toolName, Guid decisionId, Guid? sessionId, string? tenantId);

    /// <summary>
    /// Emits <c>context.tool_call_completed</c>. Called by the adapter AFTER handler dispatch.
    /// </summary>
    /// <param name="toolName">Tool name (sourced from <c>sprk_analysistool.sprk_name</c>).</param>
    /// <param name="decisionId">Per-invocation correlation GUID (matches the started event).</param>
    /// <param name="sessionId">Chat session GUID (may be null when outside chat).</param>
    /// <param name="tenantId">Opaque tenant identifier.</param>
    /// <param name="outcome">Outcome enum-like short string ("ok", "error", "validation_failed", "cancelled", "exception").</param>
    /// <param name="durationMs">Wall-clock duration in milliseconds.</param>
    void ToolCallCompleted(string toolName, Guid decisionId, Guid? sessionId, string? tenantId, string outcome, long durationMs);

    /// <summary>
    /// Emits <c>context.knowledge_retrieved</c>. Called by
    /// <see cref="Capabilities.CapabilityRouter"/> Layer 2 RAG path (and any other
    /// knowledge-retrieval emission site) for each retrieved result.
    /// </summary>
    /// <param name="knowledgeSourceId">Deterministic identifier of the source (e.g., chunk ID, document ID).</param>
    /// <param name="relevanceScore">Search relevance score (0.0..1.0 typical).</param>
    /// <param name="resultCount">Number of results in this retrieval batch.</param>
    /// <param name="sessionId">Chat session GUID (may be null).</param>
    /// <param name="tenantId">Opaque tenant identifier.</param>
    void KnowledgeRetrieved(string knowledgeSourceId, double relevanceScore, int resultCount, Guid? sessionId, string? tenantId);

    /// <summary>
    /// Emits <c>context.playbook_node_executing</c>. Called by
    /// <see cref="PlaybookOrchestrationService"/> (NOT inside any node executor — NFR-08).
    /// </summary>
    /// <param name="playbookId">Playbook GUID.</param>
    /// <param name="nodeId">Node GUID inside the playbook graph.</param>
    /// <param name="nodeType">Node type enum-like short string ("AIAnalysis", "Output", "Control", "Workflow").</param>
    /// <param name="sessionId">Chat session GUID (may be null when invoked outside chat).</param>
    /// <param name="tenantId">Opaque tenant identifier.</param>
    void PlaybookNodeExecuting(Guid playbookId, Guid nodeId, string nodeType, Guid? sessionId, string? tenantId);

    /// <summary>
    /// Emits <c>context.playbook_node_completed</c>. Called by
    /// <see cref="PlaybookOrchestrationService"/> AFTER node dispatch (NOT inside executors).
    /// </summary>
    /// <param name="playbookId">Playbook GUID.</param>
    /// <param name="nodeId">Node GUID inside the playbook graph.</param>
    /// <param name="decision">Enum-like short string: "success", "failed", "skipped".</param>
    /// <param name="durationMs">Wall-clock duration in milliseconds.</param>
    /// <param name="sessionId">Chat session GUID (may be null).</param>
    /// <param name="tenantId">Opaque tenant identifier.</param>
    void PlaybookNodeCompleted(Guid playbookId, Guid nodeId, string decision, long durationMs, Guid? sessionId, string? tenantId);

    /// <summary>
    /// Emits <c>context.decision_made</c>. Called by
    /// <see cref="Capabilities.CapabilityRouter"/> at Layer 1 / Layer 2 / Layer 3 decision
    /// points.
    /// </summary>
    /// <param name="layer">Layer enum-like short string ("layer1", "layer2", "layer3").</param>
    /// <param name="decision">Decision enum-like short string ("confident", "uncertain", "fallback", "rate_limited", "timeout").</param>
    /// <param name="capabilityName">Resolved capability name (or null if no capability was selected — Layer 3 fallback / uncertain).</param>
    /// <param name="sessionId">Chat session GUID (may be null).</param>
    /// <param name="tenantId">Opaque tenant identifier.</param>
    void DecisionMade(string layer, string decision, string? capabilityName, Guid? sessionId, string? tenantId);
}
