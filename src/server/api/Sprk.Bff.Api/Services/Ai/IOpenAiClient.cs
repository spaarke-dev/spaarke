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
    /// Generate embedding vector for text using text-embedding-3-small.
    /// Used for RAG indexing and vector search.
    /// </summary>
    /// <param name="text">Text to embed.</param>
    /// <param name="model">Optional model override. Defaults to text-embedding-3-small.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Embedding vector (1536 dimensions for text-embedding-3-small).</returns>
    Task<float[]> GenerateEmbeddingAsync(
        string text,
        string? model = null,
        CancellationToken cancellationToken = default);
}
