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

    // =========================================================================
    // chat-routing-redesign-r1 task 074 — Upload-pipeline `context.upload_*` events
    // =========================================================================
    //
    // ADR-015 BINDING (Tier 1 SAFE only): every method below carries deterministic IDs +
    // lengths + counts + durationMs ONLY. NEVER textContent, summaryText, classification
    // label text body (the short enum label like "NDA" / "Contract" is allowed; freeform
    // classifier text is NOT), recall results, file content, or user message content. The
    // method signatures are structurally constrained — no <c>object</c>, <c>JsonElement</c>,
    // or free-form <c>string content</c> parameters.
    //
    // Architecture §4.1 T6 + §7.1 audit container + P7 binding principle on telemetry safety.

    /// <summary>
    /// Emits <c>context.upload_started</c>. Called by
    /// <see cref="Api.Ai.ChatDocumentEndpoints.UploadDocumentAsync"/> after validation passes
    /// and BEFORE text extraction begins.
    /// </summary>
    /// <param name="sessionId">Chat session GUID (may be null when emitter is invoked outside chat).</param>
    /// <param name="fileId">Deterministic file identifier (GUID/N).</param>
    /// <param name="contentType">MIME content-type enum-like short string ("application/pdf", "text/plain", etc.).</param>
    /// <param name="fileSizeBytes">Binary file size in bytes (numeric metric only).</param>
    /// <param name="tenantId">Opaque tenant identifier (ADR-014 cache-key partition).</param>
    void UploadStarted(Guid? sessionId, string fileId, string contentType, long fileSizeBytes, string? tenantId);

    /// <summary>
    /// Emits <c>context.upload_classified</c>. Called by the classification service AFTER
    /// classification completes (NOT yet wired — service does not exist in code as of task 074).
    /// </summary>
    /// <param name="sessionId">Chat session GUID.</param>
    /// <param name="fileId">Deterministic file identifier.</param>
    /// <param name="documentType">Enum-like classification label ("NDA", "Contract", "Memo", etc.). MUST be a short enum string — NOT a freeform classifier text body (ADR-015 binding).</param>
    /// <param name="confidence">Numeric confidence score (0.0..1.0).</param>
    /// <param name="durationMs">Wall-clock duration in milliseconds.</param>
    /// <param name="tenantId">Opaque tenant identifier.</param>
    void UploadClassified(Guid? sessionId, string fileId, string documentType, double confidence, long durationMs, string? tenantId);

    /// <summary>
    /// Emits <c>context.upload_summarized</c>. Called by the summarization service AFTER
    /// summarization completes (NOT yet wired — service does not exist in code as of task 074).
    ///
    /// <para>ADR-015 BINDING: <paramref name="summaryCharCount"/> is the LENGTH ONLY — NEVER
    /// the summary text body. Implementations MUST NOT log or tag the summary text.</para>
    /// </summary>
    /// <param name="sessionId">Chat session GUID.</param>
    /// <param name="fileId">Deterministic file identifier.</param>
    /// <param name="summaryCharCount">Character count of the summary (numeric metric only — NEVER the summary text).</param>
    /// <param name="durationMs">Wall-clock duration in milliseconds.</param>
    /// <param name="tenantId">Opaque tenant identifier.</param>
    void UploadSummarized(Guid? sessionId, string fileId, int summaryCharCount, long durationMs, string? tenantId);

    /// <summary>
    /// Emits <c>context.upload_manifest_extracted</c>. Called by the manifest-extractor service
    /// AFTER extraction completes (NOT yet wired — service does not exist in code as of task 074).
    /// </summary>
    /// <param name="sessionId">Chat session GUID.</param>
    /// <param name="fileId">Deterministic file identifier.</param>
    /// <param name="sectionCount">Number of sections extracted (numeric metric only).</param>
    /// <param name="tableCount">Number of tables extracted (numeric metric only).</param>
    /// <param name="pageCount">Number of pages in the document (numeric metric only).</param>
    /// <param name="durationMs">Wall-clock duration in milliseconds.</param>
    /// <param name="tenantId">Opaque tenant identifier.</param>
    void UploadManifestExtracted(Guid? sessionId, string fileId, int sectionCount, int tableCount, int pageCount, long durationMs, string? tenantId);

    /// <summary>
    /// Emits <c>context.upload_indexed</c>. Called by
    /// <see cref="Api.Ai.ChatDocumentEndpoints.UploadDocumentAsync"/> AFTER
    /// <c>RagIndexingPipeline.IndexSessionFileAsync</c> returns.
    /// </summary>
    /// <param name="sessionId">Chat session GUID.</param>
    /// <param name="fileId">Deterministic file identifier.</param>
    /// <param name="chunkCount">Number of knowledge chunks indexed (numeric metric only).</param>
    /// <param name="durationMs">Wall-clock duration in milliseconds.</param>
    /// <param name="tenantId">Opaque tenant identifier.</param>
    void UploadIndexed(Guid? sessionId, string fileId, int chunkCount, long durationMs, string? tenantId);

    /// <summary>
    /// Emits <c>context.upload_persisted</c>. Called by
    /// <see cref="Sessions.SessionPersistenceService.UpdateUploadedFilesAsync"/> AFTER the
    /// uploaded-files manifest write-through (Redis hot + Cosmos warm) completes.
    /// </summary>
    /// <param name="sessionId">Chat session GUID.</param>
    /// <param name="fileId">Deterministic file identifier (per-manifest entry; pass the most-recent enriched file's id, or empty string when bulk).</param>
    /// <param name="durationMs">Wall-clock duration in milliseconds.</param>
    /// <param name="tenantId">Opaque tenant identifier.</param>
    void UploadPersisted(Guid? sessionId, string fileId, long durationMs, string? tenantId);

    /// <summary>
    /// Emits <c>context.upload_completed</c>. Called by
    /// <see cref="Api.Ai.ChatDocumentEndpoints.UploadDocumentAsync"/> immediately before
    /// returning the 202 Accepted response (end-of-pipeline marker).
    /// </summary>
    /// <param name="sessionId">Chat session GUID.</param>
    /// <param name="fileId">Deterministic file identifier.</param>
    /// <param name="totalDurationMs">Total wall-clock duration of the upload pipeline (start → completed) in milliseconds.</param>
    /// <param name="tenantId">Opaque tenant identifier.</param>
    void UploadCompleted(Guid? sessionId, string fileId, long totalDurationMs, string? tenantId);
}
