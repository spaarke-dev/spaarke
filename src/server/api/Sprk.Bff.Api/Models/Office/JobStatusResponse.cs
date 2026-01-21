using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Office;

/// <summary>
/// Response model for job status queries.
/// Corresponds to GET /office/jobs/{id} response.
/// </summary>
public record JobStatusResponse
{
    /// <summary>
    /// Processing job ID.
    /// </summary>
    public required Guid JobId { get; init; }

    /// <summary>
    /// Current job status.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required JobStatus Status { get; init; }

    /// <summary>
    /// Type of job being processed.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required JobType JobType { get; init; }

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    public int Progress { get; init; }

    /// <summary>
    /// Current processing phase.
    /// </summary>
    public string? CurrentPhase { get; init; }

    /// <summary>
    /// Phases that have been completed.
    /// </summary>
    public List<CompletedPhase>? CompletedPhases { get; init; }

    /// <summary>
    /// When the job was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// User ID who created the job (for ownership verification).
    /// </summary>
    public string? CreatedBy { get; init; }

    /// <summary>
    /// When processing started.
    /// </summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>
    /// When processing completed.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// Result information (when completed).
    /// </summary>
    public JobResult? Result { get; init; }

    /// <summary>
    /// Error information (when failed).
    /// </summary>
    public JobError? Error { get; init; }
}

/// <summary>
/// Job processing status values.
/// </summary>
public enum JobStatus
{
    /// <summary>
    /// Job is queued and waiting to be processed.
    /// </summary>
    Queued,

    /// <summary>
    /// Job is currently being processed.
    /// </summary>
    Running,

    /// <summary>
    /// Job completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Job failed with an error.
    /// </summary>
    Failed,

    /// <summary>
    /// Job was cancelled.
    /// </summary>
    Cancelled
}

/// <summary>
/// Types of processing jobs.
/// </summary>
public enum JobType
{
    /// <summary>
    /// Email save and processing.
    /// </summary>
    EmailSave,

    /// <summary>
    /// Attachment save and processing.
    /// </summary>
    AttachmentSave,

    /// <summary>
    /// Document save and processing.
    /// </summary>
    DocumentSave,

    /// <summary>
    /// AI metadata extraction.
    /// </summary>
    AiProcessing,

    /// <summary>
    /// Search indexing.
    /// </summary>
    Indexing
}

/// <summary>
/// Information about a completed processing phase.
/// </summary>
public record CompletedPhase
{
    /// <summary>
    /// Phase name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// When the phase completed.
    /// </summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>
    /// Duration of the phase in milliseconds.
    /// </summary>
    public long DurationMs { get; init; }
}

/// <summary>
/// Job result information.
/// </summary>
public record JobResult
{
    /// <summary>
    /// Created artifact information.
    /// </summary>
    public CreatedArtifact? Artifact { get; init; }

    /// <summary>
    /// AI-extracted metadata (if AI processing was enabled).
    /// </summary>
    public ExtractedMetadata? ExtractedMetadata { get; init; }
}

/// <summary>
/// AI-extracted metadata from document processing.
/// </summary>
public record ExtractedMetadata
{
    /// <summary>
    /// AI-generated summary.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Extracted topics.
    /// </summary>
    public List<string>? Topics { get; init; }

    /// <summary>
    /// Extracted entities (people, organizations, etc.).
    /// </summary>
    public List<ExtractedEntity>? Entities { get; init; }
}

/// <summary>
/// Extracted named entity.
/// </summary>
public record ExtractedEntity
{
    /// <summary>
    /// Entity type (Person, Organization, Location, etc.).
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Entity value.
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// Confidence score (0-1).
    /// </summary>
    public double? Confidence { get; init; }
}

/// <summary>
/// Job error information.
/// </summary>
public record JobError
{
    /// <summary>
    /// Error code for programmatic handling.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Additional error details.
    /// </summary>
    public string? Details { get; init; }

    /// <summary>
    /// Whether the job can be retried.
    /// </summary>
    public bool Retryable { get; init; }

    /// <summary>
    /// Number of retry attempts made.
    /// </summary>
    public int RetryCount { get; init; }
}
