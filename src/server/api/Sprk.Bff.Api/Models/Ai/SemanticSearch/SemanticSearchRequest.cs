using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai.SemanticSearch;

/// <summary>
/// Request for semantic search API endpoint.
/// </summary>
/// <remarks>
/// Supports three scopes:
/// <list type="bullet">
/// <item><c>all</c> - Search across all accessible documents for the tenant (no entity type filter)</item>
/// <item><c>entity</c> - Search within a specific business entity (requires entityType and entityId)</item>
/// <item><c>documentIds</c> - Search within a specific set of documents (requires documentIds list)</item>
/// </list>
/// </remarks>
public sealed record SemanticSearchRequest
{
    /// <summary>
    /// Search query text. Max 1000 characters.
    /// Required unless <c>hybridMode=keywordOnly</c> with filters.
    /// </summary>
    [StringLength(1000, ErrorMessage = "Query must not exceed 1000 characters")]
    [JsonPropertyName("query")]
    public string? Query { get; init; }

    /// <summary>
    /// Search scope: "all", "entity", or "documentIds".
    /// Defaults to "all" when not provided (e.g., Copilot API Plugin calls).
    /// </summary>
    [JsonPropertyName("scope")]
    public string Scope { get; init; } = SearchScope.All;

    /// <summary>
    /// Parent entity type when <c>scope=entity</c>.
    /// Valid values: matter, project, invoice, account, contact.
    /// </summary>
    [JsonPropertyName("entityType")]
    public string? EntityType { get; init; }

    /// <summary>
    /// Parent entity ID (GUID) when <c>scope=entity</c>.
    /// </summary>
    [JsonPropertyName("entityId")]
    public string? EntityId { get; init; }

    /// <summary>
    /// List of document IDs when <c>scope=documentIds</c>.
    /// Max 100 documents.
    /// </summary>
    [MaxLength(100, ErrorMessage = "Maximum 100 document IDs allowed")]
    [JsonPropertyName("documentIds")]
    public IReadOnlyList<string>? DocumentIds { get; init; }

    /// <summary>
    /// Optional search filters.
    /// </summary>
    [JsonPropertyName("filters")]
    public SearchFilters? Filters { get; init; }

    /// <summary>
    /// Search options for pagination, highlights, and hybrid mode.
    /// </summary>
    [JsonPropertyName("options")]
    public SearchOptions? Options { get; init; }

    /// <summary>
    /// When true, restricts results to documents that are DIRECTLY associated with the
    /// specified parent record in Dataverse (via <c>_sprk_matter_value</c>,
    /// <c>_sprk_project_value</c>, or <c>_sprk_invoice_value</c>) — bypassing Azure AI
    /// Search entirely. Use this when you need "live" associated documents that haven't
    /// been indexed yet (e.g., just-uploaded files).
    /// Requires <c>scope=entity</c> with valid <c>entityType</c> and <c>entityId</c>.
    /// Default false — uses the AI Search path.
    /// </summary>
    [JsonPropertyName("associatedOnly")]
    public bool AssociatedOnly { get; init; }

    /// <summary>
    /// Optional explicit Azure AI Search index name to target for this request. When
    /// provided (non-null and non-empty), the BFF resolver MUST use this index in place
    /// of the Dataverse / appsettings fallback chain — subject to the allow-list in
    /// <c>appsettings.AiSearch.AllowedIndexes</c>. When omitted (null or empty), the
    /// existing 2-tier resolver chain (<c>sprk_aiknowledgedeployment</c> Dataverse entity
    /// then <c>appsettings.AiSearch.KnowledgeIndexName</c>) is used unchanged.
    /// JSON deserialization is forward-compatible: requests without this field continue
    /// to work as today (FR-BFF-05, NFR-02).
    /// </summary>
    [JsonPropertyName("searchIndexName")]
    public string? SearchIndexName { get; init; }
}

/// <summary>
/// Valid search scope values.
/// </summary>
public static class SearchScope
{
    /// <summary>Search within a specific business entity.</summary>
    public const string Entity = "entity";

    /// <summary>Search within a specific set of document IDs.</summary>
    public const string DocumentIds = "documentIds";

    /// <summary>Search across all accessible documents for the tenant.</summary>
    public const string All = "all";

    /// <summary>All valid scope values.</summary>
    public static readonly string[] ValidScopes = [All, Entity, DocumentIds];

    /// <summary>Checks if the scope is valid.</summary>
    public static bool IsValid(string? scope) =>
        !string.IsNullOrWhiteSpace(scope) && ValidScopes.Contains(scope, StringComparer.OrdinalIgnoreCase);
}
