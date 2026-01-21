using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Office;

/// <summary>
/// Response model for Quick Create entity operations.
/// Corresponds to POST /office/quickcreate/{entityType} response.
/// </summary>
/// <remarks>
/// <para>
/// Returns the created entity's ID, type, and name for immediate use
/// in the Office add-in's association picker.
/// </para>
/// </remarks>
public record QuickCreateResponse
{
    /// <summary>
    /// ID of the created entity (Dataverse record ID).
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Type of entity created.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required QuickCreateEntityType EntityType { get; init; }

    /// <summary>
    /// Logical name of the entity in Dataverse (e.g., "sprk_matter", "account").
    /// </summary>
    public required string LogicalName { get; init; }

    /// <summary>
    /// Display name of the created entity.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// URL to the entity in Dataverse (for direct navigation).
    /// </summary>
    /// <remarks>
    /// Format: https://{org}.crm.dynamics.com/main.aspx?etn={logicalname}&id={id}&pagetype=entityrecord
    /// </remarks>
    public string? Url { get; init; }
}
