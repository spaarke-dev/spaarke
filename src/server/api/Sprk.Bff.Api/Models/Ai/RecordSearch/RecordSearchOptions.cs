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

    /// <summary>
    /// When <c>true</c>, the search skips the Azure <b>semantic reranker</b> and ranks purely by keyword
    /// relevance (BM25), normalizing scores with a bounded saturating transform. Default <c>false</c>
    /// (existing behavior — semantic reranking on).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The semantic reranker orders by conceptual similarity, which is the right default for RAG document
    /// search but WRONG for matching a communication to a NAMED record: it buries exact-name matches under
    /// conceptually-similar ones (email-r4 UAT 2026-07-17 — an email titled "New Matter … Smith v Smith"
    /// floated a matter named "Test New Matter via Workspace" above the exact-name matter "Smith v Smith",
    /// which BM25 ranked #1). The Association Engine's record-matching rungs set this flag so entity-name
    /// resolution ranks by keyword relevance. Interactive record search (the endpoints) leaves it
    /// <c>false</c> so its semantic ranking is unchanged.
    /// </para>
    /// </remarks>
    [JsonPropertyName("preferKeywordRanking")]
    public bool PreferKeywordRanking { get; init; }
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
