using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Retrieves golden reference knowledge from the <c>spaarke-rag-references</c> AI Search index.
/// Provides a parallel retrieval path alongside <see cref="RagService"/> (which queries customer documents).
/// </summary>
/// <remarks>
/// <para>
/// Hybrid search pipeline (mirrors RagService patterns):
/// <list type="ordered">
///   <item>Check Redis result cache (key: rag-ref:{tenantId}:{queryHash}:{sourceIdsHash}:{topK}).</item>
///   <item>On cache miss: generate embedding for query (with caching via <see cref="IEmbeddingCache"/>).</item>
///   <item>Build hybrid query: full-text keyword search + vector search (3072-dim cosine).</item>
///   <item>Apply semantic ranking for result re-ordering.</item>
///   <item>Filter by tenantId (required) + optional knowledgeSourceIds + optional domain.</item>
///   <item>Cache results in Redis (10-minute TTL) and return ranked results with telemetry.</item>
/// </list>
/// </para>
/// <para>
/// Constraints:
/// <list type="bullet">
///   <item>MUST query spaarke-rag-references ONLY — never the customer document index.</item>
///   <item>MUST include tenantId filter on all queries (security).</item>
///   <item>ADR-010: Registered as concrete singleton in AiModule.cs.</item>
///   <item>ADR-009: Redis result caching with 10-minute TTL (session-scoped).</item>
/// </list>
/// </para>
/// </remarks>
public sealed partial class ReferenceRetrievalService
{
    private readonly SearchIndexClient _searchIndexClient;
    private readonly IOpenAiClient _openAiClient;
    private readonly IEmbeddingCache _embeddingCache;
    private readonly IDistributedCache _cache;
    private readonly AiSearchOptions _aiSearchOptions;
    private readonly ILogger<ReferenceRetrievalService> _logger;

    // Semantic configuration name — matches the reference index definition.
    // Uses the same config name as the knowledge index (both created with the same schema).
    private const string SemanticConfigurationName = "knowledge-semantic-config";

    // Vector field name and dimensions (3072-dim text-embedding-3-large, same as RagService)
    private const string VectorFieldName = "contentVector3072";

    // Search fields for keyword queries (subset of KnowledgeDocument searchable fields)
    private static readonly string[] SearchFields = ["content", "knowledgeSourceName"];

    // Maximum query text length for structured logging (PII/compliance)
    private const int MaxQueryTextLength = 200;

    // Result cache TTL: 10 minutes (session-scoped). Prevents duplicate embedding + search
    // calls when multiple action nodes in a playbook query the same knowledge sources.
    private static readonly TimeSpan ResultCacheTtl = TimeSpan.FromMinutes(10);

    // Cache key prefix following SDAP naming convention
    private const string ResultCacheKeyPrefix = "sdap:rag-ref:";

