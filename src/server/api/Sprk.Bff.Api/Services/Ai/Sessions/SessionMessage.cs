using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// An individual message within a <see cref="StoredSession"/>.
///
/// Stored as a sub-document inside the Cosmos DB <c>sessions</c> container (Tier 3 — ADR-015).
/// Content is permitted at Tier 3 (user-owned work history). Never written to Tier 1 app logs.
/// </summary>
public class SessionMessage
{
    /// <summary>Unique identifier for this message.</summary>
    [JsonPropertyName("messageId")]
    public string MessageId { get; set; } = string.Empty;

    /// <summary>Role of the author: "user", "assistant", or "system".</summary>
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    /// <summary>Full message content. Permitted at ADR-015 Tier 3 (user-owned data).</summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the message was created.</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Tool calls made by the assistant during this turn.
    /// Each entry is a raw JSON-serialised tool call payload.
    /// </summary>
    [JsonPropertyName("toolCalls")]
    public List<string> ToolCalls { get; set; } = [];

    /// <summary>
    /// Arbitrary key/value metadata (e.g., token counts, safety scores, model version).
    /// Values are identifiers and metrics — not content. Logged at Tier 3 only.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = [];
}
