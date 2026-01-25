using System.Diagnostics;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;

namespace Sprk.Bff.Api.Services.Ai.SemanticSearch;

/// <summary>
/// Implements hybrid semantic search using Azure AI Search with vector and keyword search.
/// Supports RRF (Reciprocal Rank Fusion), vector-only, and keyword-only search modes.
/// </summary>
/// <remarks>
/// <para>
/// Search pipeline:
/// <list type="number">
/// <item>Query preprocessing (IQueryPreprocessor - no-op for R1)</item>
/// <item>Generate query embedding via IOpenAiClient (with caching)</item>
/// <item>Build OData filter via SearchFilterBuilder</item>
/// <item>Execute search in Azure AI Search (hybrid/vector/keyword modes)</item>
/// <item>Result post-processing (IResultPostprocessor - no-op for R1)</item>
/// </list>
/// </para>
/// <para>
/// On embedding failure, falls back to keyword-only search with EMBEDDING_FALLBACK warning.
/// </para>
/// </remarks>
public sealed class SemanticSearchService : ISemanticSearchService
{
    private readonly IKnowledgeDeploymentService _deploymentService;
    private readonly IOpenAiClient _openAiClient;
    private readonly IEmbeddingCache _embeddingCache;
    private readonly IQueryPreprocessor _queryPreprocessor;
    private readonly IResultPostprocessor _resultPostprocessor;
    private readonly ILogger<SemanticSearchService> _logger;

    // Semantic configuration name from the index definition
    private const string SemanticConfigurationName = "knowledge-semantic-config";

    // Vector field name (3072-dim text-embedding-3-large)
    private const string VectorFieldName = "contentVector3072";

    // Search fields for keyword queries
    // Note: parentEntityname (lowercase 'n') exists in index but not added to search fields yet
    private static readonly string[] SearchFields = ["content", "fileName", "knowledgeSourceName"];

