using System.ClientModel;
using System.Runtime.CompilerServices;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using Polly;
using Polly.CircuitBreaker;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Exception thrown when the circuit breaker is open due to repeated OpenAI failures.
/// Callers should return HTTP 503 Service Unavailable when catching this.
/// </summary>
public class OpenAiCircuitBrokenException : Exception
{
    public TimeSpan RetryAfter { get; }

    public OpenAiCircuitBrokenException(TimeSpan retryAfter)
        : base($"OpenAI service is temporarily unavailable. Retry after {retryAfter.TotalSeconds:F0} seconds.")
    {
        RetryAfter = retryAfter;
    }

    public OpenAiCircuitBrokenException(string message, TimeSpan retryAfter)
        : base(message)
    {
        RetryAfter = retryAfter;
    }
}

/// <summary>
/// Wrapper for Azure OpenAI client providing streaming and non-streaming completions.
/// Supports both Spaarke-hosted and Customer-hosted BYOK deployments.
/// Includes circuit breaker for resilience (Task 072).
/// </summary>
public class OpenAiClient : IOpenAiClient
{
    private readonly AzureOpenAIClient _client;
    private readonly DocumentIntelligenceOptions _options;
    private readonly ILogger<OpenAiClient> _logger;
    private readonly ResiliencePipeline _circuitBreaker;

    // Circuit breaker configuration (Task 072)
    private const int FailureThreshold = 5;       // Open after 5 failures
    private static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(30); // Half-open after 30s
    private const double FailureRatio = 0.5;      // 50% failure ratio to trip
    private const int MinimumThroughput = 5;      // Minimum calls before tripping

