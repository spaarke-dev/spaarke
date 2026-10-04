using Sprk.Bff.Api.Infrastructure.Authentication;
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
/// <para>
/// <b>Fails closed</b> (ADR-003; unified-access-control-r2 task 165). Every decision answers
/// <see cref="SpeAdminScopeDecision.Unverifiable"/> when a read it depends on fails — never "allow".
/// </para>
/// </remarks>
public class SpeAdminTenantScope
{
    /// <summary>Row cap for the whole-table reads; a full page means the read may be truncated.</summary>
    internal const int WholeTableReadLimit = 5000;

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
    /// <exception cref="Exception">THROWS when the business-unit hierarchy cannot be read.</exception>
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
    /// Whether the caller may act on <paramref name="configId"/>: <see cref="SpeAdminScopeDecision.Permitted"/>,
    /// <see cref="SpeAdminScopeDecision.NotFoundOrOutOfScope"/> (ONE answer for "does not exist" and "not
    /// yours", so the response is not an existence oracle), or <see cref="SpeAdminScopeDecision.Unverifiable"/>
    /// when a Dataverse read the decision depends on failed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A config with NO business unit set is treated as accessible. Those are single-tenant or
    /// pre-migration rows, and denying them would break every existing dedicated deployment on
    /// upgrade. That is a deliberate compatibility choice: it means an unassigned config is visible
    /// tenant-wide, so <b>every config MUST carry a business unit before a shared multi-customer
    /// environment is considered isolated.</b> <c>POST /api/spe/configs</c> no longer creates one
    /// (task 165).
    /// </para>
    /// <para>
    /// <b>Why this no longer fails open</b> (task 165). It used to answer "allow" when the config lookup
    /// threw, on the argument that the endpoint's own read of the same config would fail too. A transient
    /// fault breaks that argument: the filter's read fails, the endpoint's read a moment later succeeds,
    /// and a cross-business-unit config gets through. It also answered "allow" for a config that does not
    /// exist, leaving the endpoint to 404 in a different shape from the filter's out-of-scope 404 — an
    /// existence oracle. The lookup now tells "does not exist" apart from "exists with no business unit".
    /// </para>
    /// </remarks>
    public async Task<SpeAdminScopeDecision> DecideConfigAccessAsync(
        ClaimsPrincipal? user,
        Guid configId,
        CancellationToken ct = default)
    {
        ConfigBusinessUnitLookup lookup;
        try
        {
            lookup = await ResolveConfigBusinessUnitAsync(configId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "SpeAdmin tenant scope: could not read the business unit of config {ConfigId} — refusing (unverifiable).",
                configId);
            return SpeAdminScopeDecision.Unverifiable;
        }

        if (!lookup.Exists)
        {
            return SpeAdminScopeDecision.NotFoundOrOutOfScope;
        }

        if (lookup.BusinessUnitId is null)
        {
            // The business-unit-less compatibility rule above. Deliberately unchanged by task 165.
            return SpeAdminScopeDecision.Permitted;
        }

        IReadOnlyCollection<Guid> accessible;
        try
        {
            accessible = await GetAccessibleBusinessUnitsAsync(user, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "SpeAdmin tenant scope: could not read the business-unit hierarchy — refusing config {ConfigId} (unverifiable).",
                configId);
            return SpeAdminScopeDecision.Unverifiable;
        }

        return accessible.Contains(lookup.BusinessUnitId.Value)
            ? SpeAdminScopeDecision.Permitted
            : SpeAdminScopeDecision.NotFoundOrOutOfScope;
    }

