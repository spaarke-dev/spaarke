namespace Sprk.Bff.Api.Services.Ai.Capabilities;

/// <summary>
/// Provides an out-of-schedule trigger for the <see cref="ManifestRefreshService"/>
/// background poller.
///
/// Implemented by <see cref="ManifestRefreshService"/> and registered as a singleton
/// so that the webhook endpoint can wake the background loop immediately after a
/// Dataverse capability change without waiting for the next 15-minute tick.
///
/// Trigger semantics:
///   - Posting to the channel is fire-and-forget; the caller does not wait for the
///     refresh to complete before returning.
///   - If a refresh is already in progress the signal is queued and processed after
///     the current refresh finishes (bounded channel capacity = 1, so duplicate
///     signals are dropped — only one pending wake-up is retained at a time).
/// </summary>
public interface IManifestRefreshTrigger
{
    /// <summary>
    /// Signals the background poller to perform an immediate out-of-schedule refresh.
    /// Returns without waiting for the refresh to complete.
    /// </summary>
    void TriggerRefresh();
}
