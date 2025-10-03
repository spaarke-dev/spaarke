using System.Text.Json;

namespace Spe.Bff.Api.Services.Jobs;

/// <summary>
/// Standard job contract for all async processing in SDAP.
/// Follows ADR-004: Async job contract and uniform processing.
/// </summary>
public class JobContract
{
    /// <summary>
    /// Unique identifier for this job instance.
    /// </summary>
    public Guid JobId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Type of job for routing to appropriate handler.
    /// </summary>
    public string JobType { get; set; } = string.Empty;

    /// <summary>
    /// Subject (user/resource) this job operates on.
    /// </summary>
    public string SubjectId { get; set; } = string.Empty;

    /// <summary>
    /// Correlation ID for distributed tracing.
    /// </summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// Idempotency key to prevent duplicate processing.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// Current attempt number (1-based).
    /// </summary>
    public int Attempt { get; set; } = 1;

    /// <summary>
    /// Maximum number of attempts before poisoning.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Job-specific payload data.
    /// </summary>
    public JsonDocument? Payload { get; set; }

    /// <summary>
    /// When this job was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Returns true if this job has reached maximum attempts.
    /// </summary>
    public bool IsAtMaxAttempts => Attempt >= MaxAttempts;

    /// <summary>
    /// Creates a new job for retry with incremented attempt count.
    /// </summary>
    public JobContract CreateRetry()
    {
        return new JobContract
        {
            JobId = JobId,
            JobType = JobType,
            SubjectId = SubjectId,
            CorrelationId = CorrelationId,
            IdempotencyKey = IdempotencyKey,
            Attempt = Attempt + 1,
            MaxAttempts = MaxAttempts,
            Payload = Payload,
            CreatedAt = CreatedAt
        };
    }
}