    // JSON serializer options for cache serialization (camelCase to match API conventions)
    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initialises a new <see cref="ReferenceRetrievalService"/>.
    /// </summary>
    public ReferenceRetrievalService(
        SearchIndexClient searchIndexClient,
        IOpenAiClient openAiClient,
        IEmbeddingCache embeddingCache,
        IDistributedCache cache,
        IOptions<AiSearchOptions> aiSearchOptions,
        ILogger<ReferenceRetrievalService> logger)
    {
        _searchIndexClient = searchIndexClient ?? throw new ArgumentNullException(nameof(searchIndexClient));
        _openAiClient = openAiClient ?? throw new ArgumentNullException(nameof(openAiClient));
        _embeddingCache = embeddingCache ?? throw new ArgumentNullException(nameof(embeddingCache));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _aiSearchOptions = aiSearchOptions?.Value ?? throw new ArgumentNullException(nameof(aiSearchOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Searches the <c>spaarke-rag-references</c> index for relevant golden reference knowledge
    /// using hybrid search (keyword + vector + semantic reranking).
    /// </summary>
    /// <param name="query">The search query text.</param>
    /// <param name="options">Search options including tenant, filters, and limits.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ranked reference search results with relevance scores.</returns>
    /// <exception cref="ArgumentException">Thrown when query or TenantId is null/empty.</exception>
    public async Task<ReferenceSearchResponse> SearchReferencesAsync(
        string query,
        ReferenceSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(options.TenantId);

        var totalStopwatch = Stopwatch.StartNew();

        // Step 0: Check Redis result cache (prevents duplicate embedding + search calls)
        var cacheKey = BuildResultCacheKey(query, options);
        var cachedResponse = await TryGetCachedResultAsync(cacheKey, cancellationToken);
        if (cachedResponse != null)
        {
            totalStopwatch.Stop();
            LogReferenceResultCacheHit(options.TenantId, totalStopwatch.ElapsedMilliseconds);
            return cachedResponse;
        }

        LogReferenceResultCacheMiss(options.TenantId);

        var embeddingStopwatch = new Stopwatch();
        var searchStopwatch = new Stopwatch();
        var embeddingCacheHit = false;

        _logger.LogDebug(
            "Starting reference search for tenant {TenantId}, query length={QueryLength}, TopK={TopK}",
            options.TenantId, query.Length, options.TopK);

        try
        {
            // Step 1: Get embedding for query (with caching)
            embeddingStopwatch.Start();

            ReadOnlyMemory<float> queryEmbedding;
            var cachedEmbedding = await _embeddingCache.GetEmbeddingForContentAsync(query, cancellationToken);
            if (cachedEmbedding.HasValue)
            {
                queryEmbedding = cachedEmbedding.Value;
                embeddingCacheHit = true;
            }
            else
            {
                queryEmbedding = await _openAiClient.GenerateEmbeddingAsync(query, cancellationToken: cancellationToken);
                await _embeddingCache.SetEmbeddingForContentAsync(query, queryEmbedding, cancellationToken);
            }

            embeddingStopwatch.Stop();

            _logger.LogDebug("Reference query embedding in {ElapsedMs}ms, cacheHit={CacheHit}",
                embeddingStopwatch.ElapsedMilliseconds, embeddingCacheHit);

            // Step 2: Get SearchClient for the references index (always the same index, not tenant-routed)
            searchStopwatch.Start();
            var searchClient = _searchIndexClient.GetSearchClient(_aiSearchOptions.RagReferencesIndexName);

            // Step 3: Build hybrid search options
            var searchOptions = BuildSearchOptions(options, queryEmbedding);

            // Step 4: Execute search
            LogReferenceRetrievalQuery(
                tenantId: options.TenantId,
                indexName: _aiSearchOptions.RagReferencesIndexName,
                queryText: query.Length > MaxQueryTextLength ? query[..MaxQueryTextLength] : query,
                topK: options.TopK);

            var searchResponse = await searchClient.SearchAsync<KnowledgeDocument>(
                query, searchOptions, cancellationToken);
            var searchResults = searchResponse.Value;
            searchStopwatch.Stop();

            // Step 5: Process results
            var results = new List<ReferenceSearchResult>();

            await foreach (var result in searchResults.GetResultsAsync().WithCancellation(cancellationToken))
            {
                if (result.Document == null) continue;

                var score = result.Score ?? 0;
                var semanticScore = result.SemanticSearch?.RerankerScore;
                var effectiveScore = semanticScore ?? score;

                if (effectiveScore < options.MinScore) continue;

                results.Add(new ReferenceSearchResult
                {
                    Id = result.Document.Id,
                    Content = result.Document.Content,
                    KnowledgeSourceId = result.Document.KnowledgeSourceId ?? string.Empty,
                    KnowledgeSourceName = result.Document.KnowledgeSourceName ?? string.Empty,
                    Domain = result.Document.DocumentType,
                    Score = effectiveScore,
                    SemanticScore = semanticScore,
                    ChunkIndex = result.Document.ChunkIndex,
                    ChunkCount = result.Document.ChunkCount,
                    Highlights = GetHighlights(result)
                });
            }

            var totalCount = searchResults.TotalCount ?? results.Count;

            totalStopwatch.Stop();

            LogReferenceRetrievalResults(
                tenantId: options.TenantId,
                resultCount: results.Count,
                knowledgeSourceIds: string.Join(",", results.Select(r => r.KnowledgeSourceId).Distinct()),
                scores: string.Join(",", results.Select(r => r.Score.ToString("F4"))),
                elapsedMs: totalStopwatch.ElapsedMilliseconds);

            _logger.LogInformation(
                "Reference search completed for tenant {TenantId}: {ResultCount} results in {TotalMs}ms " +
                "(embedding={EmbeddingMs}ms, search={SearchMs}ms)",
                options.TenantId, results.Count, totalStopwatch.ElapsedMilliseconds,
                embeddingStopwatch.ElapsedMilliseconds, searchStopwatch.ElapsedMilliseconds);

            var response = new ReferenceSearchResponse
            {
                Query = query,
                Results = results,
                TotalCount = totalCount,
                SearchDurationMs = searchStopwatch.ElapsedMilliseconds,
                EmbeddingDurationMs = embeddingStopwatch.ElapsedMilliseconds,
                EmbeddingCacheHit = embeddingCacheHit,
                ResultCacheHit = false
            };

            // Cache the response for subsequent action nodes that query the same knowledge
            await TryCacheResultAsync(cacheKey, response, cancellationToken);

            return response;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure AI Search error during reference search for tenant {TenantId}", options.TenantId);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected error during reference search for tenant {TenantId}", options.TenantId);
            throw;
        }
    }

    #region Private Methods

    private SearchOptions BuildSearchOptions(ReferenceSearchOptions options, ReadOnlyMemory<float> queryEmbedding)
    {
        var searchOptions = new SearchOptions
        {
            Size = options.TopK,
            IncludeTotalCount = true,
            QueryType = SearchQueryType.Semantic,
            SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = SemanticConfigurationName,
                QueryCaption = new QueryCaption(QueryCaptionType.Extractive),
                QueryAnswer = new QueryAnswer(QueryAnswerType.None)
            }
        };

        // Vector search (3072-dim text-embedding-3-large)
        if (queryEmbedding.Length > 0)
        {
            searchOptions.VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(queryEmbedding)
                    {
                        KNearestNeighborsCount = options.TopK * 2, // Retrieve more for hybrid fusion
                        Fields = { VectorFieldName }
                    }
                }
            };
        }

