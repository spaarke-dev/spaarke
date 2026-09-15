using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Communication;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// The set of root-record ids a systemuser can read, plus whether the read hit its row cap.
/// </summary>
/// <remarks>
/// <para><b><see cref="Truncated"/> is not decoration.</b> A truncated set is a set that is missing
/// records the user CAN see. Consumed downstream by task 036 for NFR-03: a caller that treats a
/// truncated set as complete silently under-grants, and an under-grant is invisible to the person it
/// affects — they simply never see the record and have no way to know it exists. Never drop this
/// flag on the floor.</para>
/// </remarks>
public readonly record struct RootIdSet(IReadOnlySet<Guid> Ids, bool Truncated)
{
    /// <summary>An empty, complete set. Never use this to represent a FAILED read — see the class remarks.</summary>
    public static readonly RootIdSet Empty = new(new HashSet<Guid>(), false);
}

/// <summary>
/// Answers "which root records can systemuser U actually read?" by asking Dataverse itself, with one
/// impersonated id-only query per root entity type.
/// </summary>
/// <remarks>
/// <para><b>Why this exists (spec FR-20, design §4.4).</b> Every other way of computing this set is
/// pattern-matching — reproducing Dataverse's own rules in C# and getting them wrong in BOTH
/// directions: a business-unit column match OVER-grants to users whose role depth does not actually
/// reach the record, and it silently MISSES records reachable only through a POA share. Impersonating
/// the user and letting Dataverse answer applies ownership, role depth, business unit, teams, sharing
/// and the user hierarchy natively, and removes the need for a systemuser allow-list entirely.</para>
///
/// <para><b>ADR-010 seam decision (CLAUDE.md §11) — this class adds NO second query interface.</b>
/// <see cref="IImpersonatedCommunicationQuery"/> already wraps
/// <c>DataverseWebApiService.RetrieveMultipleImpersonatedAsync</c>, and its contract
/// <c>(entitySetName, odataQuery, callerSystemUserId)</c> is entirely generic — it is
/// communication-specific in NAME only, and it is registered UNCONDITIONALLY (ADR-032), so this path
/// cannot be broken by a feature flag. Declaring a second, identical interface here is exactly the
/// duplication §11 exists to prevent. The naming mismatch is real but cosmetic; hoisting that
/// interface to a neutral name/namespace touches CommunicationModule plus four communication services
/// and is filed as a follow-up rather than done as a drive-by on an authorization path.</para>
///
/// <para><b>The seam this class DOES add</b> is <see cref="IImpersonatedRootSetSource"/> — the swap
/// point task 036 flips behind its flag. That is a different seam from the query one, and it is the
/// one the POML asks for.</para>
///
/// <para><b>Fail-closed, and specifically NOT fail-empty.</b> App-only is the silent default of the
/// whole Dataverse client, so the dangerous failure here is not an exception — it is quietly
/// answering with an app-only query, or swallowing a fault into an empty set that reads as "this user
/// can see nothing". Both are wrong in opposite directions and both are silent. Every read on this
/// path goes through the impersonated primitive, which itself refuses <see cref="Guid.Empty"/>; that
/// refusal PROPAGATES here and is never caught.</para>
/// </remarks>
public interface IImpersonatedRootSetSource
{
    /// <summary>
    /// The root ids <paramref name="systemUserId"/> can read for <paramref name="entityType"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="systemUserId"/> is <see cref="Guid.Empty"/> (propagated from the impersonated
    /// primitive — never degraded to an app-only query), or <paramref name="entityType"/> is not one
    /// of the three root types.
    /// </exception>
    Task<RootIdSet> GetAsync(Guid systemUserId, string entityType, CancellationToken ct = default);
}

/// <inheritdoc cref="IImpersonatedRootSetSource"/>
public sealed class ImpersonatedRootSetSource : IImpersonatedRootSetSource
{
    /// <summary>Cache resource label (per <see cref="ITenantCache"/>). On-wire key becomes
    /// <c>tenant:{tenantId}:impersonated-root-set:{systemUserId:D}:{entityType}:v{n}</c>.</summary>
    internal const string CacheResource = "impersonated-root-set";

    /// <summary>
    /// Cache schema version per ADR-009. Bump whenever the QUERY SHAPE or the cached payload changes —
    /// entries written under an older shape may carry a silently different id set, and serving one is
    /// an authorization answer derived from a query nobody ran.
    /// </summary>
    private const int CacheVersion = 1;

    /// <summary>5-minute TTL, matching the membership precedent (FR-1A.8).</summary>
    internal static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Row cap for a single impersonated read. Hitting it sets <see cref="RootIdSet.Truncated"/>;
    /// it never silently shortens the answer.
    /// </summary>
    internal const int RowCap = 5000;

