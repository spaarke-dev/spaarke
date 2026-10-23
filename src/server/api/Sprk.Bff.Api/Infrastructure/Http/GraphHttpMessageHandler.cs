using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;
using Polly.Timeout;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Resilience;
using ResilienceCircuitState = Sprk.Bff.Api.Infrastructure.Resilience.CircuitState;

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
    private readonly ICircuitBreakerRegistry? _circuitRegistry;
    private readonly IAsyncPolicy<HttpResponseMessage> _resiliencePolicy;
    private readonly IAsyncPolicy<HttpResponseMessage> _nonIdempotentPolicy;

    public GraphHttpMessageHandler(
        ILogger<GraphHttpMessageHandler> logger,
        IOptions<GraphResilienceOptions> options,
        ICircuitBreakerRegistry? circuitRegistry = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _circuitRegistry = circuitRegistry;

        // Register with circuit breaker registry
        _circuitRegistry?.RegisterCircuit(CircuitBreakerRegistry.MicrosoftGraph);

        (_resiliencePolicy, _nonIdempotentPolicy) = BuildResiliencePolicies();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // A request marked NoTransientRetryOption (#1755) is not re-sent after a 5xx, 408 or transport fault: Graph may
        // have carried it out, and a second create makes a second container. It shares the timeout and circuit breaker.
        if (NoTransientRetryOption.TryGetFrom(request, out var nonIdempotent))
        {
            return await SendNonIdempotentAsync(request, nonIdempotent, cancellationToken);
        }

        return await _resiliencePolicy.ExecuteAsync(async ct =>
        {
            return await base.SendAsync(request, ct);
        }, cancellationToken);
    }

    /// <summary>
    /// The non-idempotent chain, recording on <paramref name="option"/> each attempt as it goes to the wire and the
    /// answer it got (#1755 round 2), so the caller can tell a failure that never reached Graph (an open circuit throws
    /// before this delegate runs) from one Graph may have carried out.
    /// </summary>
    private Task<HttpResponseMessage> SendNonIdempotentAsync(
        HttpRequestMessage request, NoTransientRetryOption option, CancellationToken cancellationToken) =>
        _nonIdempotentPolicy.ExecuteAsync(async ct =>
        {
            // The caller has already read the outcome (the timeout fired as a 429's backoff ended): sending now would
            // change an outcome it has acted on.
            if (!option.TryBeginAttempt())
            {
                throw new OperationCanceledException("The request's outcome was already read; this attempt was not sent.");
            }

            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(request, ct);
            }
            catch (HttpRequestException ex) when (NeverConnected(ex))
            {
                // #1755 round 3: a refused connect or a name that did not resolve fails before any byte of the request
                // is sent, so Graph cannot have carried it out. Any other fault stays in flight: an unknown outcome.
                option.RecordNotSent();
                throw;
            }

            option.RecordAnswer(response.StatusCode);
            return response;
        }, cancellationToken);

    /// <summary>
    /// True when <paramref name="ex"/> is a failure to reach the server at all: the name did not resolve, or the
    /// connection was refused. Nothing was sent. A reset, a timeout or a dropped connection is not this: the request may
    /// have arrived.
    /// </summary>
    internal static bool NeverConnected(HttpRequestException ex)
    {
        if (ex.HttpRequestError == HttpRequestError.NameResolutionError)
        {
            return true;
        }

        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is SocketException { SocketErrorCode: SocketError.ConnectionRefused or SocketError.HostNotFound })
            {
                return true;
            }
        }

        return false;
    }

    private (IAsyncPolicy<HttpResponseMessage> Standard, IAsyncPolicy<HttpResponseMessage> NonIdempotent) BuildResiliencePolicies()
    {
        // 1. Retry Policy: Handle transient errors (429, 503, 504, 5xx)
        var retryPolicy = WithRetry(HttpPolicyExtensions
            .HandleTransientHttpError() // 5xx and 408
            .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)); // 429

        // 1b. A non-idempotent request (NoTransientRetryOption) is retried on 429 only: Graph did not process it.
        var nonIdempotentRetryPolicy = WithRetry(Policy
            .HandleResult<HttpResponseMessage>(response => response.StatusCode == HttpStatusCode.TooManyRequests));

        // The retry both chains use: Retry-After honoured, exponential backoff otherwise, each retry logged and counted.
        IAsyncPolicy<HttpResponseMessage> WithRetry(PolicyBuilder<HttpResponseMessage> handles) => handles
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
                    _circuitRegistry?.RecordStateChange(
                        CircuitBreakerRegistry.MicrosoftGraph,
                        ResilienceCircuitState.Open,
                        duration);
                },
                onReset: () =>
                {
                    _logger.LogInformation("Circuit breaker RESET to closed state - service recovered");
                    _circuitRegistry?.RecordStateChange(
                        CircuitBreakerRegistry.MicrosoftGraph,
                        ResilienceCircuitState.Closed);
                },
                onHalfOpen: () =>
                {
                    _logger.LogInformation("Circuit breaker in HALF-OPEN state - testing connection");
                    _circuitRegistry?.RecordStateChange(
                        CircuitBreakerRegistry.MicrosoftGraph,
                        ResilienceCircuitState.HalfOpen);
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

        // Combine policies, outermost first: Timeout -> Retry -> Circuit Breaker. The pessimistic timeout is OUTSIDE the
        // retry, so it bounds the request as a whole — every attempt and every backoff together — not each attempt. The
        // circuit breaker is innermost and sees each attempt. Both chains share the one timeout and circuit breaker.
        return (Policy.WrapAsync(timeoutPolicy, retryPolicy, circuitBreakerPolicy),
                Policy.WrapAsync(timeoutPolicy, nonIdempotentRetryPolicy, circuitBreakerPolicy));
    }

    private void RecordRetryAttempt(HttpStatusCode statusCode, int attempt)
    {
        // Retry telemetry is tracked via circuit breaker registry and OpenTelemetry metrics
        _circuitRegistry?.RecordFailure(CircuitBreakerRegistry.MicrosoftGraph);
        _ = statusCode;
        _ = attempt;
    }

    private void RecordTimeout()
    {
        // Timeout is counted as a failure for circuit breaker tracking
        _circuitRegistry?.RecordFailure(CircuitBreakerRegistry.MicrosoftGraph);
    }
}
