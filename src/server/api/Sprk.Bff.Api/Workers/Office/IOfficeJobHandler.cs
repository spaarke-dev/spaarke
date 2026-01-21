using Sprk.Bff.Api.Workers.Office.Messages;

namespace Sprk.Bff.Api.Workers.Office;

/// <summary>
/// Interface for Office job handlers.
/// Handlers implement specific job processing logic (upload, profile, indexing).
/// </summary>
/// <remarks>
/// Per ADR-004, all handlers must be idempotent (safe under at-least-once delivery).
/// The base infrastructure handles idempotency checking using the job's IdempotencyKey.
/// </remarks>
public interface IOfficeJobHandler
{
    /// <summary>
    /// Gets the job type this handler processes.
    /// </summary>
    OfficeJobType JobType { get; }

    /// <summary>
    /// Processes a job message.
    /// </summary>
    /// <param name="message">The job message to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The processing outcome.</returns>
    Task<JobOutcome> ProcessAsync(OfficeJobMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Outcome of job processing.
/// </summary>
/// <param name="IsSuccess">Whether processing succeeded.</param>
/// <param name="ErrorCode">Error code if failed (maps to OFFICE_xxx codes).</param>
/// <param name="ErrorMessage">Error message if failed.</param>
/// <param name="Retryable">Whether the error is retryable.</param>
public record JobOutcome(
    bool IsSuccess,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    bool Retryable = false)
{
    /// <summary>
    /// Creates a successful outcome.
    /// </summary>
    public static JobOutcome Success() => new(true);

    /// <summary>
    /// Creates a failed outcome.
    /// </summary>
    /// <param name="code">Error code (OFFICE_xxx).</param>
    /// <param name="message">Error message.</param>
    /// <param name="retryable">Whether the error is retryable.</param>
    public static JobOutcome Failure(string code, string message, bool retryable = false)
        => new(false, code, message, retryable);
}

/// <summary>
/// Types of Office processing jobs.
/// </summary>
public enum OfficeJobType
{
    /// <summary>
    /// Upload finalization - moves temp files to SPE, creates records.
    /// </summary>
    UploadFinalization,

    /// <summary>
    /// Profile generation - creates AI summary.
    /// </summary>
    Profile,

    /// <summary>
    /// Search indexing - indexes document for search.
    /// </summary>
    Indexing,

    /// <summary>
    /// Deep analysis - optional detailed AI analysis.
    /// </summary>
    DeepAnalysis
}