    /// <summary>
    /// Initializes a new instance of the <see cref="SemanticSearchService"/> class.
    /// </summary>
    public SemanticSearchService(
        IKnowledgeDeploymentService deploymentService,
        IOpenAiClient openAiClient,
        IEmbeddingCache embeddingCache,
        IQueryPreprocessor queryPreprocessor,
        IResultPostprocessor resultPostprocessor,
        ILogger<SemanticSearchService> logger)
    {
        _deploymentService = deploymentService;
        _openAiClient = openAiClient;
        _embeddingCache = embeddingCache;
        _queryPreprocessor = queryPreprocessor;
        _resultPostprocessor = resultPostprocessor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SemanticSearchResponse> SearchAsync(
        SemanticSearchRequest request,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId, nameof(tenantId));

        var totalStopwatch = Stopwatch.StartNew();
        var embeddingStopwatch = new Stopwatch();
        var searchStopwatch = new Stopwatch();
        var warnings = new List<SearchWarning>();

        var hybridMode = request.Options?.HybridMode ?? HybridSearchMode.Rrf;
        var limit = request.Options?.Limit ?? 20;
        var offset = request.Options?.Offset ?? 0;
        var includeHighlights = request.Options?.IncludeHighlights ?? true;

        _logger.LogDebug(
            "Starting semantic search for tenant {TenantId}, mode={Mode}, query length={QueryLength}",
            tenantId, hybridMode, request.Query?.Length ?? 0);

        try
        {
            // Step 1: Query preprocessing (no-op for R1)
            var preprocessResult = await _queryPreprocessor.ProcessAsync(request, tenantId, cancellationToken);
            var processedRequest = preprocessResult.ProcessedRequest;

            // Step 2: Generate embedding for vector modes
            ReadOnlyMemory<float> queryEmbedding = default;
            var embeddingFailed = false;

            if (hybridMode != HybridSearchMode.KeywordOnly && !string.IsNullOrWhiteSpace(processedRequest.Query))
            {
                embeddingStopwatch.Start();
                try
                {
                    queryEmbedding = await GetEmbeddingWithCacheAsync(processedRequest.Query, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Embedding generation failed, falling back to keyword search");
                    embeddingFailed = true;
                    warnings.Add(new SearchWarning
                    {
                        Code = SearchWarningCode.EmbeddingFallback,
                        Message = "Embedding generation failed. Results from keyword search only."
                    });
                    hybridMode = HybridSearchMode.KeywordOnly;
                }
                embeddingStopwatch.Stop();
            }

            // Step 3: Get SearchClient for tenant
            var searchClient = await _deploymentService.GetSearchClientAsync(tenantId, cancellationToken);

            // Step 4: Build filter using SearchFilterBuilder
            var filter = SearchFilterBuilder.BuildFilter(
                tenantId,
                processedRequest.Scope,
                processedRequest.EntityType,
                processedRequest.EntityId,
                processedRequest.DocumentIds,
                processedRequest.Filters);

            // Step 5: Build search options
            searchStopwatch.Start();
            var searchOptions = BuildSearchOptions(
                hybridMode,
                queryEmbedding,
                filter,
                limit,
                offset,
                includeHighlights);

            // Step 6: Execute search
            var searchText = hybridMode != HybridSearchMode.VectorOnly && !string.IsNullOrWhiteSpace(processedRequest.Query)
                ? processedRequest.Query
                : "*";

            var response = await searchClient.SearchAsync<KnowledgeDocument>(
                searchText, searchOptions, cancellationToken);
            var searchResults = response.Value;
            searchStopwatch.Stop();

            // Step 7: Process results
            var results = await ProcessSearchResultsAsync(
                searchResults,
                includeHighlights,
                cancellationToken);

            // Get total count from response
            var totalResults = searchResults.TotalCount ?? results.Count;

            // Step 8: Build applied filters summary
            var appliedFilters = BuildAppliedFilters(processedRequest, filter);

            // Step 9: Build response
            var searchResponse = new SemanticSearchResponse
            {
                Results = results,
                Metadata = new SearchMetadata
                {
                    TotalResults = totalResults,
                    ReturnedResults = results.Count,
                    SearchDurationMs = searchStopwatch.ElapsedMilliseconds,
                    EmbeddingDurationMs = embeddingStopwatch.ElapsedMilliseconds,
                    ExecutedMode = embeddingFailed ? HybridSearchMode.KeywordOnly : hybridMode,
                    AppliedFilters = appliedFilters,
                    Warnings = warnings.Count > 0 ? warnings : null
                }
            };

            // Step 10: Result post-processing (no-op for R1)
            var postprocessResult = await _resultPostprocessor.ProcessAsync(
                searchResponse, processedRequest, tenantId, cancellationToken);

            totalStopwatch.Stop();

            _logger.LogInformation(
                "Semantic search completed for tenant {TenantId}: {ResultCount}/{TotalCount} results in {TotalMs}ms " +
                "(embedding={EmbeddingMs}ms, search={SearchMs}ms, mode={Mode})",
                tenantId, results.Count, totalResults, totalStopwatch.ElapsedMilliseconds,
                embeddingStopwatch.ElapsedMilliseconds, searchStopwatch.ElapsedMilliseconds, hybridMode);

            return postprocessResult.ProcessedResponse;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure AI Search error during semantic search for tenant {TenantId}", tenantId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<SemanticSearchCountResponse> CountAsync(
        SemanticSearchRequest request,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId, nameof(tenantId));

        _logger.LogDebug("Starting semantic search count for tenant {TenantId}", tenantId);

        try
        {
            // Get SearchClient for tenant
            var searchClient = await _deploymentService.GetSearchClientAsync(tenantId, cancellationToken);

            // Build filter using SearchFilterBuilder
            var filter = SearchFilterBuilder.BuildFilter(
                tenantId,
                request.Scope,
                request.EntityType,
                request.EntityId,
                request.DocumentIds,
                request.Filters);

            // Build count-only search options
            var searchOptions = new Azure.Search.Documents.SearchOptions
            {
                Filter = filter,
                Size = 0, // We only want count
                IncludeTotalCount = true
            };

            // Execute search to get count
            var response = await searchClient.SearchAsync<KnowledgeDocument>(
                "*", searchOptions, cancellationToken);
            var searchResults = response.Value;

            var count = searchResults.TotalCount ?? 0;

            _logger.LogInformation(
                "Semantic search count completed for tenant {TenantId}: {Count} documents",
                tenantId, count);

            return new SemanticSearchCountResponse
            {
                Count = count,
                AppliedFilters = BuildAppliedFilters(request, filter),
                Warnings = null
            };
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure AI Search error during count for tenant {TenantId}", tenantId);
            throw;
        }
    }

    #region Private Methods

    /// <summary>
    /// Gets embedding with caching support.
    /// </summary>
    private async Task<ReadOnlyMemory<float>> GetEmbeddingWithCacheAsync(
        string text,
        CancellationToken cancellationToken)
    {
        // Try cache first
        var cachedEmbedding = await _embeddingCache.GetEmbeddingForContentAsync(text, cancellationToken);
        if (cachedEmbedding.HasValue)
        {
            return cachedEmbedding.Value;
        }

        // Cache miss - generate and cache
        var embedding = await _openAiClient.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
        await _embeddingCache.SetEmbeddingForContentAsync(text, embedding, cancellationToken);

        return embedding;
    }

    /// <summary>
    /// Builds Azure AI Search options based on hybrid mode and parameters.
    /// </summary>
    private static Azure.Search.Documents.SearchOptions BuildSearchOptions(
        string hybridMode,
        ReadOnlyMemory<float> queryEmbedding,
        string filter,
        int limit,
        int offset,
        bool includeHighlights)
    {
        var searchOptions = new Azure.Search.Documents.SearchOptions
        {
            Filter = filter,
            Size = limit,
            Skip = offset,
            IncludeTotalCount = true
        };

        // Configure based on mode
        var useVector = hybridMode != HybridSearchMode.KeywordOnly && queryEmbedding.Length > 0;
        var useKeyword = hybridMode != HybridSearchMode.VectorOnly;

        // Add vector search if needed
        if (useVector)
        {
            searchOptions.VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(queryEmbedding)
                    {
                        KNearestNeighborsCount = limit * 2, // Retrieve more for RRF fusion
                        Fields = { VectorFieldName }
                    }
                }
            };
        }

