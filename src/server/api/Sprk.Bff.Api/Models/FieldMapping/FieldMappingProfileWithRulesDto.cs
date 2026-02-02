namespace Sprk.Bff.Api.Models.FieldMapping;

/// <summary>
/// DTO representing a field mapping profile with all its rules included.
/// Used for retrieving a complete profile configuration in a single request.
/// </summary>
/// <remarks>
/// This is the primary DTO for the GET /api/v1/field-mappings/profiles/{sourceEntity}/{targetEntity} endpoint.
/// PCF controls use this to get the full mapping configuration when a record is selected.
/// </remarks>
public record FieldMappingProfileWithRulesDto
{
    /// <summary>
    /// Unique identifier of the field mapping profile.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Display name of the profile.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Logical name of the source entity (e.g., "account", "contact", "sprk_matter").
    /// </summary>
    public string SourceEntity { get; init; } = string.Empty;

    /// <summary>
    /// Logical name of the target entity (e.g., "sprk_event").
    /// </summary>
    public string TargetEntity { get; init; } = string.Empty;

    /// <summary>
    /// Synchronization mode: OneTime, ManualRefresh, or UpdateRelated.
    /// </summary>
    public string SyncMode { get; init; } = string.Empty;

    /// <summary>
    /// Whether this profile is currently active.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Collection of field mapping rules associated with this profile.
    /// Rules define how individual fields are mapped from source to target.
    /// </summary>
    public FieldMappingRuleDto[] Rules { get; init; } = [];
}
