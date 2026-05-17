namespace Sprk.Bff.Api.Services.Ai.Capabilities;

/// <summary>
/// Loads all capability entries from a backing store (Dataverse) for ingestion into
/// <see cref="CapabilityManifest"/>.
///
/// Implementations are responsible for connectivity, authentication, and mapping
/// Dataverse columns to <see cref="CapabilityManifestEntry"/> records.
///
/// Consumed by <see cref="CapabilityManifestInitializer"/> at startup.
/// </summary>
public interface ICapabilityManifestLoader
{
    /// <summary>
    /// Loads all capability records from the backing store.
    ///
    /// Returns both enabled and disabled entries. The manifest applies the IsEnabled
    /// filter after loading so that the full dataset can be logged for diagnostics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// All capability entries found in the backing store.
    /// Returns an empty list (not null) when the table exists but contains no rows.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the backing store is unreachable or the query fails.
    /// Callers should catch this and decide whether to surface or swallow the error.
    /// </exception>
    Task<IReadOnlyList<CapabilityManifestEntry>> LoadAsync(CancellationToken cancellationToken = default);
}
