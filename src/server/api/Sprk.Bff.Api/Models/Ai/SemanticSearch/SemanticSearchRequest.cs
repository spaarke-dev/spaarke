using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai.SemanticSearch;

/// <summary>
/// Request for semantic search API endpoint.
/// </summary>
/// <remarks>
/// Supports two scopes:
/// <list type="bullet">
/// <item><c>entity</c> - Search within a specific business entity (requires entityType and entityId)</item>
/// <item><c>documentIds</c> - Search within a specific set of documents (requires documentIds list)</item>
/// </list>
/// <c>scope=all</c> is not supported in R1.
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
    /// Search scope: "entity" or "documentIds".
    /// <c>scope=all</c> returns 400 NotSupported in R1.
    /// </summary>
    [Required(ErrorMessage = "Scope is required")]
    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

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

    /// <summary>Search across all accessible documents (NOT SUPPORTED IN R1).</summary>
    public const string All = "all";

    /// <summary>All valid scope values.</summary>
    public static readonly string[] ValidScopes = [Entity, DocumentIds];

    /// <summary>Checks if the scope is valid for R1.</summary>
    public static bool IsValid(string? scope) =>
        !string.IsNullOrWhiteSpace(scope) && ValidScopes.Contains(scope.ToLowerInvariant());
}
