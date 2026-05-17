using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Feedback;

/// <summary>
/// User feedback for a single AI response (thumbs up/down + optional comment).
///
/// Storage: Cosmos DB container <c>feedback</c>, partition key <c>/tenantId</c>.
/// Retention: 90 days (ADR-015 Tier 3 operational data).
///
/// The <see cref="Comment"/> field is capped at 500 characters before storage.
/// <see cref="PlaybookId"/> and <see cref="CapabilityId"/> enable aggregation queries
/// to measure playbook and capability quality over time.
/// </summary>
public sealed class FeedbackEntry
{
    /// <summary>
    /// Cosmos DB document id. Generated as a new GUID on submission.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("D");

    /// <summary>
    /// Tenant identifier used as the Cosmos DB partition key (/tenantId).
    /// Every feedback record is scoped to a single tenant (ADR-015).
    /// </summary>
    [JsonPropertyName("tenantId")]
    public required string TenantId { get; init; }

    /// <summary>Azure AD object ID of the user who submitted this feedback.</summary>
    [JsonPropertyName("userId")]
    public required string UserId { get; init; }

    /// <summary>Session correlation identifier linking feedback to the chat/analysis session.</summary>
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }

    /// <summary>
    /// Zero-based turn index within the session identifying the specific AI response
    /// the user rated. Together with <see cref="SessionId"/> this uniquely identifies a response.
    /// </summary>
    [JsonPropertyName("turnIndex")]
    public int TurnIndex { get; init; }

    /// <summary>
    /// User's rating for the AI response.
    /// <see cref="FeedbackRating.ThumbsUp"/> (1) or <see cref="FeedbackRating.ThumbsDown"/> (-1).
    /// </summary>
    [JsonPropertyName("rating")]
    public FeedbackRating Rating { get; init; }

    /// <summary>
    /// Optional free-text comment. Capped at 500 characters on write.
    /// Null when the user did not supply a comment.
    /// </summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>
    /// Playbook identifier that produced the rated response. Null for non-playbook interactions.
    /// Used in aggregation queries to compute per-playbook satisfaction rates.
    /// </summary>
    [JsonPropertyName("playbookId")]
    public string? PlaybookId { get; init; }

    /// <summary>
    /// AI capability (tool/action) identifier associated with the rated response.
    /// Null when the response did not originate from a specific capability.
    /// Used in aggregation queries to compute per-capability satisfaction rates.
    /// </summary>
    [JsonPropertyName("capabilityId")]
    public string? CapabilityId { get; init; }

    /// <summary>UTC timestamp when the feedback was submitted.</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Rating values for AI response feedback.
/// Stored as an integer in Cosmos DB so Cosmos SQL aggregation functions work naturally.
/// </summary>
public enum FeedbackRating
{
    /// <summary>Positive rating — the response was useful.</summary>
    ThumbsUp = 1,

    /// <summary>Negative rating — the response was not useful.</summary>
    ThumbsDown = -1
}
