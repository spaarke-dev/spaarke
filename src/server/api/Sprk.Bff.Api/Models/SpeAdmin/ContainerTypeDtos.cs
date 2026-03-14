using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.SpeAdmin;

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
    /// Billing classification for the container type.
    /// Typically "standard" for most container types.
    /// </summary>
    [JsonPropertyName("billingClassification")]
    public string? BillingClassification { get; init; }

    /// <summary>When the container type was created (UTC).</summary>
    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset CreatedDateTime { get; init; }
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

    /// <summary>When the container type was created (UTC).</summary>
    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset CreatedDateTime { get; init; }
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
    /// Accepted values: "standard", "premium".
    /// When null or omitted, Graph API defaults to "standard".
    /// </summary>
    [JsonPropertyName("billingClassification")]
    public string? BillingClassification { get; init; }
}
