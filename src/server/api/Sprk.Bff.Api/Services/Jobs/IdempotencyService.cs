using System.Text;
using Microsoft.Extensions.Caching.Distributed;

namespace Sprk.Bff.Api.Services.Jobs;

/// <summary>
/// Distributed cache-based idempotency service.
/// Ensures events are processed exactly once, satisfying ADR-004 requirements.
/// </summary>
public class IdempotencyService : IIdempotencyService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<IdempotencyService> _logger;
    private readonly TimeProvider _time;
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromHours(24);
    private static readonly TimeSpan DefaultLockDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How old a lock under the SAME owner must be before that owner may take it over (task 068, #1086). A genuine
    /// Service Bus redelivery arrives only after the earlier delivery's message lock expired (<c>sdap-jobs</c>
    /// <c>lockDuration</c> PT5M, <c>service-bus.bicep</c>), so its stale lock is at least that old. A duplicate send of
    /// the same job (duplicate detection is off on the BFF queues) arrives within seconds, while the first copy still
    /// runs, and must still be refused. One minute separates the two with margin either way.
    /// </summary>
    internal static readonly TimeSpan OwnerTakeoverAge = TimeSpan.FromMinutes(1);

    /// <param name="cache">The shared cache (Redis in every non-test host).</param>
    /// <param name="logger">Logger.</param>
    /// <param name="time">Clock for the owner takeover age. Optional so existing constructions keep compiling; DI
    /// supplies the registered <see cref="TimeProvider"/>.</param>
    public IdempotencyService(IDistributedCache cache, ILogger<IdempotencyService> logger, TimeProvider? time = null)
    {
        _cache = cache;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetProcessedKey(eventId);
            // SYSTEM-LEVEL EXCEPTION (NFR-08): idempotency event IDs are cross-tenant system identifiers (Service Bus message IDs); tenant-scoping would break the exactly-once invariant.
            var value = await _cache.GetAsync(key, cancellationToken);
            var processed = value != null;

            if (processed)
            {
                _logger.LogInformation("Event {EventId} has already been processed (idempotency check)", eventId);
            }

            return processed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if event {EventId} was processed", eventId);
            // On cache failure, allow processing to proceed (fail open)
            return false;
        }
    }

    public async Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetProcessedKey(eventId);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration ?? DefaultExpiration
            };

            // SYSTEM-LEVEL EXCEPTION (NFR-08): idempotency event IDs are cross-tenant system identifiers (Service Bus message IDs); tenant-scoping would break the exactly-once invariant.
            await _cache.SetAsync(key, Encoding.UTF8.GetBytes("processed"), options, cancellationToken);
            _logger.LogDebug("Marked event {EventId} as processed", eventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark event {EventId} as processed", eventId);
            // Don't throw - this is not critical for operation
        }
    }

    public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default) =>
        TryAcquireAsync(eventId, ownerId: null, lockDuration, cancellationToken);

    /// <inheritdoc />
    /// <remarks>The lock's value is the owner and when it took the lock. A lock the same owner left behind (a crash) is
    /// taken over once it is at least <see cref="OwnerTakeoverAge"/> old, instead of being reported as another
    /// instance's; a younger one is a duplicate delivery still running, and is refused (task 068, #1086).</remarks>
    public Task<bool> TryAcquireProcessingLockAsync(
        string eventId,
        string ownerId,
        TimeSpan? lockDuration = null,
        CancellationToken cancellationToken = default) =>
        TryAcquireAsync(eventId, ownerId, lockDuration, cancellationToken);

    private async Task<bool> TryAcquireAsync(string eventId, string? ownerId, TimeSpan? lockDuration, CancellationToken cancellationToken)
    {
        try
        {
            var key = GetLockKey(eventId);
            // SYSTEM-LEVEL EXCEPTION (NFR-08): job-processing lock key is system-level (Service Bus message ID); cross-instance lock semantics must be tenant-agnostic.
            var existingValue = await _cache.GetAsync(key, cancellationToken);

            var now = _time.GetUtcNow();
            if (existingValue != null)
            {
                if (ownerId is null || !IsStaleLockOf(Encoding.UTF8.GetString(existingValue), ownerId, now))
                {
                    _logger.LogWarning("Event {EventId} is already being processed by another instance", eventId);
                    return false;
                }

                _logger.LogWarning(
                    "Event {EventId}: taking over a processing lock left by an earlier attempt of the same job {OwnerId}",
                    eventId, ownerId);
            }

            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = lockDuration ?? DefaultLockDuration
            };

            var value = ownerId is null ? "locked" : $"{ownerId}|{now.ToUnixTimeMilliseconds()}";
            // SYSTEM-LEVEL EXCEPTION (NFR-08): job-processing lock key is system-level (Service Bus message ID); cross-instance lock semantics must be tenant-agnostic.
            await _cache.SetAsync(key, Encoding.UTF8.GetBytes(value), options, cancellationToken);
            _logger.LogDebug("Acquired processing lock for event {EventId}", eventId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire processing lock for event {EventId}", eventId);
            // On cache failure, allow processing to proceed (fail open)
            return true;
        }
    }

    /// <remarks>
    /// The release never honours <paramref name="cancellationToken"/>. Handlers release in a <c>finally</c> with the job's
    /// token, which a graceful stop has already cancelled, and <c>RedisCache</c> refuses a cancelled token before it
    /// reaches Redis, so the lock outlived the stop and the job's redelivery was dropped (task 068, #1086). A release must
    /// always be attempted.
    /// </remarks>
    public async Task ReleaseProcessingLockAsync(string eventId, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetLockKey(eventId);
            // SYSTEM-LEVEL EXCEPTION (NFR-08): job-processing lock key is system-level (Service Bus message ID); release path mirrors acquire path.
            await _cache.RemoveAsync(key, CancellationToken.None);
            _logger.LogDebug("Released processing lock for event {EventId}", eventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release processing lock for event {EventId}", eventId);
            // Don't throw - lock will expire automatically
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reads the lock and rewrites it (with a fresh timestamp and <paramref name="lockDuration"/>) only when it is this
    /// owner's. Unlike the acquire, which fails OPEN on a cache fault (#984), a renewal fails CLOSED: a fault answers
    /// <see langword="false"/> (task 166, owner round 54 item 2). Read-then-write like the acquire; the caller confirms the
    /// lock before each destructive step, so two takers that both passed the acquire's check-then-set are told apart there
    /// (the last writer's value is the one read back).
    /// </remarks>
    public async Task<bool> RenewProcessingLockAsync(
        string eventId,
        string ownerId,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetLockKey(eventId);
            // SYSTEM-LEVEL EXCEPTION (NFR-08): job-processing lock key is system-level; renewal mirrors the acquire path.
            var existingValue = await _cache.GetAsync(key, cancellationToken);
            if (existingValue is null || !IsLockOf(Encoding.UTF8.GetString(existingValue), ownerId))
            {
                _logger.LogWarning("Processing lock for event {EventId} is no longer held by {OwnerId}", eventId, ownerId);
                return false;
            }

            var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lockDuration };
            var value = $"{ownerId}|{_time.GetUtcNow().ToUnixTimeMilliseconds()}";
            // SYSTEM-LEVEL EXCEPTION (NFR-08): job-processing lock key is system-level; renewal mirrors the acquire path.
            await _cache.SetAsync(key, Encoding.UTF8.GetBytes(value), options, cancellationToken);
            _logger.LogDebug("Renewed processing lock for event {EventId}", eventId);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to renew processing lock for event {EventId}; it is treated as lost", eventId);
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>Never honours <paramref name="cancellationToken"/>, like the ownerless release (task 068, #1086).</remarks>
    public async Task ReleaseProcessingLockAsync(string eventId, string ownerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetLockKey(eventId);
            // SYSTEM-LEVEL EXCEPTION (NFR-08): job-processing lock key is system-level; release path mirrors acquire path.
            var existingValue = await _cache.GetAsync(key, CancellationToken.None);
            if (existingValue is null || !IsLockOf(Encoding.UTF8.GetString(existingValue), ownerId))
            {
                _logger.LogWarning(
                    "Processing lock for event {EventId} is not held by {OwnerId}; it is left to its holder", eventId, ownerId);
                return;
            }

            // SYSTEM-LEVEL EXCEPTION (NFR-08): job-processing lock key is system-level; release path mirrors acquire path.
            await _cache.RemoveAsync(key, CancellationToken.None);
            _logger.LogDebug("Released processing lock for event {EventId} held by {OwnerId}", eventId, ownerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release processing lock for event {EventId}", eventId);
            // Don't throw - lock will expire automatically
        }
    }

    /// <summary>True when <paramref name="lockValue"/> (<c>{owner}|{unix ms}</c>) was written by <paramref name="ownerId"/>.</summary>
    private static bool IsLockOf(string lockValue, string ownerId)
    {
        var separator = lockValue.LastIndexOf('|');
        return separator > 0 && string.Equals(lockValue[..separator], ownerId, StringComparison.Ordinal);
    }

    /// <summary>
    /// True when <paramref name="lockValue"/> was written by <paramref name="ownerId"/> at least
    /// <see cref="OwnerTakeoverAge"/> ago. An ownerless value (<c>"locked"</c>), another owner's, or an unreadable one is
    /// never stale to this owner.
    /// </summary>
    private static bool IsStaleLockOf(string lockValue, string ownerId, DateTimeOffset now)
    {
        var separator = lockValue.LastIndexOf('|');
        if (separator <= 0 || !string.Equals(lockValue[..separator], ownerId, StringComparison.Ordinal)
            || !long.TryParse(lockValue[(separator + 1)..], out var acquiredMs))
        {
            return false;
        }

        return now - DateTimeOffset.FromUnixTimeMilliseconds(acquiredMs) >= OwnerTakeoverAge;
    }

    private static string GetProcessedKey(string eventId) => $"idempotency:processed:{eventId}";
    private static string GetLockKey(string eventId) => $"idempotency:lock:{eventId}";
}
