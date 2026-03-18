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

    /// <summary>Azure AD tenant ID — sprk_tenantid</summary>
    [JsonPropertyName("tenantId")]
    public string? TenantId { get; init; }

    /// <summary>Tenant display name — sprk_tenantname</summary>
    [JsonPropertyName("tenantName")]
    public string? TenantName { get; init; }

    /// <summary>SharePoint root site URL for this tenant environment — sprk_rootsiteurl</summary>
    [JsonPropertyName("rootSiteUrl")]
    public string RootSiteUrl { get; init; } = string.Empty;

    /// <summary>Microsoft Graph API endpoint override — sprk_graphapibaseurl</summary>
    [JsonPropertyName("graphEndpoint")]
    public string? GraphEndpoint { get; init; }

    /// <summary>Whether this is the default environment — sprk_isdefault</summary>
    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; init; }

    /// <summary>Active or inactive status — derived from statecode (0=active, 1=inactive)</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "active";

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

    /// <summary>Azure AD tenant ID — sprk_tenantid</summary>
    [JsonPropertyName("tenantId")]
    public string? TenantId { get; init; }

    /// <summary>Tenant display name — sprk_tenantname</summary>
    [JsonPropertyName("tenantName")]
    public string? TenantName { get; init; }

    /// <summary>SharePoint root site URL for this tenant environment — sprk_rootsiteurl</summary>
    [JsonPropertyName("rootSiteUrl")]
    public string RootSiteUrl { get; init; } = string.Empty;

    /// <summary>
    /// Microsoft Graph API endpoint override (if non-standard).
    /// Defaults to https://graph.microsoft.com/v1.0 when null — sprk_graphapibaseurl
    /// </summary>
    [JsonPropertyName("graphEndpoint")]
    public string? GraphEndpoint { get; init; }

    /// <summary>Optional description for administrators — sprk_description</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Whether this is the default environment — sprk_isdefault</summary>
    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; init; }

    /// <summary>Active or inactive status — derived from statecode (0=active, 1=inactive)</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "active";

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

    /// <summary>Azure AD tenant ID (e.g. "72f988bf-86f1-41af-91ab-2d7cd011db47"). Required.</summary>
    [JsonPropertyName("tenantId")]
    public string? TenantId { get; init; }

    /// <summary>Tenant display name (e.g. "Contoso Ltd"). Optional.</summary>
    [JsonPropertyName("tenantName")]
    public string? TenantName { get; init; }

    /// <summary>
    /// SharePoint root site URL. Required.
    /// Must be a valid HTTPS URL.
    /// </summary>
    [JsonPropertyName("rootSiteUrl")]
    public string RootSiteUrl { get; init; } = string.Empty;

    /// <summary>
    /// Microsoft Graph API endpoint override. Optional.
    /// Must be a valid HTTPS URL when provided.
    /// </summary>
    [JsonPropertyName("graphEndpoint")]
    public string? GraphEndpoint { get; init; }

    /// <summary>Optional description for administrators.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Whether this environment should be the default.
    /// If true, the existing default environment (if any) will be cleared.
    /// </summary>
    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; init; }

    /// <summary>
    /// Active or inactive status ("active" | "inactive"). Defaults to "active".
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "active";
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

    /// <summary>Azure AD tenant ID. Optional.</summary>
    [JsonPropertyName("tenantId")]
    public string? TenantId { get; init; }

    /// <summary>Tenant display name. Optional.</summary>
    [JsonPropertyName("tenantName")]
    public string? TenantName { get; init; }

    /// <summary>
    /// SharePoint root site URL. Optional.
    /// Must be a valid HTTPS URL when provided.
    /// </summary>
    [JsonPropertyName("rootSiteUrl")]
    public string? RootSiteUrl { get; init; }

    /// <summary>
    /// Microsoft Graph API endpoint override. Optional.
    /// Must be a valid HTTPS URL when provided.
    /// </summary>
    [JsonPropertyName("graphEndpoint")]
    public string? GraphEndpoint { get; init; }

    /// <summary>Optional description for administrators.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Whether this environment should be the default. Optional.
    /// If true, the existing default environment (if any) will be cleared.
    /// </summary>
    [JsonPropertyName("isDefault")]
    public bool? IsDefault { get; init; }

    /// <summary>Active or inactive status ("active" | "inactive"). Optional.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }
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

    [JsonPropertyName("sprk_tenantid")]
    public string? TenantId { get; set; }

    [JsonPropertyName("sprk_tenantname")]
    public string? TenantName { get; set; }

    [JsonPropertyName("sprk_rootsiteurl")]
    public string? RootSiteUrl { get; set; }

    [JsonPropertyName("sprk_graphendpoint")]
    public string? GraphApiBaseUrl { get; set; }

    [JsonPropertyName("sprk_description")]
    public string? Description { get; set; }

    [JsonPropertyName("sprk_isdefault")]
    public bool IsDefault { get; set; }

    /// <summary>Dataverse statecode: 0 = Active, 1 = Inactive.</summary>
    [JsonPropertyName("statecode")]
    public int StateCode { get; set; }

    [JsonPropertyName("createdon")]
    public DateTimeOffset? CreatedOn { get; set; }

    [JsonPropertyName("modifiedon")]
    public DateTimeOffset? ModifiedOn { get; set; }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private string MappedStatus => StateCode == 0 ? "active" : "inactive";

    public EnvironmentSummaryDto ToSummary() => new()
    {
        Id = Id,
        Name = Name ?? string.Empty,
        TenantId = TenantId,
        TenantName = TenantName,
        RootSiteUrl = RootSiteUrl ?? string.Empty,
        GraphEndpoint = GraphApiBaseUrl,
        IsDefault = IsDefault,
        Status = MappedStatus,
        CreatedOn = CreatedOn,
        ModifiedOn = ModifiedOn
    };

    public EnvironmentDetailDto ToDetail() => new()
    {
        Id = Id,
        Name = Name ?? string.Empty,
        TenantId = TenantId,
        TenantName = TenantName,
        RootSiteUrl = RootSiteUrl ?? string.Empty,
        GraphEndpoint = GraphApiBaseUrl,
        Description = Description,
        IsDefault = IsDefault,
        Status = MappedStatus,
        CreatedOn = CreatedOn,
        ModifiedOn = ModifiedOn
    };
}
