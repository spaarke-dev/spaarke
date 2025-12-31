using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Infrastructure.Resilience;

/// <summary>
/// Exception thrown when the Azure AI Search circuit breaker is open.
/// Callers should return HTTP 503 Service Unavailable.
/// </summary>
public class AiSearchCircuitBrokenException : Exception
{
    public TimeSpan RetryAfter { get; }

    public AiSearchCircuitBrokenException(TimeSpan retryAfter)
        : base($"Azure AI Search is temporarily unavailable. Retry after {retryAfter.TotalSeconds:F0} seconds.")
    {
        RetryAfter = retryAfter;
    }
}

/// <summary>
/// Wrapper that provides resilience patterns for Azure AI Search operations.
/// Implements circuit breaker, retry, and timeout using Polly v8.x.
/// </summary>
public class ResilientSearchClient : IResilientSearchClient
{
    private readonly AiSearchResilienceOptions _options;
    private readonly ICircuitBreakerRegistry _circuitRegistry;
    private readonly ILogger<ResilientSearchClient> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly TimeSpan _breakDuration;

    public ResilientSearchClient(
        IOptions<AiSearchResilienceOptions> options,
        ICircuitBreakerRegistry circuitRegistry,
        ILogger<ResilientSearchClient> logger)
    {
        _options = options.Value;
        _circuitRegistry = circuitRegistry;
        _logger = logger;
        _breakDuration = TimeSpan.FromSeconds(_options.CircuitBreakerBreakDurationSeconds);

        // Register with circuit breaker registry
        _circuitRegistry.RegisterCircuit(CircuitBreakerRegistry.AzureAISearch);

        // Build resilience pipeline
        _resiliencePipeline = BuildResiliencePipeline();
    }

