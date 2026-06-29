namespace Sprk.Bff.Api.Services.Ai.Telemetry;

/// <summary>
/// R6 DEF-001 / task 095 Phase 3 — Per-request SSE payload that carries one of the six
/// typed context.* trace events from <see cref="ContextEventEmitter"/> through the
/// scoped <see cref="IContextSseRelay"/> to the chat SSE stream as a
/// <c>"context_event"</c> frame.
///
/// <para>
/// Field shape mirrors the frontend <c>IChatSseEventData</c> context_event sub-shape
/// (see <c>Spaarke.UI.Components/src/components/SprkChat/types.ts</c>): one
/// <c>contextEventType</c> discriminant + 13 typed sub-fields. The
/// <see cref="ChatEndpoints"/> JSON options enforce
/// <c>JsonNamingPolicy.CamelCase</c>, so the PascalCase properties below auto-serialize
/// to <c>contextEventType</c> / <c>contextTimestamp</c> / etc.
/// </para>
///
/// <para>
/// <b>ADR-015 binding</b>: this DTO carries typed enumerated fields, deterministic IDs,
/// numeric metrics, and ISO-8601 timestamps ONLY — never user-message text, tool
/// input/output bodies, retrieved chunk text, or LLM response text.
/// </para>
///
/// <para>
/// <b>ADR-030 compliance</b>: events are forwarded to the existing 4-channel
/// PaneEventBus (<c>context</c> channel). The frontend <c>SprkChat</c> host receives
/// the <c>context_event</c> SSE frame and dispatches a typed
/// <c>ContextPaneEvent</c> sub-shape via the bus — no 5th channel is introduced.
/// </para>
///
/// <para>
/// <b>ADR-033 streaming side-channel precedent</b>: the per-request sink design follows
/// the same pattern as <see cref="Chat.ChatInvocationContext"/>'s
/// <c>DocumentStreamWriter</c> — a scoped relay set at SSE stream start, cleared on
/// stream end, never carrying cross-request state.
/// </para>
/// </summary>
public sealed record ContextSseEventDto
{
    /// <summary>
    /// Discriminant — one of the six R6 task 059 / 063 sub-types.
    /// </summary>
    public string? ContextEventType { get; init; }

    /// <summary>ISO-8601 UTC timestamp of the trace event.</summary>
    public string? ContextTimestamp { get; init; }

    /// <summary>Registered tool name (tool_call_* events).</summary>
    public string? ContextToolName { get; init; }

    /// <summary>Tool / decision correlation GUID (tool_call_*, decision_made).</summary>
    public string? ContextDecisionId { get; init; }

    /// <summary>Tool outcome enum (tool_call_completed).</summary>
    public string? ContextOutcome { get; init; }

    /// <summary>Wall-clock duration ms (tool_call_completed, playbook_node_completed).</summary>
    public long? ContextDurationMs { get; init; }

    /// <summary>Knowledge source identifier (knowledge_retrieved).</summary>
    public string? ContextKnowledgeSourceId { get; init; }

    /// <summary>Numeric relevance score 0..1 (knowledge_retrieved).</summary>
    public double? ContextRelevanceScore { get; init; }

    /// <summary>Result count (knowledge_retrieved).</summary>
    public int? ContextResultCount { get; init; }

    /// <summary>Playbook GUID, string form (playbook_node_*).</summary>
    public string? ContextPlaybookId { get; init; }

    /// <summary>Node GUID, string form (playbook_node_*).</summary>
    public string? ContextNodeId { get; init; }

    /// <summary>Node type enum (playbook_node_executing).</summary>
    public string? ContextNodeType { get; init; }

    /// <summary>Routing-layer identifier (decision_made).</summary>
    public string? ContextLayer { get; init; }

    /// <summary>Decision enum-like string (decision_made).</summary>
    public string? ContextDecision { get; init; }

    /// <summary>Capability name (decision_made).</summary>
    public string? ContextCapabilityName { get; init; }
}
