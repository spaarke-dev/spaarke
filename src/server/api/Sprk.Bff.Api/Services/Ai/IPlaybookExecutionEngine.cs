using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Unified playbook execution engine supporting both batch (document analysis)
/// and conversational (builder) modes.
/// Implements ADR-013 AI Architecture with dual execution paths.
/// </summary>
/// <remarks>
/// <para>
/// Execution modes:
/// </para>
/// <list type="bullet">
/// <item><b>Batch mode</b>: Process documents through playbook nodes.
/// Delegates to <see cref="IPlaybookOrchestrationService"/> for node execution.</item>
/// <item><b>Conversational mode</b>: Multi-turn builder interactions.
/// Accepts chat history + canvas state, returns incremental canvas updates.</item>
/// </list>
/// </remarks>
public interface IPlaybookExecutionEngine
{
    /// <summary>
    /// Execute a playbook in conversational mode for multi-turn builder interactions.
    /// Accepts chat history and canvas state as context, returns incremental canvas updates.
    /// </summary>
    /// <param name="context">Conversation context with history, current message, and session state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of builder results for streaming response.</returns>
    IAsyncEnumerable<BuilderResult> ExecuteConversationalAsync(
        ConversationContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Execute a playbook in batch mode for document analysis.
    /// Delegates to PlaybookOrchestrationService for node-based execution.
    /// </summary>
    /// <param name="request">Batch execution request with playbook and documents.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of stream events for SSE response.</returns>
    IAsyncEnumerable<PlaybookStreamEvent> ExecuteBatchAsync(
        PlaybookRunRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Determine the execution mode for a playbook based on context.
    /// Returns Conversational if invoked from builder with canvas state,
    /// Batch if invoked with document IDs for analysis.
    /// </summary>
    /// <param name="playbookId">Playbook ID to check.</param>
    /// <param name="hasCanvasState">Whether canvas state is provided.</param>
    /// <param name="hasDocuments">Whether documents are provided.</param>
    /// <returns>The recommended execution mode.</returns>
    ExecutionMode DetermineExecutionMode(
        Guid playbookId,
        bool hasCanvasState,
        bool hasDocuments);
}

/// <summary>
/// Execution mode for the playbook engine.
/// </summary>
public enum ExecutionMode
{
    /// <summary>
    /// Batch mode for document analysis.
    /// Process documents through playbook nodes sequentially.
    /// </summary>
    Batch,

    /// <summary>
    /// Conversational mode for builder interactions.
    /// Multi-turn chat with canvas state management.
    /// </summary>
    Conversational
}

/// <summary>
/// Context for conversational execution mode.
/// Contains chat history, current message, and session state.
/// </summary>
public record ConversationContext
{
    /// <summary>
    /// Conversation history (previous messages).
    /// Ordered chronologically, oldest first.
    /// </summary>
    public ConversationMessage[] History { get; init; } = [];

    /// <summary>
    /// Current user message to process.
    /// </summary>
    public required string CurrentMessage { get; init; }

    /// <summary>
    /// Current session state including canvas and metadata.
    /// </summary>
    public required SessionState SessionState { get; init; }

    /// <summary>
    /// Optional playbook ID if working with an existing playbook.
    /// </summary>
    public Guid? PlaybookId { get; init; }

    /// <summary>
    /// Optional user ID for personalization and permissions.
    /// </summary>
    public string? UserId { get; init; }
}

/// <summary>
/// A message in the conversation history.
/// </summary>
public record ConversationMessage
{
    /// <summary>
    /// Unique message identifier.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Message role (user, assistant, or system).
    /// </summary>
    public required ConversationRole Role { get; init; }

    /// <summary>
    /// Message content.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// When the message was sent.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional metadata about operations performed (for assistant messages).
    /// </summary>
    public MessageMetadata? Metadata { get; init; }
}

/// <summary>
/// Role of a conversation participant.
/// </summary>
public enum ConversationRole
{
    /// <summary>User message.</summary>
    User,

    /// <summary>Assistant response.</summary>
    Assistant,

    /// <summary>System instruction.</summary>
    System
}

/// <summary>
/// Metadata attached to conversation messages.
/// </summary>
public record MessageMetadata
{
    /// <summary>
    /// Canvas operations that were performed with this message.
    /// </summary>
    public CanvasPatch[]? CanvasOperations { get; init; }

    /// <summary>
    /// Intent that was classified for user messages.
    /// </summary>
    public BuilderIntent? ClassifiedIntent { get; init; }

    /// <summary>
    /// Confidence score for the classification.
    /// </summary>
    public double? Confidence { get; init; }

    /// <summary>
    /// Token usage for this message.
    /// </summary>
    public ConversationTokenUsage? TokenUsage { get; init; }
}

/// <summary>
/// Token usage statistics for conversational execution.
/// Named distinctly to avoid conflict with Api.Ai.TokenUsage.
/// </summary>
public record ConversationTokenUsage
{
    /// <summary>Input tokens consumed.</summary>
    public int InputTokens { get; init; }

    /// <summary>Output tokens generated.</summary>
    public int OutputTokens { get; init; }

    /// <summary>Total tokens used.</summary>
    public int TotalTokens => InputTokens + OutputTokens;
}

/// <summary>
/// Current session state for conversational execution.
/// </summary>
public record SessionState
{
    /// <summary>
    /// Unique session identifier for continuity across requests.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Current canvas state (nodes and edges).
    /// </summary>
    public required CanvasState CanvasState { get; init; }

    /// <summary>
    /// When the session was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the session was last active.
    /// </summary>
    public DateTimeOffset LastActiveAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional build plan being executed.
    /// </summary>
    public BuildPlan? ActiveBuildPlan { get; init; }

    /// <summary>
    /// Current step in the build plan (if active).
    /// </summary>
    public int? CurrentBuildStep { get; init; }

    /// <summary>
    /// Variables accumulated during the session (e.g., scope IDs created).
    /// </summary>
    public Dictionary<string, object?>? SessionVariables { get; init; }
}

/// <summary>
/// Result from conversational execution.
/// Represents a single streaming chunk of the response.
/// </summary>
public record BuilderResult
{
    /// <summary>
    /// Result type.
    /// </summary>
    public required BuilderResultType Type { get; init; }

    /// <summary>
    /// Text content (for message results).
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Canvas patch (for operation results).
    /// </summary>
    public CanvasPatch? Patch { get; init; }

    /// <summary>
    /// Clarification question (for clarification results).
    /// </summary>
    public string? ClarificationQuestion { get; init; }

    /// <summary>
    /// Options for clarification (multiple choice).
    /// </summary>
    public string[]? ClarificationOptions { get; init; }

    /// <summary>
    /// Build plan preview (for plan results).
    /// </summary>
    public BuildPlan? PlanPreview { get; init; }

    /// <summary>
    /// Error details (for error results).
    /// </summary>
    public ErrorDetail? Error { get; init; }

    /// <summary>
    /// Updated session state (for state change results).
    /// </summary>
    public SessionState? UpdatedState { get; init; }

    /// <summary>
    /// Timestamp of this result.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    // Factory methods for creating common results

    /// <summary>Create a thinking/progress message result.</summary>
    public static BuilderResult Thinking(string text) =>
        new() { Type = BuilderResultType.Thinking, Text = text };

    /// <summary>Create a text message result.</summary>
    public static BuilderResult Message(string text) =>
        new() { Type = BuilderResultType.Message, Text = text };

    /// <summary>Create a canvas operation result.</summary>
    public static BuilderResult Operation(CanvasPatch patch) =>
        new() { Type = BuilderResultType.CanvasOperation, Patch = patch };

    /// <summary>Create a clarification request result.</summary>
    public static BuilderResult Clarification(string question, string[]? options = null) =>
        new() { Type = BuilderResultType.Clarification, ClarificationQuestion = question, ClarificationOptions = options };

    /// <summary>Create a plan preview result.</summary>
    public static BuilderResult Plan(BuildPlan plan) =>
        new() { Type = BuilderResultType.PlanPreview, PlanPreview = plan };

    /// <summary>Create a completion result.</summary>
    public static BuilderResult Complete() =>
        new() { Type = BuilderResultType.Complete };

    /// <summary>Create an error result.</summary>
    public static BuilderResult ErrorResult(string message, string? code = null) =>
        new() { Type = BuilderResultType.Error, Error = new ErrorDetail { Message = message, Code = code } };

    /// <summary>Create a state update result.</summary>
    public static BuilderResult StateUpdate(SessionState state) =>
        new() { Type = BuilderResultType.StateUpdate, UpdatedState = state };
}

/// <summary>
/// Types of builder results.
/// </summary>
public enum BuilderResultType
{
    /// <summary>Thinking/progress indicator.</summary>
    Thinking,

    /// <summary>Text message to display.</summary>
    Message,

    /// <summary>Canvas operation to apply.</summary>
    CanvasOperation,

    /// <summary>Clarification request.</summary>
    Clarification,

    /// <summary>Build plan preview.</summary>
    PlanPreview,

    /// <summary>Execution complete.</summary>
    Complete,

    /// <summary>Error occurred.</summary>
    Error,

    /// <summary>Session state update.</summary>
    StateUpdate
}

/// <summary>
/// Error details for builder errors.
/// </summary>
public record ErrorDetail
{
    /// <summary>Error message.</summary>
    public required string Message { get; init; }

    /// <summary>Optional error code.</summary>
    public string? Code { get; init; }

    /// <summary>Whether the error is recoverable.</summary>
    public bool IsRecoverable { get; init; } = true;

    /// <summary>Suggested action to recover.</summary>
    public string? SuggestedAction { get; init; }
}
