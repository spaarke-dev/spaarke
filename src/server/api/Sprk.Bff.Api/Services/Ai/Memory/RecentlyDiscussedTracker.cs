using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Infrastructure.Cache;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Redis-backed implementation of <see cref="IRecentlyDiscussedTracker"/> via
/// <see cref="IDistributedCache"/> (Redis in prod; in-memory in dev/tests).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Storage</strong>: key <c>session:{sessionId}:recent-files</c>. Value is a JSON
/// array of <c>{ fileId, lastDiscussedAt }</c> entries, newest first, capped at 20.
/// </para>
/// <para>
/// <strong>TTL</strong>: 24-hour sliding TTL, mirroring
/// <c>SessionPersistenceService.RedisTtl</c> (ADR-009 / NFR-07). A discussion cue that is
/// 24h+ stale is past its useful surfacing horizon.
/// </para>
/// <para>
/// <strong>Concurrency</strong>: read-modify-write within a single call is best-effort;
/// concurrent writers may overwrite each other. This is acceptable for a UX-cue layer
/// (no correctness invariant at stake). If a future requirement needs strict ordering,
/// migrate to Redis sorted-set operations via <c>StackExchange.Redis</c> directly.
/// </para>
/// <para>
/// <strong>ADR-010</strong>: registered as concrete singleton; no interface gateway lift.
/// The interface exists because the consuming handler injects optionally so tests can mock.
/// </para>
/// <para>
/// <strong>ADR-015</strong>: structured logs carry sessionId + fileId + operation name +
/// durationMs ONLY. NEVER file content, summary text, query strings, or recall body. The
/// fileId is treated as a deterministic identifier per the ADR-015 tier-1 vocabulary.
/// </para>
/// </remarks>
public sealed class RecentlyDiscussedTracker : IRecentlyDiscussedTracker
{
    /// <summary>Sliding TTL for the per-session recent-files list (mirrors SessionPersistenceService.RedisTtl).</summary>
    internal static readonly TimeSpan SlidingTtl = TimeSpan.FromHours(24);

    /// <summary>Maximum number of entries persisted per session before oldest is dropped.</summary>
    internal const int MaxEntries = 20;

    /// <summary>Tenant-cache resource name for recently-discussed cue payloads (FR-05).</summary>
    internal const string CacheResource = "recently-discussed";

    /// <summary>Tenant-cache schema version for recently-discussed cue payloads.</summary>
    internal const int CacheVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ITenantCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RecentlyDiscussedTracker> _logger;

    public RecentlyDiscussedTracker(
        ITenantCache cache,
        ILogger<RecentlyDiscussedTracker> logger,
        TimeProvider? timeProvider = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task MarkAsync(string tenantId, string sessionId, string fileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);

        var stopwatch = Stopwatch.StartNew();
        var cacheId = BuildCacheId(sessionId);
        var nowUtc = _timeProvider.GetUtcNow();

        try
        {
            var current = await LoadEntriesAsync(tenantId, cacheId, cancellationToken).ConfigureAwait(false);

            // Remove existing entry for this fileId so the new prepend is idempotent.
            var rebuilt = new List<RecentFileEntry>(current.Count + 1)
            {
                new(fileId, nowUtc)
            };
            foreach (var entry in current)
            {
                if (!string.Equals(entry.FileId, fileId, StringComparison.Ordinal))
                {
                    rebuilt.Add(entry);
                }
            }

            // Trim to MaxEntries (newest first, so drop the tail).
            if (rebuilt.Count > MaxEntries)
            {
                rebuilt.RemoveRange(MaxEntries, rebuilt.Count - MaxEntries);
            }

            await SaveEntriesAsync(tenantId, cacheId, rebuilt, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            // ADR-015: sessionId + fileId + operation + duration. No content.
            _logger.LogInformation(
                "RecentlyDiscussedTracker.MarkAsync sessionId={SessionId} fileId={FileId} entryCount={EntryCount} durationMs={DurationMs}",
                sessionId, fileId, rebuilt.Count, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            // ADR-015: log exception TYPE only.
            _logger.LogWarning(ex,
                "RecentlyDiscussedTracker.MarkAsync failed sessionId={SessionId} fileId={FileId} errorType={ErrorType} durationMs={DurationMs}",
                sessionId, fileId, ex.GetType().Name, stopwatch.ElapsedMilliseconds);
            // Swallow — this is a UX-cue layer; a Redis transient must NEVER fail the recall pipeline.
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetRecentAsync(
        string tenantId,
        string sessionId,
        int maxCount = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (maxCount <= 0)
        {
            return Array.Empty<string>();
        }

        var stopwatch = Stopwatch.StartNew();
        var cacheId = BuildCacheId(sessionId);

        try
        {
            var entries = await LoadEntriesAsync(tenantId, cacheId, cancellationToken).ConfigureAwait(false);
            if (entries.Count == 0)
            {
                stopwatch.Stop();
                _logger.LogDebug(
                    "RecentlyDiscussedTracker.GetRecentAsync sessionId={SessionId} resultCount=0 durationMs={DurationMs}",
                    sessionId, stopwatch.ElapsedMilliseconds);
                return Array.Empty<string>();
            }

            var take = Math.Min(entries.Count, maxCount);
            var result = new string[take];
            for (var i = 0; i < take; i++)
            {
                result[i] = entries[i].FileId;
            }

            stopwatch.Stop();
            _logger.LogDebug(
                "RecentlyDiscussedTracker.GetRecentAsync sessionId={SessionId} resultCount={ResultCount} durationMs={DurationMs}",
                sessionId, take, stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex,
                "RecentlyDiscussedTracker.GetRecentAsync failed sessionId={SessionId} errorType={ErrorType} durationMs={DurationMs}",
                sessionId, ex.GetType().Name, stopwatch.ElapsedMilliseconds);
            return Array.Empty<string>();
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Storage helpers
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deterministic per-session tenant-cache id. SessionId is presumed lowercase "N"-format
    /// GUID from the only caller (ChatSessionManager); we additionally lowercase to harden
    /// against future callers that supply mixed case. Architecture §11.1 storage key
    /// convention; tenant prefix added by the wrapper per FR-05.
    /// </summary>
    internal static string BuildCacheId(string sessionId) => sessionId.ToLowerInvariant();

    private async Task<List<RecentFileEntry>> LoadEntriesAsync(string tenantId, string cacheId, CancellationToken ct)
    {
        try
        {
            var entries = await _cache.GetAsync<List<RecentFileEntry>>(
                tenantId, CacheResource, cacheId, CacheVersion, ct: ct).ConfigureAwait(false);
            return entries ?? new List<RecentFileEntry>(MaxEntries);
        }
        catch (JsonException)
        {
            // Corrupted entry — treat as empty rather than failing the recall. The next
            // MarkAsync will overwrite with a clean list.
            return new List<RecentFileEntry>(MaxEntries);
        }
    }

    private Task SaveEntriesAsync(string tenantId, string cacheId, List<RecentFileEntry> entries, CancellationToken ct)
    {
        return _cache.SetSlidingAsync(
            tenantId,
            CacheResource,
            cacheId,
            CacheVersion,
            entries,
            SlidingTtl,
            ct: ct);
    }

    /// <summary>Per-file entry persisted in the per-session recent-files list.</summary>
    internal sealed record RecentFileEntry(
        [property: JsonPropertyName("fileId")] string FileId,
        [property: JsonPropertyName("lastDiscussedAt")] DateTimeOffset LastDiscussedAt);
}
