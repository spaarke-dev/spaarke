using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.SpeAdmin;

// ─────────────────────────────────────────────────────────────────────────────
// List / Detail response DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Summary projection of a sprk_speenvironment record,
/// returned in list responses (GET /api/spe/environments).
/// </summary>
public sealed record EnvironmentSummaryDto
{
    /// <summary>Primary key — sprk_speenvironmentid</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>Display name — sprk_name</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>SharePoint root site URL for this tenant environment — sprk_rootsiteurl</summary>
    [JsonPropertyName("rootSiteUrl")]
    public string RootSiteUrl { get; init; } = string.Empty;

    /// <summary>Microsoft Graph API base URL override — sprk_graphapibaseurl</summary>
    [JsonPropertyName("graphApiBaseUrl")]
    public string? GraphApiBaseUrl { get; init; }

    /// <summary>Whether this is the default environment — sprk_isdefault</summary>
    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; init; }

    /// <summary>Record creation timestamp.</summary>
    [JsonPropertyName("createdOn")]
    public DateTimeOffset? CreatedOn { get; init; }

    /// <summary>Last modified timestamp.</summary>
    [JsonPropertyName("modifiedOn")]
    public DateTimeOffset? ModifiedOn { get; init; }
}

/// <summary>
/// Full detail of a sprk_speenvironment record,
/// returned in single-record responses (GET /api/spe/environments/{id}).
/// Includes all fields required for administration and editing.
/// </summary>
public sealed record EnvironmentDetailDto
{
    /// <summary>Primary key — sprk_speenvironmentid</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>Display name — sprk_name</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>SharePoint root site URL for this tenant environment — sprk_rootsiteurl</summary>
    [JsonPropertyName("rootSiteUrl")]
    public string RootSiteUrl { get; init; } = string.Empty;

    /// <summary>
    /// Microsoft Graph API base URL override (if non-standard).
    /// Defaults to https://graph.microsoft.com/v1.0 when null — sprk_graphapibaseurl
    /// </summary>
    [JsonPropertyName("graphApiBaseUrl")]
    public string? GraphApiBaseUrl { get; init; }

    /// <summary>Optional description for administrators — sprk_description</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Whether this is the default environment — sprk_isdefault</summary>
    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; init; }

    /// <summary>Record creation timestamp.</summary>
    [JsonPropertyName("createdOn")]
    public DateTimeOffset? CreatedOn { get; init; }

    /// <summary>Last modified timestamp.</summary>
    [JsonPropertyName("modifiedOn")]
    public DateTimeOffset? ModifiedOn { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Mutation request DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Request body for creating a new SPE environment (POST /api/spe/environments).
/// </summary>
public sealed record CreateEnvironmentRequest
{
    /// <summary>Display name for this environment. Required.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// SharePoint root site URL. Required.
    /// Must be a valid HTTPS URL.
    /// </summary>
    [JsonPropertyName("rootSiteUrl")]
    public string RootSiteUrl { get; init; } = string.Empty;

    /// <summary>
    /// Microsoft Graph API base URL override. Optional.
    /// Must be a valid HTTPS URL when provided.
    /// </summary>
    [JsonPropertyName("graphApiBaseUrl")]
    public string? GraphApiBaseUrl { get; init; }

    /// <summary>Optional description for administrators.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Whether this environment should be the default.
    /// If true, the existing default environment (if any) will be cleared.
    /// </summary>
    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; init; }
}

/// <summary>
/// Request body for updating an existing SPE environment (PUT /api/spe/environments/{id}).
/// All fields are optional — only supplied non-null fields are applied.
/// </summary>
public sealed record UpdateEnvironmentRequest
{
    /// <summary>Display name. Optional.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// SharePoint root site URL. Optional.
    /// Must be a valid HTTPS URL when provided.
    /// </summary>
    [JsonPropertyName("rootSiteUrl")]
    public string? RootSiteUrl { get; init; }

    /// <summary>
    /// Microsoft Graph API base URL override. Optional.
    /// Must be a valid HTTPS URL when provided.
    /// </summary>
    [JsonPropertyName("graphApiBaseUrl")]
    public string? GraphApiBaseUrl { get; init; }

    /// <summary>Optional description for administrators.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Whether this environment should be the default. Optional.
    /// If true, the existing default environment (if any) will be cleared.
    /// </summary>
    [JsonPropertyName("isDefault")]
    public bool? IsDefault { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// List response wrapper
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// List response for GET /api/spe/environments.
/// </summary>
public sealed record EnvironmentListResponse
{
    [JsonPropertyName("items")]
    public IReadOnlyList<EnvironmentSummaryDto> Items { get; init; } = Array.Empty<EnvironmentSummaryDto>();

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Internal Dataverse projection (for QueryAsync / RetrieveAsync deserialization)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Raw Dataverse response projection for sprk_speenvironment records.
/// Property names match Dataverse logical attribute names / OData query results.
/// Used internally by EnvironmentEndpoints for QueryAsync / RetrieveAsync deserialization only.
/// </summary>
internal sealed class EnvironmentDataverseRow
{
    [JsonPropertyName("sprk_speenvironmentid")]
    public Guid Id { get; set; }

    [JsonPropertyName("sprk_name")]
    public string? Name { get; set; }

    [JsonPropertyName("sprk_rootsiteurl")]
    public string? RootSiteUrl { get; set; }

    [JsonPropertyName("sprk_graphapibaseurl")]
    public string? GraphApiBaseUrl { get; set; }

    [JsonPropertyName("sprk_description")]
    public string? Description { get; set; }

    [JsonPropertyName("sprk_isdefault")]
    public bool IsDefault { get; set; }

    [JsonPropertyName("createdon")]
    public DateTimeOffset? CreatedOn { get; set; }

    [JsonPropertyName("modifiedon")]
    public DateTimeOffset? ModifiedOn { get; set; }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    public EnvironmentSummaryDto ToSummary() => new()
    {
        Id = Id,
        Name = Name ?? string.Empty,
        RootSiteUrl = RootSiteUrl ?? string.Empty,
        GraphApiBaseUrl = GraphApiBaseUrl,
        IsDefault = IsDefault,
        CreatedOn = CreatedOn,
        ModifiedOn = ModifiedOn
    };

    public EnvironmentDetailDto ToDetail() => new()
    {
        Id = Id,
        Name = Name ?? string.Empty,
        RootSiteUrl = RootSiteUrl ?? string.Empty,
        GraphApiBaseUrl = GraphApiBaseUrl,
        Description = Description,
        IsDefault = IsDefault,
        CreatedOn = CreatedOn,
        ModifiedOn = ModifiedOn
    };
}
