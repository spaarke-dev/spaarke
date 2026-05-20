using System.Collections.Immutable;

namespace Sprk.Bff.Api.Services.Ai.Capabilities;

/// <summary>
/// Singleton in-memory catalog of all active AI capabilities.
///
/// Thread-safety model:
///   - Read path (<see cref="TryGet"/>, <see cref="GetAll"/>): lock-free.
///     Reads the current <c>_state</c> volatile reference once and operates on the immutable snapshot.
///   - Write path (<see cref="Refresh"/>): uses <see cref="Interlocked.Exchange"/> to atomically
///     swap the state reference. Readers in flight against the old state complete safely because
///     <see cref="ImmutableDictionary{TKey,TValue}"/> is never mutated after assignment.
///
/// Startup contract:
///   <see cref="CapabilityManifestInitializer"/> calls <see cref="Refresh"/> before the HTTP
///   pipeline begins serving requests. Callers may therefore assume the manifest is populated
///   on any code path reached after application startup completes.
///
/// ADR-009 exception: CapabilityManifest uses IMemoryCache semantics (singleton in-process cache)
/// rather than Redis. Capabilities are structural metadata loaded once per startup; using Redis
/// would add network latency to the hot read path with no correctness benefit. Runtime refreshes
/// are expected to be rare (admin-triggered only).
/// </summary>
public sealed class CapabilityManifest : ICapabilityManifest
{
    /// <summary>
    /// Immutable snapshot of the catalog state.
    /// Replaced atomically on each <see cref="Refresh"/> call.
    /// Marked volatile so that reads always observe the latest write without a memory barrier overhead.
    /// </summary>
    private volatile ManifestState _state = ManifestState.Empty;

    private readonly ILogger<CapabilityManifest> _logger;

    public CapabilityManifest(ILogger<CapabilityManifest> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public DateTimeOffset LastRefreshedUtc => _state.LastRefreshedUtc;

    /// <inheritdoc/>
    public bool TryGet(string name, out CapabilityManifestEntry? entry)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            entry = null;
            return false;
        }

        // Snapshot the current state reference once — reads are lock-free.
        var state = _state;
        return state.EnabledByName.TryGetValue(name, out entry);
    }

    /// <inheritdoc/>
    public IReadOnlyList<CapabilityManifestEntry> GetAll()
    {
        // Snapshot the current state reference once — reads are lock-free.
        return _state.EnabledList;
    }

    /// <summary>
    /// Atomically replaces the in-memory catalog with the supplied entries.
    ///
    /// All entries where <see cref="CapabilityManifestEntry.IsEnabled"/> is false are
    /// filtered out before building the read-optimised index.
    ///
    /// Thread-safety: uses <see cref="Interlocked.Exchange"/> — safe to call concurrently
    /// (last writer wins; concurrent callers both produce valid snapshots).
    /// </summary>
    /// <param name="entries">
    /// Full list of entries returned by <see cref="ICapabilityManifestLoader.LoadAsync"/>.
    /// May include disabled entries; they are excluded from the read path automatically.
    /// </param>
    public void Refresh(IReadOnlyList<CapabilityManifestEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var enabledEntries = entries
            .Where(e => e.IsEnabled)
            .ToImmutableList();

        var enabledByName = enabledEntries
            .ToImmutableDictionary(
                e => e.CapabilityName,
                e => e,
                StringComparer.OrdinalIgnoreCase);

        var newState = new ManifestState(enabledByName, enabledEntries, DateTimeOffset.UtcNow);

        // Atomic swap — no lock needed on the read path.
        Interlocked.Exchange(ref _state, newState);

        _logger.LogInformation(
            "CapabilityManifest refreshed: {EnabledCount} enabled capabilities out of {TotalCount} loaded",
            enabledEntries.Count, entries.Count);
    }

    // ── Inner state snapshot ──────────────────────────────────────────────────

    /// <summary>
    /// Immutable snapshot of catalog state.
    /// Created on each refresh; never mutated after creation.
    /// </summary>
    private sealed class ManifestState
    {
        public static readonly ManifestState Empty = new(
            ImmutableDictionary<string, CapabilityManifestEntry>.Empty,
            ImmutableList<CapabilityManifestEntry>.Empty,
            DateTimeOffset.MinValue);

        public ImmutableDictionary<string, CapabilityManifestEntry> EnabledByName { get; }
        public ImmutableList<CapabilityManifestEntry> EnabledList { get; }
        public DateTimeOffset LastRefreshedUtc { get; }

        public ManifestState(
            ImmutableDictionary<string, CapabilityManifestEntry> enabledByName,
            ImmutableList<CapabilityManifestEntry> enabledList,
            DateTimeOffset lastRefreshedUtc)
        {
            EnabledByName = enabledByName;
            EnabledList = enabledList;
            LastRefreshedUtc = lastRefreshedUtc;
        }
    }
}
