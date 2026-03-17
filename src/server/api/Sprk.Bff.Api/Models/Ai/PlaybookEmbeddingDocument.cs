using System.Text.Json.Serialization;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Represents a playbook document in the Azure AI Search playbook-embeddings index.
/// Used for semantic playbook matching via vector similarity search.
/// </summary>
/// <remarks>
/// <para>
/// This model maps to the "playbook-embeddings" index in Azure AI Search.
/// Index schema: infrastructure/ai-search/playbook-embeddings.json.
/// </para>
/// <para>
/// Unlike the knowledge index (100K+ document chunks), this index contains ~100 short
/// playbook documents. Each document represents a single playbook's identity and intent,
/// enabling fast vector similarity matching for PlaybookDispatcher (R2-015).
/// </para>
/// </remarks>
public class PlaybookEmbeddingDocument
{
    /// <summary>
    /// Unique identifier for the index document.
    /// Typically the same as <see cref="PlaybookId"/>.
    /// </summary>
    [SimpleField(IsKey = true)]
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Playbook record identifier (sprk_aiplaybook GUID).
    /// </summary>
    [SimpleField(IsFilterable = true)]
    [JsonPropertyName("playbookId")]
    public string PlaybookId { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the playbook.
    /// </summary>
    [SearchableField(AnalyzerName = LexicalAnalyzerName.Values.StandardLucene, IsSortable = true)]
    [JsonPropertyName("playbookName")]
    public string PlaybookName { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description of what the playbook does.
    /// </summary>
    [SearchableField(AnalyzerName = LexicalAnalyzerName.Values.StandardLucene)]
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Natural language phrases that should trigger this playbook.
    /// </summary>
    [SearchableField(AnalyzerName = LexicalAnalyzerName.Values.StandardLucene)]
    [JsonPropertyName("triggerPhrases")]
    public IList<string> TriggerPhrases { get; set; } = [];

    /// <summary>
    /// Dataverse record type this playbook operates on (e.g., "sprk_matter", "sprk_project").
    /// Used for filtering search results to playbooks relevant to the current entity context.
    /// </summary>
    [SimpleField(IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("recordType")]
    public string RecordType { get; set; } = string.Empty;

    /// <summary>
    /// Entity type category (e.g., "matter", "project", "invoice").
    /// </summary>
    [SimpleField(IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("entityType")]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Tags for categorization and filtering.
    /// </summary>
    [SearchableField(AnalyzerName = "keyword", IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("tags")]
    public IList<string> Tags { get; set; } = [];

    /// <summary>
    /// Vector embedding of the playbook content (3072 dimensions, text-embedding-3-large).
    /// Generated from: playbookName + description + triggerPhrases + tags.
    /// </summary>
    [VectorSearchField(VectorSearchDimensions = 3072, VectorSearchProfileName = "playbook-vector-profile")]
    [JsonPropertyName("contentVector3072")]
    public ReadOnlyMemory<float> ContentVector3072 { get; set; }
}
