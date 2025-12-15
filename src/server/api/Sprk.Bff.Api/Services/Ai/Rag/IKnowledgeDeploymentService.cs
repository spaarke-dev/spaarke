namespace Sprk.Bff.Api.Services.Ai.Rag;

/// <summary>
/// Manages knowledge deployments and indexing operations for RAG.
/// Supports three deployment models:
/// - Model 1: Shared index with tenant filtering (cost-effective)
/// - Model 2: Dedicated index per customer (isolated, higher performance)
/// - Model 3: Customer-owned index (customer manages their own AI Search)
///
/// See SPEC.md section 2.4 for deployment model details.
/// </summary>
public interface IKnowledgeDeploymentService
{
    /// <summary>
    /// Get deployment configuration for a customer.
    /// </summary>
    /// <param name="customerId">Customer (Account) ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deployment configuration or null if not configured.</returns>
    Task<KnowledgeDeployment?> GetDeploymentAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Index a document into the knowledge base.
    /// Document is chunked, embedded, and stored in the appropriate index based on deployment model.
    /// </summary>
    /// <param name="request">Indexing request with document content and metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with chunk count and any errors.</returns>
    Task<IndexDocumentResult> IndexDocumentAsync(
        IndexDocumentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a document from the knowledge index.
    /// </summary>
    /// <param name="customerId">Customer ID.</param>
    /// <param name="documentId">Document ID to remove.</param>
    /// <param name="knowledgeSourceId">Optional: specific knowledge source. If null, removes from all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with number of chunks removed.</returns>
    Task<RemoveDocumentResult> RemoveDocumentAsync(
        Guid customerId,
        string documentId,
        Guid? knowledgeSourceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reindex all documents for a knowledge source.
    /// Used when knowledge source configuration changes or for maintenance.
    /// </summary>
    /// <param name="knowledgeSourceId">Knowledge source to reindex.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with document and chunk counts.</returns>
    Task<ReindexResult> ReindexKnowledgeSourceAsync(
        Guid knowledgeSourceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get indexing status and statistics for a customer.
    /// </summary>
    /// <param name="customerId">Customer ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Index statistics.</returns>
    Task<IndexStatistics> GetIndexStatisticsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Knowledge deployment configuration from sprk_knowledgedeployment entity.
/// </summary>
public record KnowledgeDeployment
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public Guid CustomerId { get; init; }

    /// <summary>
    /// Deployment model: Shared, Dedicated, or CustomerOwned.
    /// </summary>
    public DeploymentModel Model { get; init; }

    /// <summary>
    /// Index name (for Model 1: shared index, Model 2: customer-specific index).
    /// </summary>
    public string IndexName { get; init; } = "spaarke-knowledge-shared";

    /// <summary>
    /// Field used for tenant filtering in shared index (Model 1).
    /// </summary>
    public string TenantFilterField { get; init; } = "customerId";

    /// <summary>
    /// Customer's Azure tenant ID (Model 3 only).
    /// </summary>
    public string? CustomerTenantId { get; init; }

    /// <summary>
    /// Customer's AI Search endpoint (Model 3 only).
    /// </summary>
    public string? CustomerSearchEndpoint { get; init; }

    /// <summary>
    /// Key Vault secret name for customer's AI Search key (Model 3 only).
    /// </summary>
    public string? CustomerSearchKeySecretName { get; init; }

    /// <summary>
    /// Whether this deployment is active.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Chunk size for document splitting (default 1000 characters).
    /// </summary>
    public int ChunkSize { get; init; } = 1000;

    /// <summary>
    /// Overlap between chunks (default 200 characters).
    /// </summary>
    public int ChunkOverlap { get; init; } = 200;
}

/// <summary>
/// Deployment model types.
/// </summary>
public enum DeploymentModel
{
    /// <summary>
    /// Shared index with tenant filtering (Model 1).
    /// Cost-effective, suitable for most customers.
    /// </summary>
    Shared = 0,

    /// <summary>
    /// Dedicated index per customer (Model 2).
    /// Higher performance, better isolation.
    /// </summary>
    Dedicated = 1,

    /// <summary>
    /// Customer-owned index (Model 3).
    /// Customer manages their own AI Search in their tenant.
    /// </summary>
    CustomerOwned = 2
}

/// <summary>
/// Request to index a document.
/// </summary>
public record IndexDocumentRequest
{
    /// <summary>
    /// Customer ID for tenant isolation.
    /// </summary>
    public required Guid CustomerId { get; init; }

    /// <summary>
    /// Knowledge source this document belongs to.
    /// </summary>
    public required Guid KnowledgeSourceId { get; init; }

    /// <summary>
    /// Document ID (unique identifier from SPE or external source).
    /// </summary>
    public required string DocumentId { get; init; }

    /// <summary>
    /// Extracted document text to chunk and index.
    /// </summary>
    public required string DocumentContent { get; init; }

    /// <summary>
    /// Document title for search display.
    /// </summary>
    public string? DocumentTitle { get; init; }

    /// <summary>
    /// Original filename.
    /// </summary>
    public string? DocumentFileName { get; init; }

    /// <summary>
    /// Knowledge type (Document, Rule, Template).
    /// </summary>
    public string? KnowledgeType { get; init; }

    /// <summary>
    /// Category for filtering.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Tags for search and filtering.
    /// </summary>
    public string[]? Tags { get; init; }

    /// <summary>
    /// Whether content is public (Spaarke templates) vs customer-specific.
    /// </summary>
    public bool IsPublic { get; init; }

    /// <summary>
    /// Additional metadata as JSON.
    /// </summary>
    public string? SourceMetadata { get; init; }
}

/// <summary>
/// Result of document indexing.
/// </summary>
public record IndexDocumentResult
{
    public required bool Success { get; init; }
    public int ChunksCreated { get; init; }
    public int ChunksFailed { get; init; }
    public string? ErrorMessage { get; init; }
    public long DurationMs { get; init; }

    public static IndexDocumentResult Ok(int chunksCreated, long durationMs) => new()
    {
        Success = true,
        ChunksCreated = chunksCreated,
        DurationMs = durationMs
    };

    public static IndexDocumentResult Fail(string errorMessage, int chunksFailed = 0, long durationMs = 0) => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
        ChunksFailed = chunksFailed,
        DurationMs = durationMs
    };
}

/// <summary>
/// Result of document removal.
/// </summary>
public record RemoveDocumentResult
{
    public required bool Success { get; init; }
    public int ChunksRemoved { get; init; }
    public string? ErrorMessage { get; init; }

    public static RemoveDocumentResult Ok(int chunksRemoved) => new()
    {
        Success = true,
        ChunksRemoved = chunksRemoved
    };

    public static RemoveDocumentResult Fail(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// Result of reindexing operation.
/// </summary>
public record ReindexResult
{
    public required bool Success { get; init; }
    public int DocumentsProcessed { get; init; }
    public int TotalChunks { get; init; }
    public int FailedDocuments { get; init; }
    public string? ErrorMessage { get; init; }
    public long DurationMs { get; init; }
}

/// <summary>
/// Index statistics for a customer.
/// </summary>
public record IndexStatistics
{
    public Guid CustomerId { get; init; }
    public DeploymentModel Model { get; init; }
    public string IndexName { get; init; } = string.Empty;
    public int TotalDocuments { get; init; }
    public int TotalChunks { get; init; }
    public long StorageSizeBytes { get; init; }
    public DateTime? LastIndexedAt { get; init; }
}
