using System.Security.Claims;
using System.Text.Json.Serialization;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.SpeAdmin;

/// <summary>
/// Resolves which business units the calling user may act on, and whether a given
/// <c>sprk_specontainertypeconfig</c> falls inside that set.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> In the multi-customer deployment model every customer shares one
/// <c>bff.api</c> app registration. That registration holds <c>FileStorageContainer.Selected</c> and
/// <c>FileStorageContainerType.Manage.All</c>, so Microsoft Graph will serve it any container type it
/// is registered against — Graph has no concept of which customer a request is "for". The
/// cross-customer boundary therefore lives in this codebase, not in Entra.
/// </para>
/// <para>
/// Before this type existed there was no such boundary: <c>configId</c> was effectively a bearer
/// capability. Fifteen endpoint files accepted it with no ownership check, and
/// <c>ConfigEndpoints</c> took <c>businessUnitId</c> as a caller-supplied query parameter — so
/// omitting it returned every customer's configuration. Harmless in a dedicated deployment; a
/// cross-customer disclosure in a shared one. See <c>notes/tenant-isolation-gap.md</c>.
/// </para>
/// <para>
/// <b>The BFF reads Dataverse app-only</b> (<see cref="DataverseWebApiClient"/> authenticates as the
/// application), so Dataverse's own business-unit security trimming never applies to these rows.
/// Everything is visible to the query; the filtering has to be explicit. That is the cost of the
/// BFF-centric design, and this is where it gets paid.
/// </para>
/// <para>
/// <b>Deliberately not cached.</b> This sits on an authorization path, where a stale answer is a
/// security defect rather than a slow page: a user moved out of a business unit would keep their old
/// reach for the life of the cache entry. The cost is one or two Dataverse reads per request on a
/// low-volume admin surface. If it ever becomes hot, cache it in Redis per ADR-009 with a short TTL
/// and explicit invalidation — never in-process.
/// </para>
/// </remarks>
public class SpeAdminTenantScope
{
    private readonly DataverseWebApiClient _dataverseClient;
    private readonly ILogger<SpeAdminTenantScope> _logger;

