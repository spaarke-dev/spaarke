namespace Sprk.Bff.Api.Api.FieldMappings.Dtos;

/// <summary>
/// Response DTO for field mapping type compatibility validation.
/// Returned by POST /api/v1/field-mappings/validate endpoint.
/// </summary>
public record ValidateMappingResponse
{
    /// <summary>
    /// Whether the source type can be mapped to the target type.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Error messages if validation failed.
    /// Empty array if valid.
    /// </summary>
    public string[] Errors { get; init; } = [];

    /// <summary>
    /// Warning messages for valid but potentially lossy conversions.
    /// </summary>
    public string[] Warnings { get; init; } = [];

    /// <summary>
    /// Suggested compatible target types for the given source type.
    /// Useful for UI to show valid options when validation fails.
    /// </summary>
    public string[] CompatibleTargetTypes { get; init; } = [];

    /// <summary>
    /// Compatibility level: "exact", "safe_conversion", or "incompatible".
    /// </summary>
    public string CompatibilityLevel { get; init; } = string.Empty;
}
