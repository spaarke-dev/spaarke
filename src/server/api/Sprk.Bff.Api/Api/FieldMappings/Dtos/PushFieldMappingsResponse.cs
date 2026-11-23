namespace Sprk.Bff.Api.Api.FieldMappings.Dtos;

/// <summary>
/// Response DTO for the push field mappings operation.
/// Returned by POST /api/v1/field-mappings/push endpoint.
/// </summary>
/// <remarks>
/// Provides detailed results of the push operation including counts of successfully updated,
/// failed, and skipped records. The operation continues on partial failures, allowing
/// callers to see which records succeeded and which failed.
/// </remarks>
public record PushFieldMappingsResponse
{
    /// <summary>
    /// Overall success indicator. True if at least one record was updated successfully
    /// and no critical errors occurred.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Logical name of the target entity that records were pushed to.
    /// </summary>
    public string TargetEntity { get; init; } = string.Empty;

    /// <summary>
    /// Total number of child records found to update.
    /// </summary>
    public int TotalRecords { get; init; }

    /// <summary>
    /// Number of records successfully updated.
    /// </summary>
    public int UpdatedCount { get; init; }

    /// <summary>
    /// Number of records that failed to update.
    /// </summary>
    public int FailedCount { get; init; }

    /// <summary>
    /// Number of records skipped (e.g., no changes needed).
    /// </summary>
    public int SkippedCount { get; init; }

    /// <summary>
    /// List of errors encountered during the operation.
    /// Each entry includes the record ID and error message.
    /// </summary>
    /// <remarks>Task 022 fix round 4: on a preview, the children the apply would not write under the mapping-source rule, each
    /// with its <see cref="PushFieldMappingsError.ReasonCode"/>; their values are not listed in <see cref="Changes"/>.</remarks>
    public PushFieldMappingsError[] Errors { get; init; } = [];

    /// <summary>
    /// Warning messages for non-fatal issues during processing.
    /// </summary>
    public string[] Warnings { get; init; } = [];

    /// <summary>
    /// Detailed results for each field mapping rule applied.
    /// Only populated when detailed results are requested.
    /// </summary>
    /// <remarks>Task 022c (unified-access-control-r2, 2026-11-12): a rule whose target is an access column (<c>sprk_issecure</c>,
    /// <c>sprk_accesspermission</c>, <c>sprk_accessinheritance</c>, <c>sprk_securitybu</c>, <c>sprk_containerid</c>) is never
    /// applied, on any table: its entry is <see cref="FieldMappingStatus.Skipped"/> and its
    /// <see cref="FieldMappingResultDto.ErrorMessage"/> carries the code <c>field_mapping.push.access_column_not_mapped</c>. A
    /// preview does not list the column in <see cref="Changes"/>.</remarks>
    public FieldMappingResultDto[] FieldResults { get; init; } = [];

    /// <summary>
    /// Task 040 (FR-34): true when the request asked for a preview and nothing was written. Absent from an ordinary push.
    /// </summary>
    public bool? Preview { get; init; }

    /// <summary>
    /// Task 040 (FR-34), preview only: one entry per child and column whose value would change. A child whose value is
    /// already the source's is not listed.
    /// </summary>
    public PushPreviewChange[]? Changes { get; init; }

    /// <summary>
    /// Task 040 (FR-34), preview only: how many candidate children were left out because the caller lacks Write on them.
    /// </summary>
    public int? SkippedNoWriteCount { get; init; }

    /// <summary>
    /// Task 040 (FR-34), preview only: true when more children exist than one push handles, so the list is partial.
    /// </summary>
    public bool? Truncated { get; init; }

    /// <summary>
    /// Task 040 (FR-23): how many of the profile's rules a push did not apply (Default, Concat, Template, or not set to
    /// Manual Refresh). Skipped and counted, never an error.
    /// </summary>
    public int? SkippedRuleCount { get; init; }

    /// <summary>
    /// Task 040 (FR-34), preview only: the confirmed ids (<c>childIds</c>) the server did not derive as children of the source
    /// for the caller. They are not previewed, and an apply refuses them per id; the client says they will not be updated.
    /// </summary>
    public Guid[]? RefusedChildIds { get; init; }
}

/// <summary>
/// One value a push would change (task 040, FR-34 preview): the child, the column and what it holds now and would hold.
/// </summary>
public record PushPreviewChange
{
    /// <summary>The child record.</summary>
    public Guid RecordId { get; init; }

    /// <summary>The child's display name, or null when the caller cannot read it.</summary>
    public string? Name { get; init; }

    /// <summary>The target column's logical name.</summary>
    public string Column { get; init; } = string.Empty;

    /// <summary>What the column holds now, as text; null when empty.</summary>
    public string? From { get; init; }

    /// <summary>What the column would hold, as text; null when it would be cleared.</summary>
    public string? To { get; init; }

    /// <summary>
    /// The Assigned-To policies the change meets (UAC-r2 4a): "secure" (a suggestion, no access written), "restricted" (no
    /// contact grant) and "limited" (no organization grant). Empty for an ordinary column.
    /// </summary>
    public string[] Flags { get; init; } = [];
}

/// <summary>
/// Error details for a failed record update during push operation.
/// </summary>
public record PushFieldMappingsError
{
    /// <summary>
    /// GUID of the record that failed to update.
    /// </summary>
    public Guid RecordId { get; init; }

    /// <summary>
    /// Human-readable error message describing the failure.
    /// </summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>
    /// Task 022 fix round 4: the stable code of a child the push does not write because of the mapping-source rule
    /// (<c>FieldMappingPushSourceGate</c>), one of <c>field_mapping.push.secure_source_not_isolated</c>,
    /// <c>field_mapping.push.secure_source_undetermined</c> and <c>field_mapping.push.secure_source_refile_not_allowed</c>
    /// (a secure source's push would change what the child is filed under, UAC-r2 (a)), and, task 022c,
    /// <c>field_mapping.push.restricted_source_not_restricted</c> (the source is Restricted and the child is not); null for every
    /// other entry. Carried on the apply and on the preview.
    /// </summary>
    public string? ReasonCode { get; init; }
}

/// <summary>
/// Result of applying a single field mapping rule to a record.
/// </summary>
public record FieldMappingResultDto
{
    /// <summary>
    /// Logical name of the source field that was read.
    /// </summary>
    public string SourceField { get; init; } = string.Empty;

    /// <summary>
    /// Logical name of the target field that was written.
    /// </summary>
    public string TargetField { get; init; } = string.Empty;

    /// <summary>
    /// Status of the mapping operation: "Mapped", "Skipped", or "Error".
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Error message if the mapping failed, null otherwise. On a skipped rule, why it was skipped; a stable code, where the skip
    /// has one, is in parentheses (task 022c: <c>field_mapping.push.access_column_not_mapped</c>).
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Status values for field mapping results.
/// </summary>
public static class FieldMappingStatus
{
    /// <summary>
    /// Field was successfully mapped from source to target.
    /// </summary>
    public const string Mapped = "Mapped";

    /// <summary>
    /// Field was skipped (e.g., source value was null and not required).
    /// </summary>
    public const string Skipped = "Skipped";

    /// <summary>
    /// Field mapping failed due to an error.
    /// </summary>
    public const string Error = "Error";
}
