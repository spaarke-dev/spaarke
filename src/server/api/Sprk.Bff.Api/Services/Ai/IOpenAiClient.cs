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
}
