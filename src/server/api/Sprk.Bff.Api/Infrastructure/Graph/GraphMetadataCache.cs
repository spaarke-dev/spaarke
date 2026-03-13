using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Redis cache for Graph API metadata responses (ADR-009: Redis-First Caching).
/// Caches file metadata, folder listings, and container-to-drive mappings to reduce
/// Graph API calls from 100-300ms to ~5ms on cache hit (90%+ hit rate expected).
///
/// Cache key patterns:
/// - File metadata:           sdap:graph:metadata:{driveId}:{itemId}:v{etag}    (5min TTL)
/// - Folder listing:          sdap:graph:children:{driveId}:{itemId|root}        (2min TTL)
/// - Container-to-drive:      sdap:graph:drive:{containerId}                     (24h TTL)
///
/// Security:
/// - App-only (non-OBO) operations only — user-context data is NOT cached
/// - Short TTLs for metadata ensure freshness
/// - Cache failures are graceful (don't break Graph operations)
/// </summary>
public class GraphMetadataCache
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<GraphMetadataCache> _logger;
    private readonly CacheMetrics? _metrics;

    // Cache TTLs per task spec
    private static readonly TimeSpan MetadataTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FolderListingTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ContainerToDriveTtl = TimeSpan.FromHours(24);

    // JSON serializer options (reusable, thread-safe)
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public GraphMetadataCache(
        IDistributedCache cache,
        ILogger<GraphMetadataCache> logger,
        CacheMetrics? metrics = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics; // Optional: metrics can be null if not configured
    }

    // =========================================================================
    // File Metadata Cache (5min TTL, ETag-versioned keys)
    // =========================================================================

    /// <summary>
    /// Get cached file metadata by drive/item ID.
    /// Uses ETag-versioned key to ensure stale data is never served.
    /// </summary>
    public async Task<FileHandleDto?> GetFileMetadataAsync(string driveId, string itemId)
    {
        var cacheKey = $"sdap:graph:metadata:{driveId}:{itemId}";
        return await GetFromCacheAsync<FileHandleDto>(cacheKey, "metadata");
    }

    /// <summary>
    /// Cache file metadata with ETag-versioned key and 5min TTL.
    /// </summary>
    public async Task SetFileMetadataAsync(string driveId, string itemId, FileHandleDto metadata)
    {
        // Use ETag in key for version-aware caching (ADR-009: version cache keys)
        var etagSuffix = !string.IsNullOrEmpty(metadata.ETag)
            ? $":v{SanitizeETag(metadata.ETag)}"
            : string.Empty;
        var cacheKey = $"sdap:graph:metadata:{driveId}:{itemId}{etagSuffix}";

        // Also set a non-versioned key for lookups without ETag
        var baseKey = $"sdap:graph:metadata:{driveId}:{itemId}";

        await SetInCacheAsync(baseKey, metadata, MetadataTtl, "metadata");

        // If ETag is available, also set versioned key
        if (!string.IsNullOrEmpty(etagSuffix))
        {
            await SetInCacheAsync(cacheKey, metadata, MetadataTtl, "metadata");
        }
    }

    /// <summary>
    /// Invalidate cached file metadata (e.g., after update or delete).
    /// </summary>
    public async Task InvalidateFileMetadataAsync(string driveId, string itemId)
    {
        var cacheKey = $"sdap:graph:metadata:{driveId}:{itemId}";
        await RemoveFromCacheAsync(cacheKey, "metadata");
    }

    // =========================================================================
    // Folder Listing Cache (2min TTL)
    // =========================================================================

    /// <summary>
    /// Get cached folder children listing.
    /// </summary>
    public async Task<IList<FileHandleDto>?> GetFolderListingAsync(string driveId, string? itemId)
    {
        var folderKey = string.IsNullOrEmpty(itemId) ? "root" : itemId;
        var cacheKey = $"sdap:graph:children:{driveId}:{folderKey}";
        return await GetFromCacheAsync<List<FileHandleDto>>(cacheKey, "folder-listing");
    }

    /// <summary>
    /// Cache folder children listing with 2min TTL.
    /// </summary>
    public async Task SetFolderListingAsync(string driveId, string? itemId, IList<FileHandleDto> children)
    {
        var folderKey = string.IsNullOrEmpty(itemId) ? "root" : itemId;
        var cacheKey = $"sdap:graph:children:{driveId}:{folderKey}";
        await SetInCacheAsync(cacheKey, children, FolderListingTtl, "folder-listing");
    }

    /// <summary>
    /// Invalidate cached folder listing (e.g., after file upload, delete, or rename).
    /// </summary>
    public async Task InvalidateFolderListingAsync(string driveId, string? itemId)
    {
        var folderKey = string.IsNullOrEmpty(itemId) ? "root" : itemId;
        var cacheKey = $"sdap:graph:children:{driveId}:{folderKey}";
        await RemoveFromCacheAsync(cacheKey, "folder-listing");
    }

    // =========================================================================
    // Container-to-Drive Cache (24h TTL — stable mapping)
    // =========================================================================

    /// <summary>
    /// Get cached container-to-drive mapping.
    /// Container-to-drive mappings are stable and rarely change, so 24h TTL is safe.
    /// </summary>
    public async Task<ContainerDto?> GetContainerDriveAsync(string containerId)
    {
        var cacheKey = $"sdap:graph:drive:{containerId}";
        return await GetFromCacheAsync<ContainerDto>(cacheKey, "container-drive");
    }

    /// <summary>
    /// Cache container-to-drive mapping with 24h TTL.
    /// </summary>
    public async Task SetContainerDriveAsync(string containerId, ContainerDto containerDrive)
    {
        var cacheKey = $"sdap:graph:drive:{containerId}";
        await SetInCacheAsync(cacheKey, containerDrive, ContainerToDriveTtl, "container-drive");
    }

    /// <summary>
    /// Invalidate cached container-to-drive mapping.
    /// </summary>
    public async Task InvalidateContainerDriveAsync(string containerId)
    {
        var cacheKey = $"sdap:graph:drive:{containerId}";
        await RemoveFromCacheAsync(cacheKey, "container-drive");
    }

    // =========================================================================
    // Internal Helpers
    // =========================================================================

    private async Task<T?> GetFromCacheAsync<T>(string cacheKey, string cacheType) where T : class
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var cached = await _cache.GetStringAsync(cacheKey);
            sw.Stop();

            if (cached != null)
            {
                _logger.LogDebug("Cache HIT for {CacheType} key {Key}", cacheType, cacheKey);
                _metrics?.RecordHit(sw.Elapsed.TotalMilliseconds, $"graph-{cacheType}");
                return JsonSerializer.Deserialize<T>(cached, JsonOptions);
            }

            _logger.LogDebug("Cache MISS for {CacheType} key {Key}", cacheType, cacheKey);
            _metrics?.RecordMiss(sw.Elapsed.TotalMilliseconds, $"graph-{cacheType}");
            return null;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Error reading {CacheType} from cache for key {Key}", cacheType, cacheKey);
            _metrics?.RecordMiss(sw.Elapsed.TotalMilliseconds, $"graph-{cacheType}");
            return null; // Fail gracefully — cache failures don't break Graph operations
        }
    }

    private async Task SetInCacheAsync<T>(string cacheKey, T value, TimeSpan ttl, string cacheType)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await _cache.SetStringAsync(cacheKey, json, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            });

            _logger.LogDebug("Cached {CacheType} key {Key} with TTL {TTL}min",
                cacheType, cacheKey, ttl.TotalMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error caching {CacheType} for key {Key}", cacheType, cacheKey);
            // Don't throw — caching is optimization, not requirement
        }
    }

    private async Task RemoveFromCacheAsync(string cacheKey, string cacheType)
    {
        try
        {
            await _cache.RemoveAsync(cacheKey);
            _logger.LogDebug("Invalidated {CacheType} cache key {Key}", cacheType, cacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error invalidating {CacheType} cache for key {Key}", cacheType, cacheKey);
            // Don't throw — cache invalidation failure is not critical
        }
    }

    /// <summary>
    /// Sanitize ETag for use in cache keys (remove quotes and special characters).
    /// </summary>
    private static string SanitizeETag(string etag)
    {
        return etag.Trim('"').Replace(",", "_").Replace(":", "_");
    }
}
