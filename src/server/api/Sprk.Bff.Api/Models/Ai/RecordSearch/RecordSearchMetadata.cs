using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai.RecordSearch;

/// <summary>
/// Metadata about the record search execution.
/// </summary>
public sealed record RecordSearchMetadata
{
    /// <summary>
    /// Total number of matching records in the index.
    /// </summary>
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; init; }

    /// <summary>
    /// Total search execution time in milliseconds.
    /// </summary>
    [JsonPropertyName("searchTime")]
    public double SearchTime { get; init; }

    /// <summary>
    /// The hybrid search mode that was executed (echoes back the requested mode).
    /// </summary>
    [JsonPropertyName("hybridMode")]
    public required string HybridMode { get; init; }
}
