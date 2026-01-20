using Sprk.Bff.Api.Models.Ai.SemanticSearch;

namespace Sprk.Bff.Api.Services.Ai.SemanticSearch;

/// <summary>
/// Extensibility interface for query preprocessing before search execution.
/// </summary>
/// <remarks>
/// <para>
/// This interface is an extensibility hook for future agentic RAG capabilities.
/// For R1, a no-op implementation is used that passes through the query unchanged.
/// </para>
/// <para>
/// Future implementations may include:
/// <list type="bullet">
/// <item>LLM-based query rewriting for better semantic matching</item>
/// <item>Query expansion with synonyms or related terms</item>
/// <item>Auto-inferred filters from natural language query</item>
/// <item>Query intent classification for specialized handling</item>
/// </list>
/// </para>
/// </remarks>
public interface IQueryPreprocessor
{
    /// <summary>
    /// Process and optionally transform the search request before execution.
    /// </summary>
    /// <param name="request">The original search request.</param>
    /// <param name="tenantId">The tenant ID for context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The processed request and any preprocessing metadata.
    /// For R1 no-op implementation, returns the original request unchanged.
    /// </returns>
    Task<QueryPreprocessorResult> ProcessAsync(
        SemanticSearchRequest request,
        string tenantId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of query preprocessing.
/// </summary>
/// <param name="ProcessedRequest">The processed search request (may be modified or original).</param>
/// <param name="OriginalQuery">The original query text for reference.</param>
/// <param name="WasModified">Whether the query was modified during preprocessing.</param>
/// <param name="PreprocessingMetadata">Optional metadata about preprocessing operations performed.</param>
public sealed record QueryPreprocessorResult(
    SemanticSearchRequest ProcessedRequest,
    string? OriginalQuery,
    bool WasModified,
    IReadOnlyDictionary<string, object>? PreprocessingMetadata = null);
