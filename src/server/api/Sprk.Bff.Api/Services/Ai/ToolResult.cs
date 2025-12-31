using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Standardized result from tool handler execution.
/// Provides consistent output structure for all tool types.
/// </summary>
/// <remarks>
/// <para>
/// Tool results contain:
/// </para>
/// <list type="bullet">
/// <item>Structured data output (JSON-serializable)</item>
/// <item>Plain text summary for display</item>
/// <item>Confidence scores</item>
/// <item>Execution metadata (timing, tokens)</item>
/// <item>Error information if failed</item>
/// </list>
/// </remarks>
public record ToolResult
{
    /// <summary>
    /// The tool handler ID that produced this result.
    /// </summary>
    public required string HandlerId { get; init; }

    /// <summary>
    /// The tool ID from Dataverse (AnalysisTool.Id).
    /// </summary>
    public required Guid ToolId { get; init; }

    /// <summary>
    /// Tool name for display purposes.
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Whether the tool execution completed successfully.
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
    /// Structured output data from the tool.
    /// Schema varies by tool type. Use GetData&lt;T&gt; to deserialize.
    /// </summary>
    /// <remarks>
    /// Examples:
    /// - EntityExtractor: { "entities": [{ "name": "...", "type": "..." }] }
    /// - ClauseAnalyzer: { "clauses": [{ "text": "...", "category": "..." }] }
    /// </remarks>
    public JsonElement? Data { get; init; }

    /// <summary>
    /// Plain text summary of results for display.
    /// Should be human-readable and concise.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Overall confidence score (0.0 - 1.0) for the results.
    /// </summary>
    public double? Confidence { get; init; }

    /// <summary>
    /// Individual item confidence scores if applicable.
    /// Maps item identifiers to their confidence scores.
    /// </summary>
    public IReadOnlyDictionary<string, double>? ItemConfidences { get; init; }

    /// <summary>
    /// Execution metadata including timing and resource usage.
    /// </summary>
    public required ToolExecutionMetadata Execution { get; init; }

    /// <summary>
    /// Warnings or informational messages from tool execution.
    /// Non-fatal issues that don't prevent success.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Deserializes the Data property to the specified type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <returns>Deserialized data or default if Data is null.</returns>
    public T? GetData<T>()
    {
        if (Data is null)
            return default;

        return JsonSerializer.Deserialize<T>(Data.Value.GetRawText(), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    /// <summary>
    /// Creates a successful result with structured data.
    /// </summary>
    public static ToolResult Ok(
        string handlerId,
        Guid toolId,
        string toolName,
        object data,
        string? summary = null,
        double? confidence = null,
        ToolExecutionMetadata? execution = null,
        IReadOnlyList<string>? warnings = null)
    {
        var jsonData = JsonSerializer.SerializeToElement(data);

        return new ToolResult
        {
            HandlerId = handlerId,
            ToolId = toolId,
            ToolName = toolName,
            Success = true,
            Data = jsonData,
            Summary = summary,
            Confidence = confidence,
            Execution = execution ?? ToolExecutionMetadata.Empty,
            Warnings = warnings ?? Array.Empty<string>()
        };
    }

    /// <summary>
    /// Creates a failed result with error information.
    /// </summary>
    public static ToolResult Error(
        string handlerId,
        Guid toolId,
        string toolName,
        string errorMessage,
        string? errorCode = null,
        ToolExecutionMetadata? execution = null)
    {
        return new ToolResult
        {
            HandlerId = handlerId,
            ToolId = toolId,
            ToolName = toolName,
            Success = false,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode,
            Execution = execution ?? ToolExecutionMetadata.Empty
        };
    }
}

/// <summary>
/// Execution metadata captured during tool execution.
/// </summary>
public record ToolExecutionMetadata
{
    /// <summary>
    /// When tool execution started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// When tool execution completed.
    /// </summary>
    public DateTimeOffset CompletedAt { get; init; }

    /// <summary>
    /// Total execution duration.
    /// </summary>
    public TimeSpan Duration => CompletedAt - StartedAt;

    /// <summary>
    /// Input tokens used (if AI model was called).
    /// </summary>
    public int? InputTokens { get; init; }

    /// <summary>
    /// Output tokens generated (if AI model was called).
    /// </summary>
    public int? OutputTokens { get; init; }

    /// <summary>
    /// Total tokens used.
    /// </summary>
    public int? TotalTokens => InputTokens.HasValue && OutputTokens.HasValue
        ? InputTokens.Value + OutputTokens.Value
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
    /// Name of the AI model used, if applicable.
    /// </summary>
    public string? ModelName { get; init; }

    /// <summary>
    /// Creates an empty metadata instance (for errors before execution starts).
    /// </summary>
    public static ToolExecutionMetadata Empty => new()
    {
        StartedAt = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow
    };

    /// <summary>
    /// Creates metadata with timing only.
    /// </summary>
    public static ToolExecutionMetadata Timed(DateTimeOffset started, DateTimeOffset completed) => new()
    {
        StartedAt = started,
        CompletedAt = completed
    };
}

/// <summary>
/// Common error codes for tool execution failures.
/// </summary>
public static class ToolErrorCodes
{
    /// <summary>Tool validation failed.</summary>
    public const string ValidationFailed = "VALIDATION_FAILED";

    /// <summary>Invalid tool configuration.</summary>
    public const string InvalidConfiguration = "INVALID_CONFIGURATION";

    /// <summary>Document content is empty or invalid.</summary>
    public const string InvalidContent = "INVALID_CONTENT";

    /// <summary>AI model call failed.</summary>
    public const string ModelError = "MODEL_ERROR";

    /// <summary>AI model rate limit exceeded.</summary>
    public const string RateLimitExceeded = "RATE_LIMIT_EXCEEDED";

    /// <summary>Tool execution timed out.</summary>
    public const string Timeout = "TIMEOUT";

    /// <summary>Tool execution was cancelled.</summary>
    public const string Cancelled = "CANCELLED";

    /// <summary>Required dependency not available.</summary>
    public const string DependencyUnavailable = "DEPENDENCY_UNAVAILABLE";

    /// <summary>Unexpected internal error.</summary>
    public const string InternalError = "INTERNAL_ERROR";
}
