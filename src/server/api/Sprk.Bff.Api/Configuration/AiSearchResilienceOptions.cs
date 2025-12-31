using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration options for Azure AI Search resilience policies.
/// Controls retry, circuit breaker, and timeout behavior.
/// </summary>
public class AiSearchResilienceOptions
{
    public const string SectionName = "AiSearchResilience";

    /// <summary>
    /// Number of retry attempts for transient failures (429, 503, 504).
    /// Default: 3
    /// </summary>
    [Range(1, 10)]
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Base backoff duration in seconds for exponential backoff.
    /// Actual delay = 2^retryAttempt * RetryBackoffSeconds.
    /// Default: 1
    /// </summary>
    [Range(1, 30)]
    public int RetryBackoffSeconds { get; set; } = 1;

    /// <summary>
    /// Maximum jitter in seconds to add to backoff.
    /// Helps prevent thundering herd.
    /// Default: 2
    /// </summary>
    [Range(0, 10)]
    public int RetryMaxJitterSeconds { get; set; } = 2;

    /// <summary>
    /// Failure ratio threshold to trip the circuit breaker (0.0 to 1.0).
    /// Default: 0.5 (50% failures)
    /// </summary>
    [Range(0.1, 1.0)]
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    /// <summary>
    /// Minimum number of calls before circuit breaker can trip.
    /// Prevents opening on first few failures.
    /// Default: 10
    /// </summary>
    [Range(3, 50)]
    public int CircuitBreakerMinimumThroughput { get; set; } = 10;

    /// <summary>
    /// Duration in seconds the circuit breaker remains open before entering half-open state.
    /// Default: 30
    /// </summary>
    [Range(10, 300)]
    public int CircuitBreakerBreakDurationSeconds { get; set; } = 30;

    /// <summary>
    /// Sampling duration in seconds for calculating failure ratio.
    /// Default: 60
    /// </summary>
    [Range(30, 300)]
    public int CircuitBreakerSamplingDurationSeconds { get; set; } = 60;

    /// <summary>
    /// Timeout in seconds for individual search requests.
    /// Default: 30
    /// </summary>
    [Range(5, 120)]
    public int TimeoutSeconds { get; set; } = 30;
}
