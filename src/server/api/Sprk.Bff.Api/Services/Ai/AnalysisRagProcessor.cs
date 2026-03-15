using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Telemetry;
using AnalysisDocumentResult = Sprk.Bff.Api.Models.Ai.DocumentAnalysisResult;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Handles RAG (Retrieval-Augmented Generation) search, caching, and tenant resolution
/// for the analysis pipeline.
/// Extracted from AnalysisOrchestrationService to reduce constructor dependency count (ADR-010).
/// </summary>
public class AnalysisRagProcessor
{
    private readonly IRagService _ragService;
    private readonly RagQueryBuilder _ragQueryBuilder;
    private readonly IDistributedCache _cache;
    private readonly CacheMetrics? _cacheMetrics;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AnalysisOptions _options;
    private readonly ILogger<AnalysisRagProcessor> _logger;

    /// <summary>
    /// TTL for cached RAG search results. Short TTL ensures index updates are reflected quickly.
    /// </summary>
    private static readonly TimeSpan RagCacheTtl = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Limits concurrent RAG searches to prevent overwhelming Azure AI Search.
    /// Per ADR-013: no unbounded Task.WhenAll on throttled services.
    /// </summary>
    private static readonly SemaphoreSlim _ragSearchSemaphore = new(5);

    public AnalysisRagProcessor(
        IRagService ragService,
        RagQueryBuilder ragQueryBuilder,
        IDistributedCache cache,
        IHttpContextAccessor httpContextAccessor,
        IOptions<AnalysisOptions> options,
        ILogger<AnalysisRagProcessor> logger,
        CacheMetrics? cacheMetrics = null)
    {
        _ragService = ragService;
        _ragQueryBuilder = ragQueryBuilder;
        _cache = cache;
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
        _logger = logger;
        _cacheMetrics = cacheMetrics;
    }

