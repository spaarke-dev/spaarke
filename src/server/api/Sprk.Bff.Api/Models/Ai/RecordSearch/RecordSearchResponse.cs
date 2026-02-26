using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai.RecordSearch;

/// <summary>
/// Response from record search API endpoint (POST /api/ai/search/records).
/// </summary>
public sealed record RecordSearchResponse
{
    /// <summary>
    /// List of matching record search results.
    /// </summary>
    [JsonPropertyName("results")]
    public required IReadOnlyList<RecordSearchResult> Results { get; init; }

    /// <summary>
    /// Search metadata including total count, timing, and mode.
    /// </summary>
    [JsonPropertyName("metadata")]
    public required RecordSearchMetadata Metadata { get; init; }
}