    /// <summary>
    /// Execute a search query with resilience protection.
    /// </summary>
    public async Task<SearchResults<T>> SearchAsync<T>(
        SearchClient client,
        string searchText,
        SearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                return await client.SearchAsync<T>(searchText, options, ct);
            }, cancellationToken);

            _circuitRegistry.RecordSuccess(CircuitBreakerRegistry.AzureAISearch);
            return response.Value;
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("Azure AI Search circuit breaker is open. Rejecting search request.");
            throw new AiSearchCircuitBrokenException(_breakDuration);
        }
        catch (RequestFailedException ex) when (IsTransientError(ex))
        {
            _circuitRegistry.RecordFailure(CircuitBreakerRegistry.AzureAISearch);
            _logger.LogWarning(ex, "Transient Azure AI Search error after retries");
            throw;
        }
        catch (TimeoutRejectedException ex)
        {
            _circuitRegistry.RecordFailure(CircuitBreakerRegistry.AzureAISearch);
            _logger.LogWarning(ex, "Azure AI Search request timed out after {Timeout}s", _options.TimeoutSeconds);
            throw;
        }
    }

    /// <summary>
    /// Execute a merge/upload operation with resilience protection.
    /// </summary>
    public async Task<IndexDocumentsResult> MergeOrUploadDocumentsAsync<T>(
        SearchClient client,
        IEnumerable<T> documents,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                return await client.MergeOrUploadDocumentsAsync(documents, cancellationToken: ct);
            }, cancellationToken);

            _circuitRegistry.RecordSuccess(CircuitBreakerRegistry.AzureAISearch);
            return response.Value;
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("Azure AI Search circuit breaker is open. Rejecting index request.");
            throw new AiSearchCircuitBrokenException(_breakDuration);
        }
        catch (RequestFailedException ex) when (IsTransientError(ex))
        {
            _circuitRegistry.RecordFailure(CircuitBreakerRegistry.AzureAISearch);
            _logger.LogWarning(ex, "Transient Azure AI Search error during indexing");
            throw;
        }
    }

    /// <summary>
    /// Execute a delete operation with resilience protection.
    /// </summary>
    public async Task<IndexDocumentsResult> DeleteDocumentsAsync(
        SearchClient client,
        string keyName,
        IEnumerable<string> keyValues,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                return await client.DeleteDocumentsAsync(keyName, keyValues, cancellationToken: ct);
            }, cancellationToken);

            _circuitRegistry.RecordSuccess(CircuitBreakerRegistry.AzureAISearch);
            return response.Value;
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("Azure AI Search circuit breaker is open. Rejecting delete request.");
            throw new AiSearchCircuitBrokenException(_breakDuration);
        }
        catch (RequestFailedException ex) when (IsTransientError(ex))
        {
            _circuitRegistry.RecordFailure(CircuitBreakerRegistry.AzureAISearch);
            _logger.LogWarning(ex, "Transient Azure AI Search error during delete");
            throw;
        }
    }

    private ResiliencePipeline BuildResiliencePipeline()
    {
        return new ResiliencePipelineBuilder()
            // 1. Timeout (innermost - per-attempt timeout)
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds),
                OnTimeout = args =>
                {
                    _logger.LogWarning("Azure AI Search request timed out after {Timeout}s", _options.TimeoutSeconds);
                    return default;
                }
            })
            // 2. Retry (middle - handles transient failures)
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = _options.RetryCount,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(_options.RetryBackoffSeconds),
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<RequestFailedException>(ex => IsTransientError(ex))
                    .Handle<TimeoutRejectedException>(),
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Azure AI Search request failed, retrying in {Delay}s (attempt {Attempt}/{Max}). Error: {Error}",
                        args.RetryDelay.TotalSeconds,
                        args.AttemptNumber + 1,
                        _options.RetryCount,
                        args.Outcome.Exception?.Message ?? "unknown");
                    return default;
                }
            })
            // 3. Circuit Breaker (outermost - protects against cascading failures)
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = _options.CircuitBreakerFailureRatio,
                MinimumThroughput = _options.CircuitBreakerMinimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(_options.CircuitBreakerSamplingDurationSeconds),
                BreakDuration = _breakDuration,
                ShouldHandle = new PredicateBuilder()
                    .Handle<RequestFailedException>(ex => IsTransientError(ex))
                    .Handle<TimeoutRejectedException>(),
                OnOpened = args =>
                {
                    _circuitRegistry.RecordStateChange(
                        CircuitBreakerRegistry.AzureAISearch,
                        CircuitState.Open,
                        _breakDuration);
                    return default;
                },
                OnClosed = args =>
                {
                    _circuitRegistry.RecordStateChange(
                        CircuitBreakerRegistry.AzureAISearch,
                        CircuitState.Closed);
                    return default;
                },
                OnHalfOpened = args =>
                {
                    _circuitRegistry.RecordStateChange(
                        CircuitBreakerRegistry.AzureAISearch,
                        CircuitState.HalfOpen);
                    return default;
                }
            })
            .Build();
    }

    private static bool IsTransientError(RequestFailedException ex)
    {
        // Azure AI Search transient error codes
        return ex.Status switch
        {
            429 => true,  // Too Many Requests
            503 => true,  // Service Unavailable
            504 => true,  // Gateway Timeout
            >= 500 => true, // Any server error
            _ => false
        };
    }
}

/// <summary>
/// Interface for resilient Azure AI Search operations.
/// </summary>
public interface IResilientSearchClient
{
    /// <summary>Execute a search query with resilience protection.</summary>
    Task<SearchResults<T>> SearchAsync<T>(
        SearchClient client,
        string searchText,
        SearchOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Execute a merge/upload operation with resilience protection.</summary>
    Task<IndexDocumentsResult> MergeOrUploadDocumentsAsync<T>(
        SearchClient client,
        IEnumerable<T> documents,
        CancellationToken cancellationToken = default);

    /// <summary>Execute a delete operation with resilience protection.</summary>
    Task<IndexDocumentsResult> DeleteDocumentsAsync(
        SearchClient client,
        string keyName,
        IEnumerable<string> keyValues,
        CancellationToken cancellationToken = default);
}
