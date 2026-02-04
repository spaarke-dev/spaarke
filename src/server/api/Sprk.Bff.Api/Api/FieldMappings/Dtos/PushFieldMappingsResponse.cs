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
    public PushFieldMappingsError[] Errors { get; init; } = [];

    /// <summary>
    /// Warning messages for non-fatal issues during processing.
    /// </summary>
    public string[] Warnings { get; init; } = [];

    /// <summary>
    /// Detailed results for each field mapping rule applied.
    /// Only populated when detailed results are requested.
    /// </summary>
    public FieldMappingResultDto[] FieldResults { get; init; } = [];
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
    /// Error message if the mapping failed, null otherwise.
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
