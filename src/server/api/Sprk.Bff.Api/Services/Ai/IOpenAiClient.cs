namespace Sprk.Bff.Api.Services.Ai;

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
    /// Uses text-embedding-3-small (1536 dimensions) by default.
    /// </summary>
    /// <param name="text">The text to generate embeddings for.</param>
    /// <param name="model">Optional model override. Defaults to text-embedding-3-small.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Vector embedding as float array.</returns>
    Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        string? model = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate vector embeddings for multiple texts in a batch.
    /// More efficient than individual calls for bulk operations.
    /// </summary>
    /// <param name="texts">The texts to generate embeddings for.</param>
    /// <param name="model">Optional model override. Defaults to text-embedding-3-small.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of vector embeddings in same order as input texts.</returns>
    Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        string? model = null,
        CancellationToken cancellationToken = default);
}
