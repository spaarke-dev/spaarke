using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai.SemanticSearch;

/// <summary>
/// Individual search result from semantic search.
/// </summary>
public sealed record SearchResult
{
    /// <summary>
    /// Spaarke document ID (GUID).
    /// </summary>
    [JsonPropertyName("documentId")]
    public string? DocumentId { get; init; }

    /// <summary>
    /// SharePoint Embedded file ID (driveItem ID).
    /// </summary>
    [JsonPropertyName("speFileId")]
    public string? SpeFileId { get; init; }

    /// <summary>
    /// SharePoint Embedded drive ID. Needed (alongside speFileId) to invoke
    /// AI analysis endpoints like Document Profile / Summarize.
    /// </summary>
    [JsonPropertyName("driveId")]
    public string? DriveId { get; init; }

    /// <summary>
    /// Document display name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Document type classification (e.g., "Contract", "Invoice").
    /// </summary>
    [JsonPropertyName("documentType")]
    public string? DocumentType { get; init; }

    /// <summary>
    /// File extension/type (e.g., "pdf", "docx").
    /// </summary>
    [JsonPropertyName("fileType")]
    public string? FileType { get; init; }

    /// <summary>
    /// Combined relevance score from RRF fusion (0.0-1.0).
    /// </summary>
    [JsonPropertyName("combinedScore")]
    public double CombinedScore { get; init; }

    /// <summary>
    /// Vector similarity score. Null in R1 (always populated by combinedScore).
    /// </summary>
    [JsonPropertyName("similarity")]
    public double? Similarity { get; init; }

    /// <summary>
    /// Keyword/BM25 score. Null in R1 (always populated by combinedScore).
    /// </summary>
    [JsonPropertyName("keywordScore")]
    public double? KeywordScore { get; init; }

    /// <summary>
    /// Highlighted text snippets containing matching content.
    /// </summary>
    [JsonPropertyName("highlights")]
    public IReadOnlyList<string>? Highlights { get; init; }

    /// <summary>
    /// Parent entity type (matter, project, invoice, account, contact).
    /// </summary>
    [JsonPropertyName("parentEntityType")]
    public string? ParentEntityType { get; init; }

    /// <summary>
    /// Parent entity ID (GUID).
    /// </summary>
    [JsonPropertyName("parentEntityId")]
    public string? ParentEntityId { get; init; }

    /// <summary>
    /// Parent entity display name.
    /// </summary>
    [JsonPropertyName("parentEntityName")]
    public string? ParentEntityName { get; init; }

    /// <summary>
    /// URL to access the file content.
    /// </summary>
    [JsonPropertyName("fileUrl")]
    public string? FileUrl { get; init; }

    /// <summary>
    /// URL to the Dataverse record.
    /// </summary>
    [JsonPropertyName("recordUrl")]
    public string? RecordUrl { get; init; }

    /// <summary>
    /// File size in bytes (from Dataverse <c>sprk_filesize</c> via post-search enrichment;
    /// also populated on the associated-only direct-Dataverse path). Used by the Documents
    /// PCF email wizard to enforce the 25 MB attachment-cap warning.
    /// </summary>
    [JsonPropertyName("fileSize")]
    public long? FileSize { get; init; }

    /// <summary>
    /// Document creation timestamp.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// Document last update timestamp.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>
    /// Name of user who created the document (from Dataverse post-search lookup).
    /// </summary>
    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; init; }

    /// <summary>
    /// Document last-modified timestamp (from Dataverse <c>modifiedon</c> system field via post-search lookup; falls back to AI Search <c>updatedAt</c>).
    /// Mirrors <see cref="CreatedAt"/> for the modify lifecycle. Consumed by the Documents PCF list view for default sort (<c>modifiedAt DESC</c>) per FR-BFF-01.
    /// </summary>
    [JsonPropertyName("modifiedAt")]
    public DateTimeOffset? ModifiedAt { get; init; }

    /// <summary>
    /// Name of user who last modified the document (from Dataverse <c>_modifiedby_value</c> formatted-value lookup via post-search enrichment).
    /// Mirrors <see cref="CreatedBy"/>. Per FR-BFF-01; consumed by the Documents PCF list view "Modified by" column.
    /// </summary>
    [JsonPropertyName("modifiedBy")]
    public string? ModifiedBy { get; init; }

    /// <summary>
    /// AI-generated full summary (from Dataverse post-search lookup).
    /// </summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    /// <summary>
    /// AI-generated TL;DR (from Dataverse post-search lookup).
    /// </summary>
    [JsonPropertyName("tldr")]
    public string? Tldr { get; init; }
}
