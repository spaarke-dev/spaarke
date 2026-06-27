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
    /// Optional side-channel metadata for cross-cutting post-processing by chat infrastructure
    /// (R6 Wave 7b). Handlers return well-known keys; the
    /// <see cref="Chat.ToolHandlerToAIFunctionAdapter"/> reads them and performs side effects
    /// (citation accumulation, SSE widget event emission) so handlers remain pure-input/pure-output.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Well-known keys (see <see cref="ToolResultMetadataKeys"/>):
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <see cref="ToolResultMetadataKeys.Citations"/> — array of citation envelopes the adapter
    /// forwards into the per-chat-turn <see cref="Models.Ai.Chat.CitationContext"/>. Each envelope
    /// MUST carry deterministic source identifiers (<c>chunkId</c>, <c>sourceName</c>) + optional
    /// excerpt/url/snippet. ADR-015: NEVER user message content.
    /// </item>
    /// <item>
    /// <see cref="ToolResultMetadataKeys.Widget"/> — pane-type + widget-type + widget-data envelope
    /// the adapter emits as a <c>source_pane</c> or <c>output_pane</c> SSE event. ADR-015: widget
    /// data SHOULD be deterministic identifiers + display metadata only — never raw user text.
    /// </item>
    /// </list>
    /// <para>
    /// Backward-compat: Existing handlers do not set <see cref="Metadata"/>; the adapter handles
    /// null gracefully (no post-processing performed). Values are JSON-serializable so they
    /// round-trip cleanly through the function-calling protocol if a future handler emits them
    /// upstream of the adapter.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }

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
/// Well-known keys for <see cref="ToolResult.Metadata"/> recognized by the
/// chat-tool adapter (R6 Wave 7b infrastructure).
/// </summary>
/// <remarks>
/// <para>
/// Handlers running in chat context (via <see cref="IToolHandler.ExecuteChatAsync"/>) MAY
/// populate <see cref="ToolResult.Metadata"/> using these keys to trigger adapter-side
/// post-processing (citation accumulation, SSE widget emission). The adapter handles missing
/// or malformed values resiliently — see
/// <see cref="Chat.ToolHandlerToAIFunctionAdapter"/>.
/// </para>
/// <para>
/// Backward-compat: handlers that do NOT emit metadata get null-treated and the adapter
/// performs no post-processing. The 8 typed handlers (Wave 1 / Wave 2) and the newly migrated
/// chat tools (AnalysisQuery, TextRefinement) do not require metadata.
/// </para>
/// </remarks>
public static class ToolResultMetadataKeys
{
    /// <summary>
    /// Metadata key for citation envelopes. Value SHOULD be an
    /// <see cref="IEnumerable{T}"/> of <see cref="ToolResultCitation"/> (or a JSON-serializable
    /// equivalent: array of objects with <c>chunkId</c>, <c>sourceName</c>, optional
    /// <c>pageNumber</c>, optional <c>excerpt</c>, optional <c>sourceType</c>, optional
    /// <c>url</c>, optional <c>snippet</c>).
    /// </summary>
    /// <remarks>
    /// The adapter forwards each entry into the constructor-supplied
    /// <see cref="Models.Ai.Chat.CitationContext"/> via
    /// <see cref="Models.Ai.Chat.CitationContext.AddCitation"/>. When the accumulator is null
    /// (e.g., non-chat path), citations are dropped silently.
    /// </remarks>
    public const string Citations = "citations";

    /// <summary>
    /// Metadata key for a single widget event envelope. Value SHOULD be a
    /// <see cref="ToolResultWidget"/> (or JSON-serializable equivalent with <c>paneType</c>,
    /// <c>widgetType</c>, optional <c>citationId</c>, and <c>data</c>).
    /// </summary>
    /// <remarks>
    /// The adapter emits a corresponding
    /// <see cref="Chat.SseEventTypes.ChatSseEventFactory.CreateSourcePaneEvent"/> or
    /// <see cref="Chat.SseEventTypes.ChatSseEventFactory.CreateOutputPaneEvent"/> via the
    /// constructor-supplied SSE writer delegate. When the writer is null, the widget event is
    /// dropped silently.
    /// </remarks>
    public const string Widget = "widget";
}

/// <summary>
/// Citation envelope used in <see cref="ToolResult.Metadata"/> under
/// <see cref="ToolResultMetadataKeys.Citations"/>. Shape mirrors
/// <see cref="Models.Ai.Chat.CitationMetadata"/> but excludes the citation ID
/// (the accumulator assigns IDs deterministically in tool-call order).
/// </summary>
/// <param name="ChunkId">Unique chunk identifier from the source index.</param>
/// <param name="SourceName">Display name of the source document or article.</param>
/// <param name="PageNumber">Optional page number in the source document.</param>
/// <param name="Excerpt">Short content excerpt (capped to 200 chars by the accumulator).</param>
/// <param name="SourceType">Optional discriminator: null/"document" or "web".</param>
/// <param name="Url">Optional URL for web citations.</param>
/// <param name="Snippet">Optional short snippet for web citations.</param>
public sealed record ToolResultCitation(
    string ChunkId,
    string SourceName,
    int? PageNumber = null,
    string Excerpt = "",
    string? SourceType = null,
    string? Url = null,
    string? Snippet = null);

/// <summary>
/// Widget event envelope used in <see cref="ToolResult.Metadata"/> under
/// <see cref="ToolResultMetadataKeys.Widget"/>. The adapter routes by
/// <see cref="PaneType"/> to the matching SSE factory method.
/// </summary>
/// <param name="PaneType">"source_pane" or "output_pane". Any other value is ignored.</param>
/// <param name="WidgetType">Frontend widget-registry key (e.g., "DocumentViewer", "SearchResults").</param>
/// <param name="Data">Widget data payload — JSON-serializable; frontend owns schema interpretation.</param>
/// <param name="CitationId">
/// Optional citation ID linking the source-pane widget to a [N] marker. Used only when
/// <see cref="PaneType"/> = "source_pane".
/// </param>
public sealed record ToolResultWidget(
    string PaneType,
    string WidgetType,
    object Data,
    string? CitationId = null);

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