    /// <summary>
    /// Entity set + id column per root type. **Verified against live Dataverse metadata 2026-09-09**
    /// (`EntityDefinitions?$select=LogicalName,EntitySetName,PrimaryIdAttribute`) — not derived from a
    /// pluralization convention. This project has six stale-column defects; two of the three primary
    /// NAME attributes on these same entities are non-obvious, so the id columns were checked rather
    /// than assumed.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string EntitySet, string IdColumn)> Bindings =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["sprk_project"] = ("sprk_projects", "sprk_projectid"),
            ["sprk_matter"] = ("sprk_matters", "sprk_matterid"),
            ["sprk_workassignment"] = ("sprk_workassignments", "sprk_workassignmentid"),
        };

    private readonly IImpersonatedCommunicationQuery _query;
    private readonly ITenantCache _cache;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<ImpersonatedRootSetSource> _logger;

    public ImpersonatedRootSetSource(
        IImpersonatedCommunicationQuery query,
        ITenantCache cache,
        ILogger<ImpersonatedRootSetSource> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>The three root entity types this source can answer for.</summary>
    internal static IReadOnlyCollection<string> SupportedEntityTypes => (IReadOnlyCollection<string>)Bindings.Keys;

    /// <inheritdoc />
    public async Task<RootIdSet> GetAsync(Guid systemUserId, string entityType, CancellationToken ct = default)
    {
        if (!Bindings.TryGetValue(entityType, out var binding))
            throw new ArgumentException(
                $"'{entityType}' is not a root record type. Supported: {string.Join(", ", Bindings.Keys)}. "
                + "Service request is deliberately absent — it is core but never externally grantable "
                + "(owner decision 2026-09-09, task 028).",
                nameof(entityType));

        // NOTE: Guid.Empty is deliberately NOT checked here. The impersonated primitive refuses it and
        // that refusal must be the single point of truth — a second guard here could drift from it, and
        // a guard that drifts toward leniency on this path is an app-only query nobody intended.

        var tenantId = GetTenantId();
        var cacheId = $"{systemUserId:D}:{entityType}";

        var cached = await TryGetFromCacheAsync(tenantId, cacheId, ct).ConfigureAwait(false);
        if (cached is not null)
            return new RootIdSet(cached.Ids.ToHashSet(), cached.Truncated);

        var live = await QueryAsync(systemUserId, binding.EntitySet, binding.IdColumn, ct).ConfigureAwait(false);

        await TrySetCacheAsync(
            tenantId, cacheId, new CachedRootIdSet(live.Ids.ToArray(), live.Truncated), ct).ConfigureAwait(false);

        return live;
    }

    private async Task<RootIdSet> QueryAsync(
        Guid systemUserId, string entitySet, string idColumn, CancellationToken ct)
    {
        // Id-only $select with a bounded $top — one round trip per entity type (NFR-02). No expansion,
        // no fan-out. Any non-success status throws inside the primitive and propagates: this method
        // has NO catch, by design.
        var odata = $"$select={idColumn}&$top={RowCap}";

        var rows = await _query.QueryAsync(entitySet, odata, systemUserId, ct).ConfigureAwait(false);

        var ids = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (row.TryGetValue(idColumn, out var value)
                && value.ValueKind == JsonValueKind.String
                && Guid.TryParse(value.GetString(), out var id))
            {
                ids.Add(id);
            }
        }

        // Truncation is measured on ROWS RETURNED, not on distinct ids — duplicates would mask it.
        var truncated = rows.Count >= RowCap;
        if (truncated)
        {
            _logger.LogWarning(
                "[ROOT-SET] Impersonated read of {EntitySet} for {SystemUserId} hit the {RowCap}-row cap; "
                + "the returned set is INCOMPLETE and is flagged Truncated.",
                entitySet, systemUserId, RowCap);
        }

        return new RootIdSet(ids, truncated);
    }

    /// <summary>
    /// Tenant for the cache key. Falls back to <c>"anonymous"</c>, matching
    /// <c>MembershipResolverService.GetTenantId</c> — the key's SECURITY property is
    /// <c>systemUserId</c>, which is always present; tenant is a namespace, not the isolator.
    /// </summary>
    private string GetTenantId()
        => _httpContextAccessor?.HttpContext?.User?.FindFirst("tid")?.Value
            ?? _httpContextAccessor?.HttpContext?.User?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
            ?? "anonymous";

    // ── Cache: fail-open on the CACHE, never on the ANSWER ────────────────────────────────────────
    //
    // A Redis outage must degrade to a live impersonated query, not to a failed authorization read and
    // not to an empty set. That is why these use GetAsync/SetAsync with an explicit catch rather than
    // ITenantCache.GetOrCreateAsync, which would surface a cache fault as the caller's error. Copied
    // from MembershipResolverService, which established the same contract for the same reason.
    //
    // OperationCanceledException is deliberately rethrown: a cancelled request is not a cache fault,
    // and swallowing it would turn a caller's cancellation into a full uncached Dataverse round trip.

    private async Task<CachedRootIdSet?> TryGetFromCacheAsync(string tenantId, string cacheId, CancellationToken ct)
    {
        try
        {
            return await _cache.GetAsync<CachedRootIdSet>(
                tenantId, CacheResource, cacheId, CacheVersion, ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[ROOT-SET] Cache read failed for {CacheId}; falling through to a live impersonated query.", cacheId);
            return null;
        }
    }

    private async Task TrySetCacheAsync(string tenantId, string cacheId, CachedRootIdSet value, CancellationToken ct)
    {
        try
        {
            await _cache.SetAsync(
                tenantId, CacheResource, cacheId, CacheVersion, value, CacheTtl, ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[ROOT-SET] Cache write failed for {CacheId}; the live answer is returned regardless.", cacheId);
        }
    }

    /// <summary>
    /// Serializable cache payload. A <c>HashSet&lt;Guid&gt;</c> round-trips through the cache's JSON
    /// serializer as an array anyway; naming it explicitly keeps the stored shape obvious to anyone
    /// deciding whether a change needs a <see cref="CacheVersion"/> bump.
    /// </summary>
    internal sealed record CachedRootIdSet(Guid[] Ids, bool Truncated);
}
