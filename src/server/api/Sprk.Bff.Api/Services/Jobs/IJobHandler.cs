namespace Sprk.Bff.Api.Services.Jobs;

/// <summary>
/// Interface for job handlers that process specific job types.
/// Handlers must be idempotent and handle their own business logic.
/// </summary>
public interface IJobHandler
{
    /// <summary>
    /// The job type this handler processes.
    /// </summary>
    string JobType { get; }

    /// <summary>
    /// Processes a job and returns the outcome.
    /// Must be idempotent - calling multiple times with same IdempotencyKey should be safe.
    /// </summary>
    /// <param name="job">The job to process</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The outcome of processing</returns>
    Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct);
}
