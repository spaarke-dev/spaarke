using System.Text.Json;

namespace Sprk.Bff.Api.Services.Jobs;

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
    /// The logical delivery count (<see cref="DeliveryCount"/>) at which <c>ServiceBusJobProcessor</c> dead-letters a job that did
    /// not complete.
    /// </summary>
    public const int MaxDeliveryCount = 5;

    /// <summary>
    /// ISS-025 / DEF-055: the Service Bus application property a retry copy carries — how many deliveries earlier copies of this
    /// job already spent. <c>ServiceBusJobProcessor</c> retries a failed job by sending a copy scheduled for later (and completing
    /// the original), and the broker starts that copy's own delivery count at 1, so this property is what keeps the count logical.
    /// A message without it (a first submission, a dead-letter re-drive) has spent none.
    /// </summary>
    public const string PriorDeliveryCountProperty = "PriorDeliveryCount";

    /// <summary>The wait before the retry that follows the first failed delivery (<see cref="RetryDelay"/>).</summary>
    public static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMinutes(1);

    /// <summary>The longest wait between two deliveries of one job (<see cref="RetryDelay"/>).</summary>
    public static readonly TimeSpan RetryMaxDelay = TimeSpan.FromMinutes(15);

    /// <summary>
    /// ISS-025 / DEF-055: how long <c>ServiceBusJobProcessor</c> waits before it re-delivers a job whose logical delivery
    /// <paramref name="failedDelivery"/> failed and was not the final one: <see cref="RetryBaseDelay"/> doubled per delivery, capped at
    /// <see cref="RetryMaxDelay"/> — 1, 2, 4 and 8 minutes after deliveries 1 to 4. A value below 1 is treated as 1.
    /// </summary>
    public static TimeSpan RetryDelay(int failedDelivery)
    {
        var exponent = Math.Clamp(failedDelivery, 1, 31) - 1;
        var ticks = RetryBaseDelay.Ticks * Math.Pow(2, exponent);
        return ticks >= RetryMaxDelay.Ticks ? RetryMaxDelay : TimeSpan.FromTicks((long)ticks);
    }

    /// <summary>
    /// The total back-off a job that fails every delivery waits before its final one: the sum of <see cref="RetryDelay"/> over
    /// deliveries 1 to <see cref="MaxDeliveryCount"/> - 1 (15 minutes), on top of the time the deliveries themselves take.
    /// </summary>
    public static TimeSpan RetryWindow { get; } =
        TimeSpan.FromTicks(Enumerable.Range(1, MaxDeliveryCount - 1).Sum(delivery => RetryDelay(delivery).Ticks));

    /// <summary>
    /// The LOGICAL delivery count of this job — the deliveries earlier copies spent (<see cref="PriorDeliveryCountProperty"/>) plus
    /// the times the broker has delivered THIS message (Service Bus <c>DeliveryCount</c>) — set by <c>ServiceBusJobProcessor</c>
    /// before the handler runs. Not part of the wire contract: a retry is a copy of the SAME job, so <see cref="Attempt"/> does not
    /// advance on it — only this does.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int DeliveryCount { get; set; }

    /// <summary>
    /// True when the processor will dead-letter this job if it does not complete on this delivery: its attempts are
    /// exhausted OR this is its <see cref="MaxDeliveryCount"/>th logical delivery (<see cref="DeliveryCount"/>). The ONE definition the processor's
    /// dead-letter branch and a handler's "last chance" logic share (unified-access-control-r2 task 146 r2, verifier
    /// item 11: the inbound HOLD keyed on <see cref="IsAtMaxAttempts"/> alone, which an abandoned-and-redelivered message
    /// never reaches, so the email was dead-lettered by delivery count with no administrator alert).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsFinalDelivery => IsAtMaxAttempts || DeliveryCount >= MaxDeliveryCount;

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
