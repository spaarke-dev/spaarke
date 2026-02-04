namespace Sprk.Bff.Api.Models.FieldMapping;

/// <summary>
/// DTO representing a single field mapping rule from Dataverse.
/// Defines how a source field maps to a target field within a profile.
/// </summary>
/// <remarks>
/// Used by PCF controls to apply field mappings when a record is selected.
/// Rules are returned as part of FieldMappingProfileWithRulesDto.
/// </remarks>
public record FieldMappingRuleDto
{
    /// <summary>
    /// Unique identifier of the field mapping rule.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Logical name of the source field to copy from (e.g., "name", "sprk_clientid").
    /// </summary>
    public string SourceField { get; init; } = string.Empty;

    /// <summary>
    /// Logical name of the target field to copy to (e.g., "sprk_name", "sprk_relatedclient").
    /// </summary>
    public string TargetField { get; init; } = string.Empty;

    /// <summary>
    /// Data type of the source field (e.g., "Text", "Lookup", "OptionSet").
    /// </summary>
    public string SourceFieldType { get; init; } = string.Empty;

    /// <summary>
    /// Data type of the target field (e.g., "Text", "Lookup", "OptionSet").
    /// </summary>
    public string TargetFieldType { get; init; } = string.Empty;

    /// <summary>
    /// Priority order for applying this rule (lower number = higher priority).
    /// Used when multiple rules could affect the same target field.
    /// </summary>
    public int Priority { get; init; }
}
