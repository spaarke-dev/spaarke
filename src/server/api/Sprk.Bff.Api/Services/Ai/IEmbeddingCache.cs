namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Caches embeddings by content hash to reduce Azure OpenAI API costs and latency.
/// Follows ADR-009 Redis-First Caching patterns.
/// </summary>
/// <remarks>
/// Cache strategy:
/// - Key: SHA256 hash of content text (ensures consistent key length, safe for any content)
/// - Value: Float array serialized as byte array then Base64
/// - TTL: 7 days (embeddings are deterministic for same model, content doesn't change meaning)
///
/// Performance targets:
/// - Cache hit: ~5-10ms (Redis lookup)
/// - Cache miss: ~150-200ms (Azure OpenAI API call)
/// - Expected hit rate: >80% for document-heavy workloads (many queries on same documents)
/// </remarks>
public interface IEmbeddingCache
{
    /// <summary>
    /// Get cached embedding for content.
    /// </summary>
    /// <param name="contentHash">SHA256 hash of content text (from ComputeContentHash)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cached embedding or null if cache miss</returns>
    Task<ReadOnlyMemory<float>?> GetEmbeddingAsync(string contentHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cache embedding for content.
    /// </summary>
    /// <param name="contentHash">SHA256 hash of content text (from ComputeContentHash)</param>
    /// <param name="embedding">The embedding vector to cache</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SetEmbeddingAsync(string contentHash, ReadOnlyMemory<float> embedding, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compute SHA256 hash of content for cache key.
    /// Ensures consistent key length and handles any content safely.
    /// </summary>
    /// <param name="content">The text content to hash</param>
    /// <returns>Base64-encoded SHA256 hash</returns>
    string ComputeContentHash(string content);

    /// <summary>
    /// Get cached embedding for content, computing hash internally.
    /// Convenience method that combines hashing and lookup.
    /// </summary>
    /// <param name="content">The text content</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cached embedding or null if cache miss</returns>
    Task<ReadOnlyMemory<float>?> GetEmbeddingForContentAsync(string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cache embedding for content, computing hash internally.
    /// Convenience method that combines hashing and storage.
    /// </summary>
    /// <param name="content">The text content</param>
    /// <param name="embedding">The embedding vector to cache</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SetEmbeddingForContentAsync(string content, ReadOnlyMemory<float> embedding, CancellationToken cancellationToken = default);
}
