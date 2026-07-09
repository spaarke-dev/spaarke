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

    /// <summary>
    /// Mapping type: "Copy" (verbatim), "Default" (literal from DefaultValue),
    /// "Concat" / "Template" (format string from Expression). Discriminates which engine applies the rule.
    /// </summary>
    public string MappingType { get; init; } = "Copy";

    /// <summary>
    /// Literal value used by the Default mapping type when the source is empty (from sprk_defaultvalue). Nullable.
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// Format string used by the Concat/Template mapping types (from sprk_expression). Nullable; unused for Copy/Default.
    /// </summary>
    public string? Expression { get; init; }

    /// <summary>
    /// When true, the mapping fails (or warns) if the resolved source value is empty.
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    /// Compatibility mode for type coercion: "Strict" (0) or "Resolve" (1).
    /// </summary>
    public string CompatibilityMode { get; init; } = "Strict";
}
