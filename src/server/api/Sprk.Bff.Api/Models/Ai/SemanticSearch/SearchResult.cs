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
    /// SharePoint Embedded file ID.
    /// </summary>
    [JsonPropertyName("speFileId")]
    public string? SpeFileId { get; init; }

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
    /// Document creation timestamp.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// Document last update timestamp.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }
}