        // Add keyword search fields
        if (useKeyword)
        {
            foreach (var field in SearchFields)
            {
                searchOptions.SearchFields.Add(field);
            }
        }

        // Enable semantic ranking for hybrid and keyword modes
        if (useKeyword)
        {
            searchOptions.QueryType = SearchQueryType.Semantic;
            searchOptions.SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = SemanticConfigurationName,
                QueryCaption = new QueryCaption(QueryCaptionType.Extractive)
            };
        }

        // Enable highlighting
        if (includeHighlights)
        {
            searchOptions.HighlightFields.Add("content");
        }

        return searchOptions;
    }

    /// <summary>
    /// Processes search results into response format.
    /// </summary>
    private async Task<IReadOnlyList<SearchResult>> ProcessSearchResultsAsync(
        SearchResults<KnowledgeDocument> searchResults,
        bool includeHighlights,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchResult>();

        await foreach (var result in searchResults.GetResultsAsync().WithCancellation(cancellationToken))
        {
            if (result.Document == null) continue;

            var doc = result.Document;
            var combinedScore = result.Score ?? 0;

            // Use semantic reranker score if available
            if (result.SemanticSearch?.RerankerScore.HasValue == true)
            {
                combinedScore = result.SemanticSearch.RerankerScore.Value;
            }

            results.Add(new SearchResult
            {
                DocumentId = doc.DocumentId,
                SpeFileId = doc.SpeFileId,
                Name = doc.FileName,
                DocumentType = doc.DocumentType,
                FileType = doc.FileType,
                CombinedScore = combinedScore,
                Similarity = null, // Reserved for R2
                KeywordScore = null, // Reserved for R2
                Highlights = includeHighlights ? GetHighlights(result) : null,
                ParentEntityType = doc.ParentEntityType,
                ParentEntityId = doc.ParentEntityId,
                ParentEntityName = doc.ParentEntityName,
                FileUrl = null, // To be populated by endpoint if needed
                RecordUrl = null, // To be populated by endpoint if needed
                CreatedAt = doc.CreatedAt,
                UpdatedAt = doc.UpdatedAt
            });
        }

        return results;
    }

    /// <summary>
    /// Extracts highlights from search result.
    /// </summary>
    private static IReadOnlyList<string>? GetHighlights(Azure.Search.Documents.Models.SearchResult<KnowledgeDocument> result)
    {
        // Try regular highlights first
        if (result.Highlights?.TryGetValue("content", out var highlights) == true)
        {
            return highlights.ToList();
        }

        // Try semantic captions if available
        if (result.SemanticSearch?.Captions?.Count > 0)
        {
            return result.SemanticSearch.Captions
                .Select(c => c.Highlights ?? c.Text)
                .Where(h => !string.IsNullOrEmpty(h))
                .ToList()!;
        }

        return null;
    }

    /// <summary>
    /// Builds applied filters summary for response.
    /// </summary>
    private static AppliedFilters BuildAppliedFilters(SemanticSearchRequest request, string filterExpression)
    {
        return new AppliedFilters
        {
            Scope = request.Scope,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            DocumentIdCount = request.DocumentIds?.Count,
            DocumentTypes = request.Filters?.DocumentTypes,
            FileTypes = request.Filters?.FileTypes,
            Tags = request.Filters?.Tags,
            DateRange = request.Filters?.DateRange is not null
            ? new AppliedDateRange { From = request.Filters.DateRange.From, To = request.Filters.DateRange.To }
            : null
        };
    }

    #endregion
}
