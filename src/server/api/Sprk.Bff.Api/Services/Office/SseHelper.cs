using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// Helper class for formatting Server-Sent Events (SSE) messages.
/// Implements the SSE protocol per spec.md for job status streaming.
/// </summary>
/// <remarks>
/// <para>
/// SSE format per W3C specification:
/// - event: {event-type}\n
/// - id: {event-id}\n
/// - retry: {milliseconds}\n
/// - data: {json-payload}\n
/// - \n (empty line to signal end of event)
/// </para>
/// <para>
/// Event types used:
/// - stage-update: Job phase transition
/// - progress: Progress percentage update
/// - job-complete: Job finished successfully
/// - job-failed: Job encountered an error
/// - heartbeat: Keep-alive signal (every 15 seconds per spec)
/// - error: Terminal error event (per ADR-019)
/// </para>
/// </remarks>
public static class SseHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// SSE event types for job status streaming.
    /// </summary>
    public static class EventTypes
    {
        public const string StageUpdate = "stage-update";
        public const string Progress = "progress";
        public const string JobComplete = "job-complete";
        public const string JobFailed = "job-failed";
        public const string Heartbeat = "heartbeat";
        public const string Error = "error";
        public const string Connected = "connected";
    }

    /// <summary>
    /// Formats an SSE event message.
    /// </summary>
    /// <param name="eventType">The event type (e.g., "stage-update", "heartbeat").</param>
    /// <param name="data">The data payload to serialize as JSON.</param>
    /// <param name="eventId">Optional event ID for reconnection support.</param>
    /// <param name="retry">Optional retry interval in milliseconds.</param>
    /// <returns>Formatted SSE message bytes.</returns>
    public static byte[] FormatEvent(string eventType, object data, string? eventId = null, int? retry = null)
    {
        var sb = new StringBuilder();

        // Event type
        sb.Append("event: ");
        sb.Append(eventType);
        sb.Append('\n');

        // Event ID (for Last-Event-ID reconnection)
        if (!string.IsNullOrEmpty(eventId))
        {
            sb.Append("id: ");
            sb.Append(eventId);
            sb.Append('\n');
        }

        // Retry interval (client reconnection hint)
        if (retry.HasValue)
        {
            sb.Append("retry: ");
            sb.Append(retry.Value);
            sb.Append('\n');
        }

        // Data payload (JSON)
        var json = JsonSerializer.Serialize(data, JsonOptions);
        sb.Append("data: ");
        sb.Append(json);
        sb.Append('\n');

        // Empty line to signal end of event
        sb.Append('\n');

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Formats a simple comment/keep-alive message.
    /// Comments start with ':' and are ignored by EventSource but keep the connection alive.
    /// </summary>
    /// <param name="comment">Optional comment text.</param>
    /// <returns>Formatted comment bytes.</returns>
    public static byte[] FormatComment(string? comment = null)
    {
        var message = string.IsNullOrEmpty(comment) ? ":\n" : $": {comment}\n";
        return Encoding.UTF8.GetBytes(message);
    }

    /// <summary>
    /// Formats a heartbeat event.
    /// </summary>
    /// <param name="timestamp">Current server timestamp.</param>
    /// <param name="eventId">Event ID for reconnection support.</param>
    /// <returns>Formatted heartbeat event bytes.</returns>
    public static byte[] FormatHeartbeat(DateTimeOffset timestamp, string? eventId = null)
    {
        return FormatEvent(EventTypes.Heartbeat, new HeartbeatPayload
        {
            Timestamp = timestamp,
            Type = "heartbeat"
        }, eventId);
    }

    /// <summary>
    /// Formats a connected event sent on initial connection.
    /// </summary>
    /// <param name="jobId">The job ID being streamed.</param>
    /// <param name="eventId">Initial event ID.</param>
    /// <returns>Formatted connected event bytes.</returns>
    public static byte[] FormatConnected(Guid jobId, string eventId)
    {
        return FormatEvent(EventTypes.Connected, new ConnectedPayload
        {
            JobId = jobId,
            Message = "Connected to job status stream",
            Timestamp = DateTimeOffset.UtcNow
        }, eventId, retry: 3000); // 3 second retry on disconnect per spec
    }

    /// <summary>
    /// Formats a stage update event.
    /// </summary>
    /// <param name="stage">The stage that was updated.</param>
    /// <param name="status">The new status of the stage.</param>
    /// <param name="timestamp">When the update occurred.</param>
    /// <param name="eventId">Event ID for reconnection support.</param>
    /// <returns>Formatted stage update event bytes.</returns>
    public static byte[] FormatStageUpdate(string stage, string status, DateTimeOffset timestamp, string eventId)
    {
        return FormatEvent(EventTypes.StageUpdate, new StageUpdatePayload
        {
            Stage = stage,
            Status = status,
            Timestamp = timestamp
        }, eventId);
    }

    /// <summary>
    /// Formats a progress update event.
    /// </summary>
    /// <param name="progress">Progress percentage (0-100).</param>
    /// <param name="currentPhase">Current processing phase.</param>
    /// <param name="eventId">Event ID for reconnection support.</param>
    /// <returns>Formatted progress event bytes.</returns>
    public static byte[] FormatProgress(int progress, string? currentPhase, string eventId)
    {
        return FormatEvent(EventTypes.Progress, new ProgressPayload
        {
            Progress = progress,
            CurrentPhase = currentPhase,
            Timestamp = DateTimeOffset.UtcNow
        }, eventId);
    }

    /// <summary>
    /// Formats a job complete event.
    /// </summary>
    /// <param name="jobId">The completed job ID.</param>
    /// <param name="documentId">The created document ID.</param>
    /// <param name="documentUrl">URL to the created document.</param>
    /// <param name="eventId">Event ID for reconnection support.</param>
    /// <returns>Formatted job complete event bytes.</returns>
    public static byte[] FormatJobComplete(Guid jobId, Guid? documentId, string? documentUrl, string eventId)
    {
        return FormatEvent(EventTypes.JobComplete, new JobCompletePayload
        {
            JobId = jobId,
            Status = "Completed",
            DocumentId = documentId,
            DocumentUrl = documentUrl,
            Timestamp = DateTimeOffset.UtcNow
        }, eventId);
    }

    /// <summary>
    /// Formats a job failed event.
    /// </summary>
    /// <param name="jobId">The failed job ID.</param>
    /// <param name="errorCode">Error code for programmatic handling.</param>
    /// <param name="errorMessage">Human-readable error message.</param>
    /// <param name="retryable">Whether the job can be retried.</param>
    /// <param name="eventId">Event ID for reconnection support.</param>
    /// <returns>Formatted job failed event bytes.</returns>
    public static byte[] FormatJobFailed(Guid jobId, string errorCode, string errorMessage, bool retryable, string eventId)
    {
        return FormatEvent(EventTypes.JobFailed, new JobFailedPayload
        {
            JobId = jobId,
            Status = "Failed",
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Retryable = retryable,
            Timestamp = DateTimeOffset.UtcNow
        }, eventId);
    }

    /// <summary>
    /// Formats a terminal error event per ADR-019.
    /// This is sent when SSE connection encounters an unrecoverable error.
    /// </summary>
    /// <param name="errorCode">Error code for programmatic handling.</param>
    /// <param name="errorMessage">Human-readable error message.</param>
    /// <param name="correlationId">Correlation ID for debugging.</param>
    /// <returns>Formatted error event bytes.</returns>
    public static byte[] FormatError(string errorCode, string errorMessage, string correlationId)
    {
        return FormatEvent(EventTypes.Error, new ErrorPayload
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            CorrelationId = correlationId,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Generates an event ID based on job ID and sequence number.
    /// Format: {jobId}:{sequenceNumber}
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="sequence">The event sequence number.</param>
    /// <returns>Formatted event ID.</returns>
    public static string GenerateEventId(Guid jobId, long sequence)
    {
        return $"{jobId}:{sequence}";
    }

    /// <summary>
    /// Parses a Last-Event-ID to extract job ID and sequence number.
    /// </summary>
    /// <param name="lastEventId">The Last-Event-ID header value.</param>
    /// <param name="jobId">Extracted job ID.</param>
    /// <param name="sequence">Extracted sequence number.</param>
    /// <returns>True if parsing succeeded.</returns>
    public static bool TryParseLastEventId(string? lastEventId, out Guid jobId, out long sequence)
    {
        jobId = Guid.Empty;
        sequence = 0;

        if (string.IsNullOrEmpty(lastEventId))
            return false;

        var parts = lastEventId.Split(':');
        if (parts.Length != 2)
            return false;

        return Guid.TryParse(parts[0], out jobId) && long.TryParse(parts[1], out sequence);
    }

    #region Payload Records

    /// <summary>
    /// Heartbeat event payload.
    /// </summary>
    public record HeartbeatPayload
    {
        public string Type { get; init; } = "heartbeat";
        public required DateTimeOffset Timestamp { get; init; }
    }

    /// <summary>
    /// Connected event payload.
    /// </summary>
    public record ConnectedPayload
    {
        public required Guid JobId { get; init; }
        public string Message { get; init; } = "Connected";
        public required DateTimeOffset Timestamp { get; init; }
    }

    /// <summary>
    /// Stage update event payload.
    /// </summary>
    public record StageUpdatePayload
    {
        public required string Stage { get; init; }
        public required string Status { get; init; }
        public required DateTimeOffset Timestamp { get; init; }
    }

    /// <summary>
    /// Progress update event payload.
    /// </summary>
    public record ProgressPayload
    {
        public required int Progress { get; init; }
        public string? CurrentPhase { get; init; }
        public required DateTimeOffset Timestamp { get; init; }
    }

    /// <summary>
    /// Job complete event payload per spec.md.
    /// </summary>
    public record JobCompletePayload
    {
        public required Guid JobId { get; init; }
        public string Status { get; init; } = "Completed";
        public Guid? DocumentId { get; init; }
        public string? DocumentUrl { get; init; }
        public required DateTimeOffset Timestamp { get; init; }
    }

    /// <summary>
    /// Job failed event payload per spec.md.
    /// </summary>
    public record JobFailedPayload
    {
        public required Guid JobId { get; init; }
        public string Status { get; init; } = "Failed";
        public required string ErrorCode { get; init; }
        public required string ErrorMessage { get; init; }
        public bool Retryable { get; init; }
        public required DateTimeOffset Timestamp { get; init; }
    }

    /// <summary>
    /// Terminal error event payload per ADR-019.
    /// </summary>
    public record ErrorPayload
    {
        public required string ErrorCode { get; init; }
        public required string ErrorMessage { get; init; }
        public required string CorrelationId { get; init; }
        public required DateTimeOffset Timestamp { get; init; }
    }

    #endregion
}
