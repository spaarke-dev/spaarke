using Sprk.Bff.Api.Models.Ai.SemanticSearch;

namespace Sprk.Bff.Api.Services.Ai.SemanticSearch;

/// <summary>
/// No-op implementation of <see cref="IQueryPreprocessor"/> for R1.
/// Passes the query through unchanged without any preprocessing.
/// </summary>
/// <remarks>
/// <para>
/// This is a placeholder implementation that enables the extensibility hook
/// without adding processing overhead in R1. Future releases may replace
/// this with LLM-based query rewriting or expansion.
/// </para>
/// <para>
/// R1 behavior: Query passes through unchanged. No modifications, no latency added.
/// </para>
/// </remarks>
public sealed class NoOpQueryPreprocessor : IQueryPreprocessor
{
    /// <inheritdoc />
    /// <remarks>
    /// R1: Returns the original request unchanged with WasModified = false.
    /// </remarks>
    public Task<QueryPreprocessorResult> ProcessAsync(
        SemanticSearchRequest request,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var result = new QueryPreprocessorResult(
            ProcessedRequest: request,
            OriginalQuery: request.Query,
            WasModified: false,
            PreprocessingMetadata: null);

        return Task.FromResult(result);
    }
}
