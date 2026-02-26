using Sprk.Bff.Api.Models.Ai.RecordSearch;

namespace Sprk.Bff.Api.Services.Ai.RecordSearch;

/// <summary>
/// Provides hybrid semantic search capabilities against the spaarke-records-index.
/// Searches Dataverse entity records (Matters, Projects, Invoices) using keyword,
/// vector, or RRF (Reciprocal Rank Fusion) hybrid search modes.
/// </summary>
/// <remarks>
/// <para>
/// Search pipeline:
/// <list type="number">
/// <item>Validate request (query, recordTypes)</item>
/// <item>Generate query embedding via Azure OpenAI (with caching)</item>
/// <item>Build OData filter (recordType + optional organizations/people/referenceNumbers)</item>
/// <item>Execute search against spaarke-records-index</item>
/// <item>Map results to RecordSearchResult with confidence scoring</item>
/// </list>
/// </para>
/// <para>
/// On embedding failure, falls back to keyword-only search.
/// When contentVector is not populated in the index, vector search degrades gracefully
/// (no vector results but keyword still works).
/// </para>
/// <para>
/// Note: The spaarke-records-index does NOT have a tenantId field.
/// Tenant isolation is enforced at the Dataverse level, not at the search index level.
/// </para>
/// </remarks>
public interface IRecordSearchService
{
    /// <summary>
    /// Execute a record search with hybrid search capabilities.
    /// </summary>
    /// <param name="request">The search request with query, recordTypes, filters, and options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search response with results and metadata.</returns>
    /// <remarks>
    /// <para>
    /// The search mode is determined by <see cref="RecordSearchOptions.HybridMode"/>:
    /// <list type="bullet">
    /// <item><c>rrf</c> (default): Combines vector and keyword search with RRF fusion</item>
    /// <item><c>vectorOnly</c>: Pure vector similarity search</item>
    /// <item><c>keywordOnly</c>: Pure keyword/BM25 search</item>
    /// </list>
    /// </para>
    /// <para>
    /// Results include <c>confidenceScore</c> (0.0-1.0) normalized from search ranking scores.
    /// <c>matchReasons</c> are derived from field overlap analysis (highlights and captions).
    /// </para>
    /// </remarks>
    Task<RecordSearchResponse> SearchAsync(
        RecordSearchRequest request,
        CancellationToken cancellationToken = default);
}
