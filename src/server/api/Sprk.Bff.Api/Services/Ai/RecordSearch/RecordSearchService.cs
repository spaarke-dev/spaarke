using System.Diagnostics;
using System.Text.Json;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
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
/// Per FR-12 (spaarke-ai-azure-setup-dev-r1): the spaarke-records-index now has a tenantId
/// field and this service applies an unconditional <c>tenantId eq '...'</c> OData filter
/// (derived from the user's 'tid' claim via <see cref="IHttpContextAccessor"/>) so cross-tenant
/// record leaks are impossible at the search layer. The existing Dataverse-layer enforcement
/// remains as defense-in-depth.
/// </para>
/// </remarks>
public sealed class RecordSearchService : IRecordSearchService
{
    private readonly SearchIndexClient _searchIndexClient;
    private readonly IOpenAiClient _openAiClient;
    private readonly IEmbeddingCache _embeddingCache;
    private readonly ITenantCache _distributedCache;

    // ITenantCache resource name (FR-05 redis remediation r1). Final on-wire key:
    // spaarke:tenant:{tenantId}:record-search:{requestHash}:v2
    // Tenant scoping in the cache key avoids cross-tenant cache poisoning. It is separate from the
    // tenantId search filter (class remarks), which is the search-layer tenant boundary.
    private const string CacheResource = "record-search";
    // v2: the cache id now covers the explicit index and PreferKeywordRanking and hashes an unambiguous
    // serialisation (review F1); v1 entries are unreachable by construction.
    private const int CacheVersion = 2;
    private readonly DocumentIntelligenceOptions _docIntelOptions;
    private readonly ILogger<RecordSearchService> _logger;
    // multi-container-multi-index-r1 FR-BFF-07 (part 3) — resolver routing for caller-supplied
    // explicit index. Allow-list validation lives inside KnowledgeDeploymentService (task 010);
    // RecordSearchService never replicates that logic locally (single-source-of-truth invariant).
    // Required for the explicit-index path; the existing direct path never touches this field
    // (NFR-02 backward-compat).
    private readonly IKnowledgeDeploymentService _deploymentService;
    // On the explicit-index path, also used to derive tenantId for the resolver's cache key.
    // This is separate from the tenantId search filter (class remarks): here tenantId is
    // a resolver cache-key isolator per (tenant, indexName) — see KnowledgeDeploymentService
    // GetOrCreateExplicitIndexClient. Optional so existing direct-path tests need not stand up
    // an HttpContext mock.
    private readonly IHttpContextAccessor? _httpContextAccessor;

    // Tenant to filter on when there is NO user context (background/job callers such as the
    // Communication Association Engine's semantic-match rung, run from InboundPollingBackupService /
    // IncomingCommunicationJobHandler — neither carries a 'tid' claim). The records index is populated
    // by RecordSyncJob, which stamps every document with AzureAd:TenantId (see RecordSyncJob.SyncEntityAsync).
    // Filtering on the literal "system" here yielded ZERO matches for all no-user callers (the semantic
    // rung could never find an existing matter/project). This fallback keeps the search-side tenant
    // symmetric with the sync-side tenant so background callers match the same records an interactive
    // (single-tenant) user would. Empty config → "system" (defensive; there are no "system"-stamped records).
    private readonly string _noUserTenantFallback;

    // Index name for record search
    private const string RecordsIndexName = "spaarke-records-index";

    // Semantic configuration name from the index definition
    private const string SemanticConfigurationName = "default-semantic-config";

    // Vector field name (3072-dim text-embedding-3-large)
    private const string VectorFieldName = "contentVector";

    // Search fields for keyword queries (all searchable fields in the records index)
    private static readonly string[] SearchFields =
        ["recordName", "recordDescription", "keywords", "organizations", "people"];

    // Fields to select from index results (all non-vector fields).
    // FR-12: tenantId included so retrievable=true is honored end-to-end (useful for audit/diagnostics).
    private static readonly string[] SelectFields =
    [
        "id", "tenantId", "recordType", "recordName", "recordDescription",
        "organizations", "people", "referenceNumbers", "keywords",
        "lastModified", "dataverseRecordId", "dataverseEntityName"
    ];

