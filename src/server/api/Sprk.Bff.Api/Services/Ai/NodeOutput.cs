using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Result from a completed node execution.
/// Contains the output data, metrics, and any errors from the execution.
/// </summary>
/// <remarks>
/// <para>
/// NodeOutput is produced by INodeExecutor implementations and stored
/// in PlaybookRunContext for downstream node access via template variables.
/// </para>
/// <para>
/// Two distinct output concepts:
/// </para>
/// <list type="bullet">
/// <item>AI Output (this class): Structured data from AI analysis</item>
/// <item>Delivery Output: Final rendered artifact (handled by DeliverOutputNodeExecutor)</item>
/// </list>
/// </remarks>
public record NodeOutput
{
    /// <summary>
    /// Node ID that produced this output.
    /// </summary>
    public required Guid NodeId { get; init; }

    /// <summary>
    /// Variable name for referencing this output in downstream nodes.
    /// </summary>
    public required string OutputVariable { get; init; }

    /// <summary>
    /// Whether the node execution completed successfully.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if Success is false.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Error code for programmatic error handling.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Plain text content from node execution.
    /// For AI nodes, this is the raw LLM response text.
    /// </summary>
    public string? TextContent { get; init; }

    /// <summary>
    /// Structured data output from the node.
    /// For AI analysis nodes, this contains parsed entities, clauses, etc.
    /// Use GetData&lt;T&gt; to deserialize to a specific type.
    /// </summary>
    public JsonElement? StructuredData { get; init; }

    /// <summary>
    /// Tool results if this was an AI analysis node with tool handlers.
    /// Contains the full ToolResult from each executed tool.
    /// </summary>
    public IReadOnlyList<ToolResult> ToolResults { get; init; } = Array.Empty<ToolResult>();

    /// <summary>
    /// Overall confidence score (0.0 - 1.0) for the output.
    /// May be aggregated from tool results or provided by the LLM.
    /// </summary>
    public double? Confidence { get; init; }

    /// <summary>
    /// Execution metrics for this node.
    /// </summary>
    public required NodeExecutionMetrics Metrics { get; init; }