        // Build OData filter expression
        var filters = new List<string>();

        // ALWAYS filter by tenant for security
        filters.Add($"tenantId eq '{EscapeFilterValue(options.TenantId)}'");

        // Optional: filter by knowledge source IDs
        if (options.KnowledgeSourceIds is { Count: > 0 })
        {
            if (options.KnowledgeSourceIds.Count > 10)
            {
                var escaped = string.Join(",", options.KnowledgeSourceIds.Select(EscapeFilterValue));
                filters.Add($"search.in(knowledgeSourceId, '{escaped}', ',')");
            }
            else
            {
                var sourceFilters = options.KnowledgeSourceIds
                    .Select(id => $"knowledgeSourceId eq '{EscapeFilterValue(id)}'");
                filters.Add($"({string.Join(" or ", sourceFilters)})");
            }
        }

        // Optional: filter by domain (stored in documentType field)
        if (!string.IsNullOrEmpty(options.Domain))
        {
            filters.Add($"documentType eq '{EscapeFilterValue(options.Domain)}'");
        }

        searchOptions.Filter = string.Join(" and ", filters);

        // Set search fields for keyword search
        foreach (var field in SearchFields)
        {
            searchOptions.SearchFields.Add(field);
        }

        // Enable highlighting
        searchOptions.HighlightFields.Add("content");

