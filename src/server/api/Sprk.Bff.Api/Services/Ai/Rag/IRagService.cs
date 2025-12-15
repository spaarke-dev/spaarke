namespace Sprk.Bff.Api.Services.Ai.Rag;

/// <summary>
/// Retrieval-Augmented Generation (RAG) service for knowledge search.
/// Performs hybrid search (keyword + vector) against Azure AI Search indexes.
/// Results are used to ground AI responses with relevant context.
///
/// See SPEC.md section 2.4 for RAG architecture details.
/// </summary>
public interface IRagService
{
    /// <summary>
    /// Search knowledge base using hybrid search (keyword + vector).
    /// Returns ranked results with semantic reranking.
    /// </summary>
    /// <param name="request">Search request with query and filters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results with relevance scores.</returns>
    Task<RagSearchResult> SearchAsync(
        RagSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate embeddings for text using the configured embedding model.
    /// Used for vector search and document indexing.
    /// </summary>
    /// <param name="text">Text to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Vector embedding (1536 dimensions for text-embedding-3-small).</returns>
    Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get grounded context for an AI prompt.
    /// Searches knowledge base and formats results for injection into AI prompt.
    /// </summary>
    /// <param name="query">User query or document context.</param>
    /// <param name="customerId">Customer ID for tenant filtering.</param>
    /// <param name="knowledgeSourceIds">Optional: specific knowledge sources to search.</param>
    /// <param name="maxChunks">Maximum chunks to return (default 5).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Formatted context string for prompt injection.</returns>
    Task<GroundedContext> GetGroundedContextAsync(
        string query,
        Guid customerId,
        Guid[]? knowledgeSourceIds = null,
        int maxChunks = 5,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request for RAG search.
/// </summary>
public record RagSearchRequest
{
    /// <summary>
    /// Search query text.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Customer ID for tenant filtering (Model 1).
    /// </summary>
    public required Guid CustomerId { get; init; }

    /// <summary>
    /// Optional: specific knowledge sources to search.
    /// If null, searches all accessible knowledge sources.
    /// </summary>
    public Guid[]? KnowledgeSourceIds { get; init; }

    /// <summary>
    /// Optional: filter by knowledge type.
    /// </summary>
    public string? KnowledgeType { get; init; }

    /// <summary>
    /// Optional: filter by category.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Optional: filter by tags (any match).
    /// </summary>
    public string[]? Tags { get; init; }

    /// <summary>
    /// Include public (Spaarke) content in results.
    /// </summary>
    public bool IncludePublic { get; init; } = true;

    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    public int Top { get; init; } = 10;

    /// <summary>
    /// Minimum relevance score (0-1) for results.
    /// </summary>
    public double MinScore { get; init; } = 0.5;

    /// <summary>
    /// Search mode: Keyword, Vector, or Hybrid (default).
    /// </summary>
    public SearchMode Mode { get; init; } = SearchMode.Hybrid;

    /// <summary>
    /// Enable semantic reranking for improved relevance.
    /// </summary>
    public bool UseSemanticReranking { get; init; } = true;
}

/// <summary>
/// Search mode options.
/// </summary>
public enum SearchMode
{
    /// <summary>
    /// Traditional keyword/BM25 search only.
    /// </summary>
    Keyword = 0,

    /// <summary>
    /// Vector similarity search only.
    /// </summary>
    Vector = 1,

    /// <summary>
    /// Hybrid search combining keyword and vector (recommended).
    /// </summary>
    Hybrid = 2
}

/// <summary>
/// Result of RAG search.
/// </summary>
public record RagSearchResult
{
    public required bool Success { get; init; }
    public RagSearchHit[] Results { get; init; } = [];
    public int TotalCount { get; init; }
    public string? ErrorMessage { get; init; }
    public long DurationMs { get; init; }

    /// <summary>
    /// Token count if embeddings were generated.
    /// </summary>
    public int? EmbeddingTokens { get; init; }

    public static RagSearchResult Ok(RagSearchHit[] results, int totalCount, long durationMs, int? embeddingTokens = null) => new()
    {
        Success = true,
        Results = results,
        TotalCount = totalCount,
        DurationMs = durationMs,
        EmbeddingTokens = embeddingTokens
    };

    public static RagSearchResult Fail(string errorMessage, long durationMs = 0) => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
        DurationMs = durationMs
    };
}

/// <summary>
/// Individual search hit from RAG search.
/// </summary>
public record RagSearchHit
{
    /// <summary>
    /// Chunk ID (unique identifier).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Knowledge source this chunk belongs to.
    /// </summary>
    public Guid KnowledgeSourceId { get; init; }

    /// <summary>
    /// Original document ID.
    /// </summary>
    public string? DocumentId { get; init; }

    /// <summary>
    /// Document title for display.
    /// </summary>
    public string? DocumentTitle { get; init; }

    /// <summary>
    /// Document filename.
    /// </summary>
    public string? DocumentFileName { get; init; }

    /// <summary>
    /// Chunk index within document.
    /// </summary>
    public int ChunkIndex { get; init; }

    /// <summary>
    /// Chunk content text.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Relevance score (0-1, higher is better).
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Semantic reranker score (if enabled).
    /// </summary>
    public double? RerankerScore { get; init; }

    /// <summary>
    /// Knowledge type (Document, Rule, Template).
    /// </summary>
    public string? KnowledgeType { get; init; }

    /// <summary>
    /// Category.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Tags.
    /// </summary>
    public string[]? Tags { get; init; }

    /// <summary>
    /// Whether this is public (Spaarke) content.
    /// </summary>
    public bool IsPublic { get; init; }
}

/// <summary>
/// Grounded context for AI prompt injection.
/// </summary>
public record GroundedContext
{
    /// <summary>
    /// Formatted context string ready for prompt injection.
    /// </summary>
    public required string ContextText { get; init; }

    /// <summary>
    /// Source documents used for grounding.
    /// </summary>
    public ContextSource[] Sources { get; init; } = [];

    /// <summary>
    /// Total chunks retrieved.
    /// </summary>
    public int ChunkCount { get; init; }

    /// <summary>
    /// Approximate token count of context.
    /// </summary>
    public int EstimatedTokens { get; init; }

    /// <summary>
    /// Search duration.
    /// </summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// Create an empty context (no relevant knowledge found).
    /// </summary>
    public static GroundedContext Empty(long durationMs = 0) => new()
    {
        ContextText = string.Empty,
        Sources = [],
        ChunkCount = 0,
        EstimatedTokens = 0,
        DurationMs = durationMs
    };
}

/// <summary>
/// Source document for grounding attribution.
/// </summary>
public record ContextSource
{
    public string? DocumentId { get; init; }
    public string? DocumentTitle { get; init; }
    public string? DocumentFileName { get; init; }
    public Guid KnowledgeSourceId { get; init; }
    public string? KnowledgeType { get; init; }
    public int[] ChunkIndices { get; init; } = [];
}
