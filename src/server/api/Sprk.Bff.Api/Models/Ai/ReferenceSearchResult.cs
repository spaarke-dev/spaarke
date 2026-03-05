namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// A single result from a reference knowledge search against the <c>spaarke-rag-references</c> index.
/// Contains golden reference content with provenance metadata for grounding AI responses.
/// </summary>
public record ReferenceSearchResult
{
    /// <summary>
    /// The chunk ID in the reference index (format: {knowledgeSourceId}_ref_{chunkIndex}).
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// The text content of the reference chunk.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// The Dataverse knowledge source ID (sprk_analysisknowledge record).
    /// </summary>
    public string KnowledgeSourceId { get; init; } = string.Empty;

    /// <summary>
    /// Display name of the knowledge source.
    /// </summary>
    public string KnowledgeSourceName { get; init; } = string.Empty;

    /// <summary>
    /// Domain classification of the reference (e.g., "legal", "finance", "hr").
    /// Stored in the <c>documentType</c> field of the reference index.
    /// </summary>
    public string? Domain { get; init; }

    /// <summary>
    /// Combined relevance score (0.0–1.0). Uses semantic reranker score when available,
    /// otherwise falls back to the hybrid search score.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Semantic reranker score if semantic ranking was applied; null otherwise.
    /// </summary>
    public double? SemanticScore { get; init; }

    /// <summary>
    /// Zero-based chunk index within the knowledge source.
    /// </summary>
    public int ChunkIndex { get; init; }

    /// <summary>
    /// Total number of chunks for this knowledge source.
    /// </summary>
    public int ChunkCount { get; init; }

    /// <summary>
    /// Highlighted snippets showing query matches. Null if no highlights available.
    /// </summary>
    public IReadOnlyList<string>? Highlights { get; init; }
}

/// <summary>
/// Response from a reference knowledge search operation.
/// </summary>
public record ReferenceSearchResponse
{
    /// <summary>
    /// The original search query.
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Search results ranked by relevance.
    /// </summary>
    public IReadOnlyList<ReferenceSearchResult> Results { get; init; } = [];

    /// <summary>
    /// Total count of matching documents (before TopK limiting).
    /// </summary>
    public long TotalCount { get; init; }

    /// <summary>
    /// Time taken for the search execution in milliseconds.
    /// </summary>
    public long SearchDurationMs { get; init; }

    /// <summary>
    /// Time taken for embedding generation in milliseconds.
    /// </summary>
    public long EmbeddingDurationMs { get; init; }

    /// <summary>
    /// Whether the query embedding was retrieved from cache.
    /// </summary>
    public bool EmbeddingCacheHit { get; init; }
}

/// <summary>
/// Options for reference knowledge search operations.
/// </summary>
public record ReferenceSearchOptions
{
    /// <summary>
    /// Tenant identifier for multi-tenant security filtering.
    /// For system-wide golden references, use "system".
    /// Required on all queries.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Optional filter by knowledge source IDs.
    /// When set, limits search to chunks from ANY of the specified sources (OR filter).
    /// </summary>
    public IReadOnlyList<string>? KnowledgeSourceIds { get; init; }

    /// <summary>
    /// Optional domain filter (e.g., "legal", "finance").
    /// Maps to the <c>documentType</c> field in the reference index.
    /// </summary>
    public string? Domain { get; init; }

    /// <summary>
    /// Maximum number of results to return.
    /// Default: 5, Max: 20.
    /// </summary>
    public int TopK { get; init; } = 5;

    /// <summary>
    /// Minimum relevance score threshold (0.0–1.0).
    /// Results below this score are filtered out. Default: 0.5.
    /// Lower default than customer doc search because reference content is curated.
    /// </summary>
    public float MinScore { get; init; } = 0.5f;
}
