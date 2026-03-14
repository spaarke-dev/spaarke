using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.SpeAdmin;

// ─────────────────────────────────────────────────────────────────────────────
// List / Detail response DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Summary projection of a sprk_specontainertypeconfig record,
/// returned in list responses (GET /api/spe/configs).
/// </summary>
public sealed record ConfigSummaryDto
{
    /// <summary>Primary key — sprk_specontainertypeconfigid</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>Display name — sprk_name</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Azure AD Application (Client) ID for this container type registration — sprk_clientid</summary>
    [JsonPropertyName("clientId")]
    public string ClientId { get; init; } = string.Empty;

    /// <summary>SharePoint Embedded container type ID — sprk_containertypeid</summary>
    [JsonPropertyName("containerTypeId")]
    public string ContainerTypeId { get; init; } = string.Empty;

    /// <summary>Lookup: Business unit this config is scoped to — sprk_BusinessUnitId</summary>
    [JsonPropertyName("businessUnitId")]
    public Guid? BusinessUnitId { get; init; }

    [JsonPropertyName("businessUnitName")]
    public string? BusinessUnitName { get; init; }

    /// <summary>Lookup: SPE environment — sprk_EnvironmentId</summary>
    [JsonPropertyName("environmentId")]
    public Guid? EnvironmentId { get; init; }

    [JsonPropertyName("environmentName")]
    public string? EnvironmentName { get; init; }

    /// <summary>Whether the config is currently active — sprk_isactive</summary>
    [JsonPropertyName("isActive")]
    public bool IsActive { get; init; }

    /// <summary>Record creation timestamp.</summary>
    [JsonPropertyName("createdOn")]
    public DateTimeOffset? CreatedOn { get; init; }

    /// <summary>Last modified timestamp.</summary>
    [JsonPropertyName("modifiedOn")]
    public DateTimeOffset? ModifiedOn { get; init; }
}

/// <summary>
/// Full detail of a sprk_specontainertypeconfig record,
/// returned in single-record responses (GET /api/spe/configs/{id}).
/// Includes all fields required for administration and editing.
/// </summary>
public sealed record ConfigDetailDto
{
    /// <summary>Primary key — sprk_specontainertypeconfigid</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>Display name — sprk_name</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Azure AD Application (Client) ID — sprk_clientid</summary>
    [JsonPropertyName("clientId")]
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Azure AD Tenant ID for this app registration — sprk_tenantid</summary>
    [JsonPropertyName("tenantId")]
    public string TenantId { get; init; } = string.Empty;

    /// <summary>SharePoint Embedded container type ID — sprk_containertypeid</summary>
    [JsonPropertyName("containerTypeId")]
    public string ContainerTypeId { get; init; } = string.Empty;

    /// <summary>
    /// Azure Key Vault secret name holding the client secret for this app registration.
    /// Format: alphanumeric characters and hyphens only, 1-127 characters.
    /// — sprk_keyvaultsecretname
    /// </summary>
    [JsonPropertyName("keyVaultSecretName")]
    public string KeyVaultSecretName { get; init; } = string.Empty;

    /// <summary>SharePoint site URL associated with this container type — sprk_sharepointsiteurl</summary>
    [JsonPropertyName("sharePointSiteUrl")]
    public string? SharePointSiteUrl { get; init; }

    /// <summary>Microsoft Graph API base URL override (if non-standard) — sprk_graphapibaseurl</summary>
    [JsonPropertyName("graphApiBaseUrl")]
    public string? GraphApiBaseUrl { get; init; }

    /// <summary>OAuth2 authority/login URL (tenant-specific or common) — sprk_oauthauthorityurl</summary>
    [JsonPropertyName("oAuthAuthorityUrl")]
    public string? OAuthAuthorityUrl { get; init; }

    /// <summary>Microsoft Graph scopes required for this config (space-separated) — sprk_graphscopes</summary>
    [JsonPropertyName("graphScopes")]
    public string? GraphScopes { get; init; }

