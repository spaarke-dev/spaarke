using System.Security.Claims;
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
    /// Search for relevant knowledge documents using a structured <see cref="RagQuery"/>.
    /// The query is built by <see cref="RagQueryBuilder"/> from document analysis metadata,
    /// providing metadata-aware retrieval with tenant-scoped filtering.
    /// </summary>
    /// <param name="ragQuery">
    /// A structured query containing composite search text and an OData filter expression.
    /// Built from DocumentAnalysisResult metadata (entities, key phrases, document type).
    /// </param>
    /// <param name="deploymentId">
    /// Optional deployment ID for explicit index selection.
    /// If null, uses the default deployment for the tenant resolved from the filter expression.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ranked search results with relevance scores.</returns>
    Task<RagSearchResponse> SearchAsync(
        RagQuery ragQuery,
        Guid? deploymentId,
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
    /// <remarks>
    /// This 2-argument overload preserves the original signature exactly so all existing
    /// callers (and Moq expression-tree setups using <c>It.IsAny&lt;...&gt;()</c> matchers) continue
    /// to compile and behave UNCHANGED — backward compatibility is the binding requirement
    /// (multi-container-multi-index-r1 spec NFR-02). It delegates to the 3-argument
    /// overload below with <c>searchIndexName = null</c>, which routes via the tenant-
    /// default chain.
    /// </remarks>
    Task<IReadOnlyList<IndexResult>> IndexDocumentsBatchAsync(
        IEnumerable<KnowledgeDocument> documents,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch-indexes documents, routing the batch to a named Azure AI Search index.
    /// FR-BFF-07 / multi-container-multi-index-r1 indexer-routing-fix: when
    /// <paramref name="searchIndexName"/> is non-empty, the batch is written to that index
    /// after allow-list validation (FR-BFF-02). When null/empty, falls through to the tenant-
    /// default chain (existing 2-arg behavior).
    /// </summary>
    /// <param name="documents">The document chunks to index.</param>
    /// <param name="searchIndexName">
    /// Optional explicit Azure AI Search index name. When non-null/whitespace, the underlying
    /// <c>IKnowledgeDeploymentService.GetSearchClientAsync(tenantId, indexName, ct)</c> 3-arg
    /// overload validates the value against <c>AiSearchOptions.AllowedIndexes</c> and rejects
    /// non-allow-listed values with <c>SdapProblemException(INDEX_NOT_ALLOWED, 400)</c>. When
    /// null/whitespace, the existing 2-tier fall-through chain applies
    /// (<c>sprk_aiknowledgedeployment</c> → <c>AiSearchOptions.KnowledgeIndexName</c>) —
    /// byte-for-byte backward-compatible (FR-BFF-04 / NFR-02).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Results for each indexed document.</returns>
    /// <exception cref="Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException">
    /// Thrown with code <c>INDEX_NOT_ALLOWED</c> (status 400) when <paramref name="searchIndexName"/>
    /// is non-empty AND not present in <c>AiSearchOptions.AllowedIndexes</c>.
    /// </exception>
    Task<IReadOnlyList<IndexResult>> IndexDocumentsBatchAsync(
        IEnumerable<KnowledgeDocument> documents,
        string? searchIndexName,
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

    // ── Knowledge-base index administration (D-09 §2 B8, task 011 Phase 1b Tier 3) ────
    // Endpoints (KnowledgeBaseEndpoints) used to inject Azure SDK SearchIndexClient directly
    // and call its index/search APIs. Per ADR-007 (facade pattern) endpoints should consume
    // domain services, not Azure SDK clients. The following 3 methods absorb those direct
    // SDK calls so the endpoints depend only on IRagService — which has a fail-fast
    // Null-Object implementation (NullRagService) registered when the kill switch is off.

    /// <summary>
    /// Returns document chunk counts for the knowledge and discovery indexes scoped to the
    /// requesting tenant. Used by the knowledge-base admin health endpoint.
    /// </summary>
    /// <param name="tenantId">Tenant ID scoping the count filter (ADR-014).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Health summary with per-index document counts and timestamp.</returns>
    Task<KnowledgeIndexHealth> GetIndexHealthAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a paged list of indexed document summaries for the requesting tenant in the
    /// specified index. Used by the knowledge-base admin "list documents" endpoint.
    /// </summary>
    /// <param name="indexName">Target index name (knowledge or discovery).</param>
    /// <param name="tenantId">Tenant ID scoping the filter (ADR-014).</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Page size (1-200).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paged indexed-document listing.</returns>
    /// <exception cref="System.ArgumentException">Thrown when <paramref name="indexName"/> is not a known index.</exception>
    Task<IndexedDocumentsPage> GetIndexedDocumentsAsync(
        string indexName,
        string tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all chunks for a source document from the specified index, scoped to tenant.
    /// Used by the knowledge-base admin "delete document" endpoint.
    /// </summary>
    /// <param name="indexName">Target index name (knowledge or discovery).</param>
    /// <param name="documentId">Source document ID.</param>
    /// <param name="tenantId">Tenant ID scoping the filter (ADR-014).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of chunks deleted (zero when no chunks match).</returns>
    /// <exception cref="System.ArgumentException">Thrown when <paramref name="indexName"/> is not a known index.</exception>
    Task<int> DeleteIndexedDocumentAsync(
        string indexName,
        string documentId,
        string tenantId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Health summary returned by <see cref="IRagService.GetIndexHealthAsync"/>.
/// Mirrors the previous <c>KnowledgeIndexHealthResult</c> shape verbatim (D-09 §2 B8).
/// </summary>
public sealed record KnowledgeIndexHealth(
    long KnowledgeDocCount,
    long DiscoveryDocCount,
    DateTimeOffset LastUpdated,
    string KnowledgeIndexName,
    string DiscoveryIndexName);

/// <summary>
/// Paged list of indexed-document summaries returned by
/// <see cref="IRagService.GetIndexedDocumentsAsync"/>.
/// </summary>
public sealed record IndexedDocumentsPage(
    string IndexName,
    IReadOnlyList<IndexedDocumentSummary> Documents,
    int Page,
    int PageSize,
    long TotalCount);

/// <summary>
/// Summary of a single indexed document chunk; mirrors the previous
/// <c>KnowledgeDocumentSummary</c> verbatim.
/// </summary>
public sealed record IndexedDocumentSummary(
    string ChunkId,
    string? DocumentId,
    string FileName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

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
    /// Optional filter by multiple knowledge source IDs.
    /// When set, limits search to chunks from ANY of the specified sources (OR filter).
    /// Takes precedence over single <see cref="KnowledgeSourceId"/> when both are set.
    /// </summary>
    public IReadOnlyList<string>? KnowledgeSourceIds { get; init; }

    /// <summary>
    /// Optional exclusion filter for knowledge source IDs.
    /// When set, excludes chunks from ANY of the specified sources (NOT filter).
    /// </summary>
    public IReadOnlyList<string>? ExcludeKnowledgeSourceIds { get; init; }

    /// <summary>
    /// Optional filter by document type.
    /// e.g., "contract", "policy", "procedure"
    /// </summary>
    public string? DocumentType { get; init; }

    /// <summary>
    /// The calling user's <see cref="ClaimsPrincipal"/>, used to resolve Azure AD group
    /// membership for privilege_group_ids security filtering (AIPU2-027).
    /// When null, only public documents (those with no privilege_group_ids) are returned.
    /// Set this from the endpoint's HttpContext.User for all user-facing search calls.
    /// </summary>
    public ClaimsPrincipal? CallerPrincipal { get; init; }

    /// <summary>
    /// Optional filter by tags (OR semantics).
    /// Returns documents matching any of the specified tags.
    /// </summary>
    public IList<string>? Tags { get; init; }

    /// <summary>
    /// Optional required tags filter (AND semantics).
    /// Returns only documents that have ALL of the specified tags.
    /// </summary>
    public IReadOnlyList<string>? RequiredTags { get; init; }

    /// <summary>
    /// Optional tag exclusion filter.
    /// Excludes documents that have any of the specified tags.
    /// </summary>
    public IReadOnlyList<string>? ExcludeTags { get; init; }

    /// <summary>
    /// Optional parent entity type filter for entity-scoped search.
    /// e.g., "matter", "project", "invoice", "account", "contact"
    /// Both ParentEntityType and ParentEntityId must be set for entity scoping.
    /// </summary>
    public string? ParentEntityType { get; init; }

    /// <summary>
    /// Optional parent entity ID filter for entity-scoped search.
    /// Both ParentEntityType and ParentEntityId must be set for entity scoping.
    /// </summary>
    public string? ParentEntityId { get; init; }

    /// <summary>
    /// multi-container-multi-index-r1 FR-BFF-07 — optional explicit Azure AI Search index name
    /// to target for this search request. When provided (non-null / non-whitespace), the BFF
    /// resolver routes via the 3-argument <see cref="IKnowledgeDeploymentService.GetSearchClientAsync(string, string?, System.Threading.CancellationToken)"/>
    /// overload which validates the value against <c>AiSearchOptions.AllowedIndexes</c>
    /// (rejecting non-allow-listed values with <c>INDEX_NOT_ALLOWED</c> → 400 per FR-BFF-02 /
    /// NFR-08). When null or whitespace, the resolver falls through to the existing 2-tier
    /// chain (<c>sprk_aiknowledgedeployment</c> Dataverse entity → <c>AiSearchOptions.KnowledgeIndexName</c>
    /// fallback) — byte-for-byte backward-compatible with all existing callers (FR-BFF-04 /
    /// NFR-02). Has no effect under session-scoped routing (when <see cref="SessionId"/> is set,
    /// the session-files index is selected directly via the injected <c>SearchIndexClient</c>).
    /// </summary>
    public string? SearchIndexName { get; init; }

    /// <summary>
    /// R5 spec §4.2 / FR-09 — optional session identifier for session-scoped retrieval.
    /// When set (non-null/non-empty), <see cref="IRagService.SearchAsync(string, RagSearchOptions, CancellationToken)"/>
    /// routes the underlying <c>SearchClient</c> to the session-files index
    /// (see <c>AiSearchOptions.SessionFilesIndexName</c>) instead of the tenant-scoped
    /// knowledge index, and appends a <c>sessionId eq '...'</c> clause to the OData
    /// filter ANDed with the existing unconditional <c>tenantId eq '...'</c> clause —
    /// preserving the ADR-014 tenant-isolation invariant (a session query in tenant A
    /// can never leak across to tenant B). When null/empty, behavior is byte-for-byte
    /// identical to the pre-R5 path: tenant-deployment routing via
    /// <c>IKnowledgeDeploymentService</c>. Under session-scoped routing, the
    /// <c>KnowledgeSourceId(s)</c> / <c>ExcludeKnowledgeSourceIds</c> /
    /// <c>ParentEntityType</c>+<c>ParentEntityId</c> / privilege-group filters are
    /// SKIPPED (with debug log) because the session-files schema does not carry those
    /// columns (per task 001 schema).
    /// </summary>
    public string? SessionId { get; init; }

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
    /// The source document ID (sprk_document record ID).
    /// Null for orphan files that have no linked Dataverse document.
    /// </summary>
    public string? DocumentId { get; init; }

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
