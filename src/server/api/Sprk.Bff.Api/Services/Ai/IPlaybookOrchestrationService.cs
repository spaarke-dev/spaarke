using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Models.Ai;

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
    RunCancelled
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
