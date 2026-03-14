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
