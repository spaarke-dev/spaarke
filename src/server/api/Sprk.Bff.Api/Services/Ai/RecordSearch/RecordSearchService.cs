using System.Diagnostics;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Services.RecordMatching;

namespace Sprk.Bff.Api.Services.Ai.RecordSearch;

/// <summary>
/// Implements hybrid semantic search against the spaarke-records-index for Dataverse entity records.
/// Supports RRF (Reciprocal Rank Fusion), vector-only, and keyword-only search modes.
/// </summary>
/// <remarks>
/// <para>
/// Search pipeline:
/// <list type="number">
/// <item>Validate request (query not empty, recordTypes not empty)</item>
/// <item>Check Redis cache for cached results</item>
/// <item>Generate query embedding via IOpenAiClient (with embedding cache)</item>
/// <item>Build OData filter for recordType + optional filters</item>
/// <item>Execute search against spaarke-records-index</item>
/// <item>Map results to RecordSearchResult[] with confidence scoring</item>
/// <item>Populate RecordSearchMetadata</item>
/// <item>Cache results in Redis</item>
/// </list>
/// </para>
/// <para>
/// On embedding failure, falls back to keyword-only search.
/// The index currently has no contentVector populated, so vector search degrades gracefully.
/// </para>
/// <para>
/// Important: The spaarke-records-index does NOT have a tenantId field for tenant isolation.
/// This differs from the knowledge-index. Security is enforced at the Dataverse layer.
/// </para>
/// </remarks>
public sealed class RecordSearchService : IRecordSearchService
{
    private readonly SearchIndexClient _searchIndexClient;
    private readonly IOpenAiClient _openAiClient;
    private readonly IEmbeddingCache _embeddingCache;
    private readonly IDistributedCache _distributedCache;
    private readonly DocumentIntelligenceOptions _docIntelOptions;
    private readonly ILogger<RecordSearchService> _logger;

    // Index name for record search
    private const string RecordsIndexName = "spaarke-records-index";

    // Semantic configuration name from the index definition
    private const string SemanticConfigurationName = "default-semantic-config";

    // Vector field name (3072-dim text-embedding-3-large)
    private const string VectorFieldName = "contentVector";

    // Search fields for keyword queries (all searchable fields in the records index)
    private static readonly string[] SearchFields =
        ["recordName", "recordDescription", "keywords", "organizations", "people"];

    // Fields to select from index results (all non-vector fields)
    private static readonly string[] SelectFields =
    [
        "id", "recordType", "recordName", "recordDescription",
        "organizations", "people", "referenceNumbers", "keywords",
        "lastModified", "dataverseRecordId", "dataverseEntityName"
    ];