    /// <summary>
    /// Whether the caller may WRITE a config carrying these body values (<c>POST /api/spe/configs</c>,
    /// <c>PUT /api/spe/configs/{configId}</c>). Judges the values the request would STORE, not the config
    /// being acted on — that one is <c>SpeAdminTenantScopeFilter</c>'s job.
    /// </summary>
    /// <param name="user">The caller.</param>
    /// <param name="businessUnitId">
    /// The business unit the request would store, or null when it sets none. When set it must be in the
    /// caller's accessible set; another unit and a GUID that is no business unit at all get the same
    /// <see cref="SpeAdminScopeDecision.BusinessUnitOutOfScope"/>.
    /// </param>
    /// <param name="identityValues">
    /// The app-identity values the request would store, keyed by Dataverse column
    /// (<see cref="IdentityColumns"/>). Pass only the values that are NEW: on PUT, the fields whose value
    /// differs from the stored row. Blank values are ignored.
    /// </param>
    /// <param name="excludeConfigId">On PUT, the config being updated — its own row is not "another" config.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// <para>
    /// <b>Why identity is checked, not only the business unit</b> (sweep finding #74). A config is the
    /// credential selector: <c>SpeAdminGraphService.ResolveConfigAsync</c> reads the owning app and its Key
    /// Vault secret name from it, and <c>GetClientForConfigAsync</c> builds an app-only Graph client from
    /// them. An admin in unit A could create a config IN THEIR OWN UNIT naming unit B's container type, or
    /// B's app and secret; every config-scoped route then passes the filter for it while acting app-only on
    /// B's containers. Business-unit intersection alone does not stop that.
    /// </para>
    /// <para>
    /// <b>Values are compared within a kind, across columns</b>: an app id against both app-id columns, a
    /// secret name against both secret-name columns, because naming B's owning app as one's CONSUMING app is
    /// the same borrowing. Trimmed, case-insensitive. A row with no business unit does not block (the
    /// compatibility rule in <see cref="DecideConfigAccessAsync"/>).
    /// </para>
    /// <para>
    /// The other rows are read with ONE app-only query and compared in memory, with no caller value in the
    /// <c>$filter</c> (<see cref="DataverseWebApiClient.QueryAsync{T}"/> does not encode it). A fault, or a
    /// full page of <see cref="WholeTableReadLimit"/> rows (possible truncation), is
    /// <see cref="SpeAdminScopeDecision.Unverifiable"/>. The business unit is checked first.
    /// </para>
    /// </remarks>
    public async Task<SpeAdminScopeDecision> DecideConfigWriteAsync(
        ClaimsPrincipal? user,
        Guid? businessUnitId,
        IReadOnlyDictionary<string, string?> identityValues,
        Guid? excludeConfigId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identityValues);

        var candidates = identityValues
            .Where(kv => IdentityKindByColumn.ContainsKey(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => (Kind: IdentityKindByColumn[kv.Key], Value: kv.Value!.Trim()))
            .ToList();

        if (businessUnitId is null && candidates.Count == 0)
        {
            return SpeAdminScopeDecision.Permitted;
        }

        IReadOnlyCollection<Guid> accessible;
        try
        {
            accessible = await GetAccessibleBusinessUnitsAsync(user, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "SpeAdmin tenant scope: could not read the business-unit hierarchy for a config write — refusing (unverifiable).");
            return SpeAdminScopeDecision.Unverifiable;
        }

        if (businessUnitId is { } unit && !accessible.Contains(unit))
        {
            return SpeAdminScopeDecision.BusinessUnitOutOfScope;
        }

        if (candidates.Count == 0)
        {
            return SpeAdminScopeDecision.Permitted;
        }

        List<ConfigScopeRow> rows;
        try
        {
            rows = await LoadConfigScopeRowsAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "SpeAdmin tenant scope: could not read the config table for the identity check — refusing (unverifiable).");
            return SpeAdminScopeDecision.Unverifiable;
        }

        if (rows.Count >= WholeTableReadLimit)
        {
            _logger.LogError(
                "SpeAdmin tenant scope: the config table read returned {Count} rows (the read limit), so the identity " +
                "check cannot prove it saw every row — refusing (unverifiable).",
                rows.Count);
            return SpeAdminScopeDecision.Unverifiable;
        }