    // Cache configuration
    // CacheKeyPrefix removed — tenant scoping + on-wire key construction now lives in
    // ITenantCache wrapper (FR-05 redis remediation r1). Resource name is "record-search".
    private const int CacheExpirationMinutes = 5;

    // Saturation constant for the keyword-ranked (BM25) score→[0,1) transform score/(score+K).
    // K=20 gives good spread across typical record-index BM25 scores; see ProcessSearchResultsAsync.
    private const double Bm25NormalizationConstant = 20.0;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecordSearchService"/> class.
    /// </summary>
    /// <remarks>
    /// <para>
    /// multi-container-multi-index-r1 FR-BFF-07 (part 3): adds <paramref name="deploymentService"/>
    /// as the 7th constructor parameter so a caller-supplied
    /// <see cref="RecordSearchRequest.SearchIndexName"/> is routed through the resolver
    /// where allow-list validation (FR-BFF-02 / NFR-08) lives. <paramref name="httpContextAccessor"/>
    /// is the 8th parameter and is consumed ONLY when <c>SearchIndexName</c> is non-empty,
    /// to derive the tenantId required by
    /// <see cref="IKnowledgeDeploymentService.GetSearchClientAsync(string, string?, CancellationToken)"/>
    /// for per-(tenant, index) cache-key isolation. (Separately, the records-index has a tenantId
    /// field, and every search applies the tenantId filter described in the class remarks.)
    /// </para>
    /// <para>
    /// NFR-02 backward-compat: the existing direct path (when <c>SearchIndexName</c> is null
    /// or whitespace) is preserved verbatim — it never touches the resolver. Test fixtures
    /// updated to construct the 8-arg ctor exercise the same direct-path assertions as before.
    /// </para>
    /// </remarks>
    public RecordSearchService(
        SearchIndexClient searchIndexClient,
        IOpenAiClient openAiClient,
        IEmbeddingCache embeddingCache,
        ITenantCache distributedCache,
        IOptions<DocumentIntelligenceOptions> docIntelOptions,
        ILogger<RecordSearchService> logger,
        IKnowledgeDeploymentService deploymentService,
        IConfiguration configuration,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _searchIndexClient = searchIndexClient;
        _openAiClient = openAiClient;
        _embeddingCache = embeddingCache;
        _distributedCache = distributedCache;
        _docIntelOptions = docIntelOptions.Value;
        _logger = logger;
        _deploymentService = deploymentService ?? throw new ArgumentNullException(nameof(deploymentService));
        _httpContextAccessor = httpContextAccessor;

        // AzureAd:TenantId is the canonical single-tenant identity for background/no-user callers —
        // exactly the value RecordSyncJob stamps on every indexed record. See _noUserTenantFallback remarks.
        var configuredTenant = configuration?["AzureAd:TenantId"];
        _noUserTenantFallback = string.IsNullOrWhiteSpace(configuredTenant) ? "system" : configuredTenant;
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
        // When set (Association Engine record-matching rungs), rank by keyword relevance (BM25) and skip the
        // semantic reranker so exact-name records are not buried under conceptually-similar ones.
        var preferKeywordRanking = request.Options?.PreferKeywordRanking ?? false;

        // GitHub #3239: a query of just "*" is Azure AI Search's match-all ("every record that passes the filters"), not
        // text to embed. Sent with a query vector it becomes a PURE VECTOR query, and Azure rejects highlighting on one
        // (400 "Parameter 'highlight' is not supported for search requests with pure vector queries"), which surfaced as a
        // 500. So it runs as a plain keyword "*" search: no embedding, no highlights, no semantic ranking.
        var matchAll = MatchAllQuery.IsMatchAll(request.Query);
        if (matchAll)
        {
            hybridMode = RecordHybridSearchMode.KeywordOnly;
        }

        _logger.LogDebug(
            "Starting record search: mode={Mode}, query length={QueryLength}, recordTypes=[{RecordTypes}]",
            hybridMode, request.Query.Length, string.Join(",", request.RecordTypes));

        try
        {
            // Step 1: Check Redis cache (FR-05 + ADR-014)
            // Derive tenantId for cache-key isolation (cache-side scoping; the search-layer
            // tenant boundary is the tenantId filter applied below).
            var tenantIdForCache = _httpContextAccessor?.HttpContext?.User?.FindFirst("tid")?.Value
                ?? _httpContextAccessor?.HttpContext?.User?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
                ?? _noUserTenantFallback;
            // Step 1a: Resolve the search client BEFORE the cache lookup. On the explicit-index path this
            // is where the allow-list is enforced (INDEX_NOT_ALLOWED), so a cached answer can never stand
            // in for the refusal the caller would otherwise get (adversarial review F1).
            var searchClient = await ResolveSearchClientAsync(request, cancellationToken);

            var cacheId = BuildCacheId(request);
            var cachedResult = await GetFromCacheAsync(tenantIdForCache, cacheId, cancellationToken);
            if (cachedResult is not null)
            {
                _logger.LogDebug("Record search cache hit for id {CacheId}", cacheId);
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

            // Step 4: Build OData filter (FR-12: tenantId predicate ALWAYS present —
            // never bypassed even when SearchIndexName is supplied. tenantIdForCache is
            // resolved above from the user's 'tid' claim; reuse it here as the source-of-truth.)
            var filter = BuildRecordFilter(request, tenantIdForCache);

            // Step 5: Build search options
            searchStopwatch.Start();
            var searchOptions = BuildSearchOptions(
                hybridMode,
                queryEmbedding,
                filter,
                limit,
                offset,
                preferKeywordRanking,
                matchAll);

            // Step 6: Execute search
            var searchText = !matchAll && hybridMode != RecordHybridSearchMode.VectorOnly && !string.IsNullOrWhiteSpace(request.Query)
                ? request.Query
                : "*";

            var response = await searchClient.SearchAsync<SearchIndexDocument>(
                searchText, searchOptions, cancellationToken);
            var searchResults = response.Value;
            searchStopwatch.Stop();

            // Step 7: Process results
            var results = await ProcessSearchResultsAsync(searchResults, preferKeywordRanking, cancellationToken);

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
            await SetInCacheAsync(tenantIdForCache, cacheId, searchResponse, cancellationToken);

            _logger.LogInformation(
                "Record search completed: {ResultCount}/{TotalCount} results in {TotalMs}ms " +
                "(embedding={EmbeddingMs}ms, search={SearchMs}ms, mode={Mode}, recordTypes=[{RecordTypes}])",
                results.Count, totalResults, totalStopwatch.ElapsedMilliseconds,
                embeddingStopwatch.ElapsedMilliseconds, searchStopwatch.ElapsedMilliseconds,
                hybridMode, string.Join(",", request.RecordTypes));

            return searchResponse;
        }
        catch (RequestFailedException ex) when (ex.Status == 400 && IsCallerNamedOtherIndex(request.SearchIndexName))
        {
            // GitHub #3307: the caller named an allowed index that is not a records index (a document index, say), and the
            // index rejected the record-search request because it has none of the records fields. That is the caller's
            // choice of index, so it is a 400 naming the index, not a 500. Not applied to the configured records index: a
            // 400 from THAT index is a defect in this request, and stays a 500.
            _logger.LogWarning(ex,
                "Record search rejected by caller-named index {IndexName}; it is not a records index", request.SearchIndexName);

            throw new Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException(
                code: Sprk.Bff.Api.Models.Ai.SemanticSearch.SearchErrorCodes.IndexNotRecordsIndex,
                title: "AI Search index not usable for record search",
                detail: $"The AI Search index '{request.SearchIndexName!.Trim()}' is not a records index, so record search cannot run on it.",
                statusCode: 400,
                extensions: new Dictionary<string, object> { ["indexName"] = request.SearchIndexName.Trim() });
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure AI Search error during record search");
            throw;
        }
    }

    #region Private Methods

    /// <summary>
    /// Gets the SearchClient for the records index.
    /// </summary>
    /// <remarks>
    /// multi-container-multi-index-r1 FR-BFF-07 (part 3): when the caller supplied an explicit
    /// <see cref="RecordSearchRequest.SearchIndexName"/>, route via the 3-arg resolver overload. That
    /// overload validates the index against <c>AiSearchOptions.AllowedIndexes</c> (FR-BFF-02 / NFR-08) —
    /// this service MUST NOT replicate that logic locally. On rejection the resolver throws
    /// <c>SdapProblemException(INDEX_NOT_ALLOWED, 400)</c>, which propagates to the endpoint where the
    /// ProblemDetails middleware renders it. Must run before any cache read (see <see cref="SearchAsync"/>).
    /// When SearchIndexName is null/whitespace the original direct
    /// <c>SearchIndexClient.GetSearchClient(indexName)</c> path is preserved verbatim (NFR-02).
    /// </remarks>
    private async Task<SearchClient> ResolveSearchClientAsync(
        RecordSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SearchIndexName))
        {
            // Derive tenantId from the current HttpContext for resolver cache-key isolation.
            // This value is consumed only by KnowledgeDeploymentService for its
            // {tenantId}:explicit:{indexName} cache key; the tenantId search filter is built
            // separately.
            var tenantId = _httpContextAccessor?.HttpContext?.User?.FindFirst("tid")?.Value
                ?? _httpContextAccessor?.HttpContext?.User?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
                ?? "anonymous";
            return await _deploymentService.GetSearchClientAsync(
                tenantId, request.SearchIndexName, cancellationToken);
        }

