using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Models.Email;

/// <summary>
/// Request to batch process historical emails.
/// Used by POST /api/v1/emails/admin/batch-process
/// </summary>
public class BatchProcessEmailsRequest
{
    /// <summary>
    /// Start of date range for emails to process (inclusive).
    /// Uses email received/sent date.
    /// </summary>
    [Required]
    public DateTime StartDate { get; set; }

    /// <summary>
    /// End of date range for emails to process (inclusive).
    /// Uses email received/sent date.
    /// </summary>
    [Required]
    public DateTime EndDate { get; set; }

    /// <summary>
    /// SPE container ID where documents will be stored.
    /// If not specified, uses the default container from configuration.
    /// </summary>
    public string? ContainerId { get; set; }

    /// <summary>
    /// Whether to include attachments embedded in the .eml files.
    /// Default: true
    /// </summary>
    public bool IncludeAttachments { get; set; } = true;

    /// <summary>
    /// Whether to create separate document records for each attachment.
    /// Default: true
    /// </summary>
    public bool CreateAttachmentDocuments { get; set; } = true;

    /// <summary>
    /// Whether to queue the documents for AI processing.
    /// Default: false (batch mode typically doesn't need AI)
    /// </summary>
    public bool QueueForAiProcessing { get; set; } = false;

    /// <summary>
    /// Filter: Only process emails with direction matching this value.
    /// Null means process both incoming and outgoing.
    /// </summary>
    public EmailDirection? DirectionFilter { get; set; }

    /// <summary>
    /// Filter: Only process emails with status matching this value.
    /// Default: Completed (only process completed/sent emails)
    /// </summary>
    public EmailStatusFilter StatusFilter { get; set; } = EmailStatusFilter.Completed;

    /// <summary>
    /// Filter: Only process emails that have not already been converted.
    /// Default: true (skip duplicates)
    /// </summary>
    public bool SkipAlreadyConverted { get; set; } = true;

    /// <summary>
    /// Maximum number of emails to process in this batch.
    /// Default: 1000, Max: 10000
    /// </summary>
    [Range(1, 10000)]
    public int MaxEmails { get; set; } = 1000;

    /// <summary>
    /// Optional: Filter by specific mailbox/queue.
    /// If null, processes from all mailboxes.
    /// </summary>
    public string? MailboxFilter { get; set; }

    /// <summary>
    /// Optional: Filter by sender domain (e.g., "example.com").
    /// </summary>
    public string? SenderDomainFilter { get; set; }

    /// <summary>
    /// Optional: Subject contains filter (case-insensitive).
    /// </summary>
    public string? SubjectContainsFilter { get; set; }

    /// <summary>
    /// Processing priority (lower = higher priority).
    /// Default: 5 (normal)
    /// </summary>
    [Range(1, 10)]
    public int Priority { get; set; } = 5;
}

/// <summary>
/// Response from batch process initiation.
/// Returns 202 Accepted with job tracking information.
/// </summary>
public class BatchProcessEmailsResponse
{
    /// <summary>
    /// Unique identifier for tracking the batch job.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Correlation ID for tracing.
    /// </summary>
    public required string CorrelationId { get; init; }

    /// <summary>
    /// URL to check job status.
    /// </summary>
    public required string StatusUrl { get; init; }

    /// <summary>
    /// Human-readable message.
    /// </summary>
    public string Message { get; init; } = "Batch processing job submitted";

    /// <summary>
    /// Estimated number of emails to process.
    /// </summary>
    public int EstimatedEmailCount { get; init; }

    /// <summary>
    /// Applied filters for the batch.
    /// </summary>
    public BatchFiltersApplied Filters { get; init; } = new();

    /// <summary>
    /// When the job was submitted.
    /// </summary>
    public DateTime SubmittedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Summary of filters applied to batch processing.
/// </summary>
public class BatchFiltersApplied
{
    /// <summary>
    /// Date range start.
    /// </summary>
    public DateTime StartDate { get; init; }

    /// <summary>
    /// Date range end.
    /// </summary>
    public DateTime EndDate { get; init; }

    /// <summary>
    /// Direction filter applied.
    /// </summary>
    public string? DirectionFilter { get; init; }

    /// <summary>
    /// Status filter applied.
    /// </summary>
    public string StatusFilter { get; init; } = "Completed";

    /// <summary>
    /// Whether duplicates are skipped.
    /// </summary>
    public bool SkipAlreadyConverted { get; init; }

    /// <summary>
    /// Maximum emails to process.
    /// </summary>
    public int MaxEmails { get; init; }

