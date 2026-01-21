using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai.SemanticSearch;

/// <summary>
/// Options for semantic search pagination and behavior.
/// </summary>
public sealed record SearchOptions
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
    /// Whether to include highlighted text snippets in results.
    /// </summary>
    [JsonPropertyName("includeHighlights")]
    public bool IncludeHighlights { get; init; } = true;

    /// <summary>
    /// Hybrid search mode: "rrf" (default), "vectorOnly", or "keywordOnly".
    /// </summary>
    [JsonPropertyName("hybridMode")]
    public string HybridMode { get; init; } = HybridSearchMode.Rrf;
}

/// <summary>
/// Valid hybrid search mode values.
/// </summary>
public static class HybridSearchMode
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

    /// <summary>Checks if the mode requires an embedding (query text).</summary>
    public static bool RequiresEmbedding(string? mode) =>
        mode?.ToLowerInvariant() != KeywordOnly;
}
