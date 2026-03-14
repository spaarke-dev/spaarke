using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.SpeAdmin;

/// <summary>
/// Request body for PUT /api/spe/containertypes/{typeId}/settings?configId={id}.
///
/// Updates container type settings that define default behaviors for all containers
/// created from this container type. Administrators use these settings to enforce
/// organizational policies across all containers of a given type.
///
/// Only non-null fields in the request are applied. Pass null to leave a setting unchanged.
/// </summary>
/// <remarks>
/// ADR-007: This is a pure API DTO — no Graph SDK types are referenced here.
/// The service layer maps these values to Graph SDK types before sending to the Graph API.
/// </remarks>
public sealed record UpdateContainerTypeSettingsRequest
{
    /// <summary>
    /// Controls how containers of this type can be shared externally.
    ///
    /// Valid values (case-insensitive):
    ///   disabled  — sharing is completely disabled
    ///   view      — recipients can view but not edit (view-only links)
    ///   edit      — recipients can view and edit (edit links)
    ///   full      — recipients can view, edit, and invite others
    ///
    /// Null means "do not change the current sharing capability".
    /// </summary>
    [JsonPropertyName("sharingCapability")]
    public string? SharingCapability { get; init; }

    /// <summary>
    /// Whether versioning is enabled for files in containers of this type.
    /// When true, SharePoint Embedded retains previous versions of modified files.
    /// Null means "do not change the current versioning setting".
    /// </summary>
    [JsonPropertyName("isVersioningEnabled")]
    public bool? IsVersioningEnabled { get; init; }

    /// <summary>
    /// Maximum number of major versions to retain for each file.
    /// Only relevant when <see cref="IsVersioningEnabled"/> is true.
    /// Must be a positive integer. Null means "do not change".
    /// </summary>
    [JsonPropertyName("majorVersionLimit")]
    public int? MajorVersionLimit { get; init; }

    /// <summary>
    /// Storage quota limit for all containers of this type, expressed in bytes.
    /// When set, containers cannot exceed this total storage usage.
    /// Null means "do not change the storage limit".
    /// </summary>
    [JsonPropertyName("storageUsedInBytes")]
    public long? StorageUsedInBytes { get; init; }
}
