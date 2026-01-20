using OpenAI.Chat;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Result of a chat completion request with tools.
/// </summary>
public record ChatCompletionResult
{
    /// <summary>Text response from the model (null if tool calls are returned).</summary>
    public string? Content { get; init; }

    /// <summary>Tool calls requested by the model (empty if content is returned).</summary>
    public IReadOnlyList<ChatToolCall> ToolCalls { get; init; } = [];

    /// <summary>The finish reason indicating why the model stopped.</summary>
    public ChatFinishReason FinishReason { get; init; }

    /// <summary>Whether the model wants to call tools.</summary>
    public bool HasToolCalls => ToolCalls.Count > 0;
}

/// <summary>
/// Interface for OpenAI client operations.
/// Enables unit testing of services that depend on AI completions.
/// </summary>
public interface IOpenAiClient
{
    /// <summary>
    /// Stream completion tokens as they are generated.
    /// </summary>
    IAsyncEnumerable<string> StreamCompletionAsync(
        string prompt,
        string? model = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a complete response (non-streaming).
    /// </summary>
    Task<string> GetCompletionAsync(
        string prompt,
        string? model = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream completion for image/vision analysis using multimodal model.
    /// </summary>
    IAsyncEnumerable<string> StreamVisionCompletionAsync(
        string prompt,
        byte[] imageBytes,
        string mediaType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a complete response for image/vision analysis (non-streaming).
    /// </summary>
    Task<string> GetVisionCompletionAsync(
        string prompt,
        byte[] imageBytes,
        string mediaType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate vector embeddings for text content.
    /// Uses configured embedding model and dimensions (default: text-embedding-3-large, 3072 dims).
    /// </summary>
    /// <param name="text">The text to generate embeddings for.</param>
    /// <param name="model">Optional model override. Defaults to configured EmbeddingModel.</param>
    /// <param name="dimensions">Optional dimensions override. Defaults to configured EmbeddingDimensions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Vector embedding as float array.</returns>
    Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        string? model = null,
        int? dimensions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate vector embeddings for multiple texts in a batch.
    /// More efficient than individual calls for bulk operations.
    /// </summary>
    /// <param name="texts">The texts to generate embeddings for.</param>
    /// <param name="model">Optional model override. Defaults to configured EmbeddingModel.</param>
    /// <param name="dimensions">Optional dimensions override. Defaults to configured EmbeddingDimensions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of vector embeddings in same order as input texts.</returns>
    Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        string? model = null,
        int? dimensions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a chat completion with function calling tools.
    /// Used for agentic workflows where the model can call tools.
    /// </summary>
    /// <param name="messages">The conversation messages including system, user, assistant, and tool messages.</param>
    /// <param name="tools">The available tools the model can call.</param>
    /// <param name="model">Optional model override. Defaults to configured model.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Chat completion result with either content or tool calls.</returns>
    Task<ChatCompletionResult> GetChatCompletionWithToolsAsync(
        IEnumerable<ChatMessage> messages,
        IEnumerable<ChatTool> tools,
        string? model = null,
        CancellationToken cancellationToken = default);
}
