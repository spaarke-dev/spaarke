namespace Sprk.Bff.Api.Api.FieldMappings.Dtos;

/// <summary>
/// Request DTO for pushing field mappings from a parent record to all related child records.
/// Used by POST /api/v1/field-mappings/push endpoint.
/// </summary>
/// <remarks>
/// This endpoint supports the "Update Related" sync mode where changes to a parent record
/// can be pushed to all associated child records based on configured field mapping profiles.
/// </remarks>
public record PushFieldMappingsRequest
{
    /// <summary>
    /// Logical name of the source (parent) entity (e.g., "sprk_matter", "account").
    /// </summary>
    /// <example>sprk_matter</example>
    public string SourceEntity { get; init; } = string.Empty;

    /// <summary>
    /// GUID of the source (parent) record from which to copy field values.
    /// </summary>
    public Guid SourceRecordId { get; init; }

    /// <summary>
    /// Logical name of the target (child) entity (e.g., "sprk_event").
    /// </summary>
    /// <example>sprk_event</example>
    public string TargetEntity { get; init; } = string.Empty;
}