        foreach (var row in rows)
        {
            if (excludeConfigId is { } self && row.ConfigId == self) continue;
            if (row.BusinessUnitId is not { } rowUnit) continue;   // compatibility rule
            if (accessible.Contains(rowUnit)) continue;

            foreach (var (kind, value) in candidates)
            {
                if (row.ValuesOfKind(kind).Any(v => string.Equals(v?.Trim(), value, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning(
                        "SpeAdmin tenant scope: a config write names a {Kind} value that a config in an unreachable " +
                        "business unit already carries — refusing.",
                        kind);
                    return SpeAdminScopeDecision.IdentityOutOfScope;
                }
            }
        }

        return SpeAdminScopeDecision.Permitted;
    }

    /// <summary>
    /// The ids of every config the caller may see: those in an accessible business unit, plus those with no
    /// business unit (the compatibility rule). Used to project the cross-config dashboard aggregate.
    /// </summary>
    /// <exception cref="Exception">
    /// THROWS on any read fault, and when the config read returns <see cref="WholeTableReadLimit"/> rows
    /// (possible truncation). The caller must refuse, never fall back to the unprojected aggregate.
    /// </exception>
    public async Task<IReadOnlySet<Guid>> GetReachableConfigIdsAsync(
        ClaimsPrincipal? user,
        CancellationToken ct = default)
    {
        var accessible = await GetAccessibleBusinessUnitsAsync(user, ct).ConfigureAwait(false);
        var rows = await LoadConfigScopeRowsAsync(ct).ConfigureAwait(false);

        if (rows.Count >= WholeTableReadLimit)
        {
            throw new InvalidOperationException(
                $"The config table read returned {rows.Count} rows (the read limit); the reachable set cannot be proven complete.");
        }

        return rows
            .Where(r => r.ConfigId.HasValue
                        && (r.BusinessUnitId is null || accessible.Contains(r.BusinessUnitId.Value)))
            .Select(r => r.ConfigId!.Value)
            .ToHashSet();
    }

    /// <summary>The five config columns that select an app identity (sweep finding #74).</summary>
    public static class IdentityColumns
    {
        public const string ContainerTypeId = "sprk_containertypeid";
        public const string OwningAppId = "sprk_owningappid";
        public const string KeyVaultSecretName = "sprk_keyvaultsecretname";
        public const string ConsumingAppId = "sprk_consumingappid";
        public const string ConsumingAppKvSecret = "sprk_consumingappkvsecret";
    }

    private static readonly IReadOnlyDictionary<string, string> IdentityKindByColumn = new Dictionary<string, string>
    {
        [IdentityColumns.ContainerTypeId] = "container type",
        [IdentityColumns.OwningAppId] = "app id",
        [IdentityColumns.ConsumingAppId] = "app id",
        [IdentityColumns.KeyVaultSecretName] = "secret name",
        [IdentityColumns.ConsumingAppKvSecret] = "secret name",
    };

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
        var oid = CallerResolution.ResolveObjectId(user);

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

    /// <summary>
    /// Reads the business unit off a container type config, telling "does not exist" apart from "exists
    /// with no business unit". THROWS on a read fault; <see cref="DecideConfigAccessAsync"/> turns that into
    /// <see cref="SpeAdminScopeDecision.Unverifiable"/>.
    /// </summary>
    internal async Task<ConfigBusinessUnitLookup> ResolveConfigBusinessUnitAsync(Guid configId, CancellationToken ct)
    {
        var rows = await _dataverseClient.QueryAsync<ConfigBusinessUnitRow>(
            "sprk_specontainertypeconfigs",
            filter: $"sprk_specontainertypeconfigid eq {configId:D}",
            select: "sprk_specontainertypeconfigid,_sprk_businessunit_value",
            top: 1,
            cancellationToken: ct).ConfigureAwait(false);

        return rows.Count == 0
            ? new ConfigBusinessUnitLookup(Exists: false, BusinessUnitId: null)
            : new ConfigBusinessUnitLookup(Exists: true, BusinessUnitId: rows[0].BusinessUnitId);
    }

    /// <summary>
    /// Reads every config's id, business unit and app-identity columns in one app-only query, with no
    /// caller value in the filter, for in-memory decisions. Same shape as
    /// <see cref="LoadBusinessUnitHierarchyAsync"/>. THROWS on a read fault.
    /// </summary>
    private Task<List<ConfigScopeRow>> LoadConfigScopeRowsAsync(CancellationToken ct) =>
        _dataverseClient.QueryAsync<ConfigScopeRow>(
            "sprk_specontainertypeconfigs",
            filter: null,
            select: "sprk_specontainertypeconfigid,_sprk_businessunit_value," +
                    $"{IdentityColumns.ContainerTypeId},{IdentityColumns.OwningAppId},{IdentityColumns.KeyVaultSecretName}," +
                    $"{IdentityColumns.ConsumingAppId},{IdentityColumns.ConsumingAppKvSecret}",
            top: WholeTableReadLimit,
            cancellationToken: ct);

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
            top: WholeTableReadLimit,
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

    /// <summary>Result of <see cref="ResolveConfigBusinessUnitAsync"/>.</summary>
    internal readonly record struct ConfigBusinessUnitLookup(bool Exists, Guid? BusinessUnitId);

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

    private sealed class ConfigScopeRow
    {
        [JsonPropertyName("sprk_specontainertypeconfigid")]
        public Guid? ConfigId { get; set; }

        [JsonPropertyName("_sprk_businessunit_value")]
        public Guid? BusinessUnitId { get; set; }

        [JsonPropertyName(IdentityColumns.ContainerTypeId)]
        public string? ContainerTypeId { get; set; }

        [JsonPropertyName(IdentityColumns.OwningAppId)]
        public string? OwningAppId { get; set; }

        [JsonPropertyName(IdentityColumns.KeyVaultSecretName)]
        public string? KeyVaultSecretName { get; set; }

        [JsonPropertyName(IdentityColumns.ConsumingAppId)]
        public string? ConsumingAppId { get; set; }

        [JsonPropertyName(IdentityColumns.ConsumingAppKvSecret)]
        public string? ConsumingAppKvSecret { get; set; }

        public IEnumerable<string?> ValuesOfKind(string kind) => kind switch
        {
            "container type" => new[] { ContainerTypeId },
            "app id" => new[] { OwningAppId, ConsumingAppId },
            "secret name" => new[] { KeyVaultSecretName, ConsumingAppKvSecret },
            _ => Array.Empty<string?>()
        };
    }

    private sealed class BusinessUnitRow
    {
        [JsonPropertyName("businessunitid")]
        public Guid? BusinessUnitId { get; set; }

        [JsonPropertyName("_parentbusinessunitid_value")]
        public Guid? ParentBusinessUnitId { get; set; }
    }
}

/// <summary>
/// The answer of every <see cref="SpeAdminTenantScope"/> decision (unified-access-control-r2 task 165).
/// </summary>
/// <remarks>
/// A <c>bool</c> cannot carry the third case — "the boundary could not be evaluated". Collapsing it into
/// "allow" was the fail-open defect; collapsing it into "deny" would answer a Dataverse outage with a 404
/// telling an admin their own config does not exist. Each refusal has its own case because each maps to its
/// own deny code.
/// </remarks>
public enum SpeAdminScopeDecision
{
    /// <summary>The caller may proceed.</summary>
    Permitted,

    /// <summary>The config does not exist, or exists outside the caller's business units. ONE answer: 404.</summary>
    NotFoundOrOutOfScope,

    /// <summary>A body <c>businessUnitId</c> the caller cannot reach, or that is no business unit: 403.</summary>
    BusinessUnitOutOfScope,

    /// <summary>A body app-identity value already carried by a config in an unreachable business unit: 403.</summary>
    IdentityOutOfScope,

    /// <summary>A Dataverse read the decision depends on failed: 503, never allow.</summary>
    Unverifiable
}
