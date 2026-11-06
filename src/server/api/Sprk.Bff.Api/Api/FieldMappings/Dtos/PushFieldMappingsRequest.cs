namespace Sprk.Bff.Api.Api.FieldMappings.Dtos;

/// <summary>
/// Request DTO for pushing field mappings from a parent record to all related child records.
/// Used by POST /api/v1/field-mappings/push endpoint.
/// </summary>
/// <remarks>
/// This endpoint supports the "Update Related" sync mode where changes to a parent record
/// can be pushed to all associated child records based on configured field mapping profiles.
/// <para>Task 040 (FR-34) adds four OPTIONAL members; a request that sends none of them behaves as before (apart from the
/// rules it applies, FR-23): <see cref="Preview"/>, <see cref="IncludeAssignedTo"/>, <see cref="ChildIds"/> and
/// <see cref="Kind"/>.</para>
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

    /// <summary>
    /// When true nothing is written: the response lists, per child, the values that would change (FR-34). False when absent.
    /// </summary>
    public bool Preview { get; init; }

    /// <summary>
    /// When true the rules that fill the target's Assigned-To columns are pushed too, and the caller needs Write on the SOURCE
    /// (FR-34). False when absent, so an ordinary push leaves those columns, and the access they confer, alone.
    /// </summary>
    public bool IncludeAssignedTo { get; init; }

    /// <summary>
    /// The confirmed children (FR-34). When present the server finds the children as the caller and intersects them with this
    /// list; any other id is refused per id. When absent every child the caller can read is a candidate, as before.
    /// </summary>
    public Guid[]? ChildIds { get; init; }

    /// <summary>
    /// The family kind the children are found through. Absent or "Filing" is the only supported value (ontology 3b: Push and
    /// Update Assigned To are Filing-only); anything else is refused.
    /// </summary>
    public string? Kind { get; init; }
}
