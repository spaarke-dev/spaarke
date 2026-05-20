namespace Sprk.Bff.Api.Services.Ai.Capabilities;

/// <summary>
/// Read interface for the in-memory capability catalog.
///
/// Registered as singleton. All read operations complete in sub-millisecond time because
/// the backing store is an <see cref="System.Collections.Immutable.ImmutableDictionary{TKey,TValue}"/>
/// held in memory with no locks on the hot path.
///
/// The catalog is populated at BFF startup by <see cref="CapabilityManifestInitializer"/>
/// (an <see cref="Microsoft.Extensions.Hosting.IHostedService"/>) and can be atomically
/// refreshed at runtime via <see cref="CapabilityManifest.Refresh"/>.
/// </summary>
public interface ICapabilityManifest
{
    /// <summary>
    /// UTC timestamp of the last successful catalog refresh.
    /// <c>DateTimeOffset.MinValue</c> when the manifest has never been loaded.
    /// </summary>
    DateTimeOffset LastRefreshedUtc { get; }

    /// <summary>
    /// Attempts to retrieve a capability by its logical name (case-insensitive).
    /// </summary>
    /// <param name="name">Logical capability name (e.g. "web_search").</param>
    /// <param name="entry">The matching entry, or null when not found.</param>
    /// <returns><c>true</c> if a matching enabled capability was found; otherwise <c>false</c>.</returns>
    bool TryGet(string name, out CapabilityManifestEntry? entry);

    /// <summary>
    /// Returns all currently-enabled capabilities in the catalog.
    ///
    /// Disabled entries (<see cref="CapabilityManifestEntry.IsEnabled"/> = false) are excluded.
    /// The list is a snapshot; callers may cache it for the duration of a single request.
    /// </summary>
    IReadOnlyList<CapabilityManifestEntry> GetAll();
}
