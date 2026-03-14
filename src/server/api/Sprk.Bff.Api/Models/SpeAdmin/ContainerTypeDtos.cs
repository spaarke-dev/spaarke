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
