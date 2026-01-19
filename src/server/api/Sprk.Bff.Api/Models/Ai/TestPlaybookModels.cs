using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Test execution modes for validating playbooks during development.
/// </summary>
/// <remarks>
/// See design doc: Test Execution Architecture section.
/// - Mock: Quick logic validation with sample data (no storage, no Document Intelligence)
/// - Quick: Real document with ephemeral storage (temp blob with 24hr TTL)
/// - Production: Full end-to-end validation with SPE storage and Dataverse records
/// </remarks>
public enum TestMode
{
    /// <summary>
    /// Quick logic validation with synthesized sample data.
    /// No storage, no Document Intelligence integration.
    /// Fastest option (~5s) for rapid iteration.
    /// </summary>
    Mock = 0,

    /// <summary>
    /// Test with real document using ephemeral storage.
    /// File stored in temp blob with 24-hour TTL.
    /// Results NOT persisted to Dataverse.
    /// Medium speed (~20-30s), high realism.
    /// </summary>
    Quick = 1,

    /// <summary>
    /// Full production pipeline validation.
    /// Requires playbook to be saved first.
    /// Creates SPE file and Dataverse records.
    /// Slowest option (~30-60s), highest fidelity.
    /// </summary>
    Production = 2
}

/// <summary>
/// Request to execute a playbook test.
/// </summary>
/// <remarks>
/// Supports three test modes with different levels of persistence and realism.
/// For Mock mode, canvasJson is used directly without saving.
/// For Quick mode, uploaded document is stored temporarily.
/// For Production mode, playbook must be saved and document stored in SPE.
/// </remarks>
public record TestPlaybookRequest
{
    /// <summary>
    /// ID of an existing saved playbook.
    /// Required for Production mode; optional for Mock/Quick modes.
    /// </summary>
    public Guid? PlaybookId { get; init; }

    /// <summary>
    /// Canvas JSON for in-memory playbooks (not yet saved).
    /// Used for Mock and Quick modes when PlaybookId is not provided.
    /// </summary>
    public CanvasState? CanvasJson { get; init; }

    /// <summary>
    /// Test execution mode.
    /// </summary>
    public required TestMode Mode { get; init; }

    /// <summary>
    /// Test execution options.
    /// </summary>
    public TestOptions? Options { get; init; }

    /// <summary>
    /// Session ID for conversation context continuity.
    /// </summary>
    public Guid? SessionId { get; init; }
}

/// <summary>
/// Options for test execution.
/// </summary>
public record TestOptions
{
    /// <summary>
    /// Whether to persist results to Dataverse.
    /// Default: false for Mock/Quick, true for Production.
    /// </summary>
    public bool? PersistResults { get; init; }

    /// <summary>
    /// Sample document type for Mock mode.
    /// Determines which synthesized sample data to use.
    /// </summary>
    public string? SampleDocumentType { get; init; }

    /// <summary>
    /// Timeout in seconds for test execution.
    /// Default: 120 for Mock, 300 for Quick/Production.
    /// </summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>
    /// Whether to include detailed node-level timing.
    /// </summary>
    public bool IncludeNodeTiming { get; init; } = true;

    /// <summary>
    /// Maximum number of nodes to execute (for partial testing).
    /// Null means execute all nodes.
    /// </summary>
    public int? MaxNodes { get; init; }
}

/// <summary>
/// Request with uploaded document for Quick test mode.
/// </summary>
/// <remarks>
/// Used when testing with a real document without committing to production storage.
/// The document is stored in temp blob storage with 24-hour TTL.
/// </remarks>
public record TestPlaybookWithDocumentRequest : TestPlaybookRequest
{
    /// <summary>
    /// Original filename of the uploaded document.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// MIME type of the uploaded document.
    /// </summary>
    public string? ContentType { get; init; }
}