    public SpeAdminTenantScope(
        DataverseWebApiClient dataverseClient,
        ILogger<SpeAdminTenantScope> logger)
    {
        _dataverseClient = dataverseClient ?? throw new ArgumentNullException(nameof(dataverseClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The business units the caller may act on: their own, plus every descendant of it.
    /// </summary>
    /// <remarks>
    /// Descendants are included because Dataverse business units are a hierarchy and Spaarke's own
    /// operators sit above the customer units they support. Exact-match-only would lock a root-level
    /// operator out of every customer, which is not the intent — but a customer administrator sits in
    /// a leaf unit and so still sees only themselves.
    /// </remarks>
    /// <returns>
    /// The accessible set, or an EMPTY set when the caller cannot be resolved to a Dataverse user.
    /// An empty set denies everything — callers must not treat it as "no filter".
    /// </returns>
    public async Task<IReadOnlyCollection<Guid>> GetAccessibleBusinessUnitsAsync(
        ClaimsPrincipal? user,
        CancellationToken ct = default)
    {
        var callerBusinessUnit = await ResolveCallerBusinessUnitAsync(user, ct).ConfigureAwait(false);
        if (callerBusinessUnit is null)
        {
            return Array.Empty<Guid>();
        }

        var hierarchy = await LoadBusinessUnitHierarchyAsync(ct).ConfigureAwait(false);
        return CollectSelfAndDescendants(callerBusinessUnit.Value, hierarchy);
    }

    /// <summary>
    /// Whether the caller may act on <paramref name="configId"/>.
    /// </summary>
    /// <remarks>
    /// A config with NO business unit set is treated as accessible. Those are single-tenant or
    /// pre-migration rows, and denying them would break every existing dedicated deployment on
    /// upgrade. That is a deliberate compatibility choice: it means an unassigned config is visible
    /// tenant-wide, so <b>every config MUST carry a business unit before a shared multi-customer
    /// environment is considered isolated.</b>
    /// </remarks>
    public async Task<bool> CanAccessConfigAsync(
        ClaimsPrincipal? user,
        Guid configId,
        CancellationToken ct = default)
    {
        Guid? configBusinessUnit;
        try
        {
            configBusinessUnit = await ResolveConfigBusinessUnitAsync(configId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Could not determine the config's business unit, so no authorization decision is
            // possible. Allow the request to continue rather than converting every Dataverse blip
            // into a 500 on every SPE Admin endpoint.
            //
            // This is not a hole: the endpoint must itself resolve the same config from the same
            // Dataverse organisation before it can return anything, so an outage that defeats this
            // lookup also defeats the endpoint. What it avoids is a filter that changes the error
            // semantics of every endpoint behind it — including turning an input-validation 400
            // into a misleading 404.
            _logger.LogError(ex,
                "SpeAdmin tenant scope: could not resolve the business unit for config {ConfigId}; " +
                "deferring the decision to the endpoint.", configId);
            return true;
        }

        if (configBusinessUnit is null)
        {
            // Either the config does not exist, or it has no BU. Endpoints resolve the config
            // themselves and report "not found"; this filter's job is only the boundary.
            return true;
        }

        var accessible = await GetAccessibleBusinessUnitsAsync(user, ct).ConfigureAwait(false);
        return accessible.Contains(configBusinessUnit.Value);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Resolution
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the caller's business unit from Dataverse via their Entra object id.
    /// </summary>
    /// <remarks>
    /// The <c>oid</c> claim is the link: Dataverse <c>systemuserid</c> is a different value, joined
    /// through <c>systemuser.azureactivedirectoryobjectid</c>. This is read from the token, never from
    /// the request body or query string — that distinction is the entire point of the class.
    /// </remarks>
    internal async Task<Guid?> ResolveCallerBusinessUnitAsync(ClaimsPrincipal? user, CancellationToken ct)
    {
        var oid = user?.FindFirstValue("oid")
            ?? user?.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier");

        if (string.IsNullOrWhiteSpace(oid) || !Guid.TryParse(oid, out var callerOid))
        {
            _logger.LogWarning("SpeAdmin tenant scope: no usable 'oid' claim on the caller — denying all business units.");
            return null;
        }

        try
        {
            var rows = await _dataverseClient.QueryAsync<SystemUserRow>(
                "systemusers",
                filter: $"azureactivedirectoryobjectid eq {callerOid:D}",
                select: "systemuserid,_businessunitid_value",
                top: 1,
                cancellationToken: ct).ConfigureAwait(false);

            if (rows.Count == 0 || rows[0].BusinessUnitId is null)
            {
                _logger.LogWarning(
                    "SpeAdmin tenant scope: Entra user {Oid} has no matching Dataverse systemuser (or no business unit) — denying all.",
                    callerOid);
                return null;
            }

            return rows[0].BusinessUnitId;
        }
        catch (Exception ex)
        {
            // Fail CLOSED. An unavailable directory must not widen access.
            _logger.LogError(ex,
                "SpeAdmin tenant scope: failed to resolve the business unit for Entra user {Oid} — denying all.",
                callerOid);
            return null;
        }
    }

    /// <summary>Reads the business unit off a container type config.</summary>
    internal async Task<Guid?> ResolveConfigBusinessUnitAsync(Guid configId, CancellationToken ct)
    {
        try
        {
            var rows = await _dataverseClient.QueryAsync<ConfigBusinessUnitRow>(
                "sprk_specontainertypeconfigs",
                filter: $"sprk_specontainertypeconfigid eq {configId:D}",
                select: "sprk_specontainertypeconfigid,_sprk_businessunit_value",
                top: 1,
                cancellationToken: ct).ConfigureAwait(false);

            return rows.Count == 0 ? null : rows[0].BusinessUnitId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SpeAdmin tenant scope: failed to read the business unit for config {ConfigId}.", configId);
            throw;
        }
    }

    /// <summary>Loads every business unit as a child → parent map.</summary>
    /// <remarks>
    /// One query rather than a walk: business-unit counts are small (tens, not thousands), and a
    /// single read is both cheaper and race-free compared with recursing parent by parent.
    /// </remarks>
    private async Task<IReadOnlyDictionary<Guid, Guid?>> LoadBusinessUnitHierarchyAsync(CancellationToken ct)
    {
        var rows = await _dataverseClient.QueryAsync<BusinessUnitRow>(
            "businessunits",
            filter: null,
            select: "businessunitid,_parentbusinessunitid_value",
            top: 5000,
            cancellationToken: ct).ConfigureAwait(false);

        var map = new Dictionary<Guid, Guid?>();
        foreach (var row in rows)
        {
            if (row.BusinessUnitId is { } id)
            {
                map[id] = row.ParentBusinessUnitId;
            }
        }

        return map;
    }

    /// <summary>Returns <paramref name="root"/> plus every business unit beneath it.</summary>
    internal static IReadOnlyCollection<Guid> CollectSelfAndDescendants(
        Guid root,
        IReadOnlyDictionary<Guid, Guid?> childToParent)
    {
        var accessible = new HashSet<Guid> { root };

        // Repeat until no new descendants appear. Bounded by the depth of the tree, and the
        // visited-set makes a cyclic or self-parented row terminate rather than hang.
        bool added;
        do
        {
            added = false;
            foreach (var (child, parent) in childToParent)
            {
                if (parent is { } p && accessible.Contains(p) && accessible.Add(child))
                {
                    added = true;
                }
            }
        }
        while (added);

        return accessible;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Dataverse row shapes
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class SystemUserRow
    {
        [JsonPropertyName("systemuserid")]
        public Guid? SystemUserId { get; set; }

        [JsonPropertyName("_businessunitid_value")]
        public Guid? BusinessUnitId { get; set; }
    }

    private sealed class ConfigBusinessUnitRow
    {
        [JsonPropertyName("sprk_specontainertypeconfigid")]
        public Guid? ConfigId { get; set; }

        [JsonPropertyName("_sprk_businessunit_value")]
        public Guid? BusinessUnitId { get; set; }
    }

    private sealed class BusinessUnitRow
    {
        [JsonPropertyName("businessunitid")]
        public Guid? BusinessUnitId { get; set; }

        [JsonPropertyName("_parentbusinessunitid_value")]
        public Guid? ParentBusinessUnitId { get; set; }
    }
}
