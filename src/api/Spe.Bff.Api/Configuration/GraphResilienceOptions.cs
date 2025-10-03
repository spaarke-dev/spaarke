using System.ComponentModel.DataAnnotations;

namespace Spe.Bff.Api.Configuration;

/// <summary>
/// Configuration options for Microsoft Graph API resilience policies (Task 4.1).
/// Controls retry, circuit breaker, and timeout behavior via Polly.
/// </summary>
public class GraphResilienceOptions
{
    public const string SectionName = "GraphResilience";

    /// <summary>
    /// Number of retry attempts for transient failures (429, 503, 504).
    /// Default: 3
    /// </summary>
    [Range(1, 10)]
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Base backoff duration in seconds for exponential backoff.
    /// Actual delay = 2^retryAttempt * RetryBackoffSeconds (e.g., 2s, 4s, 8s).
    /// Default: 2
    /// </summary>
    [Range(1, 30)]
    public int RetryBackoffSeconds { get; set; } = 2;

    /// <summary>
    /// Number of consecutive failures before circuit breaker opens.
    /// Default: 5
    /// </summary>
    [Range(3, 20)]
    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    /// <summary>
    /// Duration in seconds the circuit breaker remains open before entering half-open state.
    /// Default: 30
    /// </summary>
    [Range(10, 300)]
    public int CircuitBreakerBreakDurationSeconds { get; set; } = 30;

    /// <summary>
    /// Timeout in seconds for individual Graph API requests.
    /// Default: 30
    /// </summary>
    [Range(5, 300)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to honor Retry-After header from Graph API 429 responses.
    /// Default: true (recommended)
    /// </summary>
    public bool HonorRetryAfterHeader { get; set; } = true;
}
