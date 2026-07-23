namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Maps a <see cref="Sprk.Bff.Api.Services.Ai.PublicContracts.MemoryItem.RetentionClass"/> to a
/// per-item Cosmos <c>ttl</c> (seconds) at WRITE time (task AIR2-052, FR-B-03 — MINIMAL governance).
///
/// <para>
/// <b>Cosmos does the expiry — there is NO reaper, NO custom expiry machinery, NO read-filter.</b>
/// The <c>memory-items</c> container's <c>defaultTtl</c> is <c>-1</c> (per-item TTL enabled, no
/// container-wide default), so an item whose <c>ttl</c> is null NEVER expires and an item whose
/// <c>ttl</c> is a positive integer is deleted by Cosmos that many seconds after its last write.
/// Retention is therefore enforced entirely by the value written here (operator ruling 2026-07-09:
/// minimal governance — retention = <c>retentionClass</c> → Cosmos <c>ttl</c> only).
/// </para>
///
/// <para>
/// <b>Documented default</b>: an absent, empty, or UNRECOGNIZED retention class maps to
/// <see langword="null"/> (no expiry). Tier-3 memory is user-owned; the safe default is to retain
/// until the user (or GDPR erasure) deletes it — never to silently drop a fact because its retention
/// class was unknown. Only an EXPLICIT finite class shortens the lifetime.
/// </para>
/// </summary>
public static class MemoryRetentionPolicy
{
    /// <summary>Retention class for user-owned Tier-3 memory (the migration default). No auto-expiry — erasure is user-driven.</summary>
    public const string Tier3UserOwned = "tier-3-user-owned";

    /// <summary>Retention class for short-lived derived memory. Expires 30 days after last write.</summary>
    public const string Ephemeral = "ephemeral";

    private const int ThirtyDaysSeconds = 30 * 24 * 60 * 60; // 2,592,000

    /// <summary>
    /// The explicit retention-class → ttl(seconds) map. A class mapped to <see langword="null"/>
    /// (or absent from the map) means "no expiry". Ordinal, case-sensitive — retention classes are
    /// stable machine tokens on the governance envelope, not user text.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int?> _ttlByClass =
        new Dictionary<string, int?>(StringComparer.Ordinal)
        {
            [Tier3UserOwned] = null,          // user-owned — retain until user/GDPR erasure
            [Ephemeral] = ThirtyDaysSeconds,  // finite example: 30-day Cosmos TTL
        };

    /// <summary>
    /// Resolves the per-item Cosmos <c>ttl</c> (seconds) for a retention class. Returns
    /// <see langword="null"/> (no expiry) for the documented default: absent, empty, or unrecognized
    /// class. Cosmos performs the expiry — nothing else does.
    /// </summary>
    /// <param name="retentionClass">The <c>retentionClass</c> from the MemoryItem governance envelope, or null.</param>
    /// <returns>Positive ttl in seconds for a finite class; <see langword="null"/> for no expiry (the default).</returns>
    public static int? ResolveTtlSeconds(string? retentionClass)
    {
        if (string.IsNullOrWhiteSpace(retentionClass))
        {
            return null; // documented default — no expiry
        }

        return _ttlByClass.TryGetValue(retentionClass, out var ttl)
            ? ttl
            : null; // unrecognized class → documented default (no expiry)
    }
}
