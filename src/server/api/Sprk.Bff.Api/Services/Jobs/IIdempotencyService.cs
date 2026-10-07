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

    /// <summary>
    /// Extends the processing lock <paramref name="ownerId"/> holds on <paramref name="eventId"/> to
    /// <paramref name="lockDuration"/> from now (a heartbeat for work that can outlive one lock duration). Returns
    /// <see langword="true"/> only when the lock was read back as THIS owner's and extended; <see langword="false"/> when
    /// it is gone, another owner's, ownerless, or could not be read or written — the caller must then treat the lock as
    /// LOST and stop before its next destructive step (fail closed).
    /// </summary>
    /// <remarks>
    /// unified-access-control-r2 task 166 (owner round 54 item 2): a document relocation replays a file's whole version
    /// history, so it can outlive a fixed lock. A default method that answers <see langword="false"/>: an implementer
    /// that cannot renew can never confirm a lock, so a caller that needs one stops rather than running unlocked.
    /// </remarks>
    Task<bool> RenewProcessingLockAsync(
        string eventId,
        string ownerId,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    /// <summary>
    /// Releases the processing lock on <paramref name="eventId"/> only while <paramref name="ownerId"/> still holds it. A
    /// holder that LOST its lock (it expired and another owner took it) must not remove the new owner's lock.
    /// </summary>
    /// <remarks>Task 166 (owner round 54 item 2). A default method: an implementer without owners keeps the ownerless release.</remarks>
    Task ReleaseProcessingLockAsync(string eventId, string ownerId, CancellationToken cancellationToken = default) =>
        ReleaseProcessingLockAsync(eventId, cancellationToken);
}
