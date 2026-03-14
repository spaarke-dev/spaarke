using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.SpeAdmin;

/// <summary>
/// Represents a soft-deleted SPE container in the recycle bin.
///
/// Containers enter the recycle bin when deleted by an administrator (soft-delete).
/// They remain recoverable for the retention period configured in the SPE tenant.
/// After the retention period expires, they are permanently removed by the platform.
///
/// ADR-007: No Graph SDK types in public API surface — only domain model fields.
/// </summary>
public sealed record DeletedContainerDto
{
    /// <summary>
    /// Graph FileStorageContainer ID. Used as the path parameter for restore and permanent-delete.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Display name of the deleted container as it appeared before deletion.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the container was soft-deleted (moved to recycle bin).
    /// </summary>
    [JsonPropertyName("deletedDateTime")]
    public DateTimeOffset? DeletedDateTime { get; init; }

    /// <summary>
    /// The SharePoint Embedded container type GUID this container belongs to.
    /// </summary>
    [JsonPropertyName("containerTypeId")]
    public string ContainerTypeId { get; init; } = string.Empty;
}
