using System.ClientModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using Polly;
using Polly.CircuitBreaker;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Resilience;
using ResilenceCircuitState = Sprk.Bff.Api.Infrastructure.Resilience.CircuitState;

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
    private readonly ICircuitBreakerRegistry? _circuitRegistry;
    private readonly ResiliencePipeline _circuitBreaker;
    private readonly Sprk.Bff.Api.Telemetry.AiTelemetry? _aiTelemetry;

    // Circuit breaker configuration (Task 072)
    private const int FailureThreshold = 5;       // Open after 5 failures
    private static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(30); // Half-open after 30s
    private const double FailureRatio = 0.5;      // 50% failure ratio to trip
    private const int MinimumThroughput = 5;      // Minimum calls before tripping

    public OpenAiClient(
        IOptions<DocumentIntelligenceOptions> options,
        ILogger<OpenAiClient> logger,
        ICircuitBreakerRegistry? circuitRegistry = null,
        Sprk.Bff.Api.Telemetry.AiTelemetry? aiTelemetry = null)
    {
        _options = options.Value;
        _logger = logger;
        _circuitRegistry = circuitRegistry;
        _aiTelemetry = aiTelemetry;

        var endpoint = new Uri(_options.OpenAiEndpoint);
        var credential = new AzureKeyCredential(_options.OpenAiKey);
        // Raise the per-attempt network timeout above the SDK default (100s). The Reasoning tier
        // (gpt-5-reasoning) routinely needs 2-4 minutes on a full document; at 100s the call is
        // cancelled + retried 3× and surfaces to the user as "couldn't run that action" (observed
        // 2026-07-27 on the HELIO NDA review). Fast/Standard tiers finish well within this, so one
        // global value is safe. Configurable via DocumentIntelligence:OpenAiNetworkTimeoutSeconds.
        var clientOptions = new AzureOpenAIClientOptions
        {
            NetworkTimeout = TimeSpan.FromSeconds(_options.OpenAiNetworkTimeoutSeconds),
        };
        _client = new AzureOpenAIClient(endpoint, credential, clientOptions);

        // Register with circuit breaker registry
        _circuitRegistry?.RegisterCircuit(CircuitBreakerRegistry.AzureOpenAI);

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
                    _circuitRegistry?.RecordStateChange(
                        CircuitBreakerRegistry.AzureOpenAI,
                        ResilenceCircuitState.Open,
                        BreakDuration);
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    _logger.LogInformation("OpenAI circuit breaker CLOSED. Service recovered.");
                    _circuitRegistry?.RecordStateChange(
                        CircuitBreakerRegistry.AzureOpenAI,
                        ResilenceCircuitState.Closed);
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    _logger.LogInformation("OpenAI circuit breaker HALF-OPEN. Testing service availability.");
                    _circuitRegistry?.RecordStateChange(
                        CircuitBreakerRegistry.AzureOpenAI,
                        ResilenceCircuitState.HalfOpen);
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
        int? maxOutputTokens = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var deploymentName = model ?? _options.SummarizeModel;
        var effectiveMaxTokens = maxOutputTokens ?? _options.MaxOutputTokens;
        var chatClient = _client.GetChatClient(deploymentName);

        var chatOptions = new ChatCompletionOptions
        {
            MaxOutputTokenCount = effectiveMaxTokens,
            Temperature = _options.Temperature
        };

        var messages = new List<ChatMessage>
        {
            new UserChatMessage(prompt)
        };

        _logger.LogDebug(
            "Starting streaming completion with model {Model}, MaxTokens={MaxTokens}, Temp={Temperature}",
            deploymentName, effectiveMaxTokens, _options.Temperature);

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
        int? maxOutputTokens = null,
        CancellationToken cancellationToken = default)
    {
        var deploymentName = model ?? _options.SummarizeModel;
        var effectiveMaxTokens = maxOutputTokens ?? _options.MaxOutputTokens;
        var chatClient = _client.GetChatClient(deploymentName);

        var chatOptions = new ChatCompletionOptions
        {
            MaxOutputTokenCount = effectiveMaxTokens,
            Temperature = _options.Temperature
        };

        var messages = new List<ChatMessage>
        {
            new UserChatMessage(prompt)
        };

        _logger.LogDebug(
            "Starting completion with model {Model}, MaxTokens={MaxTokens}, Temp={Temperature}",
            deploymentName, effectiveMaxTokens, _options.Temperature);

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

    /// <summary>
    /// Generate vector embeddings for text content.
    /// Uses configured embedding model and dimensions (default: text-embedding-3-large, 3072 dims).
    /// Protected by circuit breaker - throws OpenAiCircuitBrokenException when open.
    /// </summary>
    /// <param name="text">The text to generate embeddings for.</param>
    /// <param name="model">Optional model override. Defaults to configured EmbeddingModel.</param>
    /// <param name="dimensions">Optional dimensions override. Defaults to configured EmbeddingDimensions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Vector embedding as float array.</returns>
    /// <exception cref="OpenAiCircuitBrokenException">Thrown when circuit breaker is open.</exception>
    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        string? model = null,
        int? dimensions = null,
        CancellationToken cancellationToken = default)
    {
        var deploymentName = model ?? _options.EmbeddingModel;
        var embeddingDimensions = dimensions ?? _options.EmbeddingDimensions;
        var embeddingClient = _client.GetEmbeddingClient(deploymentName);

        _logger.LogDebug("Generating embedding with model {Model}, Dimensions={Dims}, TextLength={Length}",
            deploymentName, embeddingDimensions, text.Length);

        try
        {
            var embeddingOptions = new OpenAI.Embeddings.EmbeddingGenerationOptions
            {
                Dimensions = embeddingDimensions
            };

            var response = await _circuitBreaker.ExecuteAsync(async ct =>
            {
                return await embeddingClient.GenerateEmbeddingAsync(text, embeddingOptions, ct);
            }, cancellationToken);

            _logger.LogDebug("Embedding generated with model {Model}, Dimensions={Dims}",
                deploymentName, response.Value.ToFloats().Length);

            return response.Value.ToFloats();
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("OpenAI circuit breaker is open. Rejecting embedding request.");
            throw new OpenAiCircuitBrokenException(BreakDuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embedding with model {Model}", deploymentName);
            throw;
        }
    }

    /// <summary>
    /// Generate vector embeddings for multiple texts in a batch.
    /// More efficient than individual calls for bulk operations.
    /// Protected by circuit breaker - throws OpenAiCircuitBrokenException when open.
    /// </summary>
    /// <param name="texts">The texts to generate embeddings for.</param>
    /// <param name="model">Optional model override. Defaults to configured EmbeddingModel.</param>
    /// <param name="dimensions">Optional dimensions override. Defaults to configured EmbeddingDimensions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of vector embeddings in same order as input texts.</returns>
    /// <exception cref="OpenAiCircuitBrokenException">Thrown when circuit breaker is open.</exception>
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        string? model = null,
        int? dimensions = null,
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        var deploymentName = model ?? _options.EmbeddingModel;
        var embeddingDimensions = dimensions ?? _options.EmbeddingDimensions;
        var embeddingClient = _client.GetEmbeddingClient(deploymentName);

        _logger.LogDebug("Generating batch embeddings with model {Model}, Dimensions={Dims}, Count={Count}",
            deploymentName, embeddingDimensions, textList.Count);

        try
        {
            var embeddingOptions = new OpenAI.Embeddings.EmbeddingGenerationOptions
            {
                Dimensions = embeddingDimensions
            };

            var response = await _circuitBreaker.ExecuteAsync(async ct =>
            {
                return await embeddingClient.GenerateEmbeddingsAsync(textList, embeddingOptions, ct);
            }, cancellationToken);

            var embeddings = response.Value
                .OrderBy(e => e.Index)
                .Select(e => e.ToFloats())
                .ToList();

            _logger.LogDebug("Batch embeddings generated with model {Model}, Count={Count}, Dimensions={Dims}",
                deploymentName, embeddings.Count, embeddings.FirstOrDefault().Length);

            return embeddings;
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("OpenAI circuit breaker is open. Rejecting batch embedding request.");
            throw new OpenAiCircuitBrokenException(BreakDuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate batch embeddings with model {Model}", deploymentName);
            throw;
        }
    }

    /// <summary>
    /// Execute a chat completion with function calling tools.
    /// Used for agentic workflows where the model can call tools.
    /// Protected by circuit breaker - throws OpenAiCircuitBrokenException when open.
    /// </summary>
    /// <param name="messages">The conversation messages including system, user, assistant, and tool messages.</param>
    /// <param name="tools">The available tools the model can call.</param>
    /// <param name="model">Optional model override. Defaults to configured model.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Chat completion result with either content or tool calls.</returns>
    /// <exception cref="OpenAiCircuitBrokenException">Thrown when circuit breaker is open.</exception>
    public async Task<ChatCompletionResult> GetChatCompletionWithToolsAsync(
        IEnumerable<ChatMessage> messages,
        IEnumerable<ChatTool> tools,
        string? model = null,
        int? maxOutputTokens = null,
        CancellationToken cancellationToken = default)
    {
        var deploymentName = model ?? _options.SummarizeModel;
        var effectiveMaxTokens = maxOutputTokens ?? _options.MaxOutputTokens;
        var chatClient = _client.GetChatClient(deploymentName);

        var chatOptions = new ChatCompletionOptions
        {
            MaxOutputTokenCount = effectiveMaxTokens,
            Temperature = _options.Temperature
        };

        // Add tools to the options
        foreach (var tool in tools)
        {
            chatOptions.Tools.Add(tool);
        }

        var messageList = messages.ToList();

        _logger.LogDebug(
            "Starting chat completion with tools. Model={Model}, MessageCount={MessageCount}, ToolCount={ToolCount}",
            deploymentName, messageList.Count, chatOptions.Tools.Count);

        try
        {
            var response = await _circuitBreaker.ExecuteAsync(async ct =>
            {
                return await chatClient.CompleteChatAsync(messageList, chatOptions, ct);
            }, cancellationToken);

            var completion = response.Value;
            RecordExecutorTokenUsage(completion, deploymentName);

            // Check if the model wants to call tools
            if (completion.FinishReason == ChatFinishReason.ToolCalls && completion.ToolCalls.Count > 0)
            {
                _logger.LogInformation(
                    "Chat completion returned {ToolCallCount} tool calls",
                    completion.ToolCalls.Count);

                return new ChatCompletionResult
                {
                    Content = null,
                    ToolCalls = completion.ToolCalls.ToList(),
                    FinishReason = completion.FinishReason
                };
            }

            // Model returned content (no tool calls)
            var content = completion.Content.FirstOrDefault()?.Text ?? string.Empty;

            _logger.LogDebug(
                "Chat completion finished. Model={Model}, ResponseLength={Length}, FinishReason={FinishReason}",
                deploymentName, content.Length, completion.FinishReason);

            return new ChatCompletionResult
            {
                Content = content,
                ToolCalls = [],
                FinishReason = completion.FinishReason
            };
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("OpenAI circuit breaker is open. Rejecting chat completion with tools request.");
            throw new OpenAiCircuitBrokenException(BreakDuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get chat completion with tools. Model={Model}", deploymentName);
            throw;
        }
    }

    /// <summary>
    /// Record the model-reported token usage of an executor-path completion into the
    /// per-tenant metering counter (FR-P4-05 / NFR-05, spaarke-ai-architecture-redesign-r1
    /// task 054). Tenant/user/entry-path attribution comes from the ambient
    /// <see cref="Telemetry.AiMeteringContext"/> scope set at the entry seams (Event /
    /// Click / Text / coded). Counts only — never content (NFR-07 / ADR-015). No-op when
    /// telemetry is not injected (tests) or usage is absent.
    /// </summary>
    private void RecordExecutorTokenUsage(ChatCompletion completion, string deploymentName)
    {
        var usage = completion?.Usage;
        if (usage is null || _aiTelemetry is null)
        {
            return;
        }

        _aiTelemetry.RecordMeteredTokens(
            tenantId: null,   // resolved from AiMeteringContext.Current
            userId: null,
            inputTokens: usage.InputTokenCount,
            outputTokens: usage.OutputTokenCount,
            source: "executor",
            model: deploymentName);
    }

    /// <summary>
    /// JSON serializer options for deserializing structured completion responses.
    /// Uses Web defaults: camelCase property names, case-insensitive matching.
    /// </summary>
    private static readonly JsonSerializerOptions s_structuredJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Get a structured completion that conforms to a JSON schema.
    /// Uses constrained decoding (response_format: json_schema) to guarantee valid JSON output.
    /// Temperature is set to 0 for deterministic classification/extraction results.
    /// Protected by circuit breaker - throws OpenAiCircuitBrokenException when open.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the response into.</typeparam>
    /// <param name="messages">The conversation messages (system + user prompts).</param>
    /// <param name="jsonSchema">The JSON schema that the response must conform to.</param>
    /// <param name="schemaName">A name identifying the schema (e.g., "ClassificationResult").</param>
    /// <param name="deploymentName">The Azure OpenAI deployment name (e.g., gpt-4o-mini, gpt-4o).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response of type T.</returns>
    /// <exception cref="OpenAiCircuitBrokenException">Thrown when circuit breaker is open.</exception>
    /// <exception cref="InvalidOperationException">Thrown when response is empty or deserialization fails.</exception>
    public async Task<T> GetStructuredCompletionAsync<T>(
        IEnumerable<ChatMessage> messages,
        BinaryData jsonSchema,
        string schemaName,
        string deploymentName,
        CancellationToken cancellationToken = default)
    {
        var chatClient = _client.GetChatClient(deploymentName);

        var chatOptions = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                schemaName,
                jsonSchema,
                jsonSchemaIsStrict: true),
            Temperature = 0f
        };

        var messageList = messages.ToList();

        _logger.LogDebug(
            "Starting structured completion. Model={Model}, Schema={Schema}, MessageCount={MessageCount}",
            deploymentName, schemaName, messageList.Count);

        try
        {
            var response = await _circuitBreaker.ExecuteAsync(async ct =>
            {
                return await chatClient.CompleteChatAsync(messageList, chatOptions, ct);
            }, cancellationToken);

            RecordExecutorTokenUsage(response.Value, deploymentName);
            var content = response.Value.Content.FirstOrDefault()?.Text;

            if (string.IsNullOrEmpty(content))
            {
                throw new InvalidOperationException(
                    $"Structured completion returned empty content for schema '{schemaName}'.");
            }

            _logger.LogDebug(
                "Structured completion finished. Model={Model}, Schema={Schema}, ResponseLength={Length}",
                deploymentName, schemaName, content.Length);

            return JsonSerializer.Deserialize<T>(content, s_structuredJsonOptions)
                ?? throw new InvalidOperationException(
                    $"Structured completion deserialized to null for schema '{schemaName}'.");
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("OpenAI circuit breaker is open. Rejecting structured completion request.");
            throw new OpenAiCircuitBrokenException(BreakDuration);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "Failed to deserialize structured completion response. Schema={Schema}",
                schemaName);
            throw new InvalidOperationException(
                $"Failed to deserialize structured completion response for schema '{schemaName}'.", ex);
        }
        catch (OpenAiCircuitBrokenException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to get structured completion. Model={Model}, Schema={Schema}",
                deploymentName, schemaName);
            throw;
        }
    }

    /// <summary>
    /// Get a structured completion as raw JSON string that conforms to a JSON schema.
    /// Uses constrained decoding (response_format: json_schema) to guarantee valid JSON output.
    /// Unlike the generic overload, this returns the raw JSON string without deserialization,
    /// suitable for dynamic schemas resolved at runtime (e.g., from JPS prompt definitions).
    /// Protected by circuit breaker - throws OpenAiCircuitBrokenException when open.
    /// </summary>
    /// <param name="prompt">The prompt text to send to the model.</param>
    /// <param name="jsonSchema">The JSON schema that the response must conform to.</param>
    /// <param name="schemaName">A name identifying the schema (e.g., "prompt_response").</param>
    /// <param name="model">Optional model override. Defaults to configured SummarizeModel.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw JSON string response conforming to the schema.</returns>
    /// <exception cref="OpenAiCircuitBrokenException">Thrown when circuit breaker is open.</exception>
    /// <exception cref="InvalidOperationException">Thrown when response is empty.</exception>
    /// <summary>
    /// True when <paramref name="deploymentName"/> is the configured Reasoning-tier deployment
    /// (<see cref="DocumentIntelligenceOptions.ReasoningModel"/>). Reasoning models (o-series / gpt-5)
    /// require a distinct request shape — see <see cref="ResolveEffectiveTemperature"/> and the
    /// max-output-tokens handling in <see cref="GetStructuredCompletionRawAsync"/>.
    /// </summary>
    /// <remarks>
    /// ai-advanced-capabilities-nda-r1 follow-up (post-UAT). A config-grounded, deployment-name signal
    /// (ADR-039: tier→deployment mapping is config, not catalog) so it covers every caller of this client,
    /// not just the ActionRunner path. <c>internal static</c> + pure so the decision is unit-testable
    /// without a live Azure call (the actual on-the-wire omission is only verifiable end-to-end).
    /// </remarks>
    internal static bool IsReasoningDeployment(string deploymentName, string? reasoningModel) =>
        !string.IsNullOrWhiteSpace(reasoningModel)
        && string.Equals(deploymentName, reasoningModel, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the temperature value to send on a structured completion, or <c>null</c> to OMIT the
    /// <c>temperature</c> parameter from the request entirely.
    /// </summary>
    /// <remarks>
    /// Reasoning-tier deployments (o-series / gpt-5) REJECT any non-default temperature — the Azure OpenAI
    /// API returns 400 <c>"Only the default (1) value is supported"</c> when <c>temperature</c> is present
    /// at all, even at <c>0.0</c>. So for the configured reasoning model the request must omit the
    /// parameter (this returns <c>null</c>); every other model keeps the deterministic-structured default
    /// (<c>0.0</c> when the caller passes nothing).
    /// </remarks>
    internal static float? ResolveEffectiveTemperature(
        string deploymentName,
        string? reasoningModel,
        float? requestedTemperature)
    {
        if (IsReasoningDeployment(deploymentName, reasoningModel))
        {
            return null; // reasoning model — omit temperature
        }

        // Wave B-G9c1 (B6): non-reasoning structured output defaults to 0.0 for determinism,
        // matching GetStructuredCompletionAsync<T> / StreamStructuredCompletionAsync.
        return requestedTemperature ?? 0.0f;
    }

    public async Task<string> GetStructuredCompletionRawAsync(
        string prompt,
        BinaryData jsonSchema,
        string schemaName,
        string? model = null,
        int? maxOutputTokens = null,
        float? temperature = null,
        CancellationToken cancellationToken = default)
    {
        var deploymentName = model ?? _options.SummarizeModel;
        var effectiveMaxTokens = maxOutputTokens ?? _options.MaxOutputTokens;
        var isReasoning = IsReasoningDeployment(deploymentName, _options.ReasoningModel);
        // Callers (typically the 8 tool handlers that resolve a per-action override from
        // sprk_analysisaction.sprk_temperature) pass an explicit value when non-deterministic output is
        // desired. Reasoning-tier deployments (gpt-5 / o-series) omit it (ResolveEffectiveTemperature → null).
        var effectiveTemperature = ResolveEffectiveTemperature(deploymentName, _options.ReasoningModel, temperature);
        var chatClient = _client.GetChatClient(deploymentName);

        var chatOptions = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                schemaName,
                jsonSchema,
                jsonSchemaIsStrict: true),
        };

        // Reasoning models (gpt-5 / o-series) reject BOTH knobs, so the request must OMIT each:
        //  - temperature: any value (even 0.0) → 400 "Only the default (1) value is supported".
        //  - max tokens: the OpenAI/Azure SDK serializes ChatCompletionOptions.MaxOutputTokenCount as
        //    `max_tokens` (verified live at api-version 2025-04-01-preview), which these models 400 with
        //    "Unsupported parameter: 'max_tokens' ... use 'max_completion_tokens' instead". There is no
        //    SDK surface here that emits max_completion_tokens, so we omit the cap entirely — the model
        //    uses its own default max_completion_tokens and the strict output schema bounds the JSON body.
        //    (This was the actual live "Sorry — I couldn't run that action" on the NDA Review path.)
        if (!isReasoning)
        {
            chatOptions.MaxOutputTokenCount = effectiveMaxTokens;
        }
        if (effectiveTemperature.HasValue)
        {
            chatOptions.Temperature = effectiveTemperature.Value;
        }

        var messages = new List<ChatMessage>
        {
            new UserChatMessage(prompt)
        };

        _logger.LogInformation(
            "Starting structured raw completion. Model={Model}, Schema={Schema}, SchemaJson={SchemaJson}",
            deploymentName, schemaName, jsonSchema.ToString());

        try
        {
            var response = await _circuitBreaker.ExecuteAsync(async ct =>
            {
                return await chatClient.CompleteChatAsync(messages, chatOptions, ct);
            }, cancellationToken);

            RecordExecutorTokenUsage(response.Value, deploymentName);
            var content = response.Value.Content.FirstOrDefault()?.Text;

            if (string.IsNullOrEmpty(content))
            {
                throw new InvalidOperationException(
                    $"Structured raw completion returned empty content for schema '{schemaName}'.");
            }

            _logger.LogDebug(
                "Structured raw completion finished. Model={Model}, Schema={Schema}, ResponseLength={Length}",
                deploymentName, schemaName, content.Length);

            return content;
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("OpenAI circuit breaker is open. Rejecting structured raw completion request.");
            throw new OpenAiCircuitBrokenException(BreakDuration);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to get structured raw completion. Model={Model}, Schema={Schema}, ErrorMessage={ErrorMessage}, SchemaJson={SchemaJson}",
                deploymentName, schemaName, ex.Message, jsonSchema.ToString());
            throw;
        }
    }

    /// <summary>
    /// Stream a structured completion (json_schema strict mode + streaming).
    /// Yields raw content-token strings as Azure OpenAI emits them; the caller is responsible
    /// for accumulating the tokens into JSON and feeding them to an incremental parser
    /// (see <c>Sprk.Bff.Api.Services.Ai.Streaming.IncrementalJsonParser</c>).
    ///
    /// Per the R5 task 006 spike (<c>notes/task-006-spike-results.md</c>), Azure OpenAI streams
    /// JSON char-by-char / word-by-word via <c>delta.content</c> in declaration order. Token
    /// granularity is ~3–8 chars; the first content event arrives in &lt;500ms (NFR-01 TTFB).
    ///
    /// Combines <see cref="ChatResponseFormat.CreateJsonSchemaFormat"/> with
    /// <c>chatClient.CompleteChatStreamingAsync</c>. Mirrors the iteration shape of
    /// <see cref="StreamCompletionAsync"/>; protected by the same circuit breaker.
    /// </summary>
    /// <param name="messages">Conversation messages (system + user prompts).</param>
    /// <param name="jsonSchema">JSON schema the response must conform to (strict mode enforced).</param>
    /// <param name="schemaName">Schema name (identifier; e.g., <c>"DocumentSummary"</c>).</param>
    /// <param name="model">Optional model override. Defaults to <c>DocumentIntelligenceOptions.SummarizeModel</c>.</param>
    /// <param name="maxOutputTokens">Optional max output tokens. Defaults to <c>DocumentIntelligenceOptions.MaxOutputTokens</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of content-token strings (the raw <c>delta.content</c> payload).</returns>
    /// <exception cref="OpenAiCircuitBrokenException">Thrown when the circuit breaker is open.</exception>
    public async IAsyncEnumerable<string> StreamStructuredCompletionAsync(
        IEnumerable<ChatMessage> messages,
        BinaryData jsonSchema,
        string schemaName,
        string? model = null,
        int? maxOutputTokens = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var deploymentName = model ?? _options.SummarizeModel;
        var effectiveMaxTokens = maxOutputTokens ?? _options.MaxOutputTokens;
        var chatClient = _client.GetChatClient(deploymentName);

        var chatOptions = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                schemaName,
                jsonSchema,
                jsonSchemaIsStrict: true),
            MaxOutputTokenCount = effectiveMaxTokens,
            Temperature = 0f
        };

        var messageList = messages.ToList();

        _logger.LogDebug(
            "Starting streaming structured completion. Model={Model}, Schema={Schema}, MessageCount={MessageCount}",
            deploymentName, schemaName, messageList.Count);

        AsyncCollectionResult<StreamingChatCompletionUpdate> streamingResult;
        try
        {
            streamingResult = await _circuitBreaker.ExecuteAsync(ct =>
            {
                var result = chatClient.CompleteChatStreamingAsync(messageList, chatOptions, ct);
                return ValueTask.FromResult(result);
            }, cancellationToken);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("OpenAI circuit breaker is open. Rejecting streaming structured completion request.");
            throw new OpenAiCircuitBrokenException(BreakDuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to start streaming structured completion. Model={Model}, Schema={Schema}",
                deploymentName, schemaName);
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

        _logger.LogDebug(
            "Streaming structured completion finished. Model={Model}, Schema={Schema}",
            deploymentName, schemaName);
    }
}
