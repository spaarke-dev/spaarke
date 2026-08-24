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
    /// Valid values (case-insensitive) — the members of Graph's SharingCapabilities enum:
    ///   disabled                        — sharing is completely disabled
    ///   externalUserSharingOnly         — new external users may be invited
    ///   existingExternalUserSharingOnly — only external users already in the directory
    ///   externalUserAndGuestSharing     — external users and unauthenticated guest links
    ///
    /// Null means "do not change the current sharing capability".
    ///
    /// 🔴 CORRECTED 2026-08-24 (task 023). This previously documented "view", "edit", and "full",
    /// none of which are Graph values. The endpoint validated against that same wrong list, so every
    /// value the client actually sends except "disabled" was rejected by our own validator with a
    /// 400 — sharing capability could not be set to anything else.
    /// </summary>
    [JsonPropertyName("sharingCapability")]
    public string? SharingCapability { get; init; }

    /// <summary>
    /// Whether item versioning is enabled for files in containers of this type.
    /// When true, SharePoint Embedded retains previous versions of modified files.
    /// Null means "do not change the current versioning setting".
    /// </summary>
    /// <remarks>
    /// Renamed from <c>isVersioningEnabled</c> 2026-08-24 (task 023) to match the Graph property
    /// <c>isItemVersioningEnabled</c>. The old name was not on the resource, so it never applied.
    /// </remarks>
    [JsonPropertyName("isItemVersioningEnabled")]
    public bool? IsItemVersioningEnabled { get; init; }

    /// <summary>
    /// Maximum number of major versions to retain for each item.
    /// Only relevant when <see cref="IsItemVersioningEnabled"/> is true.
    /// Must be a positive integer. Null means "do not change".
    /// </summary>
    /// <remarks>
    /// Renamed from <c>majorVersionLimit</c> 2026-08-24 (task 023) — that name does not exist on the
    /// Graph resource, so the setting never applied. Widened to <c>long</c> to match the SDK.
    /// </remarks>
    [JsonPropertyName("itemMajorVersionLimit")]
    public long? ItemMajorVersionLimit { get; init; }

    /// <summary>
    /// Per-container storage <b>CEILING</b> in bytes — the maximum a single container of this type
    /// may grow to. Null means "do not change the ceiling".
    /// </summary>
    /// <remarks>
    /// 🔴 This is a <b>limit</b>, not a measurement. It was previously called
    /// <c>storageUsedInBytes</c>, which is the name of the consumption <i>metric</i> on a container —
    /// a different concept on a different resource. Modelling a ceiling as a usage figure is why the
    /// storage story never cohered (spec §3.2); consumption is task 024's surface and must never
    /// share a field, DTO property, parameter, or control with this one (spec FR-C05).
    /// </remarks>
    [JsonPropertyName("maxStoragePerContainerInBytes")]
    public long? MaxStoragePerContainerInBytes { get; init; }
}
