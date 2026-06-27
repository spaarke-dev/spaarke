using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Chat.SseEventTypes;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Orchestrates playbook execution supporting both Legacy and NodeBased modes.
/// Implements ADR-013 AI Architecture with node-based multi-action orchestration.
/// </summary>
/// <remarks>
/// <para>
/// Mode detection:
/// </para>
/// <list type="bullet">
/// <item><b>Legacy mode</b>: Playbook has no nodes (uses existing N:N relationships).
/// Delegates to <see cref="IAnalysisOrchestrationService.ExecutePlaybookAsync"/>.</item>
/// <item><b>NodeBased mode</b>: Playbook has nodes in sprk_playbooknode entity.
/// Uses <see cref="ExecutionGraph"/> for topological ordering and executes each node
/// via <see cref="Nodes.INodeExecutorRegistry"/>.</item>
/// </list>
/// </remarks>
public interface IPlaybookOrchestrationService
{
    /// <summary>
    /// Execute a playbook with automatic mode detection.
    /// </summary>
    /// <param name="request">Playbook execution request with playbook ID, documents, and options.</param>
    /// <param name="httpContext">HTTP context for OBO authentication.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of stream events for SSE response.</returns>
    IAsyncEnumerable<PlaybookStreamEvent> ExecuteAsync(
        PlaybookRunRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    /// <summary>
    /// Execute a playbook in app-only mode (no HttpContext, no OBO auth).
    /// Suitable for background processing jobs (email-to-document, Office add-in uploads).
    /// Only supports NodeBased mode — emits RunFailed if playbook has no nodes.
    /// </summary>
    /// <param name="request">Playbook execution request with playbook ID, documents, and options.</param>
    /// <param name="tenantId">Tenant ID for multi-tenant isolation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of stream events.</returns>
    IAsyncEnumerable<PlaybookStreamEvent> ExecuteAppOnlyAsync(
        PlaybookRunRequest request,
        string tenantId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validate a playbook's node graph before execution.
    /// Checks for cycles, missing dependencies, and invalid configurations.
    /// </summary>
    /// <param name="playbookId">Playbook ID to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with success/failure and any error messages.</returns>
    Task<PlaybookValidationResult> ValidateAsync(
        Guid playbookId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Get the status of a running or completed playbook execution.
    /// </summary>
    /// <param name="runId">Run ID from execution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Run status or null if not found.</returns>
    Task<PlaybookRunStatus?> GetRunStatusAsync(
        Guid runId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Cancel a running playbook execution.
    /// </summary>
    /// <param name="runId">Run ID to cancel.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if cancelled, false if not found or already completed.</returns>
    Task<bool> CancelAsync(
        Guid runId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Get execution history for a playbook.
    /// </summary>
    /// <param name="playbookId">Playbook ID to get history for.</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Page size.</param>
    /// <param name="stateFilter">Optional state filter (e.g., "Completed", "Failed").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated list of run summaries.</returns>
    Task<Models.Ai.PlaybookRunHistoryResponse> GetRunHistoryAsync(
        Guid playbookId,
        int page = 1,
        int pageSize = 20,
        string? stateFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get detailed information for a specific run including node-level metrics.
    /// </summary>
    /// <param name="runId">Run ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Detailed run information or null if not found.</returns>
    Task<Models.Ai.PlaybookRunDetail?> GetRunDetailAsync(
        Guid runId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Request to execute a playbook.
/// </summary>
public record PlaybookRunRequest
{
    /// <summary>
    /// Playbook ID to execute.
    /// </summary>
    public required Guid PlaybookId { get; init; }

    /// <summary>
    /// Document IDs to analyze.
    /// </summary>
    public required Guid[] DocumentIds { get; init; }

    /// <summary>
    /// User-provided context or instructions.
    /// </summary>
    public string? UserContext { get; init; }

    /// <summary>
    /// Parameters passed to the playbook (key-value pairs).
    /// Used for template substitution in node prompts.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }

    /// <summary>
    /// Pre-loaded document context with extracted text.
    /// When provided, PlaybookOrchestrationService sets this on PlaybookRunContext.Document
    /// so all nodes share the same extracted text without re-downloading from SPE.
    /// </summary>
    public DocumentContext? Document { get; init; }
}

/// <summary>
/// Streaming event from playbook execution.
/// Serialized as SSE data for real-time progress.
/// </summary>
public record PlaybookStreamEvent
{
    /// <summary>
    /// Event type (see <see cref="PlaybookEventType"/>).
    /// </summary>
    public required PlaybookEventType Type { get; init; }

    /// <summary>
    /// Run ID for correlation.
    /// </summary>
    public required Guid RunId { get; init; }

    /// <summary>
    /// Playbook ID being executed.
    /// </summary>
    public required Guid PlaybookId { get; init; }

    /// <summary>
    /// Current node ID (for node events).
    /// </summary>
    public Guid? NodeId { get; init; }

    /// <summary>
    /// Current node name (for display).
    /// </summary>
    public string? NodeName { get; init; }

    /// <summary>
    /// Streaming text content (for NodeProgress events).
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// Whether this is the final event.
    /// </summary>
    public bool Done { get; init; }

    /// <summary>
    /// Error message (for failure events).
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Node output (for NodeCompleted events).
    /// </summary>
    public NodeOutput? NodeOutput { get; init; }

    /// <summary>
    /// Overall run metrics (for RunCompleted events).
    /// </summary>
    public PlaybookRunMetrics? Metrics { get; init; }

    /// <summary>
    /// Timestamp of this event.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional section-stream payload for per-section composite delivery events
    /// (FR-53 / chat-routing-redesign-r1 task 114a). Populated only when
    /// <see cref="Type"/> is one of
    /// <see cref="PlaybookEventType.SectionStarted"/>,
    /// <see cref="PlaybookEventType.SectionData"/>, or
    /// <see cref="PlaybookEventType.SectionCompleted"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The discriminated payload mirrors the <c>section_*</c> SSE event data records
    /// (<see cref="SectionStartedSseEventData"/> / <see cref="SectionDataSseEventData"/> /
    /// <see cref="SectionCompletedSseEventData"/>). The downstream SSE writer
    /// (<see cref="Api.Ai.PlaybookRunEndpoints"/> / chat SSE writer) is responsible for
    /// projecting the active branch onto the wire as a <see cref="Api.Ai.ChatSseEvent"/>
    /// using <see cref="ChatSseEventFactory"/>.
    /// </para>
    /// <para>
    /// <b>Backward compat</b>: existing consumers that filter on
    /// <c>NodeCompleted / RunCompleted / NodeFailed</c> ignore section events naturally
    /// (the discriminated union default arm). The
    /// <see cref="AnalysisOrchestrationService.BridgePlaybookEventToStreamChunk"/> bridge
    /// returns <c>null</c> for unknown event types, so section events do NOT leak into
    /// the legacy <c>AnalysisStreamChunk</c> text path.
    /// </para>
    /// </remarks>
    public SectionStreamPayload? SectionPayload { get; init; }

    // Factory methods for common event types

    /// <summary>Creates a RunStarted event.</summary>
    public static PlaybookStreamEvent RunStarted(Guid runId, Guid playbookId, int nodeCount) => new()
    {
        Type = PlaybookEventType.RunStarted,
        RunId = runId,
        PlaybookId = playbookId,
        Metrics = new PlaybookRunMetrics { TotalNodes = nodeCount }
    };

    /// <summary>Creates a NodeStarted event.</summary>
    public static PlaybookStreamEvent NodeStarted(Guid runId, Guid playbookId, Guid nodeId, string nodeName) => new()
    {
        Type = PlaybookEventType.NodeStarted,
        RunId = runId,
        PlaybookId = playbookId,
        NodeId = nodeId,
        NodeName = nodeName
    };

    /// <summary>Creates a NodeProgress event with streaming content.</summary>
    public static PlaybookStreamEvent NodeProgress(Guid runId, Guid playbookId, Guid nodeId, string content) => new()
    {
        Type = PlaybookEventType.NodeProgress,
        RunId = runId,
        PlaybookId = playbookId,
        NodeId = nodeId,
        Content = content
    };

    /// <summary>Creates a NodeCompleted event.</summary>
    public static PlaybookStreamEvent NodeCompleted(Guid runId, Guid playbookId, Guid nodeId, string nodeName, NodeOutput output) => new()
    {
        Type = PlaybookEventType.NodeCompleted,
        RunId = runId,
        PlaybookId = playbookId,
        NodeId = nodeId,
        NodeName = nodeName,
        NodeOutput = output
    };

    /// <summary>Creates a NodeSkipped event.</summary>
    public static PlaybookStreamEvent NodeSkipped(Guid runId, Guid playbookId, Guid nodeId, string nodeName, string reason) => new()
    {
        Type = PlaybookEventType.NodeSkipped,
        RunId = runId,
        PlaybookId = playbookId,
        NodeId = nodeId,
        NodeName = nodeName,
        Content = reason
    };

    /// <summary>Creates a NodeFailed event.</summary>
    public static PlaybookStreamEvent NodeFailed(Guid runId, Guid playbookId, Guid nodeId, string nodeName, string error) => new()
    {
        Type = PlaybookEventType.NodeFailed,
        RunId = runId,
        PlaybookId = playbookId,
        NodeId = nodeId,
        NodeName = nodeName,
        Error = error
    };

    /// <summary>Creates a RunCompleted event.</summary>
    public static PlaybookStreamEvent RunCompleted(Guid runId, Guid playbookId, PlaybookRunMetrics metrics) => new()
    {
        Type = PlaybookEventType.RunCompleted,
        RunId = runId,
        PlaybookId = playbookId,
        Done = true,
        Metrics = metrics
    };

    /// <summary>Creates a RunFailed event.</summary>
    public static PlaybookStreamEvent RunFailed(Guid runId, Guid playbookId, string error, PlaybookRunMetrics? metrics = null) => new()
    {
        Type = PlaybookEventType.RunFailed,
        RunId = runId,
        PlaybookId = playbookId,
        Done = true,
        Error = error,
        Metrics = metrics
    };

    /// <summary>Creates a RunCancelled event.</summary>
    public static PlaybookStreamEvent RunCancelled(Guid runId, Guid playbookId) => new()
    {
        Type = PlaybookEventType.RunCancelled,
        RunId = runId,
        PlaybookId = playbookId,
        Done = true
    };

    /// <summary>
    /// Creates an UnrenderedTemplateDetected event (R3 FR-3H1.4 / AC-H1.2).
    /// Emitted as a non-fatal warning when a completed node's output contains
    /// literal <c>{{</c> substrings (an unrendered Handlebars template leaked
    /// into the output stream). Carries node identification + a sample of the
    /// leaked text for observability. Output continues to flow downstream
    /// (degradation, not failure) — runId on this event doubles as the
    /// correlationId per NFR-08.
    /// </summary>
    /// <param name="runId">Run ID (used as correlation ID).</param>
    /// <param name="playbookId">Playbook ID being executed.</param>
    /// <param name="nodeId">ID of the node whose output leaked an unrendered template.</param>
    /// <param name="nodeName">Display name of the node (for stream consumers).</param>
    /// <param name="sample">First ~200 chars of the offending output for diagnostics.</param>
    public static PlaybookStreamEvent UnrenderedTemplateDetected(
        Guid runId,
        Guid playbookId,
        Guid nodeId,
        string nodeName,
        string sample) => new()
        {
            Type = PlaybookEventType.UnrenderedTemplateDetected,
            RunId = runId,
            PlaybookId = playbookId,
            NodeId = nodeId,
            NodeName = nodeName,
            Content = sample
        };

    /// <summary>
    /// Creates a <see cref="PlaybookEventType.SectionStarted"/> event announcing that a
    /// section in a composite output payload has begun composition (FR-53 /
    /// chat-routing-redesign-r1 task 114a). Carries a
    /// <see cref="SectionStartedSseEventData"/> payload for the SSE writer to project
    /// onto the wire as a <c>section_started</c> <see cref="Api.Ai.ChatSseEvent"/>.
    /// </summary>
    public static PlaybookStreamEvent SectionStarted(
        Guid runId,
        Guid playbookId,
        Guid nodeId,
        string nodeName,
        SectionStartedSseEventData data) => new()
        {
            Type = PlaybookEventType.SectionStarted,
            RunId = runId,
            PlaybookId = playbookId,
            NodeId = nodeId,
            NodeName = nodeName,
            SectionPayload = SectionStreamPayload.FromStarted(data)
        };

    /// <summary>
    /// Creates a <see cref="PlaybookEventType.SectionData"/> event carrying section content
    /// (FR-53 / chat-routing-redesign-r1 task 114a). Carries a
    /// <see cref="SectionDataSseEventData"/> payload for the SSE writer to project
    /// onto the wire as a <c>section_data</c> <see cref="Api.Ai.ChatSseEvent"/>.
    /// </summary>
    public static PlaybookStreamEvent SectionData(
        Guid runId,
        Guid playbookId,
        Guid nodeId,
        string nodeName,
        SectionDataSseEventData data) => new()
        {
            Type = PlaybookEventType.SectionData,
            RunId = runId,
            PlaybookId = playbookId,
            NodeId = nodeId,
            NodeName = nodeName,
            SectionPayload = SectionStreamPayload.FromData(data)
        };

    /// <summary>
    /// Creates a <see cref="PlaybookEventType.SectionCompleted"/> event announcing that a
    /// section's composition is finalized (FR-53 / chat-routing-redesign-r1 task 114a).
    /// Carries a <see cref="SectionCompletedSseEventData"/> payload for the SSE writer
    /// to project onto the wire as a <c>section_completed</c>
    /// <see cref="Api.Ai.ChatSseEvent"/>.
    /// </summary>
    public static PlaybookStreamEvent SectionCompleted(
        Guid runId,
        Guid playbookId,
        Guid nodeId,
        string nodeName,
        SectionCompletedSseEventData data) => new()
        {
            Type = PlaybookEventType.SectionCompleted,
            RunId = runId,
            PlaybookId = playbookId,
            NodeId = nodeId,
            NodeName = nodeName,
            SectionPayload = SectionStreamPayload.FromCompleted(data)
        };
}

/// <summary>
/// Discriminated payload for the three per-section composite SSE events
/// (FR-53 / chat-routing-redesign-r1 task 114a). Exactly ONE of the three Data fields
/// is populated; the active field corresponds to the parent
/// <see cref="PlaybookStreamEvent.Type"/>'s section variant.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a wrapper record instead of three separate fields on PlaybookStreamEvent</b>:
/// keeps the section-event-specific surface isolated and self-documenting. Future
/// section-event payload changes (e.g., adding a confidence field) don't require
/// modifying <see cref="PlaybookStreamEvent"/>'s primary shape.
/// </para>
/// <para>
/// <b>ADR-015 tier-1</b>: the payload references the section data records which carry
/// section name + content. Section name is a deterministic configuration identifier;
/// content is the LLM-generated response being sent to the user (equivalent to a chat
/// token — not user-uploaded content / not internal diagnostic data).
/// </para>
/// </remarks>
public sealed record SectionStreamPayload
{
    /// <summary>
    /// <c>section_started</c> payload. Non-null only when the parent event Type is
    /// <see cref="PlaybookEventType.SectionStarted"/>.
    /// </summary>
    public SectionStartedSseEventData? Started { get; init; }

    /// <summary>
    /// <c>section_data</c> payload. Non-null only when the parent event Type is
    /// <see cref="PlaybookEventType.SectionData"/>.
    /// </summary>
    public SectionDataSseEventData? Data { get; init; }

    /// <summary>
    /// <c>section_completed</c> payload. Non-null only when the parent event Type is
    /// <see cref="PlaybookEventType.SectionCompleted"/>.
    /// </summary>
    public SectionCompletedSseEventData? Completed { get; init; }

    /// <summary>Wraps a <see cref="SectionStartedSseEventData"/>.</summary>
    public static SectionStreamPayload FromStarted(SectionStartedSseEventData data) =>
        new() { Started = data };

    /// <summary>Wraps a <see cref="SectionDataSseEventData"/>.</summary>
    public static SectionStreamPayload FromData(SectionDataSseEventData data) =>
        new() { Data = data };

    /// <summary>Wraps a <see cref="SectionCompletedSseEventData"/>.</summary>
    public static SectionStreamPayload FromCompleted(SectionCompletedSseEventData data) =>
        new() { Completed = data };
}

/// <summary>
/// Event types for playbook streaming.
/// </summary>
public enum PlaybookEventType
{
    /// <summary>Playbook execution started.</summary>
    RunStarted,

    /// <summary>Node execution started.</summary>
    NodeStarted,

    /// <summary>Node streaming text output.</summary>
    NodeProgress,

    /// <summary>Node completed successfully.</summary>
    NodeCompleted,

    /// <summary>Node skipped (condition false).</summary>
    NodeSkipped,

    /// <summary>Node failed with error.</summary>
    NodeFailed,

    /// <summary>Playbook execution completed successfully.</summary>
    RunCompleted,

    /// <summary>Playbook execution failed.</summary>
    RunFailed,

    /// <summary>Playbook execution was cancelled.</summary>
    RunCancelled,

    /// <summary>
    /// A completed node's output contains a literal <c>{{</c> substring —
    /// indicating a Handlebars template leaked unrendered into the output.
    /// Non-fatal warning (R3 FR-3H1.4 / AC-H1.2); execution continues.
    /// </summary>
    UnrenderedTemplateDetected,

    /// <summary>
    /// A section in a composite output payload has begun composition
    /// (FR-53 / chat-routing-redesign-r1 task 114a). Emitted by
    /// <see cref="PlaybookOrchestrationService"/> for each
    /// <see cref="Nodes.CompositeSectionResult"/> in a completed
    /// <see cref="Nodes.NodeType.DeliverComposite"/> node's output. The accompanying
    /// <see cref="PlaybookStreamEvent.SectionPayload"/>.<see cref="SectionStreamPayload.Started"/>
    /// carries the section name + position metadata.
    /// </summary>
    SectionStarted,

    /// <summary>
    /// A section in a composite output payload has content available
    /// (FR-53 / chat-routing-redesign-r1 task 114a). In Phase A this is emitted once per
    /// section with the consolidated content; future incremental phases emit multiple
    /// per section. The accompanying
    /// <see cref="PlaybookStreamEvent.SectionPayload"/>.<see cref="SectionStreamPayload.Data"/>
    /// carries the text delta + structured data.
    /// </summary>
    SectionData,

    /// <summary>
    /// A section in a composite output payload is finalized
    /// (FR-53 / chat-routing-redesign-r1 task 114a). The accompanying
    /// <see cref="PlaybookStreamEvent.SectionPayload"/>.<see cref="SectionStreamPayload.Completed"/>
    /// carries the final consolidated text + structured data (idempotent re-emission).
    /// </summary>
    SectionCompleted
}

/// <summary>
/// Metrics for a playbook execution run.
/// </summary>
public record PlaybookRunMetrics
{
    /// <summary>Total number of nodes in the playbook.</summary>
    public int TotalNodes { get; init; }

    /// <summary>Number of nodes completed successfully.</summary>
    public int CompletedNodes { get; init; }

    /// <summary>Number of nodes that failed.</summary>
    public int FailedNodes { get; init; }

    /// <summary>Number of nodes skipped (condition false).</summary>
    public int SkippedNodes { get; init; }

    /// <summary>Total input tokens used across all nodes.</summary>
    public int TotalTokensIn { get; init; }

    /// <summary>Total output tokens generated across all nodes.</summary>
    public int TotalTokensOut { get; init; }

    /// <summary>Total execution duration.</summary>
    public TimeSpan Duration { get; init; }
}

// Note: PlaybookValidationResult is defined in Models/Ai/PlaybookDto.cs

/// <summary>
/// Status of a playbook run.
/// </summary>
public record PlaybookRunStatus
{
    /// <summary>Run ID.</summary>
    public Guid RunId { get; init; }

    /// <summary>Playbook ID.</summary>
    public Guid PlaybookId { get; init; }

    /// <summary>Current run state.</summary>
    public PlaybookRunState State { get; init; }

    /// <summary>When the run started.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>When the run completed (if done).</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Current node being executed (if running).</summary>
    public Guid? CurrentNodeId { get; init; }

    /// <summary>Error message (if failed).</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Execution metrics.</summary>
    public PlaybookRunMetrics? Metrics { get; init; }

    /// <summary>Node outputs keyed by output variable name.</summary>
    public IReadOnlyDictionary<string, NodeOutput>? Outputs { get; init; }
}

/// <summary>
/// State of a playbook run.
/// </summary>
public enum PlaybookRunState
{
    /// <summary>Run is pending (not started).</summary>
    Pending,

    /// <summary>Run is currently executing.</summary>
    Running,

    /// <summary>Run completed successfully.</summary>
    Completed,

    /// <summary>Run failed with error.</summary>
    Failed,

    /// <summary>Run was cancelled.</summary>
    Cancelled
}
