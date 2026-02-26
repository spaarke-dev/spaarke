using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Base record for document streaming SSE events.
///
/// The streaming write engine emits these events alongside existing chat SSE events
/// (<c>token</c>, <c>done</c>, <c>error</c>) to communicate document-level operations
/// to the frontend. Each subclass carries a <see cref="Type"/> discriminator that the
/// TypeScript consumer uses for pattern matching.
///
/// Serialization follows the same <c>data: {json}\n\n</c> SSE pattern used by
/// <see cref="ChatSseEvent"/> in <c>ChatEndpoints.cs</c>.
///
/// ADR-015: Document content (tokens, HTML) MUST NOT appear in log entries.
/// ADR-014: Streaming tokens are transient and MUST NOT be cached.
/// </summary>
public abstract record DocumentStreamEvent
{
    /// <summary>
    /// SSE event type discriminator.
    /// Values: "document_stream_start", "document_stream_token", "document_stream_end",
    /// "document_replace", "progress".
    /// </summary>
    [JsonPropertyName("type")]
    public abstract string Type { get; }
}

/// <summary>
/// Signals the beginning of a streaming write operation.
/// Sent once before any <see cref="DocumentStreamTokenEvent"/> events for a given operation.
/// </summary>
/// <param name="OperationId">Unique identifier for this streaming operation.</param>
/// <param name="TargetPosition">
/// Document position where content will be inserted (e.g., paragraph ID, cursor offset).
/// </param>
/// <param name="OperationType">
/// The type of write operation: "insert" for new content, "replace" for overwriting existing content.
/// </param>
public sealed record DocumentStreamStartEvent(
    [property: JsonPropertyName("operationId")] Guid OperationId,
    [property: JsonPropertyName("targetPosition")] string TargetPosition,
    [property: JsonPropertyName("operationType")] string OperationType) : DocumentStreamEvent
{
    /// <inheritdoc />
    [JsonPropertyName("type")]
    public override string Type => "document_stream_start";
}

/// <summary>
/// Carries an individual token for insertion during a streaming write operation.
/// Emitted for each token in the stream, allowing real-time document updates.
///
/// ADR-015: The <see cref="Token"/> property MUST NOT be logged.
/// </summary>
/// <param name="OperationId">Matches the operation started by <see cref="DocumentStreamStartEvent"/>.</param>
/// <param name="Token">The text token to insert at the current position.</param>
/// <param name="Index">Zero-based sequence index of this token within the operation.</param>
public sealed record DocumentStreamTokenEvent(
    [property: JsonPropertyName("operationId")] Guid OperationId,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("index")] int Index) : DocumentStreamEvent
{
    /// <inheritdoc />
    [JsonPropertyName("type")]
    public override string Type => "document_stream_token";
}

/// <summary>
/// Signals completion of a streaming write operation.
/// Sent once after all <see cref="DocumentStreamTokenEvent"/> events for a given operation.
///
/// On success: <see cref="Cancelled"/> is false, error fields are null.
/// On cancellation: <see cref="Cancelled"/> is true, error fields are null, partial tokens preserved.
/// On error (ADR-019): <see cref="ErrorCode"/> and <see cref="ErrorMessage"/> are populated
/// with a stable code and user-friendly message; <see cref="Cancelled"/> is false.
/// </summary>
/// <param name="OperationId">Matches the operation started by <see cref="DocumentStreamStartEvent"/>.</param>
/// <param name="Cancelled">Whether the operation was cancelled before completion.</param>
/// <param name="TotalTokens">Total number of tokens emitted during the operation.</param>
/// <param name="ErrorCode">Stable error code for programmatic handling (e.g. "LLM_STREAM_FAILED"). Null on success.</param>
/// <param name="ErrorMessage">User-friendly error message. Null on success. MUST NOT contain document content (ADR-015).</param>
public sealed record DocumentStreamEndEvent(
    [property: JsonPropertyName("operationId")] Guid OperationId,
    [property: JsonPropertyName("cancelled")] bool Cancelled,
    [property: JsonPropertyName("totalTokens")] int TotalTokens,
    [property: JsonPropertyName("errorCode")] string? ErrorCode = null,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage = null) : DocumentStreamEvent
{
    /// <inheritdoc />
    [JsonPropertyName("type")]
    public override string Type => "document_stream_end";
}

/// <summary>
/// Carries a bulk content replacement for an entire document section.
/// Used by re-analysis (Package E) to replace document content in one event
/// rather than streaming token-by-token.
///
/// ADR-015: The <see cref="Html"/> property MUST NOT be logged.
/// </summary>
/// <param name="OperationId">Unique identifier for this replacement operation.</param>
/// <param name="Html">The replacement HTML content for the target section.</param>
/// <param name="PreviousVersionId">
/// Optional version ID of the content being replaced, for conflict detection.
/// </param>
public sealed record DocumentReplaceEvent(
    [property: JsonPropertyName("operationId")] Guid OperationId,
    [property: JsonPropertyName("html")] string Html,
    [property: JsonPropertyName("previousVersionId")] string? PreviousVersionId) : DocumentStreamEvent
{
    /// <inheritdoc />
    [JsonPropertyName("type")]
    public override string Type => "document_replace";
}

/// <summary>
/// Reports percent completion for long-running operations.
/// Can be emitted alongside or independently of document stream events.
/// </summary>
/// <param name="OperationId">Unique identifier for the operation being tracked.</param>
/// <param name="Percent">Completion percentage (0-100).</param>
/// <param name="Message">Optional human-readable status message.</param>
public sealed record ProgressEvent(
    [property: JsonPropertyName("operationId")] Guid OperationId,
    [property: JsonPropertyName("percent")] int Percent,
    [property: JsonPropertyName("message")] string? Message) : DocumentStreamEvent
{
    /// <inheritdoc />
    [JsonPropertyName("type")]
    public override string Type => "progress";
}
