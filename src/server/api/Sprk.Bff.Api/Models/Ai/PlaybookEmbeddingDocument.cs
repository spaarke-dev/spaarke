using System.Text.Json.Serialization;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Represents a playbook document in the Azure AI Search spaarke-playbook-embeddings index.
/// Used for semantic playbook matching via vector similarity search.
/// </summary>
/// <remarks>
/// <para>
/// This model maps to the "spaarke-playbook-embeddings" index in Azure AI Search.
/// Index schema: infrastructure/ai-search/spaarke-playbook-embeddings.json.
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
    /// Filterable document-type collection sourced from
    /// <c>sprk_jpsmatchingmetadata.documentTypes</c> (chat-routing-redesign-r1 FR-09,
    /// FR-17 v2). Populated at index time from the tolerant parse of
    /// <see cref="JpsMatchingMetadata"/>; used at query time as a structured pre-filter
    /// by <see cref="PlaybookEmbedding.PlaybookEmbeddingService.SearchPlaybooksAsync"/>
    /// when a per-file classified document type is available (Hybrid C primary path,
    /// task 112).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Empty / null when the playbook has no JPS matching metadata or the
    /// <c>documentTypes</c> array is omitted. The OData filter the dispatcher emits
    /// (<c>documentTypes/any(t: search.in(t, 'NDA,contract'))</c>) returns no matches
    /// for documents lacking the field — that is the intended graceful degradation
    /// when the upload-pipeline manifest path (Phase 4b) is not yet wired through.
    /// </para>
    /// <para>
    /// Distinct from <see cref="Tags"/>: <c>Tags</c> are general categorization tokens
    /// (e.g. <c>"chat"</c>, <c>"workspace"</c>); <c>DocumentTypes</c> is the schema-bound
    /// JPS-metadata field that mirrors classifier output labels (e.g. <c>"NDA"</c>,
    /// <c>"patent"</c>, <c>"invoice"</c>) and is the binding pre-filter for FR-17.
    /// </para>
    /// </remarks>
    [SearchableField(AnalyzerName = "keyword", IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("documentTypes")]
    public IList<string> DocumentTypes { get; set; } = [];

    /// <summary>
    /// Raw JSON content from the Dataverse <c>sprk_jps_matching_metadata</c> Memo column
    /// (added by chat-routing-redesign-r1 task 031). Optional; null/empty means the
    /// playbook has not been backfilled with JPS matching metadata yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This field is NOT persisted in the AI Search index — it is a transient input to
    /// <see cref="PlaybookEmbedding.PlaybookEmbeddingService.ComposeContentText"/>, which
    /// tolerantly parses it to append <c>documentTypes</c>, <c>intents</c>, and
    /// <c>triggerPhrases</c> arrays to the embed-input string per spec FR-10.
    /// </para>
    /// <para>
    /// Marked <see cref="JsonIgnoreAttribute"/> so it never round-trips through Azure AI
    /// Search serialization. Tolerant parse semantics: null / missing / malformed JSON
    /// falls back to baseline composition (no exception bubble; warning logged once with
    /// playbook ID per ADR-015 — never the JSON content itself).
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public string? JpsMatchingMetadata { get; set; }

    /// <summary>
    /// Vector embedding of the playbook content (3072 dimensions, text-embedding-3-large).
    /// Generated from: playbookName + description + triggerPhrases + tags
    /// + (when present) documentTypes + intents + jpsTriggerPhrases parsed from
    /// <see cref="JpsMatchingMetadata"/>.
    /// </summary>
    [VectorSearchField(VectorSearchDimensions = 3072, VectorSearchProfileName = "playbook-vector-profile")]
    [JsonPropertyName("contentVector3072")]
    public ReadOnlyMemory<float> ContentVector3072 { get; set; }
}