        var indexName = _docIntelOptions.AiSearchIndexName;
        if (string.IsNullOrWhiteSpace(indexName))
        {
            indexName = RecordsIndexName;
        }
        return _searchIndexClient.GetSearchClient(indexName);
    }

    /// <summary>
    /// True when the caller named an index and it is not the configured records index (GitHub #3307).
    /// </summary>
    private bool IsCallerNamedOtherIndex(string? searchIndexName)
    {
        if (string.IsNullOrWhiteSpace(searchIndexName))
        {
            return false;
        }

        var recordsIndex = string.IsNullOrWhiteSpace(_docIntelOptions.AiSearchIndexName)
            ? RecordsIndexName
            : _docIntelOptions.AiSearchIndexName;

        return !string.Equals(searchIndexName.Trim(), recordsIndex.Trim(), StringComparison.OrdinalIgnoreCase);
    }

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
    /// Always includes tenantId + recordType filters; optionally adds organizations, people, referenceNumbers.
    /// </summary>
    /// <remarks>
    /// FR-12: tenantId predicate is ALWAYS first and unconditional — mirrors the
    /// <see cref="Sprk.Bff.Api.Services.Ai.SemanticSearch.SearchFilterBuilder"/> pattern and
    /// matches ADR-014 tenant-isolation invariant used by knowledge/files/session indexes.
    /// </remarks>
    private static string BuildRecordFilter(RecordSearchRequest request, string tenantId)
    {
        var filterParts = new List<string>();

        // 0. tenantId filter (ALWAYS first, ALWAYS present — FR-12)
        filterParts.Add($"tenantId eq '{EscapeODataValue(tenantId)}'");

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

            // MatterTypes filter (single-valued string field, GUID ids only)
            var matterTypesFilter = Sprk.Bff.Api.Services.Ai.SemanticSearch.SearchFilterBuilder
                .BuildMatterTypesFilter(request.Filters.MatterTypes);
            if (matterTypesFilter is not null)
            {
                filterParts.Add(matterTypesFilter);
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
        int offset,
        bool preferKeywordRanking,
        bool matchAll)
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

        // Enable semantic ranking for hybrid and keyword modes — UNLESS the caller prefers keyword (BM25)
        // ranking. The semantic reranker orders by conceptual similarity and buries exact-name matches, so
        // the Association Engine's record-matching rungs opt out (preferKeywordRanking) to rank by name/keyword
        // relevance. When opted out, results carry the raw BM25 @search.score (no reranker score).
        // Neither the semantic ranker nor highlights have query text to work on for a match-all ("*") search.
        if (useKeyword && !preferKeywordRanking && !matchAll)
        {
            searchOptions.QueryType = SearchQueryType.Semantic;
            searchOptions.SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = SemanticConfigurationName,
                QueryCaption = new QueryCaption(QueryCaptionType.Extractive)
            };
        }

        // Enable highlighting on relevant fields — only when a keyword query runs. A vector-only request is sent with the
        // match-all text "*", i.e. a pure vector query, and Azure AI Search rejects highlighting on those (400).
        if (useKeyword && !matchAll)
        {
            searchOptions.HighlightFields.Add("recordName");
            searchOptions.HighlightFields.Add("recordDescription");
        }

        return searchOptions;
    }

    /// <summary>
    /// Processes search results into RecordSearchResult format.
    /// Normalizes semantic reranker scores from 0-4 range to 0-1 range.
    /// </summary>
    private async Task<IReadOnlyList<RecordSearchResult>> ProcessSearchResultsAsync(
        SearchResults<SearchIndexDocument> searchResults,
        bool preferKeywordRanking,
        CancellationToken cancellationToken)
    {
        var results = new List<RecordSearchResult>();

        await foreach (var result in searchResults.GetResultsAsync().WithCancellation(cancellationToken))
        {
            if (result.Document is null) continue;

            var doc = result.Document;
            var rawScore = result.Score ?? 0;

            double normalizedScore;
            if (!preferKeywordRanking && result.SemanticSearch?.RerankerScore.HasValue == true)
            {
                // Semantic reranker scores are 0-4 → divide by 4.
                normalizedScore = Math.Clamp(result.SemanticSearch.RerankerScore.Value / 4.0, 0.0, 1.0);
            }
            else
            {
                // Keyword-ranked (BM25) path: @search.score is unbounded, so map it to [0,1) with a bounded
                // saturating transform score/(score+K). K=20 keeps meaningful spread across typical record-index
                // BM25 scores (e.g. 55→0.73, 48→0.71, 31→0.61) while never reaching 1.0. Preserves ordering.
                normalizedScore = rawScore / (rawScore + Bm25NormalizationConstant);
            }

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
                ReferenceNumbers = doc.ReferenceNumbers?.Count > 0 ? doc.ReferenceNumbers.ToList() : null,
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
    /// Builds the cache "id" component consumed by ITenantCache: a hash of an unambiguous (JSON)
    /// serialisation of everything that changes what a request returns or whether it is allowed.
    /// </summary>
    /// <remarks>
    /// Includes the normalised explicit index name and <c>PreferKeywordRanking</c>, which the id omitted
    /// before (adversarial review F1). Lists go in as JSON arrays — never joined with a delimiter — so
    /// <c>["a","b"]</c> and <c>["a,b"]</c> cannot collide. Lists are sorted (ordinal), so order is not part
    /// of a request's identity. The cache is read only AFTER the explicit index passed the allow-list.
    /// </remarks>
    private string BuildCacheId(RecordSearchRequest request)
    {
        static string[] Sorted(IEnumerable<string>? values) =>
            values is null ? [] : values.OrderBy(v => v, StringComparer.Ordinal).ToArray();

        var identity = new
        {
            q = request.Query,
            types = Sorted(request.RecordTypes),
            orgs = Sorted(request.Filters?.Organizations),
            people = Sorted(request.Filters?.People),
            refs = Sorted(request.Filters?.ReferenceNumbers),
            matterTypes = Sorted(request.Filters?.MatterTypes),
            index = string.IsNullOrWhiteSpace(request.SearchIndexName)
                ? null
                : request.SearchIndexName.Trim().ToLowerInvariant(),
            keyword = request.Options?.PreferKeywordRanking ?? false,
            mode = request.Options?.HybridMode ?? RecordHybridSearchMode.Rrf,
            limit = request.Options?.Limit ?? 20,
            offset = request.Options?.Offset ?? 0,
        };

        return _embeddingCache.ComputeContentHash(JsonSerializer.Serialize(identity));
    }

    /// <summary>
    /// Gets cached search response from Redis via ITenantCache.
    /// </summary>
    private async Task<RecordSearchResponse?> GetFromCacheAsync(
        string tenantId,
        string cacheId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _distributedCache.GetAsync<RecordSearchResponse>(
                tenantId, CacheResource, cacheId, CacheVersion, ct: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read record search from cache, id={CacheId}", cacheId);
        }

        return null;
    }

    /// <summary>
    /// Caches search response in Redis via ITenantCache.
    /// </summary>
    private async Task SetInCacheAsync(
        string tenantId,
        string cacheId,
        RecordSearchResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            await _distributedCache.SetAsync(
                tenantId, CacheResource, cacheId, CacheVersion,
                response, TimeSpan.FromMinutes(CacheExpirationMinutes),
                ct: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache record search results, id={CacheId}", cacheId);
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
