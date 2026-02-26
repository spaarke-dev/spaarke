using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai.SemanticSearch;

/// <summary>
/// Metadata about the search execution.
/// </summary>
public sealed record SearchMetadata
{
    /// <summary>
    /// Total number of matching documents in the index.
    /// </summary>
    [JsonPropertyName("totalResults")]
    public long TotalResults { get; init; }

    /// <summary>
    /// Number of results returned in this response.
    /// </summary>
    [JsonPropertyName("returnedResults")]
    public int ReturnedResults { get; init; }

    /// <summary>
    /// Total search execution time in milliseconds.
    /// </summary>
    [JsonPropertyName("searchDurationMs")]
    public long SearchDurationMs { get; init; }

    /// <summary>
    /// Embedding generation time in milliseconds.
    /// </summary>
    [JsonPropertyName("embeddingDurationMs")]
    public long EmbeddingDurationMs { get; init; }

    /// <summary>
    /// The actual search mode executed (may differ from requested if fallback occurred).
    /// </summary>
    [JsonPropertyName("executedMode")]
    public string? ExecutedMode { get; init; }

    /// <summary>
    /// Filters that were applied to the search.
    /// </summary>
    [JsonPropertyName("appliedFilters")]
    public AppliedFilters? AppliedFilters { get; init; }

    /// <summary>
    /// Any warnings generated during search execution.
    /// </summary>
    [JsonPropertyName("warnings")]
    public IReadOnlyList<SearchWarning>? Warnings { get; init; }
}

/// <summary>
/// Filters that were applied to the search.
/// </summary>
public sealed record AppliedFilters
{
    /// <summary>
    /// Search scope that was applied.
    /// </summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    /// <summary>
    /// Entity type filter (when scope=entity).
    /// </summary>
    [JsonPropertyName("entityType")]
    public string? EntityType { get; init; }

    /// <summary>
    /// Entity ID filter (when scope=entity).
    /// </summary>
    [JsonPropertyName("entityId")]
    public string? EntityId { get; init; }

    /// <summary>
    /// Number of document IDs filtered (when scope=documentIds).
    /// </summary>
    [JsonPropertyName("documentIdCount")]
    public int? DocumentIdCount { get; init; }

    /// <summary>
    /// Entity types that were filtered.
    /// </summary>
    [JsonPropertyName("entityTypes")]
    public IReadOnlyList<string>? EntityTypes { get; init; }

    /// <summary>
    /// Document types that were filtered.
    /// </summary>
    [JsonPropertyName("documentTypes")]
    public IReadOnlyList<string>? DocumentTypes { get; init; }

    /// <summary>
    /// File types that were filtered.
    /// </summary>
    [JsonPropertyName("fileTypes")]
    public IReadOnlyList<string>? FileTypes { get; init; }

    /// <summary>
    /// Tags that were filtered.
    /// </summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>
    /// Date range that was applied.
    /// </summary>
    [JsonPropertyName("dateRange")]
    public AppliedDateRange? DateRange { get; init; }
}

/// <summary>
/// Applied date range filter details.
/// </summary>
public sealed record AppliedDateRange
{
    /// <summary>
    /// Start of date range.
    /// </summary>
    [JsonPropertyName("from")]
    public DateTimeOffset? From { get; init; }

    /// <summary>
    /// End of date range.
    /// </summary>
    [JsonPropertyName("to")]
    public DateTimeOffset? To { get; init; }
}

/// <summary>
/// Warning generated during search execution.
/// </summary>
public sealed record SearchWarning
{
    /// <summary>
    /// Warning code (e.g., "EMBEDDING_UNAVAILABLE").
    /// </summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    /// <summary>
    /// Human-readable warning message.
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>
    /// Optional additional details.
    /// </summary>
    [JsonPropertyName("details")]
    public string? Details { get; init; }
}

/// <summary>
/// Standard warning codes for semantic search.
/// </summary>
public static class SearchWarningCode
{
    /// <summary>
    /// Embedding service unavailable; fell back to keyword-only search.
    /// </summary>
    public const string EmbeddingUnavailable = "EMBEDDING_UNAVAILABLE";

    /// <summary>
    /// Embedding generation failed; fell back to keyword-only search.
    /// </summary>
    public const string EmbeddingFallback = "EMBEDDING_FALLBACK";

    /// <summary>
    /// Rate limit reached; request was throttled.
    /// </summary>
    public const string RateLimited = "RATE_LIMITED";

    /// <summary>
    /// Search was partially successful with some results unavailable.
    /// </summary>
    public const string PartialResults = "PARTIAL_RESULTS";
}
