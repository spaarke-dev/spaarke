using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Graph;
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

    /// <summary>
    /// How many single-container binding reads a list trim runs at once. Graph returns a container's custom
    /// properties only on a single-container GET (see <see cref="SpeContainerBusinessUnitStamp"/>), so a page of N
    /// containers costs N reads; bounded so one admin page cannot flood the owning app's Graph throttling budget.
    /// </summary>
    internal const int BindingReadConcurrency = 8;

    private readonly DataverseWebApiClient _dataverseClient;
    private readonly SpeAdminGraphService _graphService;
    private readonly ILogger<SpeAdminTenantScope> _logger;

    public SpeAdminTenantScope(
        DataverseWebApiClient dataverseClient,
        SpeAdminGraphService graphService,
        ILogger<SpeAdminTenantScope> logger)
    {
        _dataverseClient = dataverseClient ?? throw new ArgumentNullException(nameof(dataverseClient));
        _graphService = graphService ?? throw new ArgumentNullException(nameof(graphService));
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
        CancellationToken ct = default) =>
        (await GetCallerScopeAsync(user, ct).ConfigureAwait(false)).AccessibleBusinessUnits;

    /// <summary>
    /// The caller's reach, resolved once: the accessible business units (own unit + descendants; EMPTY when the caller
    /// cannot be resolved to a Dataverse user) and whether the own unit is the ROOT (a platform operator).
    /// </summary>
    /// <exception cref="Exception">THROWS when the business-unit hierarchy cannot be read. Refuse (503), never widen.</exception>
    public async Task<SpeAdminCallerScope> GetCallerScopeAsync(
        ClaimsPrincipal? user,
        CancellationToken ct = default)
    {
        var callerBusinessUnit = await ResolveCallerBusinessUnitAsync(user, ct).ConfigureAwait(false);
        if (callerBusinessUnit is null)
        {
            return SpeAdminCallerScope.Nobody;
        }

        var hierarchy = await LoadBusinessUnitHierarchyAsync(_dataverseClient, ct).ConfigureAwait(false);
        var isRoot = hierarchy.TryGetValue(callerBusinessUnit.Value, out var parent) && parent is null;

        return new SpeAdminCallerScope(
            CallerBusinessUnitId: callerBusinessUnit.Value,
            IsPlatformOperator: isRoot,
            AccessibleBusinessUnits: CollectSelfAndDescendants(callerBusinessUnit.Value, hierarchy));
    }

    /// <summary>
    /// Which SPE environments (<c>sprk_speenvironment</c>) the caller may read and whether they may write
    /// any (unified-access-control-r2 task 165, round 16 item 4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Environments are shared tenant infrastructure</b>, not a customer's own record:
    /// <c>sprk_speenvironment</c> has no business-unit column, and in Model 1 one environment serves every
    /// customer's configs. So: only an admin whose OWN business unit is the root (no parent) — a Spaarke
    /// platform operator — may create, change or delete environments, and may read every one. Any other
    /// admin may read only the environments linked by a config they can reach (their units' configs, plus
    /// business-unit-less configs under the compatibility rule in <see cref="DecideConfigAccessAsync"/>).
    /// </para>
    /// <para>
    /// A caller who cannot be resolved to a Dataverse user reaches nothing and writes nothing.
    /// </para>
    /// </remarks>
    /// <exception cref="Exception">
    /// THROWS on any read fault, and when the config read returns <see cref="WholeTableReadLimit"/> rows
    /// (possible truncation). The caller must refuse (503), never fall back to "every environment".
    /// </exception>
    public async Task<SpeAdminEnvironmentReach> GetEnvironmentReachAsync(
        ClaimsPrincipal? user,
        CancellationToken ct = default)
    {
        var scope = await GetCallerScopeAsync(user, ct).ConfigureAwait(false);
        if (scope.IsPlatformOperator)
        {
            return SpeAdminEnvironmentReach.PlatformOperator;
        }

        if (scope.AccessibleBusinessUnits.Count == 0)
        {
            return SpeAdminEnvironmentReach.Nothing;
        }

        var rows = await LoadConfigScopeRowsAsync(ct).ConfigureAwait(false);
        if (rows.Count >= WholeTableReadLimit)
        {
            throw new InvalidOperationException(
                $"The config table read returned {rows.Count} rows (the read limit); the readable environments cannot be proven complete.");
        }

        return new SpeAdminEnvironmentReach(
            IsPlatformOperator: false,
            LinkedEnvironmentIds: LinkedEnvironmentIds(rows, scope.AccessibleBusinessUnits));
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

        if (lookup.BusinessUnitId is { } configUnit)
        {
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

            if (!accessible.Contains(configUnit))
            {
                return SpeAdminScopeDecision.NotFoundOrOutOfScope;
            }
        }

        // A config with no business unit reaches here under the compatibility rule above (deliberately unchanged by
        // task 165).
        return SpeAdminScopeDecision.Permitted;
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
    /// <param name="environmentId">
    /// The SPE environment the request would link, or null when it links none. When set it must be one the
    /// caller can READ (<see cref="GetEnvironmentReachAsync"/>, round 16 item 4); an unknown environment and
    /// an unreadable one get the same <see cref="SpeAdminScopeDecision.EnvironmentOutOfScope"/>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// <para>
    /// <b>Why identity is checked, not only the business unit</b> (sweep finding #74). A config is the
    /// credential selector: <c>SpeAdminGraphService.ResolveConfigAsync</c> reads the owning app and its Key
    /// Vault secret name from it, and <c>GetClientForConfigAsync</c> builds an app-only Graph client from
    /// them.
    /// </para>
    /// <para>
    /// <b>Narrowed for Model 1 (owner round 20 item 3).</b> Configs of different customers may share a container
    /// type and its owning app — that is Model 1 (one container type, one owning app, one container per customer;
    /// container-type topology §3). Sharing them no longer opens another customer's containers: every container,
    /// item, permission and bulk route authorizes PER CONTAINER against the container's business-unit stamp
    /// (<see cref="DecideContainerAccessAsync"/>), and the app-only container-TYPE routes refuse a read or a write of a
    /// type a config the caller cannot reach also carries (<see cref="DecideContainerTypeAccessAsync"/>). So the SHARED
    /// identity — <see cref="IdentityColumns.ContainerTypeId"/>, <see cref="IdentityColumns.OwningAppId"/> and
    /// its secret <see cref="IdentityColumns.KeyVaultSecretName"/> — may be named by any config (the secret name only
    /// within <see cref="SpeConfigSecretNamePolicy"/>, enforced by the config endpoints, round 35 item 3). The PER-CUSTOMER
    /// identity — the consuming app (<see cref="IdentityColumns.ConsumingAppId"/>,
    /// <see cref="IdentityColumns.ConsumingAppKvSecret"/>; one per customer in both models, topology §3A) — stays
    /// exclusive: a value another unreachable config carries in a per-customer column may not be named in ANY
    /// column, within its kind (an app id against app ids, a secret name against secret names) — naming B's
    /// consuming app as one's OWNING app is the same borrowing. A row with no business unit does not block (the
    /// compatibility rule in <see cref="DecideConfigAccessAsync"/>).
    /// </para>
    /// <para>
    /// <b>Values are compared in a canonical form</b> (<see cref="CanonicalIdentityValue"/>). A container type
    /// id or app id that parses as a GUID is compared AS a GUID, so <c>N</c> (32 hex digits, no hyphens),
    /// <c>B</c> (braces) and <c>P</c> forms of another unit's value are the same value, not a new one — the
    /// columns are free text and the request is not required to send the canonical <c>D</c> form. Anything
    /// else (secret names; a non-GUID id) is compared trimmed and case-insensitively.
    /// </para>
    /// <para>
    /// The other rows are read with ONE app-only query and compared in memory, with no caller value in the
    /// <c>$filter</c> (<see cref="DataverseWebApiClient.QueryAsync{T}"/> does not encode it). A fault, or a
    /// full page of <see cref="WholeTableReadLimit"/> rows (possible truncation), is
    /// <see cref="SpeAdminScopeDecision.Unverifiable"/>. Order: business unit, environment, identity.
    /// </para>
    /// </remarks>
    public async Task<SpeAdminScopeDecision> DecideConfigWriteAsync(
        ClaimsPrincipal? user,
        Guid? businessUnitId,
        IReadOnlyDictionary<string, string?> identityValues,
        Guid? excludeConfigId,
        Guid? environmentId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identityValues);

        var candidates = identityValues
            .Where(kv => IdentityKindByColumn.ContainsKey(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => (Kind: IdentityKindByColumn[kv.Key], Value: CanonicalIdentityValue(IdentityKindByColumn[kv.Key], kv.Value)))
            .ToList();

        if (businessUnitId is null && environmentId is null && candidates.Count == 0)
        {
            return SpeAdminScopeDecision.Permitted;
        }

        SpeAdminCallerScope scope;
        try
        {
            scope = await GetCallerScopeAsync(user, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "SpeAdmin tenant scope: could not read the business-unit hierarchy for a config write — refusing (unverifiable).");
            return SpeAdminScopeDecision.Unverifiable;
        }

        var accessible = scope.AccessibleBusinessUnits;

        if (businessUnitId is { } unit && !accessible.Contains(unit))
        {
            return SpeAdminScopeDecision.BusinessUnitOutOfScope;
        }

        // A platform operator reads every environment, so only a non-empty id is required of them; anyone
        // else is judged against the config rows below.
        var environmentNeedsRows = environmentId is { } env && env != Guid.Empty && !scope.IsPlatformOperator;
        if (environmentId is { } requested && (requested == Guid.Empty || accessible.Count == 0))
        {
            return SpeAdminScopeDecision.EnvironmentOutOfScope;
        }

        if (candidates.Count == 0 && !environmentNeedsRows)
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
                "SpeAdmin tenant scope: could not read the config table for the write check — refusing (unverifiable).");
            return SpeAdminScopeDecision.Unverifiable;
        }

        if (rows.Count >= WholeTableReadLimit)
        {
            _logger.LogError(
                "SpeAdmin tenant scope: the config table read returned {Count} rows (the read limit), so the write " +
                "check cannot prove it saw every row — refusing (unverifiable).",
                rows.Count);
            return SpeAdminScopeDecision.Unverifiable;
        }

        if (environmentNeedsRows && !LinkedEnvironmentIds(rows, accessible).Contains(environmentId!.Value))
        {
            _logger.LogWarning(
                "SpeAdmin tenant scope: a config write links an SPE environment the caller cannot read — refusing.");
            return SpeAdminScopeDecision.EnvironmentOutOfScope;
        }

        foreach (var row in rows)
        {
            if (excludeConfigId is { } self && row.ConfigId == self) continue;
            if (row.BusinessUnitId is not { } rowUnit) continue;   // compatibility rule
            if (accessible.Contains(rowUnit)) continue;

            foreach (var (kind, value) in candidates)
            {
                // Only the other config's PER-CUSTOMER values are exclusive (owner round 20 item 3).
                if (row.ExclusiveValuesOfKind(kind).Any(v =>
                        !string.IsNullOrWhiteSpace(v)
                        && string.Equals(CanonicalIdentityValue(kind, v), value, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning(
                        "SpeAdmin tenant scope: a config write names a {Kind} value that a config in an unreachable " +
                        "business unit carries as its own per-customer identity — refusing.",
                        kind);
                    return SpeAdminScopeDecision.IdentityOutOfScope;
                }
            }
        }

        return SpeAdminScopeDecision.Permitted;
    }

    /// <summary>
    /// The configs the caller may see: those in an accessible business unit, plus those with no business unit (the
    /// compatibility rule). Used to project the cross-config dashboard aggregate.
    /// </summary>
    /// <remarks>
    /// "Every config" is judged over the WHOLE table, not over the configs a cached aggregate happens to
    /// name: a config skipped by the dashboard sync (incomplete) is named nowhere, yet it may belong to
    /// another customer.
    /// </remarks>
    /// <exception cref="Exception">
    /// THROWS on any read fault, and when the config read returns <see cref="WholeTableReadLimit"/> rows
    /// (possible truncation): a truncated read could make a leaf admin look as if they reached every config and hand
    /// them the unprojected cross-customer aggregate. The caller must refuse, never fall back to the aggregate.
    /// </exception>
    public async Task<SpeAdminConfigReach> GetReachableConfigIdsAsync(
        ClaimsPrincipal? user,
        CancellationToken ct = default)
    {
        var scope = await GetCallerScopeAsync(user, ct).ConfigureAwait(false);
        var rows = await LoadConfigScopeRowsAsync(ct).ConfigureAwait(false);

        if (rows.Count >= WholeTableReadLimit)
        {
            throw new InvalidOperationException(
                $"The config table read returned {rows.Count} rows (the read limit); the reachable set cannot be proven complete.");
        }

        var accessible = scope.AccessibleBusinessUnits;
        var reachable = rows
            .Where(r => r.ConfigId.HasValue && IsReachable(r, accessible))
            .Select(r => r.ConfigId!.Value)
            .ToHashSet();

        return new SpeAdminConfigReach(
            ConfigIds: reachable,
            ReachesEveryConfig: rows.All(r => IsReachable(r, accessible)),
            IsPlatformOperator: scope.IsPlatformOperator);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Per-container authorization (owner round 20 items 1-2)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether the caller may act on ONE container through <paramref name="configId"/> (the container, item,
    /// permission, column, custom-property and recycle-bin routes): <see cref="SpeAdminScopeDecision.Permitted"/>,
    /// <see cref="SpeAdminScopeDecision.NotFoundOrOutOfScope"/> (ONE answer for a container that does not exist,
    /// one of another container type, and one bound outside the caller's reach), or
    /// <see cref="SpeAdminScopeDecision.Unverifiable"/> when the caller's scope, the config or the container's
    /// binding cannot be read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule (owner round 20 item 2 as amended by round 35 item 2, <see cref="SpeAdminCallerScope.CanReach"/>): a
    /// container bound to a unit the caller reaches (their own unit or a descendant); an UNBOUND or malformed container
    /// by NO admin route, root-unit admins included — under Model 1 the root admin of any environment whose config names
    /// a shared type would otherwise reach another customer's unbound containers; an unreadable binding fails closed. The
    /// config itself was already confined by the filter's configId rule — this adds the container, because one container
    /// type can serve several customers (Model 1). Every refusal is logged with its reason
    /// (<see cref="SpeContainerRefusal"/>: <c>absent</c>, <c>other_type</c>, <c>unbound</c>, <c>malformed</c>,
    /// <c>out_of_scope</c>); the caller sees ONE 404 whichever it was.
    /// </para>
    /// <para>
    /// The binding is read from the container itself with the config's own client (<see cref="SpeContainerBusinessUnitStamp"/>
    /// explains why a list read cannot carry it). <paramref name="deleted"/> reads it from the recycle bin
    /// (<c>/deletedContainers/{id}</c>) for the routes that act on a soft-deleted container.
    /// </para>
    /// </remarks>
    public async Task<SpeAdminScopeDecision> DecideContainerAccessAsync(
        ClaimsPrincipal? user,
        Guid configId,
        string containerId,
        bool deleted,
        CancellationToken ct = default)
    {
        SpeAdminCallerScope scope;
        try
        {
            scope = await GetCallerScopeAsync(user, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "SpeAdmin tenant scope: could not read the business-unit hierarchy — refusing container {ContainerId} (unverifiable).",
                containerId);
            return SpeAdminScopeDecision.Unverifiable;
        }

        if (scope.AccessibleBusinessUnits.Count == 0)
        {
            // An unresolvable caller reaches no container; nothing is read.
            return SpeAdminScopeDecision.NotFoundOrOutOfScope;
        }

        SpeAdminGraphService.ContainerTypeConfig? config;
        SpeContainerBindingRead? read;
        try
        {
            config = await _graphService.ResolveConfigAsync(configId, ct).ConfigureAwait(false);
            if (config is null)
            {
                return SpeAdminScopeDecision.NotFoundOrOutOfScope;
            }

            read = await _graphService.GetContainerBindingForConfigAsync(config, containerId, deleted, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "SpeAdmin tenant scope: could not read the business-unit binding of container {ContainerId} — refusing (unverifiable).",
                containerId);
            return SpeAdminScopeDecision.Unverifiable;
        }

        var refusal = ClassifyContainer(scope, config.ContainerTypeId, read);
        if (refusal != SpeContainerRefusal.None)
        {
            _logger.LogWarning(
                "SpeAdmin tenant scope: container {ContainerId} refused through config {ConfigId} — reason {Reason}.",
                containerId, configId, RefusalReason(refusal));
            return SpeAdminScopeDecision.NotFoundOrOutOfScope;
        }

        return SpeAdminScopeDecision.Permitted;
    }

    /// <summary>
    /// The per-container rule on one binding read, as a decision: <see cref="SpeAdminScopeDecision.Permitted"/> only when
    /// <see cref="ClassifyContainer"/> finds no refusal; every refusal is the ONE not-found answer.
    /// </summary>
    internal static SpeAdminScopeDecision DecideContainer(
        SpeAdminCallerScope scope,
        string? configContainerTypeId,
        SpeContainerBindingRead? read) =>
        ClassifyContainer(scope, configContainerTypeId, read) == SpeContainerRefusal.None
            ? SpeAdminScopeDecision.Permitted
            : SpeAdminScopeDecision.NotFoundOrOutOfScope;

    /// <summary>
    /// The per-container rule on one binding read, with the reason: absent → <see cref="SpeContainerRefusal.Absent"/>; a
    /// container type Graph REPORTS that differs from the config's → <see cref="SpeContainerRefusal.OtherType"/>; an
    /// unbound container → <see cref="SpeContainerRefusal.Unbound"/> and a malformed stamp →
    /// <see cref="SpeContainerRefusal.Malformed"/> for EVERY caller (round 35 item 2); a unit the caller does not reach →
    /// <see cref="SpeContainerRefusal.OutOfScope"/>.
    /// </summary>
    internal static SpeContainerRefusal ClassifyContainer(
        SpeAdminCallerScope scope,
        string? configContainerTypeId,
        SpeContainerBindingRead? read)
    {
        if (read is null)
        {
            return SpeContainerRefusal.Absent;
        }

        if (!string.IsNullOrWhiteSpace(read.ContainerTypeId) && !SameGuid(read.ContainerTypeId, configContainerTypeId))
        {
            return SpeContainerRefusal.OtherType;
        }

        if (read.Binding.IsMalformed)
        {
            return SpeContainerRefusal.Malformed;
        }

        if (read.Binding.BusinessUnitId is null)
        {
            return SpeContainerRefusal.Unbound;
        }

        return scope.CanReach(read.Binding) ? SpeContainerRefusal.None : SpeContainerRefusal.OutOfScope;
    }

    /// <summary>The log word for a refusal (the 404 the caller sees is the same for every one).</summary>
    internal static string RefusalReason(SpeContainerRefusal refusal) => refusal switch
    {
        SpeContainerRefusal.Absent => "absent",
        SpeContainerRefusal.OtherType => "other_type",
        SpeContainerRefusal.Unbound => "unbound",
        SpeContainerRefusal.Malformed => "malformed",
        SpeContainerRefusal.OutOfScope => "out_of_scope",
        _ => "none",
    };

    /// <summary>
    /// The containers in <paramref name="containerIds"/> the caller may see through <paramref name="config"/> — the
    /// list trim for <c>GET /containers</c>, <c>GET /recyclebin</c> and the two search routes. Each container's binding
    /// is read (bounded concurrency); a container Graph no longer finds is simply not returned.
    /// </summary>
    /// <exception cref="Exception">THROWS when any binding read faults — the caller refuses (503), never shows an
    /// unverified container and never silently drops one it could not judge.</exception>
    public async Task<IReadOnlySet<string>> FilterReachableContainersAsync(
        SpeAdminCallerScope scope,
        SpeAdminGraphService.ContainerTypeConfig config,
        IEnumerable<string> containerIds,
        bool deleted,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(config);

        var ids = containerIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        if (ids.Count == 0 || scope.AccessibleBusinessUnits.Count == 0)
        {
            return reachable;
        }

        using var gate = new SemaphoreSlim(BindingReadConcurrency);
        var reads = ids.Select(async id =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return (Id: id, Read: await _graphService.GetContainerBindingForConfigAsync(config, id, deleted, ct).ConfigureAwait(false));
            }
            finally
            {
                gate.Release();
            }
        }).ToList();

        foreach (var (id, read) in await Task.WhenAll(reads).ConfigureAwait(false))
        {
            var refusal = ClassifyContainer(scope, config.ContainerTypeId, read);
            if (refusal == SpeContainerRefusal.None)
            {
                reachable.Add(id);
            }
            else if (refusal is SpeContainerRefusal.Unbound or SpeContainerRefusal.Malformed)
            {
                // Not an error to the caller (the container is simply not listed), but an operator must bind it: no admin
                // route reaches it until the backfill does (round 35 item 2).
                _logger.LogWarning(
                    "SpeAdmin tenant scope: container {ContainerId} of config {ConfigId} left out of a list — reason {Reason}.",
                    id, config.ConfigId, RefusalReason(refusal));
            }
        }

        return reachable;
    }

    /// <summary>
    /// The list trim every container-listing handler applies (<c>GET /containers</c>, <c>GET /recyclebin</c>,
    /// <c>POST /search/containers</c>, <c>POST /search/items</c>): the caller's scope, then
    /// <see cref="FilterReachableContainersAsync"/>. Null when either cannot be read — the handler answers 503 and
    /// shows nothing (never the untrimmed list).
    /// </summary>
    public async Task<SpeAdminContainerTrim?> TrimToReachableContainersAsync(
        ClaimsPrincipal? user,
        SpeAdminGraphService.ContainerTypeConfig config,
        IEnumerable<string> containerIds,
        bool deleted,
        CancellationToken ct = default)
    {
        try
        {
            var scope = await GetCallerScopeAsync(user, ct).ConfigureAwait(false);
            var reachable = await FilterReachableContainersAsync(scope, config, containerIds, deleted, ct).ConfigureAwait(false);
            return new SpeAdminContainerTrim(reachable, scope.IsPlatformOperator);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "SpeAdmin tenant scope: could not read the caller's scope or a container binding for config {ConfigId} — " +
                "refusing the list (unverifiable).", config.ConfigId);
            return null;
        }
    }

    /// <summary>
    /// The business unit a container created through <paramref name="configId"/> is bound to (owner round 20 item 1):
    /// the config's own unit; for a config with no unit (the compatibility rule) the creating admin's own unit. Null
    /// when neither can be determined (the caller is not a Dataverse user) — the create must then be refused.
    /// </summary>
    /// <exception cref="Exception">THROWS when a read fails — refuse (503), never create unbound.</exception>
    public async Task<Guid?> ResolveContainerOwnerAsync(
        ClaimsPrincipal? user,
        Guid configId,
        CancellationToken ct = default)
    {
        var lookup = await ResolveConfigBusinessUnitAsync(configId, ct).ConfigureAwait(false);
        if (lookup.Exists && lookup.BusinessUnitId is { } configUnit && configUnit != Guid.Empty)
        {
            return configUnit;
        }

        var scope = await GetCallerScopeAsync(user, ct).ConfigureAwait(false);
        return scope.CallerBusinessUnitId;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // App-only container-TYPE routes (the consequence of owner round 20 item 3)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether the caller may act on container type <paramref name="typeId"/> through <paramref name="configId"/> on an
    /// app-only container-type route (type permissions, consuming-app registrations, register) — reads and writes alike.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why</b>. Narrowing the identity check (round 20 item 3) lets configs of different customers carry ONE
    /// container type and owning app. The container-type routes act app-only, as that shared owning app, on settings
    /// every customer of the type shares — a consuming-app registration grants an app every container of the type — and
    /// the reads (<c>GET …/permissions</c>, <c>GET …/consumers</c>) list every customer's consuming app and registrations,
    /// the per-customer values round 20 item 3 keeps exclusive. So (round 35 item 5: the reads get the write rule):
    /// </para>
    /// <list type="bullet">
    ///   <item>the route's type must BE the config's type (compared as GUIDs) — a config is not a credential for some
    ///   other type its owning app can reach; otherwise <see cref="SpeAdminScopeDecision.NotFoundOrOutOfScope"/>;</item>
    ///   <item>the caller must reach EVERY config that carries the type — a type shared with a customer the caller cannot
    ///   reach is shared infrastructure, read or changed only by an admin who reaches all of its customers (a root-unit
    ///   admin reaches every config); otherwise <see cref="SpeAdminScopeDecision.ContainerTypeShared"/>.</item>
    /// </list>
    /// <para>A read fault, or a full page of the config table, is <see cref="SpeAdminScopeDecision.Unverifiable"/>.</para>
    /// </remarks>
    public async Task<SpeAdminScopeDecision> DecideContainerTypeAccessAsync(
        ClaimsPrincipal? user,
        Guid configId,
        string? typeId,
        CancellationToken ct = default)
    {
        SpeAdminCallerScope scope;
        List<ConfigScopeRow> rows;
        try
        {
            scope = await GetCallerScopeAsync(user, ct).ConfigureAwait(false);
            rows = await LoadConfigScopeRowsAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "SpeAdmin tenant scope: could not read the scope for a container-type route — refusing (unverifiable).");
            return SpeAdminScopeDecision.Unverifiable;
        }

        if (rows.Count >= WholeTableReadLimit)
        {
            _logger.LogError(
                "SpeAdmin tenant scope: the config table read returned {Count} rows (the read limit) — refusing a " +
                "container-type route (unverifiable).", rows.Count);
            return SpeAdminScopeDecision.Unverifiable;
        }

        var config = rows.FirstOrDefault(r => r.ConfigId == configId);
        if (config is null || !SameGuid(config.ContainerTypeId, typeId))
        {
            return SpeAdminScopeDecision.NotFoundOrOutOfScope;
        }

        var accessible = scope.AccessibleBusinessUnits;
        var sharedWithAnUnreachableConfig = rows.Any(r =>
            SameGuid(r.ContainerTypeId, typeId) && !IsReachable(r, accessible));

        if (sharedWithAnUnreachableConfig)
        {
            _logger.LogWarning(
                "SpeAdmin tenant scope: container type {TypeId} through config {ConfigId} refused — a config the caller " +
                "cannot reach also carries that type.", typeId, configId);
            return SpeAdminScopeDecision.ContainerTypeShared;
        }

        return SpeAdminScopeDecision.Permitted;
    }

    /// <summary>Two values naming the same GUID (any spelling); false when either is not a GUID.</summary>
    internal static bool SameGuid(string? left, string? right) =>
        Guid.TryParse(left?.Trim(), out var l) && Guid.TryParse(right?.Trim(), out var r) && l == r;

    /// <summary>
    /// The form an app-identity value is compared in: a container type id or app id that parses as a GUID is
    /// its canonical <c>D</c> form (so <c>N</c> / <c>B</c> / <c>P</c> spellings of one GUID are equal);
    /// anything else is the trimmed value. Compare the results case-insensitively.
    /// </summary>
    internal static string CanonicalIdentityValue(string kind, string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return kind is ContainerTypeKind or AppIdKind && Guid.TryParse(trimmed, out var id)
            ? id.ToString("D")
            : trimmed;
    }

    /// <summary>
    /// <see cref="CanonicalIdentityValue(string, string?)"/> for a value of the given identity column
    /// (<see cref="IdentityColumns"/>); an unknown column compares trimmed.
    /// </summary>
    internal static string CanonicalIdentityColumnValue(string column, string? value) =>
        CanonicalIdentityValue(IdentityKindByColumn.TryGetValue(column, out var kind) ? kind : string.Empty, value);

    /// <summary>A config row the caller reaches: in an accessible unit, or with no unit (compatibility rule).</summary>
    private static bool IsReachable(ConfigScopeRow row, IReadOnlyCollection<Guid> accessible) =>
        row.BusinessUnitId is null || accessible.Contains(row.BusinessUnitId.Value);

    /// <summary>The environments linked by the configs the caller reaches.</summary>
    private static IReadOnlySet<Guid> LinkedEnvironmentIds(
        IEnumerable<ConfigScopeRow> rows,
        IReadOnlyCollection<Guid> accessible) =>
        accessible.Count == 0
            ? new HashSet<Guid>()
            : rows.Where(r => r.EnvironmentId is { } env && env != Guid.Empty && IsReachable(r, accessible))
                  .Select(r => r.EnvironmentId!.Value)
                  .ToHashSet();

    /// <summary>The five config columns that select an app identity (sweep finding #74).</summary>
    public static class IdentityColumns
    {
        public const string ContainerTypeId = "sprk_containertypeid";
        public const string OwningAppId = "sprk_owningappid";
        public const string KeyVaultSecretName = "sprk_keyvaultsecretname";
        public const string ConsumingAppId = "sprk_consumingappid";
        public const string ConsumingAppKvSecret = "sprk_consumingappkvsecret";
    }

    private const string ContainerTypeKind = "container type";
    private const string AppIdKind = "app id";
    private const string SecretNameKind = "secret name";

    private static readonly IReadOnlyDictionary<string, string> IdentityKindByColumn = new Dictionary<string, string>
    {
        [IdentityColumns.ContainerTypeId] = ContainerTypeKind,
        [IdentityColumns.OwningAppId] = AppIdKind,
        [IdentityColumns.ConsumingAppId] = AppIdKind,
        [IdentityColumns.KeyVaultSecretName] = SecretNameKind,
        [IdentityColumns.ConsumingAppKvSecret] = SecretNameKind,
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
            select: "sprk_specontainertypeconfigid,_sprk_businessunit_value,_sprk_environment_value," +
                    $"{IdentityColumns.ContainerTypeId},{IdentityColumns.OwningAppId},{IdentityColumns.KeyVaultSecretName}," +
                    $"{IdentityColumns.ConsumingAppId},{IdentityColumns.ConsumingAppKvSecret}",
            top: WholeTableReadLimit,
            cancellationToken: ct);

    /// <summary>Loads every business unit as a child → parent map.</summary>
    /// <remarks>
    /// One query rather than a walk: business-unit counts are small (tens, not thousands), and a
    /// single read is both cheaper and race-free compared with recursing parent by parent.
    /// </remarks>
    internal static async Task<IReadOnlyDictionary<Guid, Guid?>> LoadBusinessUnitHierarchyAsync(
        DataverseWebApiClient dataverseClient,
        CancellationToken ct)
    {
        var rows = await dataverseClient.QueryAsync<BusinessUnitRow>(
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
    internal static IReadOnlySet<Guid> CollectSelfAndDescendants(
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

        [JsonPropertyName("_sprk_environment_value")]
        public Guid? EnvironmentId { get; set; }

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

        /// <summary>
        /// This config's PER-CUSTOMER values of a kind — the ones no other customer's config may name (owner round 20
        /// item 3). The container type, owning app and its secret are shared by Model 1 configs and are not listed.
        /// </summary>
        public IEnumerable<string?> ExclusiveValuesOfKind(string kind) => kind switch
        {
            AppIdKind => new[] { ConsumingAppId },
            SecretNameKind => new[] { ConsumingAppKvSecret },
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

    /// <summary>
    /// A body <c>environmentId</c> the caller cannot read, or that is no environment (round 16 item 4): 403.
    /// </summary>
    EnvironmentOutOfScope,

    /// <summary>
    /// An app-only container-type route (read or write, round 35 item 5) on a type that a config the caller cannot
    /// reach also carries (owner round 20 item 3's consequence: a Model 1 type is shared infrastructure): 403.
    /// </summary>
    ContainerTypeShared,

    /// <summary>A read the decision depends on failed: 503, never allow.</summary>
    Unverifiable
}

/// <summary>
/// The caller's reach on the SPE admin plane, resolved once per decision (unified-access-control-r2 task 165) — the
/// answer of <see cref="SpeAdminTenantScope.GetCallerScopeAsync"/>.
/// </summary>
/// <param name="CallerBusinessUnitId">The caller's own business unit, or null when the caller is not a Dataverse user.</param>
/// <param name="IsPlatformOperator">The caller's own business unit is the ROOT (it has no parent).</param>
/// <param name="AccessibleBusinessUnits">
/// The caller's own unit plus every descendant; EMPTY when the caller cannot be resolved (reaches nothing).
/// </param>
public sealed record SpeAdminCallerScope(
    Guid? CallerBusinessUnitId,
    bool IsPlatformOperator,
    IReadOnlySet<Guid> AccessibleBusinessUnits)
{
    /// <summary>A caller who cannot be resolved to a Dataverse user: reaches nothing.</summary>
    public static SpeAdminCallerScope Nobody { get; } = new(null, false, new HashSet<Guid>());

    /// <summary>
    /// THE per-container rule (owner round 20 item 2, amended by round 35 item 2): a container bound to a business unit
    /// the caller reaches. An UNBOUND or malformed container is reached by NO caller — root-unit admins included: under
    /// Model 1 the root admin of any environment whose config names a shared container type would otherwise reach another
    /// customer's unbound containers. A container is bound only by its creation path's stamp or by the backfill
    /// (<c>scripts/Backfill-SpeContainerBusinessUnitStamp.ps1</c>, including its explicit <c>-Bind</c>). A binding that
    /// could not be READ never gets here — that is a refusal (fail closed) before this is asked.
    /// </summary>
    public bool CanReach(SpeContainerBinding binding) =>
        binding.BusinessUnitId is { } unit && !binding.IsMalformed && AccessibleBusinessUnits.Contains(unit);
}

/// <summary>
/// Why a container was refused (<see cref="SpeAdminTenantScope.ClassifyContainer"/>) — for the LOG only: every refusal
/// answers the caller with the same 404, so the response is never an oracle for which case it was.
/// </summary>
public enum SpeContainerRefusal
{
    /// <summary>Not refused.</summary>
    None,

    /// <summary>Graph does not find the container.</summary>
    Absent,

    /// <summary>Graph reports a container type that is not the config's.</summary>
    OtherType,

    /// <summary>The container carries no business-unit stamp — no admin route reaches it (round 35 item 2).</summary>
    Unbound,

    /// <summary>The stamp names no single business unit — treated as unbound.</summary>
    Malformed,

    /// <summary>Bound to a business unit the caller does not reach (or one this environment does not know).</summary>
    OutOfScope
}

/// <summary>
/// The answer of <see cref="SpeAdminTenantScope.TrimToReachableContainersAsync"/>.
/// </summary>
/// <param name="Reachable">The container ids (of those asked about) the caller may see.</param>
/// <param name="IsPlatformOperator">The caller's own unit is the root.</param>
public sealed record SpeAdminContainerTrim(IReadOnlySet<string> Reachable, bool IsPlatformOperator)
{
    /// <summary>
    /// THE rule for forwarding a Graph search total, shared by the container and the item search (task 165, owner rounds
    /// 20, 35 and 41): Graph's total counts every hit the owning app sees — under Model 1 other customers' too — so it is
    /// reported ONLY to a platform operator, ONLY when nothing was removed from this page, and ONLY when there is no
    /// further page (a later page Graph counted but nobody here judged may hold unbound or another environment's
    /// containers, which no admin reaches). Otherwise null: the caller reports what it can stand behind.
    /// </summary>
    /// <param name="graphTotal">Graph's own total for the query (null when Graph reports none).</param>
    /// <param name="pageItemCount">How many hits Graph returned on this page.</param>
    /// <param name="visibleCount">How many of them the caller reaches.</param>
    /// <param name="nextSkipToken">Graph's continuation token — non-null means a further page exists.</param>
    public long? ReportableGraphTotal(long? graphTotal, int pageItemCount, int visibleCount, string? nextSkipToken) =>
        IsPlatformOperator && visibleCount == pageItemCount && nextSkipToken is null
            ? graphTotal
            : null;
}

/// <summary>
/// The configs a caller reaches — the answer of <see cref="SpeAdminTenantScope.GetReachableConfigIdsAsync"/>.
/// </summary>
/// <param name="ConfigIds">The reachable config ids (in an accessible unit, or with no unit).</param>
/// <param name="ReachesEveryConfig">The caller reaches every config in the table.</param>
/// <param name="IsPlatformOperator">
/// The caller's own unit is the root — the only caller whose dashboard view carries the AGGREGATE count of unbound
/// containers (the alarm that a backfill / <c>-Bind</c> is owed; no route reaches any of them, round 35 item 2).
/// </param>
public sealed record SpeAdminConfigReach(
    IReadOnlySet<Guid> ConfigIds,
    bool ReachesEveryConfig,
    bool IsPlatformOperator);

/// <summary>
/// What a caller may do with SPE environments (<c>sprk_speenvironment</c>) — the answer of
/// <see cref="SpeAdminTenantScope.GetEnvironmentReachAsync"/> (unified-access-control-r2 task 165, round 16
/// item 4).
/// </summary>
/// <param name="IsPlatformOperator">
/// The caller's own business unit is the root: they read every environment and may write environments.
/// </param>
/// <param name="LinkedEnvironmentIds">
/// For anyone else, the environments linked by a config they can reach — the only ones they may read.
/// </param>
public sealed record SpeAdminEnvironmentReach(bool IsPlatformOperator, IReadOnlySet<Guid> LinkedEnvironmentIds)
{
    /// <summary>A root-unit admin: reads every environment, writes environments.</summary>
    public static SpeAdminEnvironmentReach PlatformOperator { get; } = new(true, new HashSet<Guid>());

    /// <summary>A caller who reaches no config (or cannot be resolved): reads and writes nothing.</summary>
    public static SpeAdminEnvironmentReach Nothing { get; } = new(false, new HashSet<Guid>());

    /// <summary>Whether the caller may create, change or delete environments.</summary>
    public bool CanWrite => IsPlatformOperator;

    /// <summary>Whether the caller may read <paramref name="environmentId"/>.</summary>
    public bool CanRead(Guid environmentId) =>
        environmentId != Guid.Empty && (IsPlatformOperator || LinkedEnvironmentIds.Contains(environmentId));
}
