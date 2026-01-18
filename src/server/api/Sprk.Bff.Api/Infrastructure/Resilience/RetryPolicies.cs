using System.Net;
using Polly;
using Polly.Extensions.Http;
using Polly.Retry;

namespace Sprk.Bff.Api.Infrastructure.Resilience;

/// <summary>
/// Provides standardized retry policies with exponential backoff for transient failures.
/// </summary>
/// <remarks>
/// <para>
/// Retry Scenarios:
/// - Azure OpenAI rate limits (429 Too Many Requests)
/// - Dataverse throttling (429, 503)
/// - Network timeouts
/// - Document Intelligence temporary failures
/// </para>
/// <para>
/// Backoff Strategy:
/// - Initial delay: 1 second
/// - Max delay: 30 seconds
/// - Max retries: 3
/// - Jitter for distributed load balancing
/// </para>
/// <para>
/// Task 051: Retry logic with exponential backoff
/// </para>
/// </remarks>
public static class RetryPolicies
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Configuration Constants
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Maximum number of retry attempts.</summary>
    public const int MaxRetries = 3;

    /// <summary>Initial retry delay.</summary>
    public static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(1);

    /// <summary>Maximum retry delay.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    /// <summary>Jitter factor (0-1) for randomizing delays.</summary>
    private const double JitterFactor = 0.5;

    private static readonly Random _jitterRandom = new();

    // ─────────────────────────────────────────────────────────────────────────────
    // HTTP Retry Policies
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an HTTP retry policy for Azure OpenAI calls.
    /// Handles rate limiting (429), server errors (5xx), and network failures.
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetAzureOpenAiRetryPolicy(ILogger logger)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                MaxRetries,
                retryAttempt => CalculateDelay(retryAttempt),
                onRetry: (outcome, delay, retryAttempt, context) =>
                {
                    logger.LogWarning(
                        "Azure OpenAI retry {RetryAttempt}/{MaxRetries} after {Delay}ms. " +
                        "Status: {StatusCode}, Reason: {Reason}",
                        retryAttempt,
                        MaxRetries,
                        delay.TotalMilliseconds,
                        outcome.Result?.StatusCode,
                        outcome.Exception?.Message ?? outcome.Result?.ReasonPhrase);
                });
    }

    /// <summary>
    /// Creates an HTTP retry policy for Dataverse calls.
    /// Handles throttling (429), service unavailable (503), and network failures.
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetDataverseRetryPolicy(ILogger logger)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response =>
                response.StatusCode == HttpStatusCode.TooManyRequests ||
                response.StatusCode == HttpStatusCode.ServiceUnavailable)
            .WaitAndRetryAsync(
                MaxRetries,
                retryAttempt => CalculateDelay(retryAttempt),
                onRetry: (outcome, delay, retryAttempt, context) =>
                {
                    logger.LogWarning(
                        "Dataverse retry {RetryAttempt}/{MaxRetries} after {Delay}ms. " +
                        "Status: {StatusCode}, Reason: {Reason}",
                        retryAttempt,
                        MaxRetries,
                        delay.TotalMilliseconds,
                        outcome.Result?.StatusCode,
                        outcome.Exception?.Message ?? outcome.Result?.ReasonPhrase);
                });
    }

    /// <summary>
    /// Creates an HTTP retry policy for Document Intelligence calls.
    /// Handles rate limiting (429), server errors (5xx), and network failures.
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetDocumentIntelligenceRetryPolicy(ILogger logger)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                MaxRetries,
                retryAttempt => CalculateDelay(retryAttempt),
                onRetry: (outcome, delay, retryAttempt, context) =>
                {
                    logger.LogWarning(
                        "Document Intelligence retry {RetryAttempt}/{MaxRetries} after {Delay}ms. " +
                        "Status: {StatusCode}, Reason: {Reason}",
                        retryAttempt,
                        MaxRetries,
                        delay.TotalMilliseconds,
                        outcome.Result?.StatusCode,
                        outcome.Exception?.Message ?? outcome.Result?.ReasonPhrase);
                });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Generic Retry Policies
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a generic async retry policy for any operation.
    /// Handles specified exception types with exponential backoff.
    /// </summary>
    /// <typeparam name="TException">The exception type to handle.</typeparam>
    public static AsyncRetryPolicy CreateRetryPolicy<TException>(
        ILogger logger,
        string operationName,
        int maxRetries = MaxRetries) where TException : Exception
    {
        return Policy
            .Handle<TException>()
            .WaitAndRetryAsync(
                maxRetries,
                retryAttempt => CalculateDelay(retryAttempt),
                onRetry: (exception, delay, retryAttempt, context) =>
                {
                    logger.LogWarning(
                        exception,
                        "{Operation} retry {RetryAttempt}/{MaxRetries} after {Delay}ms",
                        operationName,
                        retryAttempt,
                        maxRetries,
                        delay.TotalMilliseconds);
                });
    }

    /// <summary>
    /// Creates a retry policy with result handling.
    /// Retries on specific result conditions.
    /// </summary>
    public static AsyncRetryPolicy<TResult> CreateRetryPolicy<TResult>(
        ILogger logger,
        string operationName,
        Func<TResult, bool> shouldRetry,
        int maxRetries = MaxRetries)
    {
        return Policy
            .HandleResult(shouldRetry)
            .WaitAndRetryAsync(
                maxRetries,
                retryAttempt => CalculateDelay(retryAttempt),
                onRetry: (outcome, delay, retryAttempt, context) =>
                {
                    logger.LogWarning(
                        "{Operation} retry {RetryAttempt}/{MaxRetries} after {Delay}ms. " +
                        "Result required retry.",
                        operationName,
                        retryAttempt,
                        maxRetries,
                        delay.TotalMilliseconds);
                });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Delay Calculation
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calculates delay with exponential backoff and jitter.
    /// </summary>
    /// <param name="retryAttempt">Current retry attempt (1-based).</param>
    /// <returns>Delay duration with jitter applied.</returns>
    private static TimeSpan CalculateDelay(int retryAttempt)
    {
        // Exponential backoff: 1s, 2s, 4s, 8s, ...
        var exponentialDelay = Math.Pow(2, retryAttempt - 1) * InitialDelay.TotalSeconds;

        // Cap at max delay
        var cappedDelay = Math.Min(exponentialDelay, MaxDelay.TotalSeconds);

        // Add jitter: ±50% of the delay
        var jitter = (1 - JitterFactor + (_jitterRandom.NextDouble() * 2 * JitterFactor));
        var finalDelay = cappedDelay * jitter;

        return TimeSpan.FromSeconds(finalDelay);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Retry Context
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a Polly context with correlation information.
    /// </summary>
    public static Context CreateContext(string operationName, string? correlationId = null)
    {
        var context = new Context(operationName);
        context["CorrelationId"] = correlationId ?? Guid.NewGuid().ToString();
        context["OperationName"] = operationName;
        context["StartTime"] = DateTimeOffset.UtcNow;
        return context;
    }
}

/// <summary>
/// Extension methods for applying retry policies to HttpClient.
/// </summary>
public static class RetryPolicyExtensions
{
    /// <summary>
    /// Adds standard retry policies to an IHttpClientBuilder.
    /// </summary>
    public static IHttpClientBuilder AddStandardRetryPolicy(
        this IHttpClientBuilder builder,
        ILogger logger,
        string serviceName)
    {
        return builder.AddPolicyHandler(
            HttpPolicyExtensions
                .HandleTransientHttpError()
                .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(
                    RetryPolicies.MaxRetries,
                    retryAttempt =>
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt - 1));
                        return delay > RetryPolicies.MaxDelay ? RetryPolicies.MaxDelay : delay;
                    },
                    onRetry: (outcome, delay, retryAttempt, context) =>
                    {
                        logger.LogWarning(
                            "{Service} HTTP retry {RetryAttempt}/{MaxRetries} after {Delay}ms",
                            serviceName,
                            retryAttempt,
                            RetryPolicies.MaxRetries,
                            delay.TotalMilliseconds);
                    }));
    }
}

/// <summary>
/// Result wrapper for operations that may require retry.
/// </summary>
/// <typeparam name="T">The result type.</typeparam>
public record RetryableResult<T>
{
    /// <summary>Whether the operation succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>The result value (if successful).</summary>
    public T? Value { get; init; }

    /// <summary>Whether the operation should be retried.</summary>
    public bool ShouldRetry { get; init; }

    /// <summary>Number of retries attempted.</summary>
    public int RetryCount { get; init; }

    /// <summary>Error message (if failed).</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Create a successful result.</summary>
    public static RetryableResult<T> Ok(T value, int retryCount = 0) =>
        new() { Success = true, Value = value, RetryCount = retryCount };

    /// <summary>Create a failed result that should be retried.</summary>
    public static RetryableResult<T> Retry(string errorMessage, int retryCount) =>
        new() { Success = false, ShouldRetry = true, ErrorMessage = errorMessage, RetryCount = retryCount };

    /// <summary>Create a permanently failed result.</summary>
    public static RetryableResult<T> Fail(string errorMessage, int retryCount) =>
        new() { Success = false, ShouldRetry = false, ErrorMessage = errorMessage, RetryCount = retryCount };
}
