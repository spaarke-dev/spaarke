using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;

namespace Spe.Bff.Api.Services;

/// <summary>
/// Caches Graph API OBO tokens to reduce Azure AD load (ADR-009: Redis-First Caching).
/// Target: 95% cache hit rate, 97% reduction in auth latency.
///
/// Security:
/// - User tokens are hashed with SHA256 (never stored plaintext)
/// - Only hash prefixes logged (first 8 chars)
/// - Graph tokens cached with 55-minute TTL (5-minute buffer before expiration)
/// - Graceful error handling (cache failures don't break OBO flow)
/// </summary>
public class GraphTokenCache
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<GraphTokenCache> _logger;

    public GraphTokenCache(
        IDistributedCache cache,
        ILogger<GraphTokenCache> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Compute SHA256 hash of user token for cache key.
    /// Ensures consistent key length and prevents token exposure in logs.
    /// </summary>
    /// <param name="userToken">The user's access token (will be hashed, never stored)</param>
    /// <returns>Base64-encoded SHA256 hash of the user token</returns>
    public string ComputeTokenHash(string userToken)
    {
        if (string.IsNullOrEmpty(userToken))
            throw new ArgumentException("User token cannot be null or empty", nameof(userToken));

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(userToken));
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Get cached Graph token by user token hash.
    /// </summary>
    /// <param name="tokenHash">SHA256 hash of user token (from ComputeTokenHash)</param>
    /// <returns>Cached Graph token or null if cache miss or error</returns>
    public async Task<string?> GetTokenAsync(string tokenHash)
    {
        if (string.IsNullOrEmpty(tokenHash))
            throw new ArgumentException("Token hash cannot be null or empty", nameof(tokenHash));

        var cacheKey = $"sdap:graph:token:{tokenHash}";

        try
        {
            var cachedToken = await _cache.GetStringAsync(cacheKey);

            if (cachedToken != null)
            {
                _logger.LogDebug("Cache HIT for token hash {Hash}...", tokenHash[..Math.Min(8, tokenHash.Length)]);
            }
            else
            {
                _logger.LogDebug("Cache MISS for token hash {Hash}...", tokenHash[..Math.Min(8, tokenHash.Length)]);
            }

            return cachedToken;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error retrieving token from cache for hash {Hash}..., will perform OBO exchange",
                tokenHash[..Math.Min(8, tokenHash.Length)]);
            return null; // Fail gracefully, will perform OBO exchange
        }
    }

    /// <summary>
    /// Cache Graph token with TTL.
    /// </summary>
    /// <param name="tokenHash">SHA256 hash of user token (from ComputeTokenHash)</param>
    /// <param name="graphToken">Graph API access token (result of OBO exchange)</param>
    /// <param name="expiry">Time-to-live for cached token (should be 55 minutes)</param>
    public async Task SetTokenAsync(string tokenHash, string graphToken, TimeSpan expiry)
    {
        if (string.IsNullOrEmpty(tokenHash))
            throw new ArgumentException("Token hash cannot be null or empty", nameof(tokenHash));

        if (string.IsNullOrEmpty(graphToken))
            throw new ArgumentException("Graph token cannot be null or empty", nameof(graphToken));

        var cacheKey = $"sdap:graph:token:{tokenHash}";

        try
        {
            await _cache.SetStringAsync(
                cacheKey,
                graphToken,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = expiry
                });

            _logger.LogDebug(
                "Cached token for hash {Hash}... with TTL {TTL} minutes",
                tokenHash[..Math.Min(8, tokenHash.Length)],
                expiry.TotalMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error caching token for hash {Hash}...",
                tokenHash[..Math.Min(8, tokenHash.Length)]);
            // Don't throw - caching is optimization, not requirement
        }
    }

    /// <summary>
    /// Remove token from cache (e.g., on logout or token invalidation).
    /// </summary>
    /// <param name="tokenHash">SHA256 hash of user token (from ComputeTokenHash)</param>
    public async Task RemoveTokenAsync(string tokenHash)
    {
        if (string.IsNullOrEmpty(tokenHash))
            throw new ArgumentException("Token hash cannot be null or empty", nameof(tokenHash));

        var cacheKey = $"sdap:graph:token:{tokenHash}";

        try
        {
            await _cache.RemoveAsync(cacheKey);
            _logger.LogDebug("Removed cached token for hash {Hash}...", tokenHash[..Math.Min(8, tokenHash.Length)]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error removing token from cache for hash {Hash}...",
                tokenHash[..Math.Min(8, tokenHash.Length)]);
            // Don't throw - cache removal failure is not critical
        }
    }
}
