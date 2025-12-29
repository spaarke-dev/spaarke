using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Redis-based embedding cache following ADR-009 Redis-First Caching patterns.
/// Reduces Azure OpenAI API costs and latency by caching embeddings by content hash.
/// </summary>
/// <remarks>
/// Implementation follows GraphTokenCache patterns:
/// - SHA256 hashing for cache keys (consistent length, safe for any content)
/// - Graceful error handling (cache failures don't break embedding generation)
/// - OpenTelemetry-compatible metrics via CacheMetrics
/// - Base64 serialization for embedding vectors
///
/// Cache key format: sdap:embedding:{base64-sha256-hash}
/// TTL: 7 days (embeddings are deterministic for same model version)
///
/// Serialization: float[] → byte[] via Buffer.BlockCopy → Base64 string
/// This is more efficient than JSON for large float arrays.
/// </remarks>
public class EmbeddingCache : IEmbeddingCache
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<EmbeddingCache> _logger;
    private readonly CacheMetrics? _metrics;

    // Cache TTL: 7 days - embeddings are deterministic for same content + model
    // Balance between freshness (model updates) and cost savings
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromDays(7);

    // Cache key prefix following SDAP naming convention
    private const string CacheKeyPrefix = "sdap:embedding:";

    // Cache type for metrics
    private const string CacheType = "embedding";

    public EmbeddingCache(
        IDistributedCache cache,
        ILogger<EmbeddingCache> logger,
        CacheMetrics? metrics = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics; // Optional: metrics can be null if not configured
    }

    /// <inheritdoc />
    public string ComputeContentHash(string content)
    {
        if (string.IsNullOrEmpty(content))
            throw new ArgumentException("Content cannot be null or empty", nameof(content));

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToBase64String(hashBytes);
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>?> GetEmbeddingAsync(
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(contentHash))
            throw new ArgumentException("Content hash cannot be null or empty", nameof(contentHash));

        var cacheKey = $"{CacheKeyPrefix}{contentHash}";
        var sw = Stopwatch.StartNew();

        try
        {
            var cachedData = await _cache.GetAsync(cacheKey, cancellationToken);
            sw.Stop();

            if (cachedData != null)
            {
                // Cache HIT - deserialize embedding
                var embedding = DeserializeEmbedding(cachedData);
                _logger.LogDebug(
                    "Embedding cache HIT for hash {Hash}..., vector length={Length}",
                    contentHash[..Math.Min(8, contentHash.Length)],
                    embedding.Length);
                _metrics?.RecordHit(sw.Elapsed.TotalMilliseconds, CacheType);
                return embedding;
            }
            else
            {
                // Cache MISS
                _logger.LogDebug(
                    "Embedding cache MISS for hash {Hash}...",
                    contentHash[..Math.Min(8, contentHash.Length)]);
                _metrics?.RecordMiss(sw.Elapsed.TotalMilliseconds, CacheType);
                return null;
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "Error retrieving embedding from cache for hash {Hash}..., will generate new embedding",
                contentHash[..Math.Min(8, contentHash.Length)]);
            _metrics?.RecordMiss(sw.Elapsed.TotalMilliseconds, CacheType); // Treat errors as misses
            return null; // Fail gracefully, will generate new embedding
        }
    }

    /// <inheritdoc />
    public async Task SetEmbeddingAsync(
        string contentHash,
        ReadOnlyMemory<float> embedding,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(contentHash))
            throw new ArgumentException("Content hash cannot be null or empty", nameof(contentHash));

        if (embedding.Length == 0)
            throw new ArgumentException("Embedding cannot be empty", nameof(embedding));

        var cacheKey = $"{CacheKeyPrefix}{contentHash}";

        try
        {
            var serializedData = SerializeEmbedding(embedding);
            await _cache.SetAsync(
                cacheKey,
                serializedData,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = DefaultTtl
                },
                cancellationToken);

            _logger.LogDebug(
                "Cached embedding for hash {Hash}..., vector length={Length}, TTL={TTL} days",
                contentHash[..Math.Min(8, contentHash.Length)],
                embedding.Length,
                DefaultTtl.TotalDays);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error caching embedding for hash {Hash}...",
                contentHash[..Math.Min(8, contentHash.Length)]);
            // Don't throw - caching is optimization, not requirement
        }
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>?> GetEmbeddingForContentAsync(
        string content,
        CancellationToken cancellationToken = default)
    {
        var contentHash = ComputeContentHash(content);
        return await GetEmbeddingAsync(contentHash, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetEmbeddingForContentAsync(
        string content,
        ReadOnlyMemory<float> embedding,
        CancellationToken cancellationToken = default)
    {
        var contentHash = ComputeContentHash(content);
        await SetEmbeddingAsync(contentHash, embedding, cancellationToken);
    }

    #region Serialization

    /// <summary>
    /// Serialize float array to byte array for Redis storage.
    /// More efficient than JSON for large embedding vectors.
    /// </summary>
    private static byte[] SerializeEmbedding(ReadOnlyMemory<float> embedding)
    {
        var floatArray = embedding.ToArray();
        var byteArray = new byte[floatArray.Length * sizeof(float)];
        Buffer.BlockCopy(floatArray, 0, byteArray, 0, byteArray.Length);
        return byteArray;
    }

    /// <summary>
    /// Deserialize byte array back to float array.
    /// </summary>
    private static ReadOnlyMemory<float> DeserializeEmbedding(byte[] data)
    {
        var floatArray = new float[data.Length / sizeof(float)];
        Buffer.BlockCopy(data, 0, floatArray, 0, data.Length);
        return floatArray;
    }

    #endregion
}
