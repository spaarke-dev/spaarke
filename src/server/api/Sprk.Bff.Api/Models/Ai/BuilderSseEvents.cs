using System.Text.Json.Serialization;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Server-Sent Event types for the AI Playbook Builder streaming responses.
/// Follows the SSE specification with event: and data: lines.
/// </summary>
/// <remarks>
/// Event types:
/// - thinking: AI processing status/progress indicators
/// - dataverse_operation: Dataverse record changes (scope creation, etc.)
/// - canvas_patch: Canvas node/edge changes to apply
/// - message: AI response text to display
/// - done: Completion signal with final state
/// - error: Error occurred during processing
/// </remarks>
public static class BuilderSseEventTypes
{
    /// <summary>AI is processing/thinking.</summary>
    public const string Thinking = "thinking";

    /// <summary>Dataverse operation occurred (scope created, etc.).</summary>
    public const string DataverseOperation = "dataverse_operation";

    /// <summary>Canvas patch to apply (add/remove/update nodes/edges).</summary>
    public const string CanvasPatch = "canvas_patch";

    /// <summary>AI response message text.</summary>
    public const string Message = "message";

    /// <summary>Processing complete.</summary>
    public const string Done = "done";

    /// <summary>Error occurred.</summary>
    public const string Error = "error";

    /// <summary>Clarification request from AI.</summary>
    public const string Clarification = "clarification";

    /// <summary>Build plan preview for user approval.</summary>
    public const string PlanPreview = "plan_preview";
}

/// <summary>
/// Base SSE event for playbook builder streaming.
/// All events inherit from this and add type-specific data.
/// </summary>
public record BuilderSseEvent
{
    /// <summary>Event type (thinking, dataverse_operation, canvas_patch, message, done, error).</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>Timestamp when this event was generated.</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Thinking event - AI processing status/progress indicators.
/// </summary>
public record ThinkingEvent : BuilderSseEvent
{
    /// <summary>The thinking/progress message.</summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }

    /// <summary>Optional step indicator (e.g., "Classifying intent...").</summary>
    [JsonPropertyName("step")]
    public string? Step { get; init; }

    /// <summary>Create a thinking event.</summary>
    public static ThinkingEvent Create(string content, string? step = null) => new()
    {
        Type = BuilderSseEventTypes.Thinking,
        Content = content,
        Step = step
    };
}

/// <summary>
/// Dataverse operation event - indicates a Dataverse record change occurred.
/// </summary>
public record DataverseOperationEvent : BuilderSseEvent
{
    /// <summary>Operation type (created, updated, deleted).</summary>
    [JsonPropertyName("operation")]
    public required string Operation { get; init; }

    /// <summary>Entity type (e.g., "sprk_aiaction", "sprk_aiskill").</summary>
    [JsonPropertyName("entityType")]
    public required string EntityType { get; init; }

    /// <summary>Record ID that was affected.</summary>
    [JsonPropertyName("recordId")]
    public Guid? RecordId { get; init; }

    /// <summary>Human-readable description of what happened.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Create a Dataverse operation event.</summary>
    public static DataverseOperationEvent Create(
        string operation,
        string entityType,
        Guid? recordId = null,
        string? description = null) => new()
        {
            Type = BuilderSseEventTypes.DataverseOperation,
            Operation = operation,
            EntityType = entityType,
            RecordId = recordId,
            Description = description
        };
}

/// <summary>
/// Canvas patch event - canvas node/edge changes to apply.
/// </summary>
public record CanvasPatchEvent : BuilderSseEvent
{
    /// <summary>The canvas patch to apply.</summary>
    [JsonPropertyName("patch")]
    public required CanvasPatch Patch { get; init; }

    /// <summary>Optional description of the change.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Create a canvas patch event.</summary>
    public static CanvasPatchEvent Create(CanvasPatch patch, string? description = null) => new()
    {
        Type = BuilderSseEventTypes.CanvasPatch,
        Patch = patch,
        Description = description
    };
}

/// <summary>
/// Message event - AI response text to display to user.
/// </summary>
public record MessageEvent : BuilderSseEvent
{
    /// <summary>The message text.</summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }

    /// <summary>Whether this is a partial message (more content coming).</summary>
    [JsonPropertyName("isPartial")]
    public bool IsPartial { get; init; }

    /// <summary>Create a message event.</summary>
    public static MessageEvent Create(string content, bool isPartial = false) => new()
    {
        Type = BuilderSseEventTypes.Message,
        Content = content,
        IsPartial = isPartial
    };
}

/// <summary>
/// Done event - processing complete.
/// </summary>
public record DoneEvent : BuilderSseEvent
{
    /// <summary>Total operations performed.</summary>
    [JsonPropertyName("operationCount")]
    public int OperationCount { get; init; }

    /// <summary>Optional summary message.</summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    /// <summary>Updated session state (if applicable).</summary>
    [JsonPropertyName("sessionState")]
    public SessionState? SessionState { get; init; }

    /// <summary>Create a done event.</summary>
    public static DoneEvent Create(int operationCount = 0, string? summary = null, SessionState? sessionState = null) => new()
    {
        Type = BuilderSseEventTypes.Done,
        OperationCount = operationCount,
        Summary = summary,
        SessionState = sessionState
    };
}

/// <summary>
/// Error event - error occurred during processing.
/// Task 050: Added correlationId for error tracing support.
/// </summary>
public record ErrorEvent : BuilderSseEvent
{
    /// <summary>Error message.</summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>Error code for programmatic handling.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    /// <summary>Whether the error is recoverable.</summary>
    [JsonPropertyName("isRecoverable")]
    public bool IsRecoverable { get; init; } = true;

    /// <summary>Suggested action for recovery.</summary>
    [JsonPropertyName("suggestedAction")]
    public string? SuggestedAction { get; init; }

    /// <summary>Correlation ID for support and tracing (HttpContext.TraceIdentifier).</summary>
    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; init; }

    /// <summary>Create an error event.</summary>
    public static ErrorEvent Create(
        string message,
        string? code = null,
        bool isRecoverable = true,
        string? suggestedAction = null,
        string? correlationId = null) => new()
        {
            Type = BuilderSseEventTypes.Error,
            Message = message,
            Code = code,
            IsRecoverable = isRecoverable,
            SuggestedAction = suggestedAction,
            CorrelationId = correlationId
        };
}

/// <summary>
/// Clarification event - AI needs more information from user.
/// </summary>
public record ClarificationEvent : BuilderSseEvent
{
    /// <summary>The clarification question.</summary>
    [JsonPropertyName("question")]
    public required string Question { get; init; }

    /// <summary>Optional predefined options.</summary>
    [JsonPropertyName("options")]
    public string[]? Options { get; init; }

    /// <summary>Create a clarification event.</summary>
    public static ClarificationEvent Create(string question, string[]? options = null) => new()
    {
        Type = BuilderSseEventTypes.Clarification,
        Question = question,
        Options = options
    };
}

/// <summary>
/// Plan preview event - build plan for user approval.
/// </summary>
public record PlanPreviewEvent : BuilderSseEvent
{
    /// <summary>The build plan.</summary>
    [JsonPropertyName("plan")]
    public required BuildPlan Plan { get; init; }

    /// <summary>Create a plan preview event.</summary>
    public static PlanPreviewEvent Create(BuildPlan plan) => new()
    {
        Type = BuilderSseEventTypes.PlanPreview,
        Plan = plan
    };
}
