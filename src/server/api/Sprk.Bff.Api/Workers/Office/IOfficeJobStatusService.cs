namespace Sprk.Bff.Api.Workers.Office;

/// <summary>
/// Service for updating and broadcasting job status changes.
/// Used by workers to notify connected SSE clients of progress updates.
/// </summary>
/// <remarks>
/// <para>
/// This service bridges the gap between background workers and SSE clients:
/// </para>
/// <list type="bullet">
/// <item>Workers call UpdateJobPhaseAsync to record phase transitions</item>
/// <item>Status changes are published to Redis pub/sub</item>
/// <item>SSE endpoint subscribes and forwards updates to clients</item>
/// </list>
/// <para>
/// Full implementation in task 064 - this is the interface contract.
/// </para>
/// </remarks>
public interface IOfficeJobStatusService
{
    /// <summary>
    /// Updates the current phase of a processing job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="phase">The phase name (e.g., "Uploading", "Indexing", "Indexed").</param>
    /// <param name="status">The phase status (e.g., "Running", "Completed", "Failed", "Skipped").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="errorMessage">Optional error message if status is "Failed".</param>
    /// <returns>Task representing the async operation.</returns>
    Task UpdateJobPhaseAsync(
        Guid jobId,
        string phase,
        string status,
        CancellationToken cancellationToken = default,
        string? errorMessage = null);

    /// <summary>
    /// Updates the progress percentage of a processing job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="progress">Progress percentage (0-100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    Task UpdateJobProgressAsync(
        Guid jobId,
        int progress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a job as completed with result information.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="documentId">The created document ID (if applicable).</param>
    /// <param name="documentUrl">The document URL (if applicable).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    Task CompleteJobAsync(
        Guid jobId,
        Guid? documentId = null,
        string? documentUrl = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a job as failed with error information.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="errorCode">Error code (OFFICE_xxx).</param>
    /// <param name="errorMessage">Error message.</param>
    /// <param name="retryable">Whether the error is retryable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    Task FailJobAsync(
        Guid jobId,
        string errorCode,
        string errorMessage,
        bool retryable = false,
        CancellationToken cancellationToken = default);
}
