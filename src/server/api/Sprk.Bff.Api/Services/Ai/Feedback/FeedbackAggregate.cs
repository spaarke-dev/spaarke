using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Feedback;

/// <summary>
/// Aggregated feedback statistics for a single playbook or capability.
///
/// Returned by <see cref="IFeedbackService.GetAggregateByPlaybookAsync"/> and
/// <see cref="IFeedbackService.GetAggregateByCapabilityAsync"/>.
/// </summary>
public sealed class FeedbackAggregate
{
    /// <summary>
    /// The playbook ID or capability ID being aggregated.
    /// </summary>
    [JsonPropertyName("entityId")]
    public required string EntityId { get; init; }

    /// <summary>
    /// Discriminator: <c>"playbook"</c> or <c>"capability"</c>.
    /// </summary>
    [JsonPropertyName("entityType")]
    public required string EntityType { get; init; }

    /// <summary>Total number of feedback submissions in the date range.</summary>
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; init; }

    /// <summary>Number of thumbs-up ratings.</summary>
    [JsonPropertyName("thumbsUpCount")]
    public int ThumbsUpCount { get; init; }

    /// <summary>Number of thumbs-down ratings.</summary>
    [JsonPropertyName("thumbsDownCount")]
    public int ThumbsDownCount { get; init; }

    /// <summary>
    /// Satisfaction rate as a percentage (0–100).
    /// Calculated as <c>ThumbsUpCount / TotalCount * 100</c>.
    /// Returns 0 when <see cref="TotalCount"/> is zero.
    /// </summary>
    [JsonPropertyName("satisfactionRate")]
    public double SatisfactionRate =>
        TotalCount == 0 ? 0.0 : Math.Round((double)ThumbsUpCount / TotalCount * 100.0, 2);

    /// <summary>
    /// The date range over which feedback was aggregated.
    /// </summary>
    [JsonPropertyName("dateRange")]
    public FeedbackDateRange DateRange { get; init; } = new();

    /// <summary>
    /// The last 10 comments from thumbs-down responses, ordered by most recent first.
    /// Useful for surfacing common failure patterns to product teams.
    /// Only entries that include a non-null comment are represented here.
    /// </summary>
    [JsonPropertyName("topNegativeComments")]
    public IReadOnlyList<string> TopNegativeComments { get; init; } = [];
}

/// <summary>
/// Inclusive date range used to filter feedback aggregation queries.
/// </summary>
public sealed class FeedbackDateRange
{
    /// <summary>Inclusive start of the range. Null means no lower bound.</summary>
    [JsonPropertyName("from")]
    public DateTimeOffset? From { get; init; }

    /// <summary>Inclusive end of the range. Null means no upper bound.</summary>
    [JsonPropertyName("to")]
    public DateTimeOffset? To { get; init; }
}
