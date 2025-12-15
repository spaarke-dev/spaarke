using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai.Rag;

/// <summary>
/// Caching decorator for IRagService implementing ADR-009 (Redis-First Caching).
/// Caches search results to reduce Azure AI Search load and improve latency.
///
/// Cache Strategy:
/// - Search results cached by query hash + filters
/// - TTL: 5 minutes (default) - configurable per search
/// - Embeddings NOT cached (generated fresh for each query)
/// - Grounded context inherits search caching
///
/// Key format: sdap:rag:search:{sha256(query+filters)}
/// </summary>
public class RagCacheService : IRagService
{
    private readonly IRagService _inner;
    private readonly IDistributedCache _cache;
    private readonly ILogger<RagCacheService> _logger;
    private readonly CacheMetrics? _metrics;

    private const string CacheKeyPrefix = "sdap:rag:search";
    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromMinutes(5);

    // JSON serialization options for cache
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public RagCacheService(
        IRagService inner,
        IDistributedCache cache,
        ILogger<RagCacheService> logger,
        CacheMetrics? metrics = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics;
    }

    /// <inheritdoc />
    public async Task<RagSearchResult> SearchAsync(
        RagSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildCacheKey(request);
        var sw = Stopwatch.StartNew();

        // Try cache first
        try
        {
            var cachedJson = await _cache.GetStringAsync(cacheKey, cancellationToken);
            if (cachedJson != null)
            {
                sw.Stop();
                var cached = JsonSerializer.Deserialize<RagSearchResult>(cachedJson, JsonOptions);
                if (cached != null)
                {
                    _logger.LogDebug(
                        "RAG cache HIT for key {Key}..., returning {ResultCount} cached results",
                        cacheKey[..Math.Min(40, cacheKey.Length)],
                        cached.Results.Length);

                    _metrics?.RecordHit(sw.Elapsed.TotalMilliseconds);

                    // Return cached result with updated duration (cache read time)
                    return cached with { DurationMs = sw.ElapsedMilliseconds };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error reading RAG cache for key {Key}..., falling back to search",
                cacheKey[..Math.Min(40, cacheKey.Length)]);
        }

        sw.Stop();
        _metrics?.RecordMiss(sw.Elapsed.TotalMilliseconds);

        // Cache miss - perform actual search
        _logger.LogDebug(
            "RAG cache MISS for key {Key}..., performing search",
            cacheKey[..Math.Min(40, cacheKey.Length)]);

        var result = await _inner.SearchAsync(request, cancellationToken);

        // Cache successful results only
        if (result.Success && result.Results.Length > 0)
        {
            await CacheResultAsync(cacheKey, result, DefaultCacheTtl, cancellationToken);
        }

        return result;
    }

    /// <inheritdoc />
    public Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        // Embeddings are NOT cached - they should be fresh for each query
        // Caching embeddings would add complexity without much benefit
        // (embedding generation is fast ~50ms vs search ~200ms)
        return _inner.GenerateEmbeddingAsync(text, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GroundedContext> GetGroundedContextAsync(
        string query,
        Guid customerId,
        Guid[]? knowledgeSourceIds = null,
        int maxChunks = 5,
        CancellationToken cancellationToken = default)
    {
        // Grounded context uses SearchAsync internally, which is cached
        // We don't cache the formatted context separately as it's derived from search
        return await _inner.GetGroundedContextAsync(
            query,
            customerId,
            knowledgeSourceIds,
            maxChunks,
            cancellationToken);
    }

    private static string BuildCacheKey(RagSearchRequest request)
    {
        // Create a deterministic key from request parameters
        var keyBuilder = new StringBuilder();
        keyBuilder.Append(request.Query);
        keyBuilder.Append('|');
        keyBuilder.Append(request.CustomerId);
        keyBuilder.Append('|');
        keyBuilder.Append(request.Mode);
        keyBuilder.Append('|');
        keyBuilder.Append(request.Top);
        keyBuilder.Append('|');
        keyBuilder.Append(request.MinScore);
        keyBuilder.Append('|');
        keyBuilder.Append(request.IncludePublic);
        keyBuilder.Append('|');
        keyBuilder.Append(request.UseSemanticReranking);

        if (request.KnowledgeSourceIds?.Length > 0)
        {
            keyBuilder.Append('|');
            keyBuilder.Append(string.Join(",", request.KnowledgeSourceIds.OrderBy(id => id)));
        }

        if (!string.IsNullOrEmpty(request.KnowledgeType))
        {
            keyBuilder.Append('|');
            keyBuilder.Append(request.KnowledgeType);
        }

        if (!string.IsNullOrEmpty(request.Category))
        {
            keyBuilder.Append('|');
            keyBuilder.Append(request.Category);
        }

        if (request.Tags?.Length > 0)
        {
            keyBuilder.Append('|');
            keyBuilder.Append(string.Join(",", request.Tags.OrderBy(t => t)));
        }

        // Hash the key to avoid excessively long cache keys
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyBuilder.ToString()));
        var hash = Convert.ToBase64String(hashBytes).Replace('/', '_').Replace('+', '-');

        return $"{CacheKeyPrefix}:{hash}";
    }

    private async Task CacheResultAsync(
        string cacheKey,
        RagSearchResult result,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(result, JsonOptions);

            await _cache.SetStringAsync(
                cacheKey,
                json,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl
                },
                cancellationToken);

            _logger.LogDebug(
                "Cached RAG results for key {Key}... with TTL {TTL} minutes, {ResultCount} results",
                cacheKey[..Math.Min(40, cacheKey.Length)],
                ttl.TotalMinutes,
                result.Results.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error caching RAG results for key {Key}...",
                cacheKey[..Math.Min(40, cacheKey.Length)]);
            // Don't throw - caching is optimization, not requirement
        }
    }
}
