using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.SpeAdmin;

/// <summary>
/// Container-type settings as returned by Graph — the nine v1.0 properties plus the beta-only
/// <c>isOfficeRestricted</c>.
/// </summary>
/// <remarks>
/// Verified against Graph's own OData metadata rather than documentation prose; see
/// <c>projects/sdap-SPE-admin-app-r2/notes/task-025-schema-verification.md</c>. Added by task 025
/// (spec FR-C07) — until then no settings value reached the client at all, so the Settings screen
/// could only ever show what the user had just typed.
/// <para>
/// <b>Every member is nullable and null means NOT REPORTED, never a default.</b> A settings block that
/// could not be read must not present as "search is off".
/// </para>
/// </remarks>
public sealed record ContainerTypeSettingsDto
{
    /// <summary>Which external sharing is permitted. A member of Graph's SharingCapabilities.</summary>
    [JsonPropertyName("sharingCapability")]
    public string? SharingCapability { get; init; }

    /// <summary>Whether item versioning is enabled for containers of this type.</summary>
    [JsonPropertyName("isItemVersioningEnabled")]
    public bool? IsItemVersioningEnabled { get; init; }

    /// <summary>Maximum major versions retained per item.</summary>
    [JsonPropertyName("itemMajorVersionLimit")]
    public long? ItemMajorVersionLimit { get; init; }

    /// <summary>
    /// Per-container storage <b>CEILING</b> in bytes — a limit, never a usage figure. Consumption is
    /// <c>storageUsedInBytes</c> on a container (task 023's split; spec FR-C05).
    /// </summary>
    [JsonPropertyName("maxStoragePerContainerInBytes")]
    public long? MaxStoragePerContainerInBytes { get; init; }

    /// <summary>Whether container content is indexed for search.</summary>
    [JsonPropertyName("isSearchEnabled")]
    public bool? IsSearchEnabled { get; init; }

    /// <summary>Whether containers of this type are discoverable.</summary>
    [JsonPropertyName("isDiscoverabilityEnabled")]
    public bool? IsDiscoverabilityEnabled { get; init; }

    /// <summary>Whether sharing is restricted. Distinct from <see cref="SharingCapability"/>.</summary>
    [JsonPropertyName("isSharingRestricted")]
    public bool? IsSharingRestricted { get; init; }

    /// <summary>URL template applied to containers of this type.</summary>
    [JsonPropertyName("urlTemplate")]
    public string? UrlTemplate { get; init; }

    /// <summary>
    /// Which settings a consuming tenant may override, as the raw comma-delimited flag string.
    /// Override METADATA, not a value — task 026 renders its meaning.
    /// </summary>
    [JsonPropertyName("consumingTenantOverridables")]
    public string? ConsumingTenantOverridables { get; init; }

    /// <summary>
    /// Beta-only, and <b>read-only here</b>: absent from the v1.0 schema and from the SDK's typed
    /// model, so writing it would mean reintroducing the untyped string-key pattern task 023 removed.
    /// </summary>
    [JsonPropertyName("isOfficeRestricted")]
    public bool? IsOfficeRestricted { get; init; }

    /// <summary>Maps the domain record to this DTO. Null in, null out — absence is preserved.</summary>
    public static ContainerTypeSettingsDto? FromDomain(
        Infrastructure.Graph.SpeAdminGraphService.SpeContainerTypeSettings? s) =>
        s is null ? null : new ContainerTypeSettingsDto
        {
            SharingCapability = s.SharingCapability,
            IsItemVersioningEnabled = s.IsItemVersioningEnabled,
            ItemMajorVersionLimit = s.ItemMajorVersionLimit,
            MaxStoragePerContainerInBytes = s.MaxStoragePerContainerInBytes,
            IsSearchEnabled = s.IsSearchEnabled,
            IsDiscoverabilityEnabled = s.IsDiscoverabilityEnabled,
            IsSharingRestricted = s.IsSharingRestricted,
            UrlTemplate = s.UrlTemplate,
            ConsumingTenantOverridables = s.ConsumingTenantOverridables,
            IsOfficeRestricted = s.IsOfficeRestricted,
        };
}

/// <summary>
/// Represents a single SharePoint Embedded container type returned from the Graph API.
///
/// Mapped from the Graph <c>/storage/fileStorage/containerTypes</c> response.
/// All Graph SDK types are stripped at the service layer — this record is the public API surface (ADR-007).
/// </summary>
public sealed record ContainerTypeDto
{
    /// <summary>Container type GUID assigned by SharePoint Embedded (Graph API).</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable display name for the container type.</summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Optional description of the container type's purpose.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Billing classification for the container type: "standard", "trial", or "directToCustomer".
    /// Null when Graph does not report it — which means UNKNOWN, not "standard".
    /// </summary>
    [JsonPropertyName("billingClassification")]
    public string? BillingClassification { get; init; }