    /// <summary>Maximum number of containers allowed for this config — sprk_maxcontainers</summary>
    [JsonPropertyName("maxContainers")]
    public int? MaxContainers { get; init; }

    /// <summary>Default container name prefix applied when creating containers — sprk_containerprefix</summary>
    [JsonPropertyName("containerPrefix")]
    public string? ContainerPrefix { get; init; }

    /// <summary>Graph client cache TTL override in minutes (0 = use global default) — sprk_graphclientcachettlminutes</summary>
    [JsonPropertyName("graphClientCacheTtlMinutes")]
    public int? GraphClientCacheTtlMinutes { get; init; }

    /// <summary>Whether column type-ahead lookup is enabled for this config — sprk_columnsearchenabled</summary>
    [JsonPropertyName("columnSearchEnabled")]
    public bool ColumnSearchEnabled { get; init; }

    /// <summary>Whether full-text file search is enabled — sprk_filesearchenabled</summary>
    [JsonPropertyName("fileSearchEnabled")]
    public bool FileSearchEnabled { get; init; }

    /// <summary>Whether the recycle bin feature is enabled — sprk_recyclebinenabled</summary>
    [JsonPropertyName("recycleBinEnabled")]
    public bool RecycleBinEnabled { get; init; }

    /// <summary>Whether the config is currently active — sprk_isactive</summary>
    [JsonPropertyName("isActive")]
    public bool IsActive { get; init; }

    /// <summary>Optional notes/description for administrators — sprk_notes</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    /// <summary>Lookup: Business unit this config is scoped to — sprk_BusinessUnitId</summary>
    [JsonPropertyName("businessUnitId")]
    public Guid? BusinessUnitId { get; init; }

    [JsonPropertyName("businessUnitName")]
    public string? BusinessUnitName { get; init; }

    /// <summary>Lookup: SPE environment — sprk_EnvironmentId</summary>
    [JsonPropertyName("environmentId")]
    public Guid? EnvironmentId { get; init; }

    [JsonPropertyName("environmentName")]
    public string? EnvironmentName { get; init; }

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
/// Request body for creating a new container type config (POST /api/spe/configs).
/// Required fields match those that cannot be defaulted in Dataverse.
/// </summary>
public sealed record CreateConfigRequest
{
    /// <summary>Display name for this config. Required.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Azure AD Application (Client) ID. Required.</summary>
    [JsonPropertyName("clientId")]
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Azure AD Tenant ID. Required.</summary>
    [JsonPropertyName("tenantId")]
    public string TenantId { get; init; } = string.Empty;

    /// <summary>SharePoint Embedded container type GUID. Required.</summary>
    [JsonPropertyName("containerTypeId")]
    public string ContainerTypeId { get; init; } = string.Empty;

    /// <summary>
    /// Azure Key Vault secret name for the client secret. Required.
    /// Must be alphanumeric characters and hyphens only, 1-127 characters.
    /// </summary>
    [JsonPropertyName("keyVaultSecretName")]
    public string KeyVaultSecretName { get; init; } = string.Empty;

    /// <summary>Business unit GUID to scope this config to. Required.</summary>
    [JsonPropertyName("businessUnitId")]
    public Guid? BusinessUnitId { get; init; }

    /// <summary>SPE environment GUID to associate this config with. Required.</summary>
    [JsonPropertyName("environmentId")]
    public Guid? EnvironmentId { get; init; }

    // Optional fields

    [JsonPropertyName("sharePointSiteUrl")]
    public string? SharePointSiteUrl { get; init; }

    [JsonPropertyName("graphApiBaseUrl")]
    public string? GraphApiBaseUrl { get; init; }

    [JsonPropertyName("oAuthAuthorityUrl")]
    public string? OAuthAuthorityUrl { get; init; }

    [JsonPropertyName("graphScopes")]
    public string? GraphScopes { get; init; }