    public OpenAiClient(IOptions<DocumentIntelligenceOptions> options, ILogger<OpenAiClient> logger)
    {
        _options = options.Value;
        _logger = logger;

        var endpoint = new Uri(_options.OpenAiEndpoint);
        var credential = new AzureKeyCredential(_options.OpenAiKey);
        _client = new AzureOpenAIClient(endpoint, credential);

        // Build circuit breaker pipeline (Polly 8.x)
        _circuitBreaker = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = FailureRatio,
                MinimumThroughput = MinimumThroughput,
                SamplingDuration = TimeSpan.FromMinutes(1),
                BreakDuration = BreakDuration,
                OnOpened = args =>
                {
                    _logger.LogWarning(
                        "OpenAI circuit breaker OPENED after {FailureCount} failures. " +
                        "Will retry after {BreakDuration}s. Outcome: {Outcome}",
                        MinimumThroughput,
                        BreakDuration.TotalSeconds,
                        args.Outcome.Exception?.Message ?? "unknown");
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    _logger.LogInformation("OpenAI circuit breaker CLOSED. Service recovered.");
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    _logger.LogInformation("OpenAI circuit breaker HALF-OPEN. Testing service availability.");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    /// <summary>
    /// Stream completion tokens as they are generated.
    /// Use this for real-time UI updates in the browser.
    /// Protected by circuit breaker - throws OpenAiCircuitBrokenException when open.
    /// </summary>
    /// <param name="prompt">The prompt to send to the model.</param>
    /// <param name="model">Optional model override. Defaults to DocumentIntelligenceOptions.SummarizeModel.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of content chunks.</returns>
    /// <exception cref="OpenAiCircuitBrokenException">Thrown when circuit breaker is open.</exception>
    public async IAsyncEnumerable<string> StreamCompletionAsync(
        string prompt,
        string? model = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var deploymentName = model ?? _options.SummarizeModel;
        var chatClient = _client.GetChatClient(deploymentName);

        var chatOptions = new ChatCompletionOptions
        {
            MaxOutputTokenCount = _options.MaxOutputTokens,
            Temperature = _options.Temperature
        };

        var messages = new List<ChatMessage>
        {
            new UserChatMessage(prompt)
        };

        _logger.LogDebug(
            "Starting streaming completion with model {Model}, MaxTokens={MaxTokens}, Temp={Temperature}",
            deploymentName, _options.MaxOutputTokens, _options.Temperature);

        // Circuit breaker: wrap the initial call that starts the stream
        // Note: CompleteChatStreamingAsync returns synchronously, async is in iteration
        AsyncCollectionResult<StreamingChatCompletionUpdate> streamingResult;
        try
        {
            streamingResult = await _circuitBreaker.ExecuteAsync(ct =>
            {
                var result = chatClient.CompleteChatStreamingAsync(messages, chatOptions, ct);
                return ValueTask.FromResult(result);
            }, cancellationToken);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("OpenAI circuit breaker is open. Rejecting streaming request.");
            throw new OpenAiCircuitBrokenException(BreakDuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start streaming completion with model {Model}", deploymentName);
            throw;
        }

        await foreach (var update in streamingResult.WithCancellation(cancellationToken))
        {
            foreach (var contentPart in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(contentPart.Text))
                {
                    yield return contentPart.Text;
                }
            }
        }

        _logger.LogDebug("Streaming completion finished for model {Model}", deploymentName);
    }

    /// <summary>
    /// Get a complete response (non-streaming).
    /// Use this for background job processing where streaming isn't needed.
    /// Protected by circuit breaker - throws OpenAiCircuitBrokenException when open.
    /// </summary>
    /// <param name="prompt">The prompt to send to the model.</param>
    /// <param name="model">Optional model override. Defaults to DocumentIntelligenceOptions.SummarizeModel.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The complete response text.</returns>
    /// <exception cref="OpenAiCircuitBrokenException">Thrown when circuit breaker is open.</exception>
    public async Task<string> GetCompletionAsync(
        string prompt,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        var deploymentName = model ?? _options.SummarizeModel;
        var chatClient = _client.GetChatClient(deploymentName);

        var chatOptions = new ChatCompletionOptions
        {
            MaxOutputTokenCount = _options.MaxOutputTokens,
            Temperature = _options.Temperature
        };

        var messages = new List<ChatMessage>
        {
            new UserChatMessage(prompt)
        };

        _logger.LogDebug(
            "Starting completion with model {Model}, MaxTokens={MaxTokens}, Temp={Temperature}",
            deploymentName, _options.MaxOutputTokens, _options.Temperature);

        try
        {
            // Circuit breaker: wrap the completion call
            var response = await _circuitBreaker.ExecuteAsync(async ct =>
            {
                return await chatClient.CompleteChatAsync(messages, chatOptions, ct);
            }, cancellationToken);

            var content = response.Value.Content.FirstOrDefault()?.Text ?? string.Empty;

            _logger.LogDebug(
                "Completion finished for model {Model}, ResponseLength={Length}",
                deploymentName, content.Length);

            return content;
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("OpenAI circuit breaker is open. Rejecting completion request.");
            throw new OpenAiCircuitBrokenException(BreakDuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get completion with model {Model}", deploymentName);
            throw;
        }
    }

    /// <summary>
    /// Stream completion for image/vision analysis using multimodal model.
    /// Use this for Phase 2 image summarization.
    /// Protected by circuit breaker - throws OpenAiCircuitBrokenException when open.
    /// </summary>
    /// <param name="prompt">The prompt describing what to analyze.</param>
    /// <param name="imageBytes">The image bytes.</param>
    /// <param name="mediaType">The image media type (e.g., "image/png", "image/jpeg").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of content chunks.</returns>
    /// <exception cref="OpenAiCircuitBrokenException">Thrown when circuit breaker is open.</exception>
    public async IAsyncEnumerable<string> StreamVisionCompletionAsync(
        string prompt,
        byte[] imageBytes,
        string mediaType,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var deploymentName = _options.ImageSummarizeModel ?? _options.SummarizeModel;
        var chatClient = _client.GetChatClient(deploymentName);

        var chatOptions = new ChatCompletionOptions
        {
            MaxOutputTokenCount = _options.MaxOutputTokens,
            Temperature = _options.Temperature
        };

        var imageData = BinaryData.FromBytes(imageBytes);
        var imagePart = ChatMessageContentPart.CreateImagePart(imageData, mediaType);
        var textPart = ChatMessageContentPart.CreateTextPart(prompt);

        var messages = new List<ChatMessage>
        {
            new UserChatMessage(textPart, imagePart)
        };

        _logger.LogDebug(
            "Starting vision streaming completion with model {Model}, ImageSize={Size}KB",
            deploymentName, imageBytes.Length / 1024);

        // Circuit breaker: wrap the initial call that starts the stream
        // Note: CompleteChatStreamingAsync returns synchronously, async is in iteration
        AsyncCollectionResult<StreamingChatCompletionUpdate> streamingResult;
        try
        {
            streamingResult = await _circuitBreaker.ExecuteAsync(ct =>
            {
                var result = chatClient.CompleteChatStreamingAsync(messages, chatOptions, ct);
                return ValueTask.FromResult(result);
            }, cancellationToken);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("OpenAI circuit breaker is open. Rejecting vision streaming request.");
            throw new OpenAiCircuitBrokenException(BreakDuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start vision streaming completion with model {Model}", deploymentName);
            throw;
        }

        await foreach (var update in streamingResult.WithCancellation(cancellationToken))
        {
            foreach (var contentPart in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(contentPart.Text))
                {
                    yield return contentPart.Text;
                }
            }
        }

        _logger.LogDebug("Vision streaming completion finished for model {Model}", deploymentName);
    }

    /// <summary>
    /// Get a complete response for image/vision analysis (non-streaming).
    /// Use this for background job processing of image files.
    /// Protected by circuit breaker - throws OpenAiCircuitBrokenException when open.
    /// </summary>
    /// <param name="prompt">The prompt describing what to analyze.</param>
    /// <param name="imageBytes">The image bytes.</param>
    /// <param name="mediaType">The image media type (e.g., "image/png", "image/jpeg").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The complete response text.</returns>
    /// <exception cref="OpenAiCircuitBrokenException">Thrown when circuit breaker is open.</exception>
    public async Task<string> GetVisionCompletionAsync(
        string prompt,
        byte[] imageBytes,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        var deploymentName = _options.ImageSummarizeModel ?? _options.SummarizeModel;
        var chatClient = _client.GetChatClient(deploymentName);

        var chatOptions = new ChatCompletionOptions
        {
            MaxOutputTokenCount = _options.MaxOutputTokens,
            Temperature = _options.Temperature
        };

        var imageData = BinaryData.FromBytes(imageBytes);
        var imagePart = ChatMessageContentPart.CreateImagePart(imageData, mediaType);
        var textPart = ChatMessageContentPart.CreateTextPart(prompt);

        var messages = new List<ChatMessage>
        {
            new UserChatMessage(textPart, imagePart)
        };

        _logger.LogDebug(
            "Starting vision completion with model {Model}, ImageSize={Size}KB",
            deploymentName, imageBytes.Length / 1024);

        try
        {
            // Circuit breaker: wrap the completion call
            var response = await _circuitBreaker.ExecuteAsync(async ct =>
            {
                return await chatClient.CompleteChatAsync(messages, chatOptions, ct);
            }, cancellationToken);

            var content = response.Value.Content.FirstOrDefault()?.Text ?? string.Empty;

            _logger.LogDebug(
                "Vision completion finished for model {Model}, ResponseLength={Length}",
                deploymentName, content.Length);

            return content;
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("OpenAI circuit breaker is open. Rejecting vision completion request.");
            throw new OpenAiCircuitBrokenException(BreakDuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get vision completion with model {Model}", deploymentName);
            throw;
        }
    }
}
