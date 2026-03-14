using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.SpeAdmin;

/// <summary>
/// Represents a consuming application registration for a SharePoint Embedded container type.
///
/// In multi-tenant SPE scenarios, a single container type (owned by one application)
/// can be consumed by multiple applications from different tenants. Each consuming app
/// registration defines what permissions the consuming app has been granted for the container type.
///
/// All Graph SDK types are stripped at the service layer — this record is the public API surface (ADR-007).
/// </summary>
public sealed record ConsumingTenantDto
{
    /// <summary>
    /// The Azure AD application (client) ID of the consuming application.
    /// Used as the resource key for update (PUT) and remove (DELETE) operations.
    /// </summary>
    [JsonPropertyName("appId")]
    public string AppId { get; init; } = string.Empty;

    /// <summary>
    /// Optional display name of the consuming application.
    /// Resolved from the Graph API or provided by the caller during registration.
    /// May be null or empty when the display name cannot be resolved.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    /// <summary>
    /// The Azure AD tenant ID of the consuming application's home tenant.
    /// Identifies which tenant the consuming application belongs to.
    /// </summary>
    [JsonPropertyName("tenantId")]
    public string? TenantId { get; init; }

    /// <summary>
    /// Delegated permission scopes granted to the consuming application for this container type.
    /// Typical values: "readContent", "writeContent", "manageContent", "full".
    /// May be empty when no delegated permissions have been granted.
    /// </summary>
    [JsonPropertyName("delegatedPermissions")]
    public IReadOnlyList<string> DelegatedPermissions { get; init; } = [];

    /// <summary>
    /// Application permission scopes granted to the consuming application for this container type.
    /// Typical values: "readContent", "writeContent", "manageContent", "managePermissions", "full".
    /// May be empty when no application permissions have been granted.
    /// </summary>
    [JsonPropertyName("applicationPermissions")]
    public IReadOnlyList<string> ApplicationPermissions { get; init; } = [];
}

/// <summary>
/// Response envelope for the list consuming tenants endpoint:
///   GET /api/spe/containertypes/{typeId}/consumers?configId={id}
///
/// Returns all consuming application registrations for the specified container type.
/// </summary>
public sealed record ConsumingTenantListDto
{
    /// <summary>Consuming application registration entries for this container type.</summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<ConsumingTenantDto> Items { get; init; } = [];

    /// <summary>Total number of consuming application registrations returned.</summary>
    [JsonPropertyName("count")]
    public int Count { get; init; }
}

/// <summary>
/// Request payload for registering a new consuming application for a container type.
///   POST /api/spe/containertypes/{typeId}/consumers?configId={id}
/// </summary>
public sealed record RegisterConsumingTenantRequest
{
    /// <summary>
    /// The Azure AD application (client) ID of the consuming application to register.
    /// Required — must be a valid GUID string.
    /// </summary>
    [JsonPropertyName("appId")]
    public string AppId { get; init; } = string.Empty;

    /// <summary>
    /// Optional display name for the consuming application (for admin labeling).
    /// If omitted, the API will attempt to resolve the display name from the Graph API.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    /// <summary>
    /// The Azure AD tenant ID of the consuming application's home tenant.
    /// Optional — if omitted, the consuming app is assumed to be in the same tenant as the owning app.
    /// </summary>
    [JsonPropertyName("tenantId")]
    public string? TenantId { get; init; }

    /// <summary>
    /// Delegated permission scopes to grant to the consuming application.
    /// Leave empty to grant no delegated permissions.
    /// </summary>
    [JsonPropertyName("delegatedPermissions")]
    public IReadOnlyList<string> DelegatedPermissions { get; init; } = [];

    /// <summary>
    /// Application permission scopes to grant to the consuming application.
    /// Leave empty to grant no application permissions.
    /// </summary>
    [JsonPropertyName("applicationPermissions")]
    public IReadOnlyList<string> ApplicationPermissions { get; init; } = [];
}

/// <summary>
/// Request payload for updating permissions for an existing consuming application registration.
///   PUT /api/spe/containertypes/{typeId}/consumers/{appId}?configId={id}
/// </summary>
public sealed record UpdateConsumingTenantRequest
{
    /// <summary>
    /// Updated delegated permission scopes for the consuming application.
    /// Replaces the existing delegated permissions entirely.
    /// </summary>
    [JsonPropertyName("delegatedPermissions")]
    public IReadOnlyList<string> DelegatedPermissions { get; init; } = [];

    /// <summary>
    /// Updated application permission scopes for the consuming application.
    /// Replaces the existing application permissions entirely.
    /// </summary>
    [JsonPropertyName("applicationPermissions")]
    public IReadOnlyList<string> ApplicationPermissions { get; init; } = [];
}
