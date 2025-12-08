namespace Sprk.Bff.Api.Services.Jobs;

/// <summary>
/// Result of job processing for observability and metrics.
/// </summary>
public class JobOutcome
{
    public Guid JobId { get; set; }
    public string JobType { get; set; } = string.Empty;
    public JobStatus Status { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ErrorMessage { get; set; }
    public int Attempt { get; set; }
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a successful job outcome.
    /// </summary>
    public static JobOutcome Success(Guid jobId, string jobType, TimeSpan duration)
    {
        return new JobOutcome
        {
            JobId = jobId,
            JobType = jobType,
            Status = JobStatus.Completed,
            Duration = duration,
            Attempt = 0
        };
    }

    /// <summary>
    /// Creates a failed job outcome that will be retried.
    /// </summary>
    public static JobOutcome Failure(Guid jobId, string jobType, string errorMessage, int attempt, TimeSpan duration)
    {
        return new JobOutcome
        {
            JobId = jobId,
            JobType = jobType,
            Status = JobStatus.Failed,
            ErrorMessage = errorMessage,
            Attempt = attempt,
            Duration = duration
        };
    }

    /// <summary>
    /// Creates a poisoned job outcome (max attempts exceeded).
    /// </summary>
    public static JobOutcome Poisoned(Guid jobId, string jobType, string errorMessage, int attempt, TimeSpan duration)
    {
        return new JobOutcome
        {
            JobId = jobId,
            JobType = jobType,
            Status = JobStatus.Poisoned,
            ErrorMessage = errorMessage,
            Attempt = attempt,
            Duration = duration
        };
    }
}

/// <summary>
/// Status of job processing.
/// </summary>
public enum JobStatus
{
    /// <summary>
    /// Job completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Job failed but can be retried.
    /// </summary>
    Failed,

    /// <summary>
    /// Job failed and exceeded max attempts (sent to poison queue).
    /// </summary>
    Poisoned
}