    /// <summary>
    /// Whether the container type's billing is in good standing: "valid" or "invalid".
    /// Null when Graph does not report it.
    /// </summary>
    /// <remarks>
    /// Added by task 029 (spec FR-C12). Graph declares this on <c>fileStorageContainerType</c> in both
    /// the v1.0 and beta CSDL as enum <c>fileStorageContainerBillingStatus { invalid, valid,
    /// unknownFutureValue }</c> — and until this task <b>the string "billingStatus" did not appear
    /// anywhere in the repository</b>, so invalid billing had no way of reaching an administrator.
    /// <para>
    /// <b>Null means NOT REPORTED and MUST NOT render as "valid"</b> (NFR-06). Consumers must also
    /// weigh this against <see cref="BillingClassification"/>: only a <c>standard</c> container type
    /// requires a billing profile in the developer tenant, so an "invalid" status is actionable there
    /// and not necessarily elsewhere (knowledge/sharepoint-embedded/docs/learn-containertypes.md:61,
    /// :79-:80).
    /// </para>
    /// <para>READ-ONLY. Attaching a billing profile is provisioning's scope, not this app's.</para>
    /// </remarks>
    [JsonPropertyName("billingStatus")]
    public string? BillingStatus { get; init; }

    /// <summary>When the container type was created (UTC), or null when Graph does not report it.</summary>
    /// <remarks>
    /// Nullable since 2026-08-24 (task 023). The mapper previously substituted
    /// <c>DateTimeOffset.UtcNow</c> when Graph omitted the value, so a container type of unknown age
    /// rendered as "created today" — a fabricated fact presented exactly like a real one.
    /// </remarks>
    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; init; }

    /// <summary>
    /// Entra application (client) ID of the owning application, or null when Graph does not return it.
    /// </summary>
    /// <remarks>
    /// SharePoint Embedded binds one owning app to one container type, permanently — so this is what
    /// identifies a container type to an administrator. The client has always asked for it
    /// (<c>types/spe.ts</c>) and nothing ever supplied it, which is why the grid's "Owning App" column
    /// rendered blank for every row. Added 2026-08-23 by task 030.
    /// <para>
    /// Null means <b>unknown</b>, not "none". Callers must render the difference.
    /// </para>
    /// </remarks>
    [JsonPropertyName("owningAppId")]
    public string? OwningAppId { get; init; }

    /// <summary>
    /// When a trial container type expires, or null for non-trial types and when Graph omits it.
    /// </summary>
    /// <remarks>
    /// A trial container type is valid for 30 days and is not renewable. Without this field the UI
    /// could not warn about it at all, so an administrator's first sign of the deadline was a
    /// container type that had stopped working. Added 2026-08-23 by task 030.
    /// </remarks>
    [JsonPropertyName("expiryDateTime")]
    public DateTimeOffset? ExpiryDateTime { get; init; }

    /// <summary>
    /// The container type's settings, or null when Graph did not return them.
    /// </summary>
    /// <remarks>Added by task 025 — no settings value reached the client before it.</remarks>
    [JsonPropertyName("settings")]
    public ContainerTypeSettingsDto? Settings { get; init; }
}

/// <summary>
/// Response body for PUT /api/spe/containertypes/{typeId}/settings?configId={id}.
///
/// Contains the container type resource fields echoed back from the Graph API after a successful
/// PATCH settings update. This confirms the update was applied and surfaces the current state
/// of the container type resource.
/// </summary>
public sealed record ContainerTypeSettingsResponseDto
{
    /// <summary>Container type GUID assigned by SharePoint Embedded (Graph API).</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable display name for the container type.</summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Billing classification for the container type.
    /// Typically "standard" for most container types. Null when not returned by Graph.
    /// </summary>
    [JsonPropertyName("billingClassification")]
    public string? BillingClassification { get; init; }

    /// <summary>
    /// Billing standing ("valid" / "invalid"), or null when Graph does not report it. Echoed here so a
    /// settings save returns the same billing view the list does, rather than a narrower one.
    /// </summary>
    [JsonPropertyName("billingStatus")]
    public string? BillingStatus { get; init; }