    // Cache configuration
    private const string CacheKeyPrefix = "records-search";
    private const int CacheExpirationMinutes = 5;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecordSearchService"/> class.
    /// </summary>
    public RecordSearchService(
        SearchIndexClient searchIndexClient,
        IOpenAiClient openAiClient,
        IEmbeddingCache embeddingCache,
        IDistributedCache distributedCache,
        IOptions<DocumentIntelligenceOptions> docIntelOptions,
        ILogger<RecordSearchService> logger)
    {
        _searchIndexClient = searchIndexClient;
        _openAiClient = openAiClient;
        _embeddingCache = embeddingCache;
        _distributedCache = distributedCache;
        _docIntelOptions = docIntelOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RecordSearchResponse> SearchAsync(
        RecordSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var totalStopwatch = Stopwatch.StartNew();
        var embeddingStopwatch = new Stopwatch();
        var searchStopwatch = new Stopwatch();

        var hybridMode = request.Options?.HybridMode ?? RecordHybridSearchMode.Rrf;
        var limit = request.Options?.Limit ?? 20;
        var offset = request.Options?.Offset ?? 0;

        _logger.LogDebug(
            "Starting record search: mode={Mode}, query length={QueryLength}, recordTypes=[{RecordTypes}]",
            hybridMode, request.Query.Length, string.Join(",", request.RecordTypes));

        try
        {
            // Step 1: Check Redis cache
            var cacheKey = BuildCacheKey(request);
            var cachedResult = await GetFromCacheAsync(cacheKey, cancellationToken);
            if (cachedResult is not null)
            {
                _logger.LogDebug("Record search cache hit for key {CacheKey}", cacheKey);
                return cachedResult;
            }

            // Step 2: Generate embedding for vector modes
            ReadOnlyMemory<float> queryEmbedding = default;
            var embeddingFailed = false;

            if (hybridMode != RecordHybridSearchMode.KeywordOnly && !string.IsNullOrWhiteSpace(request.Query))
            {
                embeddingStopwatch.Start();
                try
                {
                    queryEmbedding = await GetEmbeddingWithCacheAsync(request.Query, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Embedding generation failed for record search, falling back to keyword search");
                    embeddingFailed = true;
                    hybridMode = RecordHybridSearchMode.KeywordOnly;
                }
                embeddingStopwatch.Stop();
            }

            // Step 3: Get SearchClient for the records index
            var indexName = _docIntelOptions.AiSearchIndexName;
            if (string.IsNullOrWhiteSpace(indexName))
            {
                indexName = RecordsIndexName;
            }
            var searchClient = _searchIndexClient.GetSearchClient(indexName);

            // Step 4: Build OData filter
            var filter = BuildRecordFilter(request);

            // Step 5: Build search options
            searchStopwatch.Start();
            var searchOptions = BuildSearchOptions(
                hybridMode,
                queryEmbedding,
                filter,
                limit,
                offset);

            // Step 6: Execute search
            var searchText = hybridMode != RecordHybridSearchMode.VectorOnly && !string.IsNullOrWhiteSpace(request.Query)
                ? request.Query
                : "*";

            var response = await searchClient.SearchAsync<SearchIndexDocument>(
                searchText, searchOptions, cancellationToken);
            var searchResults = response.Value;
            searchStopwatch.Stop();

            // Step 7: Process results
            var results = await ProcessSearchResultsAsync(searchResults, cancellationToken);

            // Get total count from response
            var totalResults = (int)(searchResults.TotalCount ?? results.Count);

            // Step 8: Build response
            totalStopwatch.Stop();

            var searchResponse = new RecordSearchResponse
            {
                Results = results,
                Metadata = new RecordSearchMetadata
                {
                    TotalCount = totalResults,
                    SearchTime = totalStopwatch.ElapsedMilliseconds,
                    HybridMode = embeddingFailed ? RecordHybridSearchMode.KeywordOnly : hybridMode
                }
            };

            // Step 9: Cache results in Redis
            await SetInCacheAsync(cacheKey, searchResponse, cancellationToken);

            _logger.LogInformation(
                "Record search completed: {ResultCount}/{TotalCount} results in {TotalMs}ms " +
                "(embedding={EmbeddingMs}ms, search={SearchMs}ms, mode={Mode}, recordTypes=[{RecordTypes}])",
                results.Count, totalResults, totalStopwatch.ElapsedMilliseconds,
                embeddingStopwatch.ElapsedMilliseconds, searchStopwatch.ElapsedMilliseconds,
                hybridMode, string.Join(",", request.RecordTypes));

            return searchResponse;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure AI Search error during record search");
            throw;
        }
    }

    #region Private Methods

    /// <summary>
    /// Gets embedding with caching support via IEmbeddingCache.
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
    /// Builds OData filter for record search.
    /// Always includes recordType filter; optionally adds organizations, people, referenceNumbers.
    /// </summary>
    private static string BuildRecordFilter(RecordSearchRequest request)
    {
        var filterParts = new List<string>();

        // 1. RecordType filter (ALWAYS required — at least one recordType)
        if (request.RecordTypes.Count == 1)
        {
            filterParts.Add($"recordType eq '{EscapeODataValue(request.RecordTypes[0])}'");
        }
        else
        {
            var escapedTypes = request.RecordTypes.Select(EscapeODataValue);
            var typeList = string.Join(",", escapedTypes);
            filterParts.Add($"search.in(recordType, '{typeList}', ',')");
        }

        // 2. Optional filters
        if (request.Filters is not null)
        {
            // Organizations filter (collection field, use any())
            if (request.Filters.Organizations is { Count: > 0 })
            {
                var orgFilters = request.Filters.Organizations.Select(
                    org => $"organizations/any(o: o eq '{EscapeODataValue(org)}')"
                );
                filterParts.Add($"({string.Join(" or ", orgFilters)})");
            }

            // People filter (collection field, use any())
            if (request.Filters.People is { Count: > 0 })
            {
                var peopleFilters = request.Filters.People.Select(
                    person => $"people/any(p: p eq '{EscapeODataValue(person)}')"
                );
                filterParts.Add($"({string.Join(" or ", peopleFilters)})");
            }

            // ReferenceNumbers filter (collection field, use any())
            if (request.Filters.ReferenceNumbers is { Count: > 0 })
            {
                var refFilters = request.Filters.ReferenceNumbers.Select(
                    refNum => $"referenceNumbers/any(r: r eq '{EscapeODataValue(refNum)}')"
                );
                filterParts.Add($"({string.Join(" or ", refFilters)})");
            }
        }

        return string.Join(" and ", filterParts);
    }

    /// <summary>
    /// Builds Azure AI Search options based on hybrid mode and parameters.
    /// </summary>
    private static SearchOptions BuildSearchOptions(
        string hybridMode,
        ReadOnlyMemory<float> queryEmbedding,
        string filter,
        int limit,
        int offset)
    {
        var searchOptions = new SearchOptions
        {
            Filter = filter,
            Size = limit,
            Skip = offset,
            IncludeTotalCount = true
        };

        // Select only needed fields (exclude contentVector for performance)
        foreach (var field in SelectFields)
        {
            searchOptions.Select.Add(field);
        }

        // Configure based on mode
        var useVector = hybridMode != RecordHybridSearchMode.KeywordOnly && queryEmbedding.Length > 0;
        var useKeyword = hybridMode != RecordHybridSearchMode.VectorOnly;

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

        // Enable highlighting on relevant fields
        searchOptions.HighlightFields.Add("recordName");
        searchOptions.HighlightFields.Add("recordDescription");

        return searchOptions;
    }

    /// <summary>
    /// Processes search results into RecordSearchResult format.
    /// Normalizes semantic reranker scores from 0-4 range to 0-1 range.
    /// </summary>
    private async Task<IReadOnlyList<RecordSearchResult>> ProcessSearchResultsAsync(
        SearchResults<SearchIndexDocument> searchResults,
        CancellationToken cancellationToken)
    {
        var results = new List<RecordSearchResult>();

        await foreach (var result in searchResults.GetResultsAsync().WithCancellation(cancellationToken))
        {
            if (result.Document is null) continue;

            var doc = result.Document;
            var rawScore = result.Score ?? 0;

            // Use semantic reranker score if available (0-4 range)
            if (result.SemanticSearch?.RerankerScore.HasValue == true)
            {
                rawScore = result.SemanticSearch.RerankerScore.Value;
            }

            // Normalize score to 0-1 range
            // Semantic reranker scores are typically 0-4, so divide by 4
            var normalizedScore = Math.Clamp(rawScore / 4.0, 0.0, 1.0);

            // Build match reasons from highlights and captions
            var matchReasons = BuildMatchReasons(result);

            // Parse keywords from comma/space-separated string into list
            var keywords = ParseKeywords(doc.Keywords);

            results.Add(new RecordSearchResult
            {
                RecordId = doc.DataverseRecordId ?? doc.Id,
                RecordType = doc.RecordType ?? doc.DataverseEntityName ?? "unknown",
                RecordName = doc.RecordName ?? "Unknown Record",
                RecordDescription = doc.RecordDescription,
                ConfidenceScore = Math.Round(normalizedScore, 4),
                MatchReasons = matchReasons.Count > 0 ? matchReasons : null,
                Organizations = doc.Organizations?.Count > 0 ? doc.Organizations.ToList() : null,
                People = doc.People?.Count > 0 ? doc.People.ToList() : null,
                Keywords = keywords.Count > 0 ? keywords : null,
                CreatedAt = null, // Not available in records index; requires Dataverse enrichment
                ModifiedAt = doc.LastModified
            });
        }

        // Return results sorted by score descending
        return results
            .OrderByDescending(r => r.ConfidenceScore)
            .ToList();
    }

    /// <summary>
    /// Builds match reasons from search result highlights and semantic captions.
    /// </summary>
    private static List<string> BuildMatchReasons(Azure.Search.Documents.Models.SearchResult<SearchIndexDocument> result)
    {
        var reasons = new List<string>();

        // Try semantic captions first (richer explanations)
        if (result.SemanticSearch?.Captions?.Count > 0)
        {
            foreach (var caption in result.SemanticSearch.Captions)
            {
                var text = caption.Highlights ?? caption.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    reasons.Add(text);
                }
            }
        }

        // Add highlights from specific fields
        if (result.Highlights is not null)
        {
            if (result.Highlights.TryGetValue("recordName", out var nameHighlights))
            {
                reasons.AddRange(nameHighlights.Select(h => $"Name match: {h}"));
            }

            if (result.Highlights.TryGetValue("recordDescription", out var descHighlights))
            {
                reasons.AddRange(descHighlights.Select(h => $"Description match: {h}"));
            }
        }

        // Limit to 5 match reasons
        return reasons.Take(5).ToList();
    }

    /// <summary>
    /// Parses keywords from a comma/space-separated string into a list.
    /// </summary>
    private static List<string> ParseKeywords(string? keywords)
    {
        if (string.IsNullOrWhiteSpace(keywords))
        {
            return [];
        }

        return keywords
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim())
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Builds a cache key for the record search request.
    /// Format: records-search:{queryHash}:{recordTypesHash}:{filtersHash}:{options}:v1
    /// </summary>
    /// <remarks>
    /// Note: The records-index does not have tenantId, so tenant scoping is not applied
    /// at the cache key level. If tenant isolation is added to the index in the future,
    /// the tenantId should be included in the cache key.
    /// </remarks>
    private string BuildCacheKey(RecordSearchRequest request)
    {
        var queryHash = _embeddingCache.ComputeContentHash(request.Query);
        var recordTypesHash = _embeddingCache.ComputeContentHash(
            string.Join(",", request.RecordTypes.OrderBy(t => t)));

        var filtersHash = "none";
        if (request.Filters is not null)
        {
            var filterParts = new List<string>();
            if (request.Filters.Organizations is { Count: > 0 })
                filterParts.Add("org:" + string.Join(",", request.Filters.Organizations.OrderBy(o => o)));
            if (request.Filters.People is { Count: > 0 })
                filterParts.Add("ppl:" + string.Join(",", request.Filters.People.OrderBy(p => p)));
            if (request.Filters.ReferenceNumbers is { Count: > 0 })
                filterParts.Add("ref:" + string.Join(",", request.Filters.ReferenceNumbers.OrderBy(r => r)));

            if (filterParts.Count > 0)
                filtersHash = _embeddingCache.ComputeContentHash(string.Join("|", filterParts));
        }

        var mode = request.Options?.HybridMode ?? RecordHybridSearchMode.Rrf;
        var limit = request.Options?.Limit ?? 20;
        var offset = request.Options?.Offset ?? 0;

        return $"{CacheKeyPrefix}:{queryHash}:{recordTypesHash}:{filtersHash}:{mode}:{limit}:{offset}:v1";
    }

    /// <summary>
    /// Gets cached search response from Redis.
    /// </summary>
    private async Task<RecordSearchResponse?> GetFromCacheAsync(
        string cacheKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var cached = await _distributedCache.GetStringAsync(cacheKey, cancellationToken);
            if (cached is not null)
            {
                return System.Text.Json.JsonSerializer.Deserialize<RecordSearchResponse>(cached);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read record search from cache, key={CacheKey}", cacheKey);
        }

        return null;
    }

    /// <summary>
    /// Caches search response in Redis.
    /// </summary>
    private async Task SetInCacheAsync(
        string cacheKey,
        RecordSearchResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(response);
            await _distributedCache.SetStringAsync(
                cacheKey,
                json,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheExpirationMinutes)
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache record search results, key={CacheKey}", cacheKey);
        }
    }

    /// <summary>
    /// Escapes a value for use in OData filter expressions.
    /// Prevents filter injection by escaping single quotes.
    /// </summary>
    private static string EscapeODataValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        // In OData, single quotes are escaped by doubling them
        return value.Replace("'", "''");
    }

    #endregion
}
