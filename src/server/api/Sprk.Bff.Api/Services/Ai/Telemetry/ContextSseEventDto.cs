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

    // ── tool_chain fields (ai-architecture-redesign-r1 task 046 / FR-P3-07) ──
    //
    // Carried when ContextEventType == "tool_chain": the ledger ToolChain segment
    // that was JUST persisted by ChatEndpoints.FlushToolChainLedgerAsync (ADR-040
    // storage-precedes-rendering — the ledger write completes BEFORE this frame is
    // written). The ExecutionTraceWidget renders these persisted records verbatim;
    // it never synthesizes trace data client-side.
    //
    // NFR-07 / ADR-015 BINDING: every member mirrors SessionToolCall's
    // identifiers/filters/counts-only contract (ArgsSummary is produced by
    // AgentTurnContract.SummarizeArguments — redaction enforced at recording time).
    // Citations are carried as a COUNT, not ids, on the wire.

    /// <summary>1-based session turn the persisted ToolChain segment belongs to (tool_chain).</summary>
    public int? ContextTurn { get; init; }

    /// <summary>The persisted ledger ToolChain segment's calls (tool_chain). Identifiers/counts only.</summary>
    public IReadOnlyList<ContextToolChainCallDto>? ContextToolChainCalls { get; init; }

    // ── workspace_open_tab fields (G-P3 UAT round-2 R2-D, 2026-07-07) ─────────
    //
    // Carried when ContextEventType == "workspace_open_tab": the loop-invoked
    // SendWorkspaceArtifactHandler asks the client to materialize a workspace tab
    // LIVE. The post-046 SpaarkeAi client owns its tab state (restore-on-mount +
    // PATCH write-through — no polling channel exists anymore), so the server's
    // only path to the tab strip is this SSE frame → useContextEventBridge →
    // PaneEventBus `workspace.widget_load` → WorkspaceTabManager.addTab. ADR-030
    // additive: old clients ignore the unknown discriminant.
    //
    // ADR-015: ContextWidgetType is a registry key, ContextDisplayName a tab
    // title (user-visible UI content, not telemetry), ContextTabId a server-
    // generated GUID, and ContextWidgetDataJson carries identifiers only for the
    // v1 'workspace' variant ({layoutId, layoutName}).

    /// <summary>Client workspace-widget registry key (workspace_open_tab), e.g. "workspace".</summary>
    public string? ContextWidgetType { get; init; }

    /// <summary>Tab display title (workspace_open_tab).</summary>
    public string? ContextDisplayName { get; init; }

    /// <summary>Server-generated tab correlation id (workspace_open_tab).</summary>
    public string? ContextTabId { get; init; }

    /// <summary>JSON-serialized widgetData payload for the tab's widget (workspace_open_tab).</summary>
    public string? ContextWidgetDataJson { get; init; }
}

/// <summary>
/// One persisted <c>SessionToolCall</c> ledger record projected onto the
/// <c>context_event</c> SSE wire (task 046 / FR-P3-07). Identifiers / filters /
/// counts / durations ONLY — mirrors the NFR-07 contract enforced at recording
/// time by <c>AgentTurnContract</c>.
/// </summary>
public sealed record ContextToolChainCallDto
{
    /// <summary>Namespaced tool id (<c>sprk_analysistool</c> row / handler id).</summary>
    public required string ToolId { get; init; }

    /// <summary>NFR-07-safe identifier/filter summary (redacted at recording time).</summary>
    public string? ArgsSummary { get; init; }

    /// <summary>Number of results the tool returned, when countable.</summary>
    public int? ResultCount { get; init; }

    /// <summary>Count of citation ids the call emitted (ids stay in the ledger; count on the wire).</summary>
    public int? CitationCount { get; init; }

    /// <summary>Wall-clock duration of the call in milliseconds.</summary>
    public long? DurationMs { get; init; }
}
