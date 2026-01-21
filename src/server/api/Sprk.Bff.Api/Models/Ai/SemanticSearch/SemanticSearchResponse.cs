using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai.SemanticSearch;

/// <summary>
/// Response from semantic search API endpoint.
/// </summary>
public sealed record SemanticSearchResponse
{
    /// <summary>
    /// List of search results.
    /// </summary>
    [JsonPropertyName("results")]
    public required IReadOnlyList<SearchResult> Results { get; init; }

    /// <summary>
    /// Search metadata including timing and warnings.
    /// </summary>
    [JsonPropertyName("metadata")]
    public required SearchMetadata Metadata { get; init; }
}

/// <summary>
/// Response from semantic search count endpoint.
/// </summary>
public sealed record SemanticSearchCountResponse
{
    /// <summary>
    /// Total number of matching documents.
    /// </summary>
    [JsonPropertyName("count")]
    public long Count { get; init; }

    /// <summary>
    /// Filters that were applied to the search.
    /// </summary>
    [JsonPropertyName("appliedFilters")]
    public AppliedFilters? AppliedFilters { get; init; }

    /// <summary>
    /// Any warnings generated during search.
    /// </summary>
    [JsonPropertyName("warnings")]
    public IReadOnlyList<SearchWarning>? Warnings { get; init; }
}
