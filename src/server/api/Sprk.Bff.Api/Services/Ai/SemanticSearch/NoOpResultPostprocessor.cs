using Sprk.Bff.Api.Models.Ai.SemanticSearch;

namespace Sprk.Bff.Api.Services.Ai.SemanticSearch;

/// <summary>
/// No-op implementation of <see cref="IResultPostprocessor"/> for R1.
/// Passes search results through unchanged without any post-processing.
/// </summary>
/// <remarks>
/// <para>
/// This is a placeholder implementation that enables the extensibility hook
/// without adding processing overhead in R1. Future releases may replace
/// this with cross-encoder reranking or LLM-based result filtering.
/// </para>
/// <para>
/// R1 behavior: Results pass through unchanged. No modifications, no latency added.
/// </para>
/// </remarks>
public sealed class NoOpResultPostprocessor : IResultPostprocessor
{
    /// <inheritdoc />
    /// <remarks>
    /// R1: Returns the original response unchanged with WasModified = false.
    /// </remarks>
    public Task<ResultPostprocessorResult> ProcessAsync(
        SemanticSearchResponse response,
        SemanticSearchRequest request,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var result = new ResultPostprocessorResult(
            ProcessedResponse: response,
            WasModified: false,
            PostprocessingMetadata: null);

        return Task.FromResult(result);
    }
}