        return searchOptions;
    }

    private static IReadOnlyList<string>? GetHighlights(SearchResult<KnowledgeDocument> result)
    {
        if (result.Highlights != null && result.Highlights.TryGetValue("content", out var highlights))
        {
            return highlights.ToList();
        }

        // Fall back to semantic captions if available
        if (result.SemanticSearch?.Captions?.Count > 0)
        {
            return result.SemanticSearch.Captions
                .Select(c => c.Highlights ?? c.Text)
                .Where(h => !string.IsNullOrEmpty(h))
                .ToList()!;
        }

        return null;
    }

    private static string EscapeFilterValue(string value)
    {
        return value.Replace("'", "''");
    }

    /// <summary>
    /// Builds a tenant-scoped cache key for a reference search query.
    /// Format: sdap:rag-ref:{tenantId}:{queryHash}:{sourceIdsHash}:{topK}
    /// </summary>
    private static string BuildResultCacheKey(string query, ReferenceSearchOptions options)
    {
        // Hash the query text for a fixed-length, safe cache key component
        var queryHash = ComputeShortHash(query);

        // Hash the sorted source IDs (or "none" if no filter)
        var sourceIdsInput = options.KnowledgeSourceIds is { Count: > 0 }
            ? string.Join(",", options.KnowledgeSourceIds.OrderBy(id => id, StringComparer.Ordinal))
            : "none";
        var sourceIdsHash = ComputeShortHash(sourceIdsInput);

        return $"{ResultCacheKeyPrefix}{options.TenantId}:{queryHash}:{sourceIdsHash}:{options.TopK}";
    }

    /// <summary>
    /// Computes a URL-safe Base64-encoded SHA256 hash (truncated to 16 chars for compact keys).
    /// </summary>
    private static string ComputeShortHash(string input)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        // Use first 12 bytes (96 bits) → 16 Base64 chars. Collision risk is negligible for this use case.
        return Convert.ToBase64String(hashBytes, 0, 12)
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Attempts to retrieve a cached <see cref="ReferenceSearchResponse"/> from Redis.
    /// Returns null on cache miss or any error (fail-open).
    /// </summary>
    private async Task<ReferenceSearchResponse?> TryGetCachedResultAsync(
        string cacheKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var cachedData = await _cache.GetAsync(cacheKey, cancellationToken);
            if (cachedData == null)
            {
                return null;
            }

            var response = JsonSerializer.Deserialize<ReferenceSearchResponse>(cachedData, CacheJsonOptions);
            if (response == null)
            {
                return null;
            }

            // Return a copy with ResultCacheHit = true (original was cached with false)
            return response with { ResultCacheHit = true };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error retrieving reference search result from cache (key={CacheKey}), proceeding with live search",
                cacheKey);
            return null; // Fail-open: cache errors should never block search
        }
    }

    /// <summary>
    /// Caches a <see cref="ReferenceSearchResponse"/> in Redis with the configured TTL.
    /// Errors are logged and swallowed (caching is an optimisation, not a requirement).
    /// </summary>
    private async Task TryCacheResultAsync(
        string cacheKey,
        ReferenceSearchResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            var serialized = JsonSerializer.SerializeToUtf8Bytes(response, CacheJsonOptions);
            await _cache.SetAsync(
                cacheKey,
                serialized,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ResultCacheTtl
                },
                cancellationToken);

            _logger.LogDebug(
                "Cached reference search result (key={CacheKey}, results={ResultCount}, TTL={TtlMinutes}min)",
                cacheKey, response.Results.Count, ResultCacheTtl.TotalMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error caching reference search result (key={CacheKey}), result will not be cached",
                cacheKey);
            // Don't throw — caching is an optimisation
        }
    }

    #endregion

    #region High-Performance Structured Log Events

    /// <summary>
    /// Logged before each reference retrieval search. QueryText is truncated to 200 chars.
    /// </summary>
    [LoggerMessage(
        EventId = 5310,
        Level = LogLevel.Information,
        Message = "ReferenceRetrievalQuery tenant={TenantId} index={IndexName} queryText={QueryText} topK={TopK}")]
    private partial void LogReferenceRetrievalQuery(string tenantId, string indexName, string queryText, int topK);

    /// <summary>
    /// Logged after each reference retrieval search. Contains IDs and scores for evaluation.
    /// </summary>
    [LoggerMessage(
        EventId = 5311,
        Level = LogLevel.Information,
        Message = "ReferenceRetrievalResults tenant={TenantId} resultCount={ResultCount} knowledgeSourceIds={KnowledgeSourceIds} scores={Scores} elapsedMs={ElapsedMs}")]
    private partial void LogReferenceRetrievalResults(string tenantId, int resultCount, string knowledgeSourceIds, string scores, long elapsedMs);

    /// <summary>
    /// Logged when a reference search result is served from Redis cache.
    /// Content is NOT logged per ADR-014.
    /// </summary>
    [LoggerMessage(
        EventId = 5312,
        Level = LogLevel.Information,
        Message = "ReferenceResultCacheHit tenant={TenantId} elapsedMs={ElapsedMs}")]
    private partial void LogReferenceResultCacheHit(string tenantId, long elapsedMs);

    /// <summary>
    /// Logged when a reference search result is not found in Redis cache (cache miss).
    /// </summary>
    [LoggerMessage(
        EventId = 5313,
        Level = LogLevel.Information,
        Message = "ReferenceResultCacheMiss tenant={TenantId}")]
    private partial void LogReferenceResultCacheMiss(string tenantId);

    #endregion
}
