using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Api.Agent;

/// <summary>
/// Request model for POST /api/agent/message.
/// Carries a user message from the M365 Copilot agent to the BFF for routing
/// to the appropriate existing service (chat, search, or playbook).
/// </summary>
public sealed record AgentMessageRequest
{
    /// <summary>
    /// The user's natural-language message text.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Opaque conversation reference from the agent framework, used to correlate
    /// multi-turn exchanges. Null for the first message in a conversation.
    /// </summary>
    public string? ConversationReference { get; init; }

    /// <summary>
    /// Optional document ID providing document context for the message.
    /// When present, the message is scoped to this document (e.g. "summarize this").
    /// </summary>
    public Guid? DocumentId { get; init; }

    /// <summary>
    /// Optional key-value context bag forwarded from the agent manifest.
    /// Contains entity references, form context, or other Copilot-provided metadata.
    /// </summary>
    public Dictionary<string, string>? Context { get; init; }
}

/// <summary>
/// Response model for POST /api/agent/message.
/// Returns the BFF's response to be rendered by the M365 Copilot agent.
/// </summary>
public sealed record AgentMessageResponse
{
    /// <summary>
    /// Plain-text or markdown response to display in the Copilot chat.
    /// </summary>
    public required string ResponseText { get; init; }

    /// <summary>
    /// Optional Adaptive Card JSON payload for rich rendering in Teams/Copilot.
    /// Null when a plain text response is sufficient.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AdaptiveCardJson { get; init; }

    /// <summary>
    /// Optional suggested follow-up actions the user can select.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? SuggestedActions { get; init; }
}

/// <summary>
/// Request model for POST /api/agent/run-playbook.
/// Wraps existing playbook execution with the parameters needed by the agent gateway.
/// </summary>
public sealed record AgentPlaybookRequest
{
    /// <summary>
    /// The playbook definition identifier to execute.
    /// </summary>
    public required Guid PlaybookId { get; init; }

    /// <summary>
    /// The document to run the playbook against.
    /// </summary>
    public required Guid DocumentId { get; init; }

    /// <summary>
    /// Optional execution parameters forwarded to the playbook engine.
    /// Keys and values are playbook-specific.
    /// </summary>
    public Dictionary<string, string>? Parameters { get; init; }
}

/// <summary>
/// Response model for GET /api/agent/playbooks/status/{jobId}.
/// Reports the current state of an asynchronous playbook execution.
/// </summary>
public sealed record PlaybookStatusResponse
{
    /// <summary>
    /// The unique job identifier returned when the playbook was enqueued.
    /// </summary>
    public required Guid JobId { get; init; }

    /// <summary>
    /// Current execution status: Queued, Running, Completed, Failed.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Percentage progress (0-100). Null when progress tracking is not available.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ProgressPercent { get; init; }

    /// <summary>
    /// Adaptive Card JSON with the playbook result. Populated only when Status is Completed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResultCardJson { get; init; }

    /// <summary>
    /// Human-readable error message. Populated only when Status is Failed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; init; }
}