    [JsonPropertyName("maxContainers")]
    public int? MaxContainers { get; init; }

    [JsonPropertyName("containerPrefix")]
    public string? ContainerPrefix { get; init; }

    [JsonPropertyName("graphClientCacheTtlMinutes")]
    public int? GraphClientCacheTtlMinutes { get; init; }

    [JsonPropertyName("columnSearchEnabled")]
    public bool ColumnSearchEnabled { get; init; }

    [JsonPropertyName("fileSearchEnabled")]
    public bool FileSearchEnabled { get; init; }

    [JsonPropertyName("recycleBinEnabled")]
    public bool RecycleBinEnabled { get; init; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; init; } = true;

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}

/// <summary>
/// Request body for updating an existing container type config (PUT /api/spe/configs/{id}).
/// All fields are optional — only supplied fields are applied.
/// </summary>
public sealed record UpdateConfigRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("clientId")]
    public string? ClientId { get; init; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; init; }

    [JsonPropertyName("containerTypeId")]
    public string? ContainerTypeId { get; init; }

    /// <summary>
    /// Azure Key Vault secret name. If provided, must be alphanumeric + hyphens, 1-127 chars.
    /// </summary>
    [JsonPropertyName("keyVaultSecretName")]
    public string? KeyVaultSecretName { get; init; }

    [JsonPropertyName("businessUnitId")]
    public Guid? BusinessUnitId { get; init; }

    [JsonPropertyName("environmentId")]
    public Guid? EnvironmentId { get; init; }

    [JsonPropertyName("sharePointSiteUrl")]
    public string? SharePointSiteUrl { get; init; }

    [JsonPropertyName("graphApiBaseUrl")]
    public string? GraphApiBaseUrl { get; init; }

    [JsonPropertyName("oAuthAuthorityUrl")]
    public string? OAuthAuthorityUrl { get; init; }

    [JsonPropertyName("graphScopes")]
    public string? GraphScopes { get; init; }

    [JsonPropertyName("maxContainers")]
    public int? MaxContainers { get; init; }

    [JsonPropertyName("containerPrefix")]
    public string? ContainerPrefix { get; init; }

    [JsonPropertyName("graphClientCacheTtlMinutes")]
    public int? GraphClientCacheTtlMinutes { get; init; }

    [JsonPropertyName("columnSearchEnabled")]
    public bool? ColumnSearchEnabled { get; init; }

    [JsonPropertyName("fileSearchEnabled")]
    public bool? FileSearchEnabled { get; init; }

    [JsonPropertyName("recycleBinEnabled")]
    public bool? RecycleBinEnabled { get; init; }

    [JsonPropertyName("isActive")]
    public bool? IsActive { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// List response wrapper
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Paginated list response for GET /api/spe/configs.
/// </summary>
public sealed record ConfigListResponse
{
    [JsonPropertyName("items")]
    public IReadOnlyList<ConfigSummaryDto> Items { get; init; } = Array.Empty<ConfigSummaryDto>();

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Internal Dataverse projection (for QueryAsync deserialization)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Raw Dataverse response projection for sprk_specontainertypeconfig records.
/// Property names match Dataverse logical attribute names / OData query results.
/// Used internally by ConfigEndpoints for QueryAsync deserialization only.
/// </summary>
internal sealed class ConfigDataverseRow
{
    [JsonPropertyName("sprk_specontainertypeconfigid")]
    public Guid Id { get; set; }

    [JsonPropertyName("sprk_name")]
    public string? Name { get; set; }

    [JsonPropertyName("sprk_clientid")]
    public string? ClientId { get; set; }

    [JsonPropertyName("sprk_tenantid")]
    public string? TenantId { get; set; }

    [JsonPropertyName("sprk_containertypeid")]
    public string? ContainerTypeId { get; set; }

    [JsonPropertyName("sprk_keyvaultsecretname")]
    public string? KeyVaultSecretName { get; set; }

    [JsonPropertyName("sprk_sharepointsiteurl")]
    public string? SharePointSiteUrl { get; set; }

    [JsonPropertyName("sprk_graphapibaseurl")]
    public string? GraphApiBaseUrl { get; set; }

    [JsonPropertyName("sprk_oauthauthorityurl")]
    public string? OAuthAuthorityUrl { get; set; }

    [JsonPropertyName("sprk_graphscopes")]
    public string? GraphScopes { get; set; }

    [JsonPropertyName("sprk_maxcontainers")]
    public int? MaxContainers { get; set; }

    [JsonPropertyName("sprk_containerprefix")]
    public string? ContainerPrefix { get; set; }

    [JsonPropertyName("sprk_graphclientcachettlminutes")]
    public int? GraphClientCacheTtlMinutes { get; set; }

    [JsonPropertyName("sprk_columnsearchenabled")]
    public bool ColumnSearchEnabled { get; set; }

    [JsonPropertyName("sprk_filesearchenabled")]
    public bool FileSearchEnabled { get; set; }

    [JsonPropertyName("sprk_recyclebinenabled")]
    public bool RecycleBinEnabled { get; set; }

    [JsonPropertyName("sprk_isactive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("sprk_notes")]
    public string? Notes { get; set; }

    // Expanded lookup fields (using $expand or OData aliasing)
    [JsonPropertyName("_sprk_businessunitid_value")]
    public Guid? BusinessUnitId { get; set; }

    [JsonPropertyName("_sprk_businessunitid_value@OData.Community.Display.V1.FormattedValue")]
    public string? BusinessUnitName { get; set; }

    [JsonPropertyName("_sprk_environmentid_value")]
    public Guid? EnvironmentId { get; set; }

    [JsonPropertyName("_sprk_environmentid_value@OData.Community.Display.V1.FormattedValue")]
    public string? EnvironmentName { get; set; }

    [JsonPropertyName("createdon")]
    public DateTimeOffset? CreatedOn { get; set; }

    [JsonPropertyName("modifiedon")]
    public DateTimeOffset? ModifiedOn { get; set; }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    public ConfigSummaryDto ToSummary() => new()
    {
        Id = Id,
        Name = Name ?? string.Empty,
        ClientId = ClientId ?? string.Empty,
        ContainerTypeId = ContainerTypeId ?? string.Empty,
        BusinessUnitId = BusinessUnitId,
        BusinessUnitName = BusinessUnitName,
        EnvironmentId = EnvironmentId,
        EnvironmentName = EnvironmentName,
        IsActive = IsActive,
        CreatedOn = CreatedOn,
        ModifiedOn = ModifiedOn
    };

    public ConfigDetailDto ToDetail() => new()
    {
        Id = Id,
        Name = Name ?? string.Empty,
        ClientId = ClientId ?? string.Empty,
        TenantId = TenantId ?? string.Empty,
        ContainerTypeId = ContainerTypeId ?? string.Empty,
        KeyVaultSecretName = KeyVaultSecretName ?? string.Empty,
        SharePointSiteUrl = SharePointSiteUrl,
        GraphApiBaseUrl = GraphApiBaseUrl,
        OAuthAuthorityUrl = OAuthAuthorityUrl,
        GraphScopes = GraphScopes,
        MaxContainers = MaxContainers,
        ContainerPrefix = ContainerPrefix,
        GraphClientCacheTtlMinutes = GraphClientCacheTtlMinutes,
        ColumnSearchEnabled = ColumnSearchEnabled,
        FileSearchEnabled = FileSearchEnabled,
        RecycleBinEnabled = RecycleBinEnabled,
        IsActive = IsActive,
        Notes = Notes,
        BusinessUnitId = BusinessUnitId,
        BusinessUnitName = BusinessUnitName,
        EnvironmentId = EnvironmentId,
        EnvironmentName = EnvironmentName,
        CreatedOn = CreatedOn,
        ModifiedOn = ModifiedOn
    };
}