    /// <summary>
    /// Gets the tenant ID from the current HTTP context claims.
    /// </summary>
    public string? GetTenantIdFromClaims()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null)
        {
            return null;
        }

        return user.FindFirstValue("tid") ??
               user.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid") ??
               user.FindFirstValue("tenant_id");
    }

    /// <summary>
    /// Process knowledge sources, replacing RAG types with inline content from search results.
    /// This implements RAG (Retrieval-Augmented Generation) by querying Azure AI Search.
    /// Uses <see cref="RagQueryBuilder"/> to construct metadata-aware queries from the
    /// DocumentAnalysisResult rather than the naive first-500-characters approach.
    /// </summary>
    public async Task<AnalysisKnowledge[]> ProcessRagKnowledgeAsync(
        AnalysisKnowledge[] knowledge,
        AnalysisDocumentResult analysisResult,
        CancellationToken cancellationToken)
    {
        if (knowledge.Length == 0)
        {
            return knowledge;
        }

        var ragSources = knowledge.Where(k => k.Type == KnowledgeType.RagIndex).ToArray();
        if (ragSources.Length == 0)
        {
            _logger.LogDebug("No RAG knowledge sources to process");
            return knowledge;
        }

        var tenantId = GetTenantIdFromClaims();
        if (string.IsNullOrEmpty(tenantId))
        {
            _logger.LogWarning("Cannot process RAG knowledge: TenantId not found in claims");
            return knowledge;
        }

        _logger.LogInformation("Processing {RagCount} RAG knowledge sources for tenant {TenantId}",
            ragSources.Length, tenantId);

        var ragQuery = _ragQueryBuilder.BuildQuery(analysisResult, tenantId);

        _logger.LogDebug(
            "Built RAG query: searchText length={SearchTextLength}, filter={Filter}",
            ragQuery.SearchText.Length, ragQuery.FilterExpression);

        var nonRagSources = knowledge.Where(k => k.Type != KnowledgeType.RagIndex).ToList();

        var parallelStopwatch = Stopwatch.StartNew();

        var ragSearchTasks = ragSources.Select(async source =>
        {
            await _ragSearchSemaphore.WaitAsync(cancellationToken);
            try
            {
                var sourceStopwatch = Stopwatch.StartNew();

                var searchOptions = new RagSearchOptions
                {
                    TenantId = tenantId,
                    DeploymentId = source.DeploymentId,
                    KnowledgeSourceId = source.Id.ToString(),
                    TopK = _options.MaxKnowledgeResults,
                    MinScore = _options.MinRelevanceScore,
                    UseSemanticRanking = true,
                    UseVectorSearch = true,
                    UseKeywordSearch = true
                };

                _logger.LogDebug("Searching RAG index for knowledge source {SourceId}: {SourceName}",
                    source.Id, source.Name);

                var ragCacheKey = ComputeRagCacheKey(source.Id.ToString(), ragQuery.SearchText);
                var searchResult = await GetOrSearchRagCacheAsync(
                    ragCacheKey, ragQuery.SearchText, searchOptions, cancellationToken);

                sourceStopwatch.Stop();

                if (searchResult.Results.Count == 0)
                {
                    _logger.LogDebug("No RAG results found for knowledge source {SourceId} in {Duration}ms",
                        source.Id, sourceStopwatch.ElapsedMilliseconds);
                    return (AnalysisKnowledge?)null;
                }

                var ragContent = new StringBuilder();
                ragContent.AppendLine($"Retrieved from knowledge base: {source.Name}");
                ragContent.AppendLine();

                foreach (var result in searchResult.Results)
                {
                    ragContent.AppendLine($"### {result.DocumentName} (Relevance: {result.Score:P0})");
                    ragContent.AppendLine(result.Content);
                    ragContent.AppendLine();
                }

                var inlineSource = source with
                {
                    Type = KnowledgeType.Inline,
                    Content = ragContent.ToString()
                };

                _logger.LogInformation(
                    "RAG search for {SourceName} returned {ResultCount} results in {SourceDuration}ms",
                    source.Name, searchResult.Results.Count, sourceStopwatch.ElapsedMilliseconds);

                return (AnalysisKnowledge?)inlineSource;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to search RAG index for knowledge source {SourceId}: {SourceName}",
                    source.Id, source.Name);
                return (AnalysisKnowledge?)null;
            }
            finally
            {
                _ragSearchSemaphore.Release();
            }
        }).ToArray();

        var ragResults = await Task.WhenAll(ragSearchTasks);
        parallelStopwatch.Stop();

        var processedKnowledge = nonRagSources
            .Concat(ragResults.Where(r => r is not null).Cast<AnalysisKnowledge>())
            .ToList();

        _logger.LogInformation(
            "Processed {RagCount} RAG sources in parallel in {TotalDuration}ms ({SuccessCount} succeeded, {FailedCount} failed/empty). Total knowledge: {ProcessedCount}",
            ragSources.Length, parallelStopwatch.ElapsedMilliseconds,
            ragResults.Count(r => r is not null), ragResults.Count(r => r is null),
            processedKnowledge.Count);

        return processedKnowledge.ToArray();
    }

    /// <summary>
    /// Computes a composite Redis cache key for RAG search results.
    /// Key pattern: sdap:ai:rag:{knowledgeSourceId}:{SHA256 hash of query text}.
    /// Per ADR-009: Redis-first caching with structured key hierarchy.
    /// </summary>
    internal static string ComputeRagCacheKey(string knowledgeSourceId, string queryText)
    {
        var queryHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(queryText))).ToLowerInvariant();
        return $"sdap:ai:rag:{knowledgeSourceId}:{queryHash}";
    }

    /// <summary>
    /// Cache-aside pattern for RAG search results (ADR-009).
    /// Checks Redis cache first; on miss, executes the search and caches the result.
    /// Cache failures are handled gracefully -- search proceeds without caching.
    /// </summary>
    internal async Task<RagSearchResponse> GetOrSearchRagCacheAsync(
        string cacheKey,
        string searchText,
        RagSearchOptions searchOptions,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        // Step 1: Try cache
        try
        {
            var cachedJson = await _cache.GetStringAsync(cacheKey, cancellationToken);
            if (cachedJson is not null)
            {
                var cachedResult = JsonSerializer.Deserialize<RagSearchResponse>(cachedJson);
                if (cachedResult is not null)
                {
                    sw.Stop();
                    _cacheMetrics?.RecordHit(sw.Elapsed.TotalMilliseconds, "rag");
                    _logger.LogDebug("RAG cache HIT for key {CacheKey} in {ElapsedMs}ms",
                        cacheKey, sw.ElapsedMilliseconds);
                    return cachedResult;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RAG cache read error for key {CacheKey}, proceeding with live search", cacheKey);
        }

        sw.Stop();
        _cacheMetrics?.RecordMiss(sw.Elapsed.TotalMilliseconds, "rag");

        // Step 2: Cache miss -- execute live search
        var searchResult = await _ragService.SearchAsync(searchText, searchOptions, cancellationToken);

        // Step 3: Store result in cache
        try
        {
            var json = JsonSerializer.Serialize(searchResult);
            await _cache.SetStringAsync(
                cacheKey,
                json,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = RagCacheTtl
                },
                cancellationToken);

            _logger.LogDebug("RAG cache SET for key {CacheKey}, TTL={TtlMinutes}min, results={ResultCount}",
                cacheKey, RagCacheTtl.TotalMinutes, searchResult.Results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RAG cache write error for key {CacheKey}", cacheKey);
        }

        return searchResult;
    }
}
