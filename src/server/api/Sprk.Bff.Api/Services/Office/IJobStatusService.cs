using Sprk.Bff.Api.Models.Office;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// Service interface for publishing and subscribing to job status updates.
/// Enables real-time notification distribution via Redis pub/sub for SSE streaming.
/// </summary>
/// <remarks>
/// <para>
/// This service bridges background workers to SSE clients:
/// - Workers call <see cref="PublishStatusUpdateAsync"/> when job status changes
/// - SSE endpoint subscribes via <see cref="SubscribeToJobAsync"/> to receive updates
/// - Updates are distributed via Redis pub/sub for sub-second latency
/// </para>
/// <para>
/// Per ADR-009, Redis pub/sub is ephemeral - if a client is disconnected when an
/// update is published, they won't receive it. The SSE endpoint should query current
/// status on connection, then stream subsequent updates.
/// </para>
/// </remarks>
public interface IJobStatusService
{
    /// <summary>
    /// Publishes a job status update to all subscribed clients.
    /// Called by background workers when job state changes.
    /// </summary>
    /// <param name="update">The status update to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if published successfully, false if Redis unavailable.</returns>
    /// <remarks>
    /// <para>
    /// This method MUST:
    /// - Publish to Redis channel: "job:{jobId}:status"
    /// - Complete within 100ms for normal operation
    /// - Handle Redis failures gracefully (return false, don't throw)
    /// - Log failures for monitoring
    /// </para>
    /// <para>
    /// Workers should call this method for:
    /// - Progress percentage changes
    /// - Phase/stage transitions
    /// - Terminal states (Completed, Failed, Cancelled)
    /// - Error conditions
    /// </para>
    /// </remarks>
    Task<bool> PublishStatusUpdateAsync(
        JobStatusUpdate update,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to job status updates for the specified job.
    /// Returns an async enumerable of updates as they arrive.
    /// </summary>
    /// <param name="jobId">The job ID to subscribe to.</param>
    /// <param name="cancellationToken">Cancellation token to stop subscription.</param>
    /// <returns>Async enumerable of job status updates.</returns>
    /// <remarks>
    /// <para>
    /// This method:
    /// - Subscribes to Redis channel: "job:{jobId}:status"
    /// - Yields updates as they arrive
    /// - Completes when cancellation is requested or job reaches terminal state
    /// - Handles Redis reconnection automatically
    /// </para>
    /// <para>
    /// The SSE endpoint should:
    /// - Query current status before subscribing (for initial state)
    /// - Subscribe using this method for real-time updates
    /// - Handle reconnection via Last-Event-ID
    /// </para>
    /// </remarks>
    IAsyncEnumerable<JobStatusUpdate> SubscribeToJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the job status in Dataverse ProcessingJob record.
    /// Called by workers to persist status changes.
    /// </summary>
    /// <param name="jobId">The job ID to update.</param>
    /// <param name="status">New job status.</param>
    /// <param name="progress">Progress percentage (0-100).</param>
    /// <param name="currentPhase">Current processing phase name.</param>
    /// <param name="completedPhase">Phase that just completed (if any).</param>
    /// <param name="result">Job result (if completed).</param>
    /// <param name="error">Error details (if failed).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if update succeeded.</returns>
    /// <remarks>
    /// This method updates the Dataverse ProcessingJob record and optionally
    /// publishes the update to Redis for SSE subscribers.
    /// </remarks>
    Task<bool> UpdateJobStatusAsync(
        Guid jobId,
        JobStatus status,
        int progress,
        string? currentPhase = null,
        CompletedPhase? completedPhase = null,
        JobResult? result = null,
        JobError? error = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a job as completed successfully.
    /// Convenience method that updates status and publishes terminal event.
    /// </summary>
    /// <param name="jobId">The job ID to complete.</param>
    /// <param name="result">The job result with artifact information.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if completion succeeded.</returns>
    Task<bool> CompleteJobAsync(
        Guid jobId,
        JobResult result,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a job as failed.
    /// Convenience method that updates status and publishes terminal event.
    /// </summary>
    /// <param name="jobId">The job ID to fail.</param>
    /// <param name="error">Error details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if failure recorded successfully.</returns>
    Task<bool> FailJobAsync(
        Guid jobId,
        JobError error,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the Redis connection is healthy.
    /// Used for health checks and monitoring.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if Redis is connected and responsive.</returns>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a job status update for Redis pub/sub distribution.
/// </summary>
public record JobStatusUpdate
{
    /// <summary>
    /// The job ID this update belongs to.
    /// </summary>
    public required Guid JobId { get; init; }

    /// <summary>
    /// Type of status update event.
    /// </summary>
    public required JobStatusUpdateType UpdateType { get; init; }

    /// <summary>
    /// Current job status.
    /// </summary>
    public required JobStatus Status { get; init; }

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    public int Progress { get; init; }

    /// <summary>
    /// Current processing phase.
    /// </summary>
    public string? CurrentPhase { get; init; }

    /// <summary>
    /// Phase that just completed (for StageComplete events).
    /// </summary>
    public CompletedPhase? CompletedPhase { get; init; }

    /// <summary>
    /// Job result (for Completed events).
    /// </summary>
    public JobResult? Result { get; init; }

    /// <summary>
    /// Error details (for Failed events).
    /// </summary>
    public JobError? Error { get; init; }

    /// <summary>
    /// When this update was generated.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Sequence number for ordering and reconnection support.
    /// </summary>
    public long Sequence { get; init; }
}

/// <summary>
/// Types of job status update events.
/// </summary>
public enum JobStatusUpdateType
{
    /// <summary>
    /// Progress percentage changed.
    /// </summary>
    Progress,

    /// <summary>
    /// A processing stage completed.
    /// </summary>
    StageComplete,

    /// <summary>
    /// A new processing stage started.
    /// </summary>
    StageStarted,

    /// <summary>
    /// Job completed successfully.
    /// </summary>
    JobCompleted,

    /// <summary>
    /// Job failed with an error.
    /// </summary>
    JobFailed,

    /// <summary>
    /// Job was cancelled.
    /// </summary>
    JobCancelled
}
