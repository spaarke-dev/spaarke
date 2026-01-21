using Sprk.Bff.Api.Models.Ai.SemanticSearch;

namespace Sprk.Bff.Api.Services.Ai.SemanticSearch;

/// <summary>
/// Provides semantic search capabilities with hybrid search (vector + keyword).
/// Supports RRF (Reciprocal Rank Fusion), vector-only, and keyword-only search modes.
/// </summary>
/// <remarks>
/// <para>
/// This service is the core component for AI-powered document search in the BFF API.
/// It integrates with Azure AI Search for hybrid search and Azure OpenAI for embeddings.
/// </para>
/// <para>
/// Search pipeline:
/// <list type="number">
/// <item>Query preprocessing (extensibility hook for future agentic RAG)</item>
/// <item>Generate query embedding via Azure OpenAI</item>
/// <item>Execute hybrid search in Azure AI Search</item>
/// <item>Result post-processing (extensibility hook for future reranking)</item>
/// </list>
/// </para>
/// <para>
/// All search operations enforce tenant isolation via required tenantId parameter.
/// Scope-based authorization (entity or documentIds) must be validated by the calling endpoint.
/// </para>
/// </remarks>
public interface ISemanticSearchService
{
    /// <summary>
    /// Execute a semantic search with hybrid search capabilities.
    /// </summary>
    /// <param name="request">The search request with query, scope, filters, and options.</param>
    /// <param name="tenantId">Required tenant ID for tenant isolation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search response with results and metadata.</returns>
    /// <remarks>
    /// <para>
    /// The search mode is determined by <see cref="SearchOptions.HybridMode"/>:
    /// <list type="bullet">
    /// <item><c>rrf</c> (default): Combines vector and keyword search with RRF fusion</item>
    /// <item><c>vectorOnly</c>: Pure vector similarity search</item>
    /// <item><c>keywordOnly</c>: Pure keyword/BM25 search</item>
    /// </list>
    /// </para>
    /// <para>
    /// For R1, only <c>combinedScore</c> is populated in results.
    /// <c>similarity</c> and <c>keywordScore</c> are reserved for future use.
    /// </para>
    /// </remarks>
    Task<SemanticSearchResponse> SearchAsync(
        SemanticSearchRequest request,
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the count of documents matching the search criteria.
    /// </summary>
    /// <param name="request">The search request with query, scope, and filters.</param>
    /// <param name="tenantId">Required tenant ID for tenant isolation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Count response with total matching documents.</returns>
    /// <remarks>
    /// <para>
    /// This is a lightweight operation that returns only the count without retrieving documents.
    /// Useful for pagination UI or determining if a search would return results.
    /// </para>
    /// <para>
    /// The count respects all filters including scope (entity/documentIds) and optional filters.
    /// </para>
    /// </remarks>
    Task<SemanticSearchCountResponse> CountAsync(
        SemanticSearchRequest request,
        string tenantId,
        CancellationToken cancellationToken = default);
}
