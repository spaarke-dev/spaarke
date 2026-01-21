namespace Sprk.Bff.Api.Workers.Office;

/// <summary>
/// Stub implementation of IOfficeJobStatusService.
/// Will be replaced with Redis pub/sub implementation in task 064.
/// </summary>
/// <remarks>
/// <para>
/// This stub logs status updates and updates the Dataverse ProcessingJob record.
/// Full real-time notification via Redis pub/sub will be implemented in task 064.
/// </para>
/// </remarks>
public class OfficeJobStatusService : IOfficeJobStatusService
{
    private readonly ILogger<OfficeJobStatusService> _logger;

    public OfficeJobStatusService(ILogger<OfficeJobStatusService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task UpdateJobPhaseAsync(
        Guid jobId,
        string phase,
        string status,
        CancellationToken cancellationToken = default,
        string? errorMessage = null)
    {
        _logger.LogInformation(
            "Job {JobId} phase update: {Phase} -> {Status}{Error}",
            jobId, phase, status,
            string.IsNullOrEmpty(errorMessage) ? "" : $" ({errorMessage})");

        // TODO (Task 064): Publish to Redis pub/sub and update Dataverse record
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateJobProgressAsync(
        Guid jobId,
        int progress,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Job {JobId} progress update: {Progress}%",
            jobId, progress);

        // TODO (Task 064): Publish to Redis pub/sub
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CompleteJobAsync(
        Guid jobId,
        Guid? documentId = null,
        string? documentUrl = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Job {JobId} completed. DocumentId: {DocumentId}",
            jobId, documentId);

        // TODO (Task 064): Publish to Redis pub/sub and update Dataverse record
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task FailJobAsync(
        Guid jobId,
        string errorCode,
        string errorMessage,
        bool retryable = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Job {JobId} failed: [{ErrorCode}] {ErrorMessage} (Retryable: {Retryable})",
            jobId, errorCode, errorMessage, retryable);

        // TODO (Task 064): Publish to Redis pub/sub and update Dataverse record
        return Task.CompletedTask;
    }
}