    /// <summary>
    /// Warnings or informational messages from node execution.
    /// Non-fatal issues that don't prevent success.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Deserializes the StructuredData property to the specified type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <returns>Deserialized data or default if StructuredData is null.</returns>
    public T? GetData<T>()
    {
        if (StructuredData is null)
            return default;

        return JsonSerializer.Deserialize<T>(StructuredData.Value.GetRawText(), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    /// <summary>
    /// Creates a successful output with structured data.
    /// </summary>
    public static NodeOutput Ok(
        Guid nodeId,
        string outputVariable,
        object? data,
        string? textContent = null,
        double? confidence = null,
        NodeExecutionMetrics? metrics = null,
        IReadOnlyList<ToolResult>? toolResults = null,
        IReadOnlyList<string>? warnings = null)
    {
        JsonElement? jsonData = data is not null
            ? JsonSerializer.SerializeToElement(data)
            : null;

        return new NodeOutput
        {
            NodeId = nodeId,
            OutputVariable = outputVariable,
            Success = true,
            TextContent = textContent,
            StructuredData = jsonData,
            Confidence = confidence,
            Metrics = metrics ?? NodeExecutionMetrics.Empty,
            ToolResults = toolResults ?? Array.Empty<ToolResult>(),
            Warnings = warnings ?? Array.Empty<string>()
        };
    }

    /// <summary>
    /// Creates a failed output with error information.
    /// </summary>
    public static NodeOutput Error(
        Guid nodeId,
        string outputVariable,
        string errorMessage,
        string? errorCode = null,
        NodeExecutionMetrics? metrics = null)
    {
        return new NodeOutput
        {
            NodeId = nodeId,
            OutputVariable = outputVariable,
            Success = false,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode,
            Metrics = metrics ?? NodeExecutionMetrics.Empty
        };
    }
}

/// <summary>
/// Execution metrics captured during node execution.
/// </summary>
public record NodeExecutionMetrics
{
    /// <summary>
    /// When node execution started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// When node execution completed.
    /// </summary>
    public DateTimeOffset CompletedAt { get; init; }

    /// <summary>
    /// Total execution duration.
    /// </summary>
    public TimeSpan Duration => CompletedAt - StartedAt;

    /// <summary>
    /// Total duration in milliseconds (for easy serialization).
    /// </summary>
    public long DurationMs => (long)Duration.TotalMilliseconds;

    /// <summary>
    /// Input tokens used (if AI model was called).
    /// </summary>
    public int? TokensIn { get; init; }

    /// <summary>
    /// Output tokens generated (if AI model was called).
    /// </summary>
    public int? TokensOut { get; init; }

    /// <summary>
    /// Total tokens used.
    /// </summary>
    public int? TotalTokens => TokensIn.HasValue && TokensOut.HasValue
        ? TokensIn.Value + TokensOut.Value
        : null;

    /// <summary>
    /// Whether results were retrieved from cache.
    /// </summary>
    public bool CacheHit { get; init; }

    /// <summary>
    /// Number of AI model calls made.
    /// </summary>
    public int ModelCalls { get; init; }

    /// <summary>
    /// Number of retry attempts needed.
    /// </summary>
    public int RetryAttempts { get; init; }

    /// <summary>
    /// Name of the AI model used, if applicable.
    /// </summary>
    public string? ModelName { get; init; }

    /// <summary>
    /// Creates an empty metrics instance (for errors before execution starts).
    /// </summary>
    public static NodeExecutionMetrics Empty => new()
    {
        StartedAt = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow
    };

    /// <summary>
    /// Creates metrics with timing only.
    /// </summary>
    public static NodeExecutionMetrics Timed(DateTimeOffset started, DateTimeOffset completed) => new()
    {
        StartedAt = started,
        CompletedAt = completed
    };

    /// <summary>
    /// Creates metrics from ToolExecutionMetadata.
    /// </summary>
    public static NodeExecutionMetrics FromToolMetadata(ToolExecutionMetadata toolMetadata) => new()
    {
        StartedAt = toolMetadata.StartedAt,
        CompletedAt = toolMetadata.CompletedAt,
        TokensIn = toolMetadata.InputTokens,
        TokensOut = toolMetadata.OutputTokens,
        CacheHit = toolMetadata.CacheHit,
        ModelCalls = toolMetadata.ModelCalls,
        ModelName = toolMetadata.ModelName
    };
}

/// <summary>
/// Common error codes for node execution failures.
/// </summary>
public static class NodeErrorCodes
{
    /// <summary>Node validation failed.</summary>
    public const string ValidationFailed = "NODE_VALIDATION_FAILED";

    /// <summary>Invalid node configuration.</summary>
    public const string InvalidConfiguration = "INVALID_NODE_CONFIGURATION";

    /// <summary>Missing required tool for action type.</summary>
    public const string MissingTool = "MISSING_TOOL";

    /// <summary>Tool handler not found.</summary>
    public const string ToolHandlerNotFound = "TOOL_HANDLER_NOT_FOUND";

    /// <summary>AI model call failed.</summary>
    public const string ModelError = "MODEL_ERROR";

    /// <summary>Node execution timed out.</summary>
    public const string Timeout = "TIMEOUT";

    /// <summary>Node execution was cancelled.</summary>
    public const string Cancelled = "CANCELLED";

    /// <summary>Dependency node failed.</summary>
    public const string DependencyFailed = "DEPENDENCY_FAILED";

    /// <summary>Condition evaluation failed.</summary>
    public const string ConditionError = "CONDITION_ERROR";

    /// <summary>Template substitution failed.</summary>
    public const string TemplateError = "TEMPLATE_ERROR";

    /// <summary>Unexpected internal error.</summary>
    public const string InternalError = "INTERNAL_ERROR";
}
