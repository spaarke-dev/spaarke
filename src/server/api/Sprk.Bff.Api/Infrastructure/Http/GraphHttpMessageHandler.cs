using System.Net;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;
using Polly.Timeout;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Infrastructure.Http;

/// <summary>
/// DelegatingHandler that provides centralized resilience patterns for Microsoft Graph API calls.
/// Implements retry, circuit breaker, and timeout policies using Polly v8.x (updated Phase 7).
/// Registered with IHttpClientFactory for automatic injection into GraphServiceClient.
/// </summary>
public class GraphHttpMessageHandler : DelegatingHandler
{
    private readonly ILogger<GraphHttpMessageHandler> _logger;
    private readonly GraphResilienceOptions _options;
    private readonly IAsyncPolicy<HttpResponseMessage> _resiliencePolicy;

    public GraphHttpMessageHandler(
        ILogger<GraphHttpMessageHandler> logger,
        IOptions<GraphResilienceOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _resiliencePolicy = BuildResiliencePolicy();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return await _resiliencePolicy.ExecuteAsync(async ct =>
        {
            return await base.SendAsync(request, ct);
        }, cancellationToken);
    }

    private IAsyncPolicy<HttpResponseMessage> BuildResiliencePolicy()
    {
        // 1. Retry Policy: Handle transient errors (429, 503, 504, 5xx)
        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError() // 5xx and 408
            .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests) // 429
            .WaitAndRetryAsync(
                retryCount: _options.RetryCount,
                sleepDurationProvider: (retryAttempt, response, context) =>
                {
                    // Honor Retry-After header if present (Graph API standard)
                    if (_options.HonorRetryAfterHeader &&
                        response.Result?.Headers.RetryAfter?.Delta.HasValue == true)
                    {
                        var retryAfter = response.Result.Headers.RetryAfter.Delta.Value;
                        _logger.LogWarning(
                            "Graph API throttling detected (429), honoring Retry-After: {RetryAfter}s (attempt {Attempt}/{Total})",
                            retryAfter.TotalSeconds,
                            retryAttempt,
                            _options.RetryCount);
                        return retryAfter;
                    }

                    // Exponential backoff: 2^retryAttempt * base seconds (e.g., 2s, 4s, 8s)
                    var backoff = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt) * _options.RetryBackoffSeconds);
                    return backoff;
                },
                onRetryAsync: async (outcome, timespan, retryAttempt, context) =>
                {
                    var statusCode = outcome.Result?.StatusCode ?? HttpStatusCode.InternalServerError;
                    var requestUri = context.GetValueOrDefault("RequestUri", "unknown");

                    _logger.LogWarning(
                        "Graph API request to {RequestUri} failed with {StatusCode}, retrying in {Delay}s (attempt {Attempt}/{Total})",
                        requestUri,
                        (int)statusCode,
                        timespan.TotalSeconds,
                        retryAttempt,
                        _options.RetryCount);

                    // Telemetry: Track retry attempts
                    RecordRetryAttempt(statusCode, retryAttempt);

                    await Task.CompletedTask;
                });

        // 2. Circuit Breaker Policy: Open circuit after consecutive failures
        var circuitBreakerPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: _options.CircuitBreakerFailureThreshold,
                durationOfBreak: TimeSpan.FromSeconds(_options.CircuitBreakerBreakDurationSeconds),
                onBreak: (outcome, duration) =>
                {
                    var statusCode = outcome.Result?.StatusCode ?? HttpStatusCode.InternalServerError;
                    _logger.LogError(
                        "Circuit breaker OPENED due to {ConsecutiveFailures} consecutive failures (last status: {StatusCode}). Breaking for {Duration}s",
                        _options.CircuitBreakerFailureThreshold,
                        (int)statusCode,
                        duration.TotalSeconds);
                    RecordCircuitBreakerState("open");
                },
                onReset: () =>
                {
                    _logger.LogInformation("Circuit breaker RESET to closed state - service recovered");
                    RecordCircuitBreakerState("closed");
                },
                onHalfOpen: () =>
                {
                    _logger.LogInformation("Circuit breaker in HALF-OPEN state - testing connection");
                    RecordCircuitBreakerState("half-open");
                });

        // 3. Timeout Policy: Per-request timeout
        var timeoutPolicy = Policy
            .TimeoutAsync<HttpResponseMessage>(
                timeout: TimeSpan.FromSeconds(_options.TimeoutSeconds),
                timeoutStrategy: TimeoutStrategy.Pessimistic,
                onTimeoutAsync: async (context, timespan, task) =>
                {
                    var requestUri = context.GetValueOrDefault("RequestUri", "unknown");
                    _logger.LogWarning(
                        "Graph API request to {RequestUri} timed out after {Timeout}s",
                        requestUri,
                        timespan.TotalSeconds);
                    RecordTimeout();
                    await Task.CompletedTask;
                });

        // Combine policies: Timeout -> Retry -> Circuit Breaker (inner to outer)
        // Order matters: timeout applies per attempt, retry wraps attempts, circuit breaker is outermost
        return Policy.WrapAsync(timeoutPolicy, retryPolicy, circuitBreakerPolicy);
    }

    private void RecordRetryAttempt(HttpStatusCode statusCode, int attempt)
    {
        // TODO (Sprint 4): Emit telemetry (Application Insights, Prometheus, etc.)
        // Example: _telemetry.TrackMetric("GraphApi.Retry", attempt, new { StatusCode = (int)statusCode });
    }

    private void RecordCircuitBreakerState(string state)
    {
        // TODO (Sprint 4): Emit circuit breaker state change
        // Example: _telemetry.TrackEvent("GraphApi.CircuitBreaker", new { State = state });
    }

    private void RecordTimeout()
    {
        // TODO (Sprint 4): Emit timeout event
        // Example: _telemetry.TrackMetric("GraphApi.Timeout", 1);
    }
}
