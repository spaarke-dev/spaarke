namespace Sprk.Bff.Api.Services.Jobs;

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
    /// Attempts to acquire the processing lock for an event on behalf of <paramref name="ownerId"/>. The lock is held
    /// by its owner, so the same owner may take back a lock it left behind once that lock is old enough to be a dead
    /// attempt's: a Service Bus redelivery carries the same job and arrives only after the earlier delivery's message
    /// lock expired. Returns false while a DIFFERENT owner holds the lock, or the same owner took it recently (a
    /// duplicate delivery still running).
    /// </summary>
    /// <remarks>
    /// Task 068 (#1086). A default method, so an implementer without owners keeps the ownerless behaviour.
    /// </remarks>
    Task<bool> TryAcquireProcessingLockAsync(
        string eventId,
        string ownerId,
        TimeSpan? lockDuration = null,
        CancellationToken cancellationToken = default) =>
        TryAcquireProcessingLockAsync(eventId, lockDuration, cancellationToken);

    /// <summary>
    /// Releases processing lock for an event.
    /// </summary>
    Task ReleaseProcessingLockAsync(string eventId, CancellationToken cancellationToken = default);
}
