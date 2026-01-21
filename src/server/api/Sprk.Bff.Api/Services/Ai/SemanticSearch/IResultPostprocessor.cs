using Sprk.Bff.Api.Models.Ai.SemanticSearch;

namespace Sprk.Bff.Api.Services.Ai.SemanticSearch;

/// <summary>
/// Extensibility interface for result post-processing after search execution.
/// </summary>
/// <remarks>
/// <para>
/// This interface is an extensibility hook for future agentic RAG capabilities.
/// For R1, a no-op implementation is used that returns results unchanged.
/// </para>
/// <para>
/// Future implementations may include:
/// <list type="bullet">
/// <item>Cross-encoder reranking for improved relevance</item>
/// <item>LLM-based result filtering and deduplication</item>
/// <item>Dynamic snippet generation with query-aware highlighting</item>
/// <item>Result clustering and grouping by topic</item>
/// <item>Confidence scoring and uncertainty estimation</item>
/// </list>
/// </para>
/// </remarks>
public interface IResultPostprocessor
{
    /// <summary>
    /// Process and optionally transform search results after retrieval.
    /// </summary>
    /// <param name="response">The original search response.</param>
    /// <param name="request">The search request for context.</param>
    /// <param name="tenantId">The tenant ID for context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The processed response and any post-processing metadata.
    /// For R1 no-op implementation, returns the original response unchanged.
    /// </returns>
    Task<ResultPostprocessorResult> ProcessAsync(
        SemanticSearchResponse response,
        SemanticSearchRequest request,
        string tenantId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of result post-processing.
/// </summary>
/// <param name="ProcessedResponse">The processed search response (may be modified or original).</param>
/// <param name="WasModified">Whether results were modified during post-processing.</param>
/// <param name="PostprocessingMetadata">Optional metadata about post-processing operations performed.</param>
public sealed record ResultPostprocessorResult(
    SemanticSearchResponse ProcessedResponse,
    bool WasModified,
    IReadOnlyDictionary<string, object>? PostprocessingMetadata = null);