    /// <summary>When the container type was created (UTC), or null when Graph does not report it.</summary>
    /// <remarks>
    /// Nullable since 2026-08-24 (task 023). The mapper previously substituted
    /// <c>DateTimeOffset.UtcNow</c> when Graph omitted the value, so a container type of unknown age
    /// rendered as "created today" — a fabricated fact presented exactly like a real one.
    /// </remarks>
    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; init; }

    /// <summary>
    /// The settings as they stand AFTER the update — the read-back the caller needs to confirm the
    /// write actually applied, rather than trusting a 200 (spec FR-C04 constraint).
    /// </summary>
    [JsonPropertyName("settings")]
    public ContainerTypeSettingsDto? Settings { get; init; }
}

/// <summary>
/// Response envelope for the list container types endpoint (GET /api/spe/containertypes?configId={id}).
///
/// Returns all container types visible to the app registration associated with the given configId.
/// </summary>
public sealed record ContainerTypeListDto
{
    /// <summary>Container types returned from the Graph API for this config's app registration.</summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<ContainerTypeDto> Items { get; init; } = [];

    /// <summary>Total number of container types returned.</summary>
    [JsonPropertyName("count")]
    public int Count { get; init; }
}

/// <summary>
/// Response body for POST /api/spe/containertypes/{typeId}/register?configId={id}.
///
/// Confirms the container type registration was successful and surfaces the granted permissions.
/// All SharePoint REST API types are stripped at the service layer (ADR-007).
/// </summary>
public sealed record RegisterContainerTypeResponse
{
    /// <summary>The container type GUID that was registered.</summary>
    [JsonPropertyName("containerTypeId")]
    public string ContainerTypeId { get; init; } = string.Empty;

    /// <summary>
    /// The Azure AD application (client) ID of the consuming app that was granted permissions.
    /// </summary>
    [JsonPropertyName("appId")]
    public string AppId { get; init; } = string.Empty;

    /// <summary>Delegated permissions that were granted to the consuming app.</summary>
    [JsonPropertyName("delegatedPermissions")]
    public IReadOnlyList<string> DelegatedPermissions { get; init; } = [];

    /// <summary>Application permissions that were granted to the consuming app.</summary>
    [JsonPropertyName("applicationPermissions")]
    public IReadOnlyList<string> ApplicationPermissions { get; init; } = [];
}

/// <summary>
/// Request body for creating a new SharePoint Embedded container type
/// (POST /api/spe/containertypes?configId={id}).
///
/// Container types are administrative templates that define the storage classification and billing
/// model for containers. Creating a container type is a privileged, infrequent operation and is
/// audited via <see cref="Services.SpeAdmin.SpeAuditService"/>.
///
/// Graph API mapping (ADR-007 — no Graph SDK types exposed):
///   DisplayName → FileStorageContainerType.Name
///   BillingClassification → FileStorageContainerType.BillingClassification (typed enum)
///   Description is not supported by the Graph API containerType resource.
/// </summary>
public sealed record CreateContainerTypeRequest
{
    /// <summary>
    /// Required. Human-readable display name for the container type.
    /// Maps to the Graph API <c>Name</c> property (not <c>DisplayName</c>) on FileStorageContainerType.
    /// Must not be null or whitespace.
    /// </summary>
    [Required]
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Optional billing classification for the container type.
    /// Accepted values: <c>standard</c>, <c>trial</c>, <c>directToCustomer</c>.
    /// When null or omitted, Graph defaults to <c>standard</c>.
    /// </summary>
    /// <remarks>
    /// The doc here previously said <c>"standard", "premium"</c>. Graph has no "premium"
    /// classification; its enum is standard · trial · directToCustomer (beta CSDL). The choice is
    /// <b>permanent</b> — a trial type can never be converted to standard, nor standard to
    /// passthrough. Corrected UAT 2026-08-28.
    /// </remarks>
    [JsonPropertyName("billingClassification")]
    public string? BillingClassification { get; init; }

    /// <summary>
    /// Optional application (client) id of the Entra app registration that will OWN this container
    /// type. When omitted, the owning app registered on the config is used, falling back to the
    /// config's own client id.
    /// </summary>
    /// <remarks>
    /// Graph REQUIRES <c>owningAppId</c> on create (<c>Nullable="false"</c> in the beta CSDL) and
    /// rejects the request with an opaque "One of the provided arguments is not acceptable" when it
    /// is absent — the UAT 2026-08-28 failure. Supply this only to point the new container type at a
    /// DIFFERENT app than the config's; one app registration may own several container types, so a
    /// new type does not require a new registration.
    /// </remarks>
    [JsonPropertyName("owningAppId")]
    public string? OwningAppId { get; init; }
}
