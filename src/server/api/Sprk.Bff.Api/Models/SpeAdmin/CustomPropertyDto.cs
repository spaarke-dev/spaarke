namespace Sprk.Bff.Api.Models.SpeAdmin;

/// <summary>
/// Represents a single custom property on an SPE container.
/// Custom properties are key-value pairs that provide administrator-controlled metadata
/// on a container. When <see cref="IsSearchable"/> is <c>true</c>, the property value
/// is indexed and can be used in SharePoint Embedded search queries.
/// </summary>
/// <param name="Name">The property key name. Must be non-empty.</param>
/// <param name="Value">The property value string.</param>
/// <param name="IsSearchable">
/// When <c>true</c>, the property is indexed for search. Defaults to <c>false</c>.
/// </param>
/// <remarks>
/// ADR-007: No Graph SDK types are exposed — callers receive only this domain DTO.
/// Graph custom properties are stored as <c>Dictionary&lt;string, FileStorageContainerCustomPropertyValue&gt;</c>
/// on the container. This DTO flattens that structure into a simple key-value-searchable record.
/// </remarks>
public sealed record CustomPropertyDto(
    string Name,
    string Value,
    bool IsSearchable);

/// <summary>
/// Request body for PUT /api/spe/containers/{id}/customproperties.
/// Replaces all custom properties on the container with the supplied list.
/// </summary>
/// <param name="Properties">
/// The complete set of custom properties to apply to the container.
/// Must not be null. An empty list clears all existing custom properties.
/// </param>
public sealed record UpdateCustomPropertiesRequest(
    IReadOnlyList<CustomPropertyDto> Properties);