/// <summary>
/// SSE stream event for test execution progress.
/// </summary>
public record TestExecutionEvent
{
    /// <summary>
    /// Event type for SSE stream.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Event payload data.
    /// </summary>
    public object? Data { get; init; }

    /// <summary>
    /// Whether this is the final event.
    /// </summary>
    public bool Done { get; init; }
}

/// <summary>
/// Event types for test execution SSE stream.
/// </summary>
public static class TestEventTypes
{
    /// <summary>Starting test execution.</summary>
    public const string Started = "test_started";

    /// <summary>Beginning execution of a specific node.</summary>
    public const string NodeStart = "node_start";

    /// <summary>Node produced output.</summary>
    public const string NodeOutput = "node_output";

    /// <summary>Node execution completed.</summary>
    public const string NodeComplete = "node_complete";

    /// <summary>Node was skipped (e.g., condition not met).</summary>
    public const string NodeSkipped = "node_skipped";

    /// <summary>Node execution failed.</summary>
    public const string NodeError = "node_error";

    /// <summary>Progress update during execution.</summary>
    public const string Progress = "progress";

    /// <summary>Test execution completed successfully.</summary>
    public const string Complete = "test_complete";

    /// <summary>Test execution failed.</summary>
    public const string Error = "error";
}

/// <summary>
/// Data for node start event.
/// </summary>
public record NodeStartData
{
    /// <summary>Node ID being executed.</summary>
    public required string NodeId { get; init; }

    /// <summary>Node label/name.</summary>
    public required string Label { get; init; }

    /// <summary>Node type (aiAnalysis, condition, etc.).</summary>
    public required string NodeType { get; init; }

    /// <summary>Current step number.</summary>
    public int StepNumber { get; init; }

    /// <summary>Total number of steps.</summary>
    public int TotalSteps { get; init; }
}

/// <summary>
/// Data for node output event.
/// </summary>
public record NodeOutputData
{
    /// <summary>Node ID that produced output.</summary>
    public required string NodeId { get; init; }

    /// <summary>Output data from the node.</summary>
    public object? Output { get; init; }

    /// <summary>Execution duration in milliseconds.</summary>
    public int DurationMs { get; init; }

    /// <summary>Token usage for this node (if applicable).</summary>
    public TokenUsageData? TokenUsage { get; init; }
}

/// <summary>
/// Token usage data for AI operations.
/// </summary>
public record TokenUsageData
{
    /// <summary>Input tokens consumed.</summary>
    public int InputTokens { get; init; }

    /// <summary>Output tokens generated.</summary>
    public int OutputTokens { get; init; }

    /// <summary>Model used for this operation.</summary>
    public string? Model { get; init; }
}

/// <summary>
/// Data for node complete event.
/// </summary>
public record NodeCompleteData
{
    /// <summary>Node ID that completed.</summary>
    public required string NodeId { get; init; }

    /// <summary>Whether node executed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>Error message if failed.</summary>
    public string? Error { get; init; }

    /// <summary>Output variable name set by this node.</summary>
    public string? OutputVariable { get; init; }
}

/// <summary>
/// Data for test completion event.
/// </summary>
public record TestCompleteData
{
    /// <summary>Overall success status.</summary>
    public bool Success { get; init; }

    /// <summary>Number of nodes executed.</summary>
    public int NodesExecuted { get; init; }

    /// <summary>Number of nodes skipped.</summary>
    public int NodesSkipped { get; init; }

    /// <summary>Number of nodes that failed.</summary>
    public int NodesFailed { get; init; }

    /// <summary>Total execution duration in milliseconds.</summary>
    public int TotalDurationMs { get; init; }

    /// <summary>Total token usage across all nodes.</summary>
    public TokenUsageData? TotalTokenUsage { get; init; }

    /// <summary>URL to download test report (for Quick test).</summary>
    public string? ReportUrl { get; init; }

    /// <summary>ID of created analysis record (for Production test).</summary>
    public Guid? AnalysisId { get; init; }
}
