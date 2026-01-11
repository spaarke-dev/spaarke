using System.Text.Json.Serialization;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Represents a document chunk in the Azure AI Search knowledge index.
/// Used for RAG (Retrieval-Augmented Generation) with hybrid search.
/// </summary>
/// <remarks>
/// This model maps to the "spaarke-knowledge-index" in Azure AI Search.
/// Supports 3 deployment models: Shared (filtered by tenantId), Dedicated (per-customer index), CustomerOwned.
/// Vector dimensions: 1536 (text-embedding-3-small) and 3072 (text-embedding-3-large) during migration.
/// </remarks>
public class KnowledgeDocument
{
    /// <summary>
    /// Unique identifier for the chunk (format: {documentId}_{chunkIndex}).
    /// </summary>
    [SimpleField(IsKey = true)]
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Tenant identifier for multi-tenant isolation (Shared deployment model).
    /// </summary>
    [SimpleField(IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Deployment identifier (sprk_aiknowledgedeployment record ID).
    /// </summary>
    [SimpleField(IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("deploymentId")]
    public string? DeploymentId { get; set; }

    /// <summary>
    /// Deployment model type: Shared, Dedicated, or CustomerOwned.
    /// </summary>
    [SimpleField(IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("deploymentModel")]
    public string DeploymentModel { get; set; } = "Shared";

    /// <summary>
    /// Source knowledge record ID (sprk_analysisknowledge).
    /// </summary>
    [SimpleField(IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("knowledgeSourceId")]
    public string? KnowledgeSourceId { get; set; }

    /// <summary>
    /// Display name of the knowledge source.
    /// </summary>
    [SearchableField(AnalyzerName = LexicalAnalyzerName.Values.StandardLucene, IsSortable = true)]
    [JsonPropertyName("knowledgeSourceName")]
    public string? KnowledgeSourceName { get; set; }

    /// <summary>
    /// Original document identifier (sprk_document record ID).
    /// Nullable to support orphan files (files with no linked Dataverse document).
    /// </summary>
    [SimpleField(IsFilterable = true)]
    [JsonPropertyName("documentId")]
    public string? DocumentId { get; set; }

    /// <summary>
    /// SharePoint Embedded file identifier. Always populated.
    /// Use this for direct file access when documentId is null (orphan files).
    /// </summary>
    [SimpleField(IsFilterable = true)]
    [JsonPropertyName("speFileId")]
    public string? SpeFileId { get; set; }

    /// <summary>
    /// Document file name or title.
    /// </summary>
    /// <remarks>
    /// Deprecated: Use FileName instead. Kept for backward compatibility during migration.
    /// </remarks>
    [SearchableField(AnalyzerName = LexicalAnalyzerName.Values.StandardLucene, IsSortable = true)]
    [JsonPropertyName("documentName")]
    public string DocumentName { get; set; } = string.Empty;

    /// <summary>
    /// File display name.
    /// </summary>
    [SearchableField(AnalyzerName = LexicalAnalyzerName.Values.StandardLucene, IsSortable = true)]
    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    /// <summary>
    /// Document type (e.g., contract, policy, procedure).
    /// </summary>
    /// <remarks>
    /// Deprecated: Use FileType instead. Kept for backward compatibility during migration.
    /// </remarks>
    [SimpleField(IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("documentType")]
    public string? DocumentType { get; set; }

    /// <summary>
    /// File extension for icon selection (pdf, docx, msg, xlsx, etc.).
    /// </summary>
    [SimpleField(IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("fileType")]
    public string? FileType { get; set; }

    /// <summary>
    /// Zero-based index of this chunk within the document.
    /// </summary>
    [SimpleField(IsSortable = true)]
    [JsonPropertyName("chunkIndex")]
    public int ChunkIndex { get; set; }

    /// <summary>
    /// Total number of chunks for this document.
    /// </summary>
    [SimpleField]
    [JsonPropertyName("chunkCount")]
    public int ChunkCount { get; set; }

    /// <summary>
    /// Text content of the chunk.
    /// </summary>
    [SearchableField(AnalyzerName = LexicalAnalyzerName.Values.StandardLucene)]
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Vector embedding of the content (1536 dimensions for text-embedding-3-small).
    /// </summary>
    /// <remarks>
    /// Deprecated: Use ContentVector3072 after migration is complete.
    /// </remarks>
    [VectorSearchField(VectorSearchDimensions = 1536, VectorSearchProfileName = "knowledge-vector-profile")]
    [JsonPropertyName("contentVector")]
    public ReadOnlyMemory<float> ContentVector { get; set; }

    /// <summary>
    /// Document-level vector embedding computed as the normalized average of all chunk contentVectors.
    /// Used for document similarity visualization. Computed automatically during batch indexing.
    /// </summary>
    /// <remarks>
    /// Deprecated: Use DocumentVector3072 after migration is complete.
    /// </remarks>
    [VectorSearchField(VectorSearchDimensions = 1536, VectorSearchProfileName = "knowledge-vector-profile")]
    [JsonPropertyName("documentVector")]
    public ReadOnlyMemory<float> DocumentVector { get; set; }

    /// <summary>
    /// Vector embedding of the content (3072 dimensions for text-embedding-3-large).
    /// Higher quality embeddings for improved similarity matching.
    /// </summary>
    [VectorSearchField(VectorSearchDimensions = 3072, VectorSearchProfileName = "knowledge-vector-profile-3072")]
    [JsonPropertyName("contentVector3072")]
    public ReadOnlyMemory<float> ContentVector3072 { get; set; }

    /// <summary>
    /// Document-level vector embedding (3072 dimensions for text-embedding-3-large).
    /// Used for document similarity visualization with improved accuracy.
    /// </summary>
    [VectorSearchField(VectorSearchDimensions = 3072, VectorSearchProfileName = "knowledge-vector-profile-3072")]
    [JsonPropertyName("documentVector3072")]
    public ReadOnlyMemory<float> DocumentVector3072 { get; set; }

    /// <summary>
    /// JSON metadata for extensibility.
    /// </summary>
    [SimpleField]
    [JsonPropertyName("metadata")]
    public string? Metadata { get; set; }

    /// <summary>
    /// Tags for categorization and filtering.
    /// </summary>
    [SearchableField(AnalyzerName = "keyword", IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("tags")]
    public IList<string>? Tags { get; set; }

    /// <summary>
    /// Timestamp when the chunk was indexed.
    /// </summary>
    [SimpleField(IsFilterable = true, IsSortable = true)]
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Timestamp when the chunk was last updated.
    /// </summary>
    [SimpleField(IsFilterable = true, IsSortable = true)]
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Knowledge deployment models supported by the RAG system.
/// </summary>
public static class KnowledgeDeploymentModel
{
    /// <summary>
    /// Single shared index filtered by tenant ID. Default for most customers.
    /// </summary>
    public const string Shared = "Shared";

    /// <summary>
    /// Dedicated index per customer. For high-volume or isolation requirements.
    /// </summary>
    public const string Dedicated = "Dedicated";

    /// <summary>
    /// Customer's own Azure subscription. Cross-tenant auth required.
    /// </summary>
    public const string CustomerOwned = "CustomerOwned";
}
