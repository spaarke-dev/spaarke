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

    // ── Added by task 025 (spec FR-C07) ──────────────────────────────────────
    // The v1.0 settings complex type has exactly nine properties, verified against Graph's own OData
    // metadata (notes/task-025-schema-verification.md). Four were wired by task 023; these are the
    // remaining five. FR-C07's list named `agent.chatEmbedAllowedHosts`, which does not exist in
    // either API version, and omitted `sharingCapability`, which does — so this is five, not nine.

    /// <summary>
    /// Whether container content is indexed for search. Null means "do not change".
    /// </summary>
    /// <remarks>
    /// Together with <see cref="IsDiscoverabilityEnabled"/> this governs whether content is findable
    /// at all. An administrator had no way to see or set either — spec §4.5 flags this as the one
    /// R2-relevant slice of the SPE-knowledge-source question.
    /// </remarks>
    [JsonPropertyName("isSearchEnabled")]
    public bool? IsSearchEnabled { get; init; }

    /// <summary>
    /// Whether containers of this type are discoverable. Null means "do not change".
    /// </summary>
    [JsonPropertyName("isDiscoverabilityEnabled")]
    public bool? IsDiscoverabilityEnabled { get; init; }

    /// <summary>
    /// Whether sharing is restricted for containers of this type. Null means "do not change".
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="SharingCapability"/>: that selects WHICH sharing is allowed, this is a
    /// separate restriction flag. Both exist on the resource and neither substitutes for the other.
    /// </remarks>
    [JsonPropertyName("isSharingRestricted")]
    public bool? IsSharingRestricted { get; init; }

    /// <summary>
    /// URL template applied to containers of this type. Null means "do not change".
    /// </summary>
    [JsonPropertyName("urlTemplate")]
    public string? UrlTemplate { get; init; }

    /// <summary>
    /// Which settings a consuming tenant may override, as the comma-delimited flag list Graph uses
    /// (e.g. <c>"sharingCapability,itemMajorVersionLimit,isOfficeRestricted"</c>).
    /// Null means "do not change".
    /// </summary>
    /// <remarks>
    /// Deliberately a <b>string</b>, not the SDK's typed enum. The live tenant returns
    /// <c>sharingCapability</c> and <c>isOfficeRestricted</c> as flags, and <b>neither is a member of
    /// the SDK's generated <c>FileStorageContainerTypeSettingsOverride</c></b> — so parsing through
    /// the typed enum would drop or reject real data. This is the opposite of the typed-over-untyped
    /// choice task 023 made, and deliberately so: there the type was authoritative, here it is
    /// provably narrower than reality. Task 026 owns rendering the override state.
    /// </remarks>
    [JsonPropertyName("consumingTenantOverridables")]
    public string? ConsumingTenantOverridables { get; init; }
}
