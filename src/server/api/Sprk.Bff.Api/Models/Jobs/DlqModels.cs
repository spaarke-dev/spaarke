namespace Sprk.Bff.Api.Models.Jobs;

/// <summary>
/// Summary of DLQ status for a queue.
/// </summary>
public class DlqSummary
{
    /// <summary>
    /// Name of the source queue.
    /// </summary>
    public required string QueueName { get; init; }

    /// <summary>
    /// Number of messages in the DLQ.
    /// </summary>
    public long MessageCount { get; init; }

    /// <summary>
    /// Size of the DLQ in bytes.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Timestamp of oldest message in DLQ.
    /// </summary>
    public DateTime? OldestMessageTimestamp { get; init; }

    /// <summary>
    /// Breakdown by dead-letter reason.
    /// </summary>
    public Dictionary<string, int> ReasonCounts { get; init; } = [];
}

/// <summary>
/// A single dead-lettered message.
/// </summary>
public class DlqMessage
{
    /// <summary>
    /// Service Bus message ID.
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// Service Bus sequence number (unique within queue).
    /// </summary>
    public long SequenceNumber { get; init; }

    /// <summary>
    /// When the message was enqueued to DLQ.
    /// </summary>
    public DateTime EnqueuedTime { get; init; }

    /// <summary>
    /// Reason for dead-lettering.
    /// </summary>
    public string? DeadLetterReason { get; init; }

    /// <summary>
    /// Detailed error description.
    /// </summary>
    public string? DeadLetterErrorDescription { get; init; }

    /// <summary>
    /// Number of delivery attempts before dead-lettering.
    /// </summary>
    public int DeliveryCount { get; init; }

    /// <summary>
    /// The job contract if parseable.
    /// </summary>
    public DlqJobInfo? Job { get; init; }

    /// <summary>
    /// Raw message body (if job parsing failed).
    /// </summary>
    public string? RawBody { get; init; }

    /// <summary>
    /// Custom properties on the message.
    /// </summary>
    public Dictionary<string, object?> Properties { get; init; } = [];
}

/// <summary>
/// Job information extracted from a DLQ message.
/// </summary>
public class DlqJobInfo
{
    /// <summary>
    /// The job ID.
    /// </summary>
    public Guid JobId { get; init; }

    /// <summary>
    /// Job type (handler name).
    /// </summary>
    public string? JobType { get; init; }

    /// <summary>
    /// Subject ID (e.g., email ID, document ID).
    /// </summary>
    public string? SubjectId { get; init; }

    /// <summary>
    /// Correlation ID for tracing.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Idempotency key.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>
    /// Attempt number when dead-lettered.
    /// </summary>
    public int Attempt { get; init; }

    /// <summary>
    /// Max attempts configured.
    /// </summary>
    public int MaxAttempts { get; init; }
}

/// <summary>
/// Response from listing DLQ messages.
/// </summary>
public class DlqListResponse
{
    /// <summary>
    /// Summary of DLQ status.
    /// </summary>
    public required DlqSummary Summary { get; init; }

    /// <summary>
    /// Messages in the current page.
    /// </summary>
    public List<DlqMessage> Messages { get; init; } = [];

    /// <summary>
    /// Total number of messages in DLQ.
    /// </summary>
    public long TotalCount { get; init; }

    /// <summary>
    /// Whether there are more messages to fetch.
    /// </summary>
    public bool HasMore { get; init; }
}

/// <summary>
/// Request to re-drive DLQ messages.
/// </summary>
public class RedriveRequest
{
    /// <summary>
    /// Sequence numbers of messages to re-drive.
    /// If empty, re-drives all messages (use with caution).
    /// </summary>
    public List<long>? SequenceNumbers { get; init; }

    /// <summary>
    /// Maximum number of messages to re-drive (safety limit).
    /// Default: 100
    /// </summary>
    public int MaxMessages { get; init; } = 100;

    /// <summary>
    /// Optional: Filter by dead-letter reason.
    /// Only re-drive messages with this reason.
    /// </summary>
    public string? ReasonFilter { get; init; }
}

/// <summary>
/// Response from re-drive operation.
/// </summary>
public class RedriveResponse
{
    /// <summary>
    /// Number of messages successfully re-driven.
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Number of messages that failed to re-drive.
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// Error details for failed re-drives.
    /// </summary>
    public List<RedriveError> Errors { get; set; } = [];

    /// <summary>
    /// Human-readable message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Error detail for a failed re-drive.
/// </summary>
public class RedriveError
{
    /// <summary>
    /// Sequence number of the message that failed.
    /// </summary>
    public long SequenceNumber { get; init; }

    /// <summary>
    /// Error message.
    /// </summary>
    public required string Error { get; init; }
}
