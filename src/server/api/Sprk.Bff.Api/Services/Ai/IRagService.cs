using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Provides RAG (Retrieval-Augmented Generation) capabilities with hybrid search.
/// Combines keyword search, vector search, and semantic ranking for optimal relevance.
/// </summary>
/// <remarks>
/// Search pipeline:
/// 1. Generate embedding for query using Azure OpenAI
/// 2. Execute hybrid search (keyword + vector) in Azure AI Search
/// 3. Apply semantic ranking for re-ranking results
/// 4. Return ranked results with context
///
/// Integrates with IKnowledgeDeploymentService for multi-tenant index routing.
/// Supports 3 deployment models: Shared, Dedicated, CustomerOwned.
/// </remarks>
public interface IRagService
{
    /// <summary>
    /// Search for relevant knowledge documents using hybrid search.
    /// </summary>
    /// <param name="query">The search query text.</param>
    /// <param name="options">Search options including tenant, filters, and limits.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ranked search results with relevance scores.</returns>
    Task<RagSearchResponse> SearchAsync(
        string query,
        RagSearchOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Index a document chunk into the knowledge base.
    /// </summary>
    /// <param name="document">The document chunk to index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The indexed document with assigned ID.</returns>
    Task<KnowledgeDocument> IndexDocumentAsync(
        KnowledgeDocument document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Index multiple document chunks in a batch.
    /// More efficient for bulk operations.
    /// </summary>
    /// <param name="documents">The document chunks to index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Results for each indexed document.</returns>
    Task<IReadOnlyList<IndexResult>> IndexDocumentsBatchAsync(
        IEnumerable<KnowledgeDocument> documents,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a document chunk from the knowledge base.
    /// </summary>
    /// <param name="documentId">The document chunk ID to delete.</param>
    /// <param name="tenantId">The tenant ID for routing to correct index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteDocumentAsync(
        string documentId,
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all chunks for a source document.
    /// </summary>
    /// <param name="sourceDocumentId">The source document ID (SPE document ID).</param>
    /// <param name="tenantId">The tenant ID for routing to correct index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of chunks deleted.</returns>
    Task<int> DeleteBySourceDocumentAsync(
        string sourceDocumentId,
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate an embedding for text content.
    /// Uses caching when available.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Vector embedding.</returns>
    Task<ReadOnlyMemory<float>> GetEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for RAG search operations.
/// </summary>
public record RagSearchOptions
{
    /// <summary>
    /// Tenant identifier for multi-tenant routing.
    /// Required for all search operations.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Optional deployment ID for explicit deployment selection.
    /// If null, uses default deployment for tenant.
    /// </summary>
    public Guid? DeploymentId { get; init; }

    /// <summary>
    /// Maximum number of results to return.
    /// Default: 5, Max: 20
    /// </summary>
    public int TopK { get; init; } = 5;

    /// <summary>
    /// Minimum relevance score threshold (0.0-1.0).
    /// Results below this score are filtered out.
    /// Default: 0.7
    /// </summary>
    public float MinScore { get; init; } = 0.7f;

    /// <summary>
    /// Optional filter by knowledge source ID.
    /// Limits search to specific knowledge base.
    /// </summary>
    public string? KnowledgeSourceId { get; init; }

    /// <summary>
    /// Optional filter by document type.
    /// e.g., "contract", "policy", "procedure"
    /// </summary>
    public string? DocumentType { get; init; }

    /// <summary>
    /// Optional filter by tags.
    /// Returns documents matching any of the specified tags.
    /// </summary>
    public IList<string>? Tags { get; init; }

    /// <summary>
    /// Whether to use semantic ranking.
    /// Default: true (recommended for best relevance)
    /// </summary>
    public bool UseSemanticRanking { get; init; } = true;

    /// <summary>
    /// Whether to include vector search.
    /// Default: true (required for hybrid search)
    /// </summary>
    public bool UseVectorSearch { get; init; } = true;

    /// <summary>
    /// Whether to include keyword search.
    /// Default: true (required for hybrid search)
    /// </summary>
    public bool UseKeywordSearch { get; init; } = true;
}

/// <summary>
/// Response from a RAG search operation.
/// </summary>
public record RagSearchResponse
{
    /// <summary>
    /// The original search query.
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Search results ranked by relevance.
    /// </summary>
    public IReadOnlyList<RagSearchResult> Results { get; init; } = [];

    /// <summary>
    /// Total count of matching documents (before filtering/limiting).
    /// </summary>
    public long TotalCount { get; init; }

    /// <summary>
    /// Time taken for the search operation in milliseconds.
    /// </summary>
    public long SearchDurationMs { get; init; }

    /// <summary>
    /// Time taken for embedding generation in milliseconds.
    /// </summary>
    public long EmbeddingDurationMs { get; init; }

    /// <summary>
    /// Whether embedding was retrieved from cache.
    /// </summary>
    public bool EmbeddingCacheHit { get; init; }
}

/// <summary>
/// A single result from a RAG search.
/// </summary>
public record RagSearchResult
{
    /// <summary>
    /// The document chunk ID.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// The source document ID.
    /// </summary>
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>
    /// The source document name.
    /// </summary>
    public string DocumentName { get; init; } = string.Empty;

    /// <summary>
    /// The chunk content text.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// The knowledge source name.
    /// </summary>
    public string? KnowledgeSourceName { get; init; }

    /// <summary>
    /// Combined relevance score (0.0-1.0).
    /// Higher is more relevant.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Semantic ranking score (if semantic ranking was applied).
    /// </summary>
    public double? SemanticScore { get; init; }

    /// <summary>
    /// Chunk index within the source document.
    /// </summary>
    public int ChunkIndex { get; init; }

    /// <summary>
    /// Total chunks in the source document.
    /// </summary>
    public int ChunkCount { get; init; }

    /// <summary>
    /// Highlighted snippets showing query matches.
    /// </summary>
    public IReadOnlyList<string>? Highlights { get; init; }

    /// <summary>
    /// Document metadata as JSON.
    /// </summary>
    public string? Metadata { get; init; }

    /// <summary>
    /// Tags associated with the document.
    /// </summary>
    public IList<string>? Tags { get; init; }
}

/// <summary>
/// Result of an index operation.
/// </summary>
public record IndexResult
{
    /// <summary>
    /// The document chunk ID.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool Succeeded { get; init; }

    /// <summary>
    /// Error message if operation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static IndexResult Success(string id) => new() { Id = id, Succeeded = true };

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static IndexResult Failure(string id, string errorMessage) =>
        new() { Id = id, Succeeded = false, ErrorMessage = errorMessage };
}
