using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Interface for document intelligence service.
/// Gateway to Azure AI Document Intelligence capabilities including summarization,
/// entity extraction, and structured metadata generation.
/// </summary>
public interface IDocumentIntelligenceService
{
    /// <summary>
    /// Analyze a document with streaming output using user context (OBO).
    /// Use this for real-time SSE streaming to the browser.
    /// </summary>
    /// <param name="httpContext">The HTTP context for user authentication (OBO).</param>
    /// <param name="request">The analysis request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of AnalysisChunk for streaming.</returns>
    IAsyncEnumerable<AnalysisChunk> AnalyzeStreamAsync(
        HttpContext httpContext,
        DocumentAnalysisRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze a document without streaming using app-only auth.
    /// Use this for background job processing where no user context is available.
    /// </summary>
    /// <param name="request">The analysis request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The analysis result.</returns>
    Task<AnalysisResult> AnalyzeAsync(
        DocumentAnalysisRequest request,
        CancellationToken cancellationToken = default);
}
