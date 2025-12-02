namespace Spe.Bff.Api.Services.Jobs;

/// <summary>
/// Service for tracking processed events to ensure idempotency.
/// Implements ADR-004 requirement for event deduplication.
/// </summary>
public interface IIdempotencyService
{
    /// <summary>
    /// Checks if an event has already been processed.
    /// </summary>
    Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an event as processed.
    /// </summary>
    Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to acquire processing lock for an event.
    /// Returns true if lock acquired successfully, false if event is already being processed.
    /// </summary>
    Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases processing lock for an event.
    /// </summary>
    Task ReleaseProcessingLockAsync(string eventId, CancellationToken cancellationToken = default);
}
