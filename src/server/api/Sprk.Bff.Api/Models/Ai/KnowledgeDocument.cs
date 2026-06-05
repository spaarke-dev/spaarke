using System.Text.Json.Serialization;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Represents a document chunk in the Azure AI Search knowledge index.
/// Used for RAG (Retrieval-Augmented Generation) with hybrid search.
/// </summary>
/// <remarks>
/// This model maps to the "spaarke-knowledge-index-v2" in Azure AI Search.
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
    /// R5 (spec.md §4.2 / FR-09 / ADR-014): chat-session identifier for session-scoped
    /// retrieval and eviction in the <c>spaarke-session-files</c> index. NULL on writes
    /// to the knowledge / discovery / references indexes (those schemas do not declare
    /// a <c>sessionId</c> field; Azure Search SDK ignores absent fields on incoming
    /// documents, so back-compat is preserved). REQUIRED (non-null, non-empty) on writes
    /// to the session-files index per ADR-014 tenant + session isolation invariant —
    /// enforced at the call boundary by
    /// <see cref="Sprk.Bff.Api.Services.Ai.RagIndexingPipeline.IndexSessionFileAsync"/>.
    /// Serialized only when non-null so existing customer-corpus payloads are byte-for-byte
    /// unchanged.
    /// </summary>
    [SimpleField(IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("sessionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SessionId { get; set; }

    /// <summary>
    /// Deployment identifier (sprk_aiknowledgedeployment record ID).
    /// </summary>
    /// <remarks>
    /// Suppressed from JSON when null so writes to the <c>spaarke-session-files</c> index
    /// (which does not declare this field) do not trigger 400 "property does not exist".
    /// Customer-corpus writers that need this field set it explicitly.
    /// </remarks>
    [SimpleField(IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("deploymentId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeploymentId { get; set; }

    /// <summary>
    /// Deployment model type: Shared, Dedicated, or CustomerOwned. Default "Shared" preserves
    /// existing customer-corpus behavior; session-files writers null this out explicitly to
    /// suppress serialization per the same pattern as <see cref="SessionId"/>.
    /// </summary>
    [SimpleField(IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("deploymentModel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeploymentModel { get; set; } = "Shared";

    /// <summary>
    /// Source knowledge record ID (sprk_analysisknowledge).
    /// </summary>
    [SimpleField(IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("knowledgeSourceId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? KnowledgeSourceId { get; set; }

    /// <summary>
    /// Display name of the knowledge source.
    /// </summary>
    [SearchableField(AnalyzerName = LexicalAnalyzerName.Values.StandardLucene, IsSortable = true)]
    [JsonPropertyName("knowledgeSourceName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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
    /// File display name.
    /// </summary>
    [SearchableField(AnalyzerName = LexicalAnalyzerName.Values.StandardLucene, IsSortable = true)]
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

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
    /// Vector embedding of the content (3072 dimensions for text-embedding-3-large).
    /// </summary>
    [VectorSearchField(VectorSearchDimensions = 3072, VectorSearchProfileName = "knowledge-vector-profile-3072")]
    [JsonPropertyName("contentVector3072")]
    public ReadOnlyMemory<float> ContentVector { get; set; }

    /// <summary>
    /// Document-level vector embedding (3072 dimensions for text-embedding-3-large).
    /// Used for document similarity visualization.
    /// </summary>
    /// <remarks>
    /// Suppressed from JSON when default (empty <see cref="ReadOnlyMemory{T}"/>) — session-files
    /// writers leave this default and the index would reject an empty 0-dimensional vector
    /// against its declared 3072-dimension profile. Customer-corpus writers that populate it
    /// are unaffected.
    /// </remarks>
    [VectorSearchField(VectorSearchDimensions = 3072, VectorSearchProfileName = "knowledge-vector-profile-3072")]
    [JsonPropertyName("documentVector3072")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ReadOnlyMemory<float> DocumentVector { get; set; }

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

    /// <summary>
    /// Type of parent entity that owns this document.
    /// Valid values: matter, project, invoice, account, contact.
    /// </summary>
    /// <remarks>
    /// Used for entity-scoped semantic search. Nullable to support documents
    /// indexed before parent entity tracking was implemented. Suppressed from JSON when null
    /// (session-files index lacks this field).
    /// </remarks>
    [SimpleField(IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("parentEntityType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentEntityType { get; set; }

    /// <summary>
    /// Unique identifier of the parent entity (GUID format).
    /// </summary>
    /// <remarks>
    /// Combined with ParentEntityType for entity-scoped filtering.
    /// Nullable to support legacy documents without entity association. Suppressed from JSON
    /// when null (session-files index lacks this field).
    /// </remarks>
    [SimpleField(IsFilterable = true)]
    [JsonPropertyName("parentEntityId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentEntityId { get; set; }

    /// <summary>
    /// Display name of the parent entity for search result presentation.
    /// </summary>
    /// <remarks>
    /// Searchable field to allow finding documents by entity name.
    /// Nullable to support legacy documents without entity association. Suppressed from JSON
    /// when null (session-files index lacks this field).
    /// </remarks>
    [SearchableField(AnalyzerName = LexicalAnalyzerName.Values.StandardLucene, IsSortable = true)]
    [JsonPropertyName("parentEntityname")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentEntityName { get; set; }

    /// <summary>
    /// Azure AD group IDs that are authorised to retrieve this document chunk.
    /// Empty collection means the document is public (no privilege restriction).
    /// Filterable string collection — used by privilege-aware retrieval (AIPU2-027).
    /// </summary>
    /// <remarks>
    /// Default empty list preserves customer-corpus behavior (Collection(Edm.String) is
    /// implicitly Nullable=False — rejects null writes with 400). Session-files writers
    /// explicitly null this out so JsonIgnore.WhenWritingNull suppresses it — the
    /// session-files index does not declare this field.
    /// </remarks>
    [SimpleField(IsFilterable = true)]
    [JsonPropertyName("privilege_group_ids")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IList<string>? PrivilegeGroupIds { get; set; } = new List<string>();
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
