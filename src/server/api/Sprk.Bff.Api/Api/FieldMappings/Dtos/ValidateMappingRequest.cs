namespace Sprk.Bff.Api.Api.FieldMappings.Dtos;

/// <summary>
/// Request DTO for validating type compatibility between source and target field types.
/// Used by POST /api/v1/field-mappings/validate endpoint.
/// </summary>
public record ValidateMappingRequest
{
    /// <summary>
    /// Source field type (e.g., "Lookup", "Text", "Number", "DateTime", "Boolean", "OptionSet", "Memo").
    /// </summary>
    public string SourceFieldType { get; init; } = string.Empty;

    /// <summary>
    /// Target field type (e.g., "Lookup", "Text", "Number", "DateTime", "Boolean", "OptionSet", "Memo").
    /// </summary>
    public string TargetFieldType { get; init; } = string.Empty;

    /// <summary>
    /// Optional source field schema name for detailed error messages.
    /// </summary>
    public string? SourceField { get; init; }

    /// <summary>
    /// Optional target field schema name for detailed error messages.
    /// </summary>
    public string? TargetField { get; init; }
}
