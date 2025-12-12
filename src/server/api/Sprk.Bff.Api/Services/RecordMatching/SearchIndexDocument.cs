using System.Text.Json.Serialization;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

namespace Sprk.Bff.Api.Services.RecordMatching;

/// <summary>
/// Document model for Azure AI Search index.
/// Represents a Dataverse record (Matter, Project, Invoice, etc.) in the search index.
/// </summary>
public class SearchIndexDocument
{
    /// <summary>
    /// Unique identifier for the search document.
    /// Format: "{entityName}_{recordId}" e.g., "sprk_matter_abc123"
    /// </summary>
    [SimpleField(IsKey = true, IsFilterable = false)]
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Dataverse entity logical name (e.g., "sprk_matter", "sprk_project", "sprk_invoice").
    /// </summary>
    [SimpleField(IsFilterable = true, IsSortable = true, IsFacetable = true)]
    [JsonPropertyName("recordType")]
    public string RecordType { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the record.
    /// </summary>
    [SearchableField(AnalyzerName = LexicalAnalyzerName.Values.StandardLucene, IsSortable = true)]
    [JsonPropertyName("recordName")]
    public string RecordName { get; set; } = string.Empty;

    /// <summary>
    /// Description or notes for the record.
    /// </summary>
    [SearchableField(AnalyzerName = LexicalAnalyzerName.Values.StandardLucene)]
    [JsonPropertyName("recordDescription")]
    public string? RecordDescription { get; set; }

    /// <summary>
    /// Organization names associated with this record.
    /// </summary>
    [SearchableField(AnalyzerName = LexicalAnalyzerName.Values.StandardLucene, IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("organizations")]
    public IList<string> Organizations { get; set; } = new List<string>();

    /// <summary>
    /// Person names associated with this record.
    /// </summary>
    [SearchableField(AnalyzerName = LexicalAnalyzerName.Values.StandardLucene, IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("people")]
    public IList<string> People { get; set; } = new List<string>();

    /// <summary>
    /// Reference numbers (matter numbers, invoice numbers, PO numbers, etc.).
    /// Uses keyword analyzer for exact matching.
    /// </summary>
    [SearchableField(AnalyzerName = LexicalAnalyzerName.Values.Keyword, IsFilterable = true)]
    [JsonPropertyName("referenceNumbers")]
    public IList<string> ReferenceNumbers { get; set; } = new List<string>();

    /// <summary>
    /// Searchable keywords extracted from record content.
    /// </summary>
    [SearchableField(AnalyzerName = LexicalAnalyzerName.Values.StandardLucene)]
    [JsonPropertyName("keywords")]
    public string? Keywords { get; set; }

    /// <summary>
    /// Vector embedding for semantic/hybrid search.
    /// Dimension 3072 for text-embedding-3-large model.
    /// </summary>
    [VectorSearchField(VectorSearchDimensions = 3072, VectorSearchProfileName = "default-vector-profile")]
    [JsonPropertyName("contentVector")]
    public IReadOnlyList<float>? ContentVector { get; set; }

    /// <summary>
    /// Last modified timestamp from Dataverse.
    /// </summary>
    [SimpleField(IsFilterable = true, IsSortable = true)]
    [JsonPropertyName("lastModified")]
    public DateTimeOffset? LastModified { get; set; }

    /// <summary>
    /// Original Dataverse record GUID.
    /// </summary>
    [SimpleField(IsFilterable = true)]
    [JsonPropertyName("dataverseRecordId")]
    public string DataverseRecordId { get; set; } = string.Empty;

    /// <summary>
    /// Dataverse entity logical name (same as RecordType, for reference).
    /// </summary>
    [SimpleField(IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("dataverseEntityName")]
    public string DataverseEntityName { get; set; } = string.Empty;
}
