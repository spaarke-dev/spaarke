using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai.RecordSearch;

/// <summary>
/// Options for record search pagination and behavior.
/// </summary>
public sealed record RecordSearchOptions
{
    /// <summary>
    /// Maximum number of results to return. Range: 1-50, default: 20.
    /// </summary>
    [Range(1, 50, ErrorMessage = "Limit must be between 1 and 50")]
    [JsonPropertyName("limit")]
    public int Limit { get; init; } = 20;

    /// <summary>
    /// Number of results to skip for pagination. Range: 0-1000, default: 0.
    /// </summary>
    [Range(0, 1000, ErrorMessage = "Offset must be between 0 and 1000")]
    [JsonPropertyName("offset")]
    public int Offset { get; init; } = 0;

    /// <summary>
    /// Hybrid search mode: "rrf" (default), "vectorOnly", or "keywordOnly".
    /// </summary>
    [JsonPropertyName("hybridMode")]
    public string HybridMode { get; init; } = RecordHybridSearchMode.Rrf;
}

/// <summary>
/// Valid hybrid search mode values for record search.
/// </summary>
public static class RecordHybridSearchMode
{
    /// <summary>
    /// RRF (Reciprocal Rank Fusion) - combines vector and keyword search (default).
    /// </summary>
    public const string Rrf = "rrf";

    /// <summary>
    /// Vector-only search - uses embeddings, no keyword matching.
    /// </summary>
    public const string VectorOnly = "vectorOnly";

    /// <summary>
    /// Keyword-only search - uses BM25, no embeddings.
    /// </summary>
    public const string KeywordOnly = "keywordOnly";

    /// <summary>All valid mode values.</summary>
    public static readonly string[] ValidModes = [Rrf, VectorOnly, KeywordOnly];

    /// <summary>Checks if the mode is valid.</summary>
    public static bool IsValid(string? mode) =>
        !string.IsNullOrWhiteSpace(mode) && ValidModes.Contains(mode.ToLowerInvariant());
}