    /// <summary>
    /// Mailbox filter if specified.
    /// </summary>
    public string? MailboxFilter { get; init; }

    /// <summary>
    /// Sender domain filter if specified.
    /// </summary>
    public string? SenderDomainFilter { get; init; }

    /// <summary>
    /// Subject contains filter if specified.
    /// </summary>
    public string? SubjectContainsFilter { get; init; }
}

/// <summary>
/// Email direction for filtering.
/// </summary>
public enum EmailDirection
{
    /// <summary>
    /// Incoming emails (received).
    /// </summary>
    Incoming = 100000000,

    /// <summary>
    /// Outgoing emails (sent).
    /// </summary>
    Outgoing = 100000001
}

/// <summary>
/// Email status filter for batch processing.
/// </summary>
public enum EmailStatusFilter
{
    /// <summary>
    /// Only completed/sent emails (default for batch).
    /// </summary>
    Completed,

    /// <summary>
    /// All statuses (draft, pending, completed).
    /// </summary>
    All,

    /// <summary>
    /// Only draft emails.
    /// </summary>
    Draft
}

/// <summary>
/// Response for batch job status query.
/// Used by GET /api/v1/emails/admin/batch-process/{jobId}/status
/// </summary>
public class BatchJobStatusResponse
{
    /// <summary>
    /// The job ID being queried.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Current status of the batch job.
    /// </summary>
    public required BatchJobState Status { get; init; }

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    public int ProgressPercent { get; init; }

    /// <summary>
    /// Total number of emails to process.
    /// </summary>
    public int TotalEmails { get; init; }

    /// <summary>
    /// Number of emails processed successfully.
    /// </summary>
    public int ProcessedCount { get; init; }

    /// <summary>
    /// Number of emails that failed processing.
    /// </summary>
    public int ErrorCount { get; init; }

    /// <summary>
    /// Number of emails skipped (e.g., already converted).
    /// </summary>
    public int SkippedCount { get; init; }

    /// <summary>
    /// When the job was submitted.
    /// </summary>
    public DateTime SubmittedAt { get; init; }

    /// <summary>
    /// When processing started.
    /// </summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>
    /// When processing completed (success or failure).
    /// </summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>
    /// Estimated time remaining (only when in progress).
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; init; }

    /// <summary>
    /// Average processing time per email (for estimation).
    /// </summary>
    public TimeSpan? AverageProcessingTime { get; init; }

    /// <summary>
    /// Sample error messages (up to 5).
    /// </summary>
    public List<BatchJobError> RecentErrors { get; init; } = [];

    /// <summary>
    /// Human-readable message about current status.
    /// </summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// State of a batch processing job.
/// </summary>
public enum BatchJobState
{
    /// <summary>
    /// Job is queued but not yet started.
    /// </summary>
    Pending,

    /// <summary>
    /// Job is currently processing emails.
    /// </summary>
    InProgress,

    /// <summary>
    /// All emails processed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Job completed but some emails failed.
    /// </summary>
    PartiallyCompleted,

    /// <summary>
    /// Job failed completely.
    /// </summary>
    Failed,

    /// <summary>
    /// Job was cancelled.
    /// </summary>
    Cancelled
}

/// <summary>
/// Error detail for batch processing.
/// </summary>
public class BatchJobError
{
    /// <summary>
    /// Email ID that failed.
    /// </summary>
    public string? EmailId { get; init; }

    /// <summary>
    /// Error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// When the error occurred.
    /// </summary>
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Internal storage model for batch job status (stored in Redis).
/// </summary>
public class BatchJobStatusRecord
{
    /// <summary>
    /// The job ID.
    /// </summary>
    public required string JobId { get; set; }

    /// <summary>
    /// Current state.
    /// </summary>
    public BatchJobState Status { get; set; } = BatchJobState.Pending;

    /// <summary>
    /// Total emails to process.
    /// </summary>
    public int TotalEmails { get; set; }

    /// <summary>
    /// Emails processed successfully.
    /// </summary>
    public int ProcessedCount { get; set; }

    /// <summary>
    /// Emails that failed.
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// Emails skipped.
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// When the job was submitted.
    /// </summary>
    public DateTime SubmittedAt { get; set; }

    /// <summary>
    /// When processing started.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// When processing completed.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Recent errors (max 10).
    /// </summary>
    public List<BatchJobError> Errors { get; set; } = [];

    /// <summary>
    /// Request parameters (for reference).
    /// </summary>
    public BatchFiltersApplied? Filters { get; set; }
}
