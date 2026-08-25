using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.SpeAdmin;

/// <summary>
/// Represents a single application permission entry on a SharePoint Embedded container type,
/// returned from the Graph API applicationPermissions endpoint.
///
/// Mapped from the Graph beta endpoint:
///   GET /storage/fileStorage/containerTypes/{containerTypeId}/applicationPermissions
///
/// All Graph SDK types are stripped at the service layer — this record is the public API surface (ADR-007).
/// </summary>
public sealed record ContainerTypePermissionDto
{
    /// <summary>
    /// The Azure AD application (client) ID of the consuming application that has been granted permissions.
    /// </summary>
    [JsonPropertyName("appId")]
    public string AppId { get; init; } = string.Empty;

    /// <summary>
    /// Delegated permission scopes granted to the consuming application for this container type.
    /// Typical values: "FileStorageContainer.Selected", "Files.Read.All", "Files.ReadWrite.All".
    /// May be empty when no delegated permissions have been granted.
    /// </summary>
    [JsonPropertyName("delegatedPermissions")]
    public IReadOnlyList<string> DelegatedPermissions { get; init; } = [];

    /// <summary>
    /// Application permission scopes granted to the consuming application for this container type.
    /// Typical values: "FileStorageContainer.Selected", "Files.Read.All", "Files.ReadWrite.All".
    /// May be empty when no application permissions have been granted.
    /// </summary>
    [JsonPropertyName("applicationPermissions")]
    public IReadOnlyList<string> ApplicationPermissions { get; init; } = [];
}

/// <summary>
/// Response envelope for the list container type permissions endpoint:
///   GET /api/spe/containertypes/{typeId}/permissions?configId={id}
///
/// Returns all application permissions registered for the specified container type.
/// </summary>
public sealed record ContainerTypePermissionListDto
{
    /// <summary>Application permission entries for this container type.</summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<ContainerTypePermissionDto> Items { get; init; } = [];

    /// <summary>Total number of application permission entries returned.</summary>
    [JsonPropertyName("count")]
    public int Count { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Container-type OWNERS (FR-C09, task 027)
//
// 🔑 Deliberately SEPARATE records from ContainerTypePermissionDto above, not an extension of it.
// That DTO describes Graph's `applicationPermissions` — which APPLICATIONS may access containers of
// a type. These describe `fileStorageContainerType.permissions` — which PEOPLE own the type. Task
// 027's POML treated them as one surface; they share a Graph word and nothing else, and folding them
// into one shape would bake that conflation into the wire contract.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A person who owns (administers) a SharePoint Embedded container type.
/// </summary>
public sealed record ContainerTypeOwnerDto
{
    /// <summary>Graph permission id — the handle required to revoke this grant.</summary>
    [JsonPropertyName("permissionId")]
    public string PermissionId { get; init; } = string.Empty;

    /// <summary>
    /// Display name, or null when Graph did not report one.
    /// Null means NOT REPORTED and must render as unknown — never as a blank that reads as "no name".
    /// </summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    /// <summary>Email/UPN, or null when Graph did not report one.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    /// <summary>Directory object id, or null when Graph did not report one.</summary>
    [JsonPropertyName("userId")]
    public string? UserId { get; init; }

    /// <summary>Roles carried by this grant (e.g. "owner"). Empty when Graph reported none.</summary>
    [JsonPropertyName("roles")]
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>Maps the domain record to the wire DTO (ADR-007 — no Graph types cross this line).</summary>
    public static ContainerTypeOwnerDto FromDomain(
        Sprk.Bff.Api.Infrastructure.Graph.SpeAdminGraphService.SpeContainerTypeOwner owner) =>
        new()
        {
            PermissionId = owner.PermissionId,
            DisplayName = owner.DisplayName,
            Email = owner.Email,
            UserId = owner.UserId,
            Roles = owner.Roles,
        };
}

/// <summary>Response envelope for GET /api/spe/containertypes/{typeId}/owners.</summary>
public sealed record ContainerTypeOwnerListDto
{
    /// <summary>The container type's owners. Empty means Graph reported none, not "not loaded".</summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<ContainerTypeOwnerDto> Items { get; init; } = [];
}

/// <summary>Request body for POST /api/spe/containertypes/{typeId}/owners.</summary>
public sealed record AddContainerTypeOwnerRequest
{
    /// <summary>
    /// The user to grant ownership to — an email/UPN or a directory object id.
    /// Passed to Graph as given; an unknown principal surfaces Graph's own error (AC-6).
    /// </summary>
    [JsonPropertyName("userIdentifier")]
    public string UserIdentifier { get; init; } = string.Empty;
}
