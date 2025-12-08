using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Interface for document summarization service.
/// Enables unit testing of endpoints that depend on summarization.
/// </summary>
public interface ISummarizeService
{
    /// <summary>
    /// Summarize a document with streaming output.
    /// Use this for real-time SSE streaming to the browser.
    /// </summary>
    /// <param name="request">The summarization request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of SummarizeChunk for streaming.</returns>
    IAsyncEnumerable<SummarizeChunk> SummarizeStreamAsync(
        SummarizeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Summarize a document without streaming.
    /// Use this for background job processing.
    /// </summary>
    /// <param name="request">The summarization request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The summarization result.</returns>
    Task<SummarizeResult> SummarizeAsync(
        SummarizeRequest request,
        CancellationToken cancellationToken = default);
}
