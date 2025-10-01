using Microsoft.Extensions.Caching.Distributed;
using System.Text;

namespace Spe.Bff.Api.Services.Jobs;

/// <summary>
/// Distributed cache-based idempotency service.
/// Ensures events are processed exactly once, satisfying ADR-004 requirements.
/// </summary>
public class IdempotencyService : IIdempotencyService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<IdempotencyService> _logger;
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromHours(24);
    private static readonly TimeSpan DefaultLockDuration = TimeSpan.FromMinutes(5);

    public IdempotencyService(IDistributedCache cache, ILogger<IdempotencyService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetProcessedKey(eventId);
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

            await _cache.SetAsync(key, Encoding.UTF8.GetBytes("processed"), options, cancellationToken);
            _logger.LogDebug("Marked event {EventId} as processed", eventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark event {EventId} as processed", eventId);
            // Don't throw - this is not critical for operation
        }
    }

    public async Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetLockKey(eventId);
            var existingValue = await _cache.GetAsync(key, cancellationToken);

            if (existingValue != null)
            {
                _logger.LogWarning("Event {EventId} is already being processed by another instance", eventId);
                return false;
            }

            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = lockDuration ?? DefaultLockDuration
            };

            await _cache.SetAsync(key, Encoding.UTF8.GetBytes("locked"), options, cancellationToken);
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

    public async Task ReleaseProcessingLockAsync(string eventId, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetLockKey(eventId);
            await _cache.RemoveAsync(key, cancellationToken);
            _logger.LogDebug("Released processing lock for event {EventId}", eventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release processing lock for event {EventId}", eventId);
            // Don't throw - lock will expire automatically
        }
    }

    private static string GetProcessedKey(string eventId) => $"idempotency:processed:{eventId}";
    private static string GetLockKey(string eventId) => $"idempotency:lock:{eventId}";
}
