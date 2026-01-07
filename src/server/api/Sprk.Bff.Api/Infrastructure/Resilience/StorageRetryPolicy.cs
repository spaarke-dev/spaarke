using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Sprk.Bff.Api.Infrastructure.Resilience;

/// <summary>
/// Retry policy for Dataverse storage operations.
/// </summary>
/// <remarks>
/// <para>
/// This policy handles replication lag scenarios where newly created documents
/// may not be immediately visible via Dataverse queries. It applies exponential
/// backoff retry for storage operations only (not AI execution).
/// </para>
/// <para>
/// <strong>Retry Configuration:</strong>
/// <list type="bullet">
/// <item>3 retry attempts</item>
/// <item>Exponential backoff: 2s, 4s, 8s (base delay × 2^attempt)</item>
/// <item>Handles 404 (NotFound) and 503 (ServiceUnavailable)</item>
/// </list>
/// </para>
/// <para>
/// <strong>When to Use:</strong>
/// <list type="bullet">
/// <item>Saving Document Profile outputs to sprk_document fields</item>
/// <item>Creating sprk_analysisoutput records for newly created documents</item>
/// <item>Any Dataverse write operation that depends on a recently created record</item>
/// </list>
/// </para>
/// <para>
/// <strong>When NOT to Use:</strong>
/// <list type="bullet">
/// <item>AI execution (Azure OpenAI calls) - has its own resilience policies</item>
/// <item>Authorization checks - handled by AiAuthorizationService</item>
/// <item>Reading from SPE (SharePoint Embedded) - different failure modes</item>
/// </list>
/// </para>
/// </remarks>
public class StorageRetryPolicy : IStorageRetryPolicy
{
    /// <summary>
    /// Maximum number of retry attempts.
    /// </summary>
    public const int MaxRetryAttempts = 3;

    /// <summary>
    /// Base delay for exponential backoff (seconds).
    /// Actual delays: 2s, 4s, 8s (2^1, 2^2, 2^3)
    /// </summary>
    public const int BaseDelaySeconds = 2;

    private readonly ILogger<StorageRetryPolicy> _logger;
    private readonly ResiliencePipeline _pipeline;

    /// <summary>
    /// Creates a new instance of StorageRetryPolicy.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    public StorageRetryPolicy(ILogger<StorageRetryPolicy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pipeline = BuildPipeline();
    }

    /// <inheritdoc />
    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        // Polly v8 requires ValueTask, wrap the Task-based action
        return await _pipeline.ExecuteAsync(async ct =>
        {
            return await action(ct);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        // Polly v8 requires a return value, use dummy bool for void operations
        await _pipeline.ExecuteAsync(async ct =>
        {
            await action(ct);
            return true;
        }, cancellationToken);
    }

    private ResiliencePipeline BuildPipeline()
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(BaseDelaySeconds),
                UseJitter = false, // Deterministic delays for predictable behavior: 2s, 4s, 8s
                ShouldHandle = new PredicateBuilder()
                    .Handle<StorageRetryableException>()
                    .Handle<HttpRequestException>(ex => IsRetryableHttpError(ex)),
                OnRetry = args =>
                {
                    var exception = args.Outcome.Exception;
                    var statusCode = GetStatusCode(exception);

                    _logger.LogWarning(
                        "[STORAGE-RETRY] Retry {Attempt}/{Max} after {Delay}s. " +
                        "StatusCode={StatusCode}, Error={Error}",
                        args.AttemptNumber + 1,
                        MaxRetryAttempts,
                        args.RetryDelay.TotalSeconds,
                        statusCode,
                        exception?.Message ?? "unknown");

                    return default;
                }
            })
            .Build();
    }

    private static bool IsRetryableHttpError(HttpRequestException ex)
    {
        // Handle 404 (document not yet replicated) and 503 (service unavailable)
        return ex.StatusCode switch
        {
            HttpStatusCode.NotFound => true,       // 404 - Replication lag
            HttpStatusCode.ServiceUnavailable => true, // 503 - Transient service issue
            _ => false
        };
    }

    private static string GetStatusCode(Exception? exception)
    {
        return exception switch
        {
            StorageRetryableException sre => sre.StatusCode.ToString(),
            HttpRequestException hre when hre.StatusCode.HasValue => ((int)hre.StatusCode.Value).ToString(),
            _ => "N/A"
        };
    }
}

/// <summary>
/// Exception indicating a storage operation failed but can be retried.
/// </summary>
/// <remarks>
/// Throw this exception from storage operations when the failure is due to
/// a transient condition (like replication lag) that may resolve with retry.
/// </remarks>
public class StorageRetryableException : Exception
{
    /// <summary>
    /// HTTP status code if applicable (404, 503, etc.)
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// The document ID that failed to be stored.
    /// </summary>
    public Guid? DocumentId { get; }

    /// <summary>
    /// Creates a new StorageRetryableException.
    /// </summary>
    public StorageRetryableException(
        string message,
        HttpStatusCode statusCode,
        Guid? documentId = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        DocumentId = documentId;
    }

    /// <summary>
    /// Creates a StorageRetryableException for a document not found scenario.
    /// </summary>
    public static StorageRetryableException DocumentNotFound(Guid documentId, Exception? innerException = null)
        => new(
            $"Document {documentId} not found. May be due to replication lag.",
            HttpStatusCode.NotFound,
            documentId,
            innerException);

    /// <summary>
    /// Creates a StorageRetryableException for a service unavailable scenario.
    /// </summary>
    public static StorageRetryableException ServiceUnavailable(string message, Exception? innerException = null)
        => new(
            message,
            HttpStatusCode.ServiceUnavailable,
            null,
            innerException);
}

/// <summary>
/// Interface for storage retry policy operations.
/// </summary>
public interface IStorageRetryPolicy
{
    /// <summary>
    /// Executes an action with retry policy for storage operations.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="action">The async action to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the action.</returns>
    Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an action with retry policy for storage operations (void return).
    /// </summary>
    /// <param name="action">The async action to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default);
}
