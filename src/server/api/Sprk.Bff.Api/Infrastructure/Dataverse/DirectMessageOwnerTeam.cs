using System.Text.Json.Serialization;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// field-mapping-server-write-path-r1 task 054a — the owner team of Direct 1:1 messages. A Direct 1:1 message that is not
/// filed under a record is owned by a dedicated owner team: a NAMED, NON-DEFAULT, MEMBERLESS Owner team in a dedicated
/// business unit that holds NO users, is a direct child of the root business unit, and is neither the root nor the Secure
/// Record business unit. This type answers WHICH team that is, and refuses — with a stable reason code — whenever the
/// configuration does not name exactly such a team.
/// </summary>
/// <remarks>
/// <para><b>One lookup, not a second mechanism (CLAUDE.md §11).</b> The business unit and its named team are found, and the
/// team's and the unit's emptiness proven, by the SAME parameterised lookup the Secure Record owner team uses
/// (<see cref="SecureRecordOwnerTeam.IdentifyNamedAsync"/> and <see cref="SecureRecordOwnerTeam.ProveUnoccupiedAsync"/>), with
/// this type's names. What this type adds is the placement of the unit in the business-unit tree, a distinct refusal for a
/// configured name that is the unit's DEFAULT team, and a short-lived cache.</para>
///
/// <para><b>No defaults.</b> <see cref="BusinessUnitNameConfigKey"/> and <see cref="OwnerTeamNameConfigKey"/> have no compiled
/// fallback: an unset value refuses at use (<see cref="DirectMessageOwnerTeamRefusal.NotConfigured"/>) and never fails startup.
/// Never the root business unit, never a default team, never the Secure Record unit: each is a refusal, not a fallback.</para>
///
/// <para><b>Names are matched by Dataverse only.</b> Dataverse's <c>name eq</c> ignores case AND accents, so no name is ever
/// compared in code: the Secure Record exclusion compares the resolved business-unit and team IDS with the Secure Record
/// lookup's ids, and <see cref="CannotBeDirectOwnerTeamAsync"/> compares a team id with <see cref="IdentifyDirectMessageOwnerTeamAsync"/>'s.</para>
///
/// <para><b>Two answers.</b> <see cref="ResolveDirectMessageOwnerTeamAsync"/> answers "may this team be assigned to" (every
/// check). <see cref="IdentifyDirectMessageOwnerTeamAsync"/> answers only "which team does the configuration name" (the unit and
/// its named team, whatever their members, users or placement). A refusal from Resolve means "could be the Direct team"; a
/// caller that must know whether a given team IS the configured one settles it with Identify.</para>
///
/// <para><b>Checked once, cached briefly (ADR-009).</b> The checks are several Dataverse reads; they run once and their verdict
/// (the team, or a refusal) is shared for <see cref="CacheLifetime"/> by every caller, so no message pays for them. This is
/// ADR-009's "Allowed L1 Exceptions: Metadata, ≤ 15 min, document in code" — this is that documentation: a process-local
/// verdict about the environment's business-unit and team setup, holding no user data and no per-user decision. A FAULT (an
/// unreadable answer) is never cached: the next caller reads again. The cache is keyed by the configured names, so a changed
/// setting is read fresh.</para>
///
/// <para><b>Placement</b> (CLAUDE.md §10/§11): a concrete singleton beside <see cref="SecureRecordOwnerTeam"/>, registered in
/// <c>CommunicationModule</c> (its callers are the communication write paths), on the <see cref="DataverseWebApiClient"/>
/// singleton the shared lookup already reads through. No interface (ADR-010), no options type (the Secure Record keys'
/// pattern: named configuration keys read where they are used), no new package. It is an instance, not a static helper like
/// <see cref="SecureRecordOwnerTeam"/>, only because the cache needs a home.</para>
/// </remarks>
public sealed class DirectMessageOwnerTeam
{
    /// <summary>Configuration key naming the Direct message business unit. No default: unset refuses.</summary>
    public const string BusinessUnitNameConfigKey = "DirectMessage:BusinessUnitName";

    /// <summary>Configuration key naming the Direct message owner team inside that unit. No default: unset refuses.</summary>
    public const string OwnerTeamNameConfigKey = "DirectMessage:OwnerTeamName";

    /// <summary>How long one verdict (the team, or a refusal that is not a fault) is shared.</summary>
    internal static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(2);

    private const string BusinessUnitEntitySet = "businessunits";
    private const string TeamEntitySet = "teams";

    private readonly DataverseWebApiClient _dataverse;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _time;
    private readonly ILogger<DirectMessageOwnerTeam> _logger;
    private readonly SemaphoreSlim _evaluation = new(1, 1);
    private CachedVerdict? _cached;

    public DirectMessageOwnerTeam(
        DataverseWebApiClient dataverse,
        IConfiguration configuration,
        TimeProvider time,
        ILogger<DirectMessageOwnerTeam> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The Direct message owner team, or a refusal with a stable reason code (<see cref="DirectMessageOwnerTeamRefusal"/>). Only
    /// a resolved answer (<see cref="DirectMessageOwnerTeamResolution.IsResolved"/>) may be used to assign. Never throws for a
    /// Dataverse fault (the fault is carried on the refusal and is not cached); cancellation of <paramref name="ct"/> propagates.
    /// </summary>
    /// <remarks>
    /// <b>Caller rule.</b> Only <see cref="DirectMessageOwnerTeamResolution.IsNotConfigured"/> means "no Direct team can exist".
    /// Every other refusal, and every fault, means "could be the Direct team" (unknown): fail closed. A caller that must know
    /// whether a given team IS the configured Direct team settles it with <see cref="IdentifyDirectMessageOwnerTeamAsync"/>,
    /// never by reading a refusal as "not that team" (a refusal carries no team id).
    /// </remarks>
    public async Task<DirectMessageOwnerTeamResolution> ResolveDirectMessageOwnerTeamAsync(CancellationToken ct)
    {
        // Configuration only — no read, nothing cached: these are the cache key.
        var buName = Configured(BusinessUnitNameConfigKey);
        var teamName = Configured(OwnerTeamNameConfigKey);
        if (buName is null || teamName is null)
            return DirectMessageOwnerTeamResolution.Refused(DirectMessageOwnerTeamRefusal.NotConfigured);

        // The Secure Record names are part of the key: the Secure Record exclusion is decided against them (by id).
        var key = new VerdictKey(new NamedOwnerTeamLookup(buName, teamName), SecureRecordOwnerTeam.SecureRecordLookup(_configuration));
        if (Fresh(key) is { } cached)
            return cached;

        await _evaluation.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Another caller may have evaluated while this one waited.
            if (Fresh(key) is { } evaluated)
                return evaluated;

            var verdict = await EvaluateAsync(key.Direct, ct).ConfigureAwait(false);
            if (verdict.Fault is null)
                Volatile.Write(ref _cached, new CachedVerdict(key, verdict, _time.GetUtcNow()));

            if (!verdict.IsResolved)
            {
                _logger.LogError(verdict.Fault,
                    "[DIRECT-OWNER-TEAM] The Direct message owner team is not usable ({Refusal}): business unit '{BusinessUnitName}' "
                    + "({BusinessUnitKey}), team '{OwnerTeamName}' ({OwnerTeamKey}). Direct messages not filed under a record "
                    + "cannot be given their owner until this is corrected.{FaultNote}",
                    verdict.Refusal, buName, BusinessUnitNameConfigKey, teamName, OwnerTeamNameConfigKey,
                    verdict.Fault is null ? string.Empty : " The read faulted; it is not cached and is retried on the next call.");
            }

            return verdict;
        }
        finally
        {
            _evaluation.Release();
        }
    }

    /// <summary>
    /// WHICH team the configuration names as the Direct message owner team — the business unit by its configured name, then its
    /// NAMED, non-default Owner team, proven by id NOT to be the Secure Record unit or team — WITHOUT the other validity checks
    /// <see cref="ResolveDirectMessageOwnerTeamAsync"/> adds (no member, no user, placement in the tree). Not an assignment
    /// target: only a resolved answer from <see cref="ResolveDirectMessageOwnerTeamAsync"/> may be assigned to.
    /// </summary>
    /// <remarks>
    /// <para><b>Contract.</b> Ids are returned only when configured, read, and proven not Secure Record.</para>
    /// <list type="bullet">
    /// <item>Both ids, whenever the unit and its named team both exist and neither is the Secure Record unit or team, whatever
    /// their member, user or placement state (<see cref="DirectMessageOwnerTeamIdentity.IsIdentified"/>). The same lookup
    /// <see cref="ResolveDirectMessageOwnerTeamAsync"/> runs first, so Resolve can never select any other team.</item>
    /// <item><see cref="DirectMessageOwnerTeamIdentity.IsNotConfigured"/> when either setting is unset or blank: no ids, no fault,
    /// no read. Only this answer means "no Direct team can exist".</item>
    /// <item>The unit or team IS the Secure Record unit or team (by id — Dataverse's name match ignores accents): no ids,
    /// <see cref="DirectMessageOwnerTeamRefusal.BusinessUnitIsSecureRecord"/>. A caller comparing a row's owner with
    /// <see cref="DirectMessageOwnerTeamIdentity.TeamId"/> therefore never reads a Secure-owned row as Direct-owned.</item>
    /// <item>The Secure Record lookup cannot be decided: no ids, <see cref="DirectMessageOwnerTeamRefusal.SecureRecordAmbiguous"/>
    /// or <see cref="DirectMessageOwnerTeamRefusal.SecureRecordUnreadable"/> — the latter for a fault (carried), a Secure
    /// Record team not found, or a Secure Record unit not found while a Secure Record setting is set (the setting may be
    /// wrong, so the real Secure unit is unknown). Only with NO Secure Record setting and no unit by the compiled default
    /// name is "no Secure Record unit here" accepted, as <see cref="SecureRecordOwnerTeam"/> reads that case.</item>
    /// <item>A <see cref="DirectMessageOwnerTeamIdentity.Fault"/> only when a read was unreadable (no ids).</item>
    /// <item>Otherwise no ids and a <see cref="DirectMessageOwnerTeamIdentity.Refusal"/> code: not found (no such unit or named
    /// team exists under the configuration), or ambiguous (unknown — fail closed).</item>
    /// </list>
    /// <para>Never throws for a Dataverse fault; cancellation of <paramref name="ct"/> propagates. Not cached (four reads when
    /// identified: the Direct unit and team, the Secure Record unit and team).</para>
    /// <para><b>Caller rule.</b> A refusal from <see cref="ResolveDirectMessageOwnerTeamAsync"/> means "could be the Direct team";
    /// settle identity here.</para>
    /// </remarks>
    public async Task<DirectMessageOwnerTeamIdentity> IdentifyDirectMessageOwnerTeamAsync(CancellationToken ct)
    {
        var buName = Configured(BusinessUnitNameConfigKey);
        var teamName = Configured(OwnerTeamNameConfigKey);
        if (buName is null || teamName is null)
            return DirectMessageOwnerTeamIdentity.NotConfigured;

        var identity = await SecureRecordOwnerTeam.IdentifyNamedAsync(
            _dataverse, new NamedOwnerTeamLookup(buName, teamName), ct).ConfigureAwait(false);
        if (!identity.IsIdentified)
            return new DirectMessageOwnerTeamIdentity(null, null, Refusal(identity.Refusal, identity.Fault).Refusal, identity.Fault);

        // Ids only once proven NOT the Secure Record unit or team (by id): a caller comparing an owner with TeamId must never
        // read a Secure-owned row as Direct-owned. An undecidable Secure Record lookup returns no ids either.
        if (await SecureRecordRefusalAsync(identity, ct).ConfigureAwait(false) is { } secure)
            return new DirectMessageOwnerTeamIdentity(null, null, secure.Refusal, secure.Fault);

        return new DirectMessageOwnerTeamIdentity(identity.BusinessUnitId, identity.OwnerTeamId, null, null);
    }

    /// <summary>
    /// <c>true</c> only when <paramref name="teamId"/> is proven not to be the team <see cref="ResolveDirectMessageOwnerTeamAsync"/>
    /// could ever select: the configuration names a team (<see cref="IdentifyDirectMessageOwnerTeamAsync"/> identified it) and
    /// this is another id. <c>false</c> means "it could be" (the full resolution decides): an empty id, an unset configuration,
    /// a configured unit or team not found, an ambiguous one, a configuration naming the Secure Record unit or team, or an
    /// ambiguous Secure Record lookup. Not cached. A Dataverse fault (including the Secure Record lookup's) propagates.
    /// </summary>
    /// <remarks>Mirrors <c>SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync</c>'s role, but decides by id against the
    /// one identity lookup — never by comparing names in code, since Dataverse's name match also ignores accents. Its contract:
    /// never a wrong "cannot be"; any doubt answers <c>false</c>, which costs only the full resolution.</remarks>
    public async Task<bool> CannotBeDirectOwnerTeamAsync(Guid teamId, CancellationToken ct)
    {
        if (teamId == Guid.Empty)
            return false;

        var identity = await IdentifyDirectMessageOwnerTeamAsync(ct).ConfigureAwait(false);
        if (identity.Fault is { } fault)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fault).Throw();

        return identity.IsIdentified && identity.TeamId != teamId;
    }

    /// <summary>The trimmed setting, or <c>null</c> when it is unset or blank (no default).</summary>
    private string? Configured(string key) =>
        _configuration[key] is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    /// <summary>The cached verdict for <paramref name="key"/> while it is younger than <see cref="CacheLifetime"/>.</summary>
    private DirectMessageOwnerTeamResolution? Fresh(VerdictKey key)
    {
        var cached = Volatile.Read(ref _cached);
        return cached is not null
               && cached.Key == key
               && _time.GetUtcNow() - cached.At < CacheLifetime
            ? cached.Verdict
            : null;
    }

    /// <summary>
    /// The checks, in order: the business unit and its named team (the shared lookup's steps 1–2); once the unit is found, that
    /// neither it nor the team is the Secure Record unit or team (by id); a not-found name told apart from the unit's default
    /// team; the unit's place in the tree (not the root, a direct child of the root); then the shared lookup's invariants (no
    /// member, no user). Never throws for a Dataverse fault.
    /// </summary>
    private async Task<DirectMessageOwnerTeamResolution> EvaluateAsync(NamedOwnerTeamLookup lookup, CancellationToken ct)
    {
        var identity = await SecureRecordOwnerTeam.IdentifyNamedAsync(_dataverse, lookup, ct).ConfigureAwait(false);

        // The Secure Record exclusion runs as soon as the unit is known (and nothing faulted), so a setting naming the
        // Secure Record unit refuses as such even when it names no team there.
        if (identity.BusinessUnitId is not null && identity.Fault is null
            && await SecureRecordRefusalAsync(identity, ct).ConfigureAwait(false) is { } secure)
            return secure;

        if (!identity.IsIdentified)
        {
            if (identity.Refusal == SecureOwnerTeamStatus.OwnerTeamNotFound && identity.BusinessUnitId is { } unit)
                return await NotFoundOrDefaultTeamAsync(unit, lookup.OwnerTeamName, ct).ConfigureAwait(false);
            return Refusal(identity.Refusal, identity.Fault);
        }

        if (await PlacementRefusalAsync(identity.BusinessUnitId!.Value, ct).ConfigureAwait(false) is { } misplaced)
            return misplaced;

        var proven = await SecureRecordOwnerTeam.ProveUnoccupiedAsync(_dataverse, identity, ct).ConfigureAwait(false);
        return proven.IsResolved
            ? DirectMessageOwnerTeamResolution.Resolved(proven.OwnerTeamId!.Value)
            : Refusal(proven.Status, proven.Fault);
    }

    /// <summary>
    /// Whether the configured unit (and team, when found) is the Secure Record unit or its owner team — decided by comparing
    /// IDS with the Secure Record lookup (<see cref="SecureRecordOwnerTeam.IdentifyAsync"/>), never names: Dataverse's name
    /// match also ignores accents, so a setting that differs from the Secure names only by an accent finds the Secure unit.
    /// <c>null</c> when it is proven neither. An unreadable or ambiguous Secure Record lookup, a Secure Record team not found,
    /// or a Secure Record unit not found while a Secure Record setting is set cannot prove "not Secure", so each refuses with
    /// its own code. Only with no Secure Record setting is a unit not found by the compiled default name "no Secure Record
    /// unit here" (<see cref="SecureRecordOwnerTeam.ResolveSecureBusinessUnitAsync"/>'s reading).
    /// </summary>
    private async Task<DirectMessageOwnerTeamResolution?> SecureRecordRefusalAsync(SecureOwnerTeamIdentity direct, CancellationToken ct)
    {
        var secure = await SecureRecordOwnerTeam.IdentifyAsync(_dataverse, _configuration, ct).ConfigureAwait(false);

        // Whatever was found is compared first: a match is the precise answer.
        if (secure.BusinessUnitId is { } secureUnit && secureUnit != Guid.Empty && direct.BusinessUnitId == secureUnit)
            return DirectMessageOwnerTeamResolution.Refused(DirectMessageOwnerTeamRefusal.BusinessUnitIsSecureRecord);
        if (secure.OwnerTeamId is { } secureTeam && secureTeam != Guid.Empty && direct.OwnerTeamId == secureTeam)
            return DirectMessageOwnerTeamResolution.Refused(DirectMessageOwnerTeamRefusal.BusinessUnitIsSecureRecord);

        switch (secure.Refusal)
        {
            case null:
                return null;

            case SecureOwnerTeamStatus.BusinessUnitNotFound when !SecureRecordSettingsPresent():
                // No Secure Record setting and no unit by the compiled default name: this environment has no Secure Record
                // unit, so no unit is it — SecureRecordOwnerTeam.ResolveSecureBusinessUnitAsync's own reading of that case.
                return null;

            case SecureOwnerTeamStatus.BusinessUnitAmbiguous:
            case SecureOwnerTeamStatus.OwnerTeamAmbiguous:
                return DirectMessageOwnerTeamResolution.Refused(DirectMessageOwnerTeamRefusal.SecureRecordAmbiguous);

            default:
                // Unreadable; a CONFIGURED Secure Record unit not found (the setting may be wrong, so the real unit is
                // unknown); the Secure Record team not found; or a status the identity step does not produce today. None
                // proves "not Secure".
                return DirectMessageOwnerTeamResolution.Refused(DirectMessageOwnerTeamRefusal.SecureRecordUnreadable, secure.Fault);
        }
    }

    /// <summary>
    /// Whether either Secure Record setting is set (non-blank). Unset means <see cref="SecureRecordOwnerTeam"/> looks up its
    /// compiled default names — the only case in which a unit that is not found is read as "no Secure Record unit here".
    /// </summary>
    private bool SecureRecordSettingsPresent() =>
        !string.IsNullOrWhiteSpace(_configuration[SecureRecordOwnerTeam.BusinessUnitNameConfigKey])
        || !string.IsNullOrWhiteSpace(_configuration[SecureRecordOwnerTeam.OwnerTeamNameConfigKey]);

    /// <summary>
    /// The unit has no NAMED, non-default Owner team with the configured name. When its DEFAULT team carries that name, the
    /// setting names the default team — its own refusal. A fault reading that is <see cref="DirectMessageOwnerTeamRefusal.OwnerTeamUnreadable"/>
    /// with the fault, not cached.
    /// </summary>
    private async Task<DirectMessageOwnerTeamResolution> NotFoundOrDefaultTeamAsync(Guid businessUnitId, string teamName, CancellationToken ct)
    {
        List<SecureRecordOwnerTeam.IdRow> defaults;
        try
        {
            defaults = await _dataverse.QueryAsync<SecureRecordOwnerTeam.IdRow>(
                TeamEntitySet, filter: DefaultTeamFilter(businessUnitId, teamName), select: "teamid",
                top: 1, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return DirectMessageOwnerTeamResolution.Refused(DirectMessageOwnerTeamRefusal.OwnerTeamUnreadable, ex);
        }

        return DirectMessageOwnerTeamResolution.Refused(defaults.Count > 0
            ? DirectMessageOwnerTeamRefusal.OwnerTeamIsDefault
            : DirectMessageOwnerTeamRefusal.OwnerTeamNotFound);
    }

    /// <summary>
    /// The unit's place in the tree: it has a parent (it is not the root), and that parent has none (it is a direct child of
    /// the root). <c>null</c> when it is so placed; otherwise the refusal. An unreadable row is a refusal, never "placed".
    /// </summary>
    private async Task<DirectMessageOwnerTeamResolution?> PlacementRefusalAsync(Guid businessUnitId, CancellationToken ct)
    {
        Guid? parent;
        try
        {
            var unit = await ReadBusinessUnitAsync(businessUnitId, ct).ConfigureAwait(false);
            if (unit is null)
                return DirectMessageOwnerTeamResolution.Refused(DirectMessageOwnerTeamRefusal.BusinessUnitNotFound);
            parent = unit.parentbusinessunitid is { } p && p != Guid.Empty ? p : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return DirectMessageOwnerTeamResolution.Refused(DirectMessageOwnerTeamRefusal.BusinessUnitUnreadable, ex);
        }

        if (parent is not { } parentId)
            return DirectMessageOwnerTeamResolution.Refused(DirectMessageOwnerTeamRefusal.BusinessUnitIsRoot);

        try
        {
            var parentUnit = await ReadBusinessUnitAsync(parentId, ct).ConfigureAwait(false);
            if (parentUnit is null)
                return DirectMessageOwnerTeamResolution.Refused(DirectMessageOwnerTeamRefusal.BusinessUnitUnreadable);
            if (parentUnit.parentbusinessunitid is { } grandparent && grandparent != Guid.Empty)
                return DirectMessageOwnerTeamResolution.Refused(DirectMessageOwnerTeamRefusal.BusinessUnitNotRootChild);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return DirectMessageOwnerTeamResolution.Refused(DirectMessageOwnerTeamRefusal.BusinessUnitUnreadable, ex);
        }

        return null;
    }

    private async Task<BusinessUnitRow?> ReadBusinessUnitAsync(Guid businessUnitId, CancellationToken ct)
    {
        var rows = await _dataverse.QueryAsync<BusinessUnitRow>(
            BusinessUnitEntitySet, filter: BusinessUnitByIdFilter(businessUnitId),
            select: "businessunitid,_parentbusinessunitid_value", top: 1, cancellationToken: ct).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0];
    }

    /// <summary>The shared lookup's refusal, as this type's stable reason code.</summary>
    private static DirectMessageOwnerTeamResolution Refusal(SecureOwnerTeamStatus? status, Exception? fault) =>
        DirectMessageOwnerTeamResolution.Refused(status switch
        {
            SecureOwnerTeamStatus.BusinessUnitNotFound => DirectMessageOwnerTeamRefusal.BusinessUnitNotFound,
            SecureOwnerTeamStatus.BusinessUnitAmbiguous => DirectMessageOwnerTeamRefusal.BusinessUnitAmbiguous,
            SecureOwnerTeamStatus.BusinessUnitUnreadable => DirectMessageOwnerTeamRefusal.BusinessUnitUnreadable,
            SecureOwnerTeamStatus.OwnerTeamNotFound => DirectMessageOwnerTeamRefusal.OwnerTeamNotFound,
            SecureOwnerTeamStatus.OwnerTeamAmbiguous => DirectMessageOwnerTeamRefusal.OwnerTeamAmbiguous,
            SecureOwnerTeamStatus.OwnerTeamUnreadable => DirectMessageOwnerTeamRefusal.OwnerTeamUnreadable,
            SecureOwnerTeamStatus.OwnerTeamHasMembers => DirectMessageOwnerTeamRefusal.OwnerTeamHasMembers,
            SecureOwnerTeamStatus.OwnerTeamMembershipUnreadable => DirectMessageOwnerTeamRefusal.OwnerTeamMembershipUnreadable,
            SecureOwnerTeamStatus.BusinessUnitHasUsers => DirectMessageOwnerTeamRefusal.BusinessUnitHasUsers,
            SecureOwnerTeamStatus.BusinessUnitUsersUnreadable => DirectMessageOwnerTeamRefusal.BusinessUnitUsersUnreadable,
            // A refusal the shared lookup did not name (none today) is still a refusal, never a resolution.
            _ => DirectMessageOwnerTeamRefusal.OwnerTeamNotFound,
        }, fault);

    // ── OData filters (internal so tests can pin each predicate — ADR-038 A2) ──

    /// <summary>One business unit by id, read for its parent.</summary>
    internal static string BusinessUnitByIdFilter(Guid businessUnitId) => $"businessunitid eq {businessUnitId}";

    /// <summary>The unit's DEFAULT team carrying the configured name (any team type) — told apart from "not found".</summary>
    internal static string DefaultTeamFilter(Guid businessUnitId, string teamName) =>
        $"_{SecureRecordOwnerTeam.BusinessUnitColumn}_value eq {businessUnitId} and {SecureRecordOwnerTeam.NameColumn} eq "
        + $"'{SecureRecordOwnerTeam.EscapeODataStringLiteral(teamName)}' and {SecureRecordOwnerTeam.IsDefaultColumn} eq true";

    /// <summary>What a verdict depends on: the Direct names and the Secure Record names it was checked against.</summary>
    private readonly record struct VerdictKey(NamedOwnerTeamLookup Direct, NamedOwnerTeamLookup SecureRecord);

    private sealed record CachedVerdict(VerdictKey Key, DirectMessageOwnerTeamResolution Verdict, DateTimeOffset At);

    /// <summary>A business unit's id and parent. Internal so a test can answer the read through <c>QueryAsync</c>.</summary>
    internal sealed class BusinessUnitRow
    {
        [JsonPropertyName("businessunitid")]
        public Guid? businessunitid { get; set; }

        [JsonPropertyName("_parentbusinessunitid_value")]
        public Guid? parentbusinessunitid { get; set; }
    }
}

/// <summary>
/// The stable reason codes of a <see cref="DirectMessageOwnerTeam"/> refusal. Each names one state; a caller maps them to its
/// own response (a refusal before a send) or log event (after the row exists). Never renamed: logs and alerts key on them.
/// </summary>
public static class DirectMessageOwnerTeamRefusal
{
    private const string Prefix = "sdap.communication.direct_owner_team.";

    /// <summary><c>DirectMessage:BusinessUnitName</c> or <c>DirectMessage:OwnerTeamName</c> is unset or blank.</summary>
    public const string NotConfigured = Prefix + "not_configured";

    /// <summary>The configured business unit is the Secure Record business unit, or the team is its owner team (decided by id).</summary>
    public const string BusinessUnitIsSecureRecord = Prefix + "bu_is_secure_record";

    /// <summary>The Secure Record lookup is ambiguous, so "not the Secure Record unit" cannot be proven.</summary>
    public const string SecureRecordAmbiguous = Prefix + "secure_record_ambiguous";

    /// <summary>
    /// "Not the Secure Record unit" cannot be proven: the Secure Record lookup faulted, its team was not found, or its unit
    /// was not found while a Secure Record setting is set.
    /// </summary>
    public const string SecureRecordUnreadable = Prefix + "secure_record_unreadable";

    /// <summary>No business unit has the configured name.</summary>
    public const string BusinessUnitNotFound = Prefix + "bu_not_found";

    /// <summary>More than one business unit has the configured name.</summary>
    public const string BusinessUnitAmbiguous = Prefix + "bu_ambiguous";

    /// <summary>A business-unit read faulted or returned no row for a unit it had found.</summary>
    public const string BusinessUnitUnreadable = Prefix + "bu_unreadable";

    /// <summary>The configured business unit is the root business unit.</summary>
    public const string BusinessUnitIsRoot = Prefix + "bu_is_root";

    /// <summary>The configured business unit is not a direct child of the root business unit.</summary>
    public const string BusinessUnitNotRootChild = Prefix + "bu_not_root_child";

    /// <summary>No non-default Owner team with the configured name exists in the business unit.</summary>
    public const string OwnerTeamNotFound = Prefix + "team_not_found";

    /// <summary>The configured team name is the business unit's DEFAULT team.</summary>
    public const string OwnerTeamIsDefault = Prefix + "team_is_default";

    /// <summary>More than one non-default Owner team in the business unit has the configured name.</summary>
    public const string OwnerTeamAmbiguous = Prefix + "team_ambiguous";

    /// <summary>The team read faulted.</summary>
    public const string OwnerTeamUnreadable = Prefix + "team_unreadable";

    /// <summary>The team has at least one member (human or application user).</summary>
    public const string OwnerTeamHasMembers = Prefix + "team_has_members";

    /// <summary>The team's membership read faulted — not read as zero.</summary>
    public const string OwnerTeamMembershipUnreadable = Prefix + "team_membership_unreadable";

    /// <summary>At least one systemuser (any kind) sits in the business unit.</summary>
    public const string BusinessUnitHasUsers = Prefix + "bu_has_users";

    /// <summary>The business unit's user read faulted — not read as zero.</summary>
    public const string BusinessUnitUsersUnreadable = Prefix + "bu_users_unreadable";
}

/// <summary>
/// The answer of <see cref="DirectMessageOwnerTeam.ResolveDirectMessageOwnerTeamAsync"/>: the team, or a refusal. A refusal
/// carries no team id: only <see cref="IsNotConfigured"/> means "no Direct team can exist"; any other refusal or fault means
/// "could be the Direct team" — settle identity with <see cref="DirectMessageOwnerTeam.IdentifyDirectMessageOwnerTeamAsync"/>.
/// </summary>
/// <param name="TeamId">The Direct message owner team, when resolved.</param>
/// <param name="Refusal">A <see cref="DirectMessageOwnerTeamRefusal"/> code, when not.</param>
/// <param name="Fault">The Dataverse fault behind an unreadable refusal (never cached).</param>
public sealed record DirectMessageOwnerTeamResolution(Guid? TeamId, string? Refusal, Exception? Fault)
{
    /// <summary>True only for a resolved team. Only then may the team be used to assign.</summary>
    public bool IsResolved => Refusal is null && TeamId is { } id && id != Guid.Empty;

    /// <summary>
    /// True only when a Direct setting is unset or blank (<see cref="DirectMessageOwnerTeamRefusal.NotConfigured"/>): no Direct
    /// team can exist. The one refusal a caller may read as "not the Direct team".
    /// </summary>
    public bool IsNotConfigured => Refusal == DirectMessageOwnerTeamRefusal.NotConfigured;

    internal static DirectMessageOwnerTeamResolution Resolved(Guid teamId) => new(teamId, null, null);

    internal static DirectMessageOwnerTeamResolution Refused(string refusal, Exception? fault = null) => new(null, refusal, fault);
}

/// <summary>
/// The answer of <see cref="DirectMessageOwnerTeam.IdentifyDirectMessageOwnerTeamAsync"/>: WHICH business unit and team the
/// configuration names, proven not to be the Secure Record unit or team, independent of the other validity checks. Not an
/// assignment target.
/// </summary>
/// <param name="BusinessUnitId">The configured business unit, when both it and its named team were found and proven not Secure
/// Record; otherwise <c>null</c>.</param>
/// <param name="TeamId">The configured named team, when identified; otherwise <c>null</c>.</param>
/// <param name="Refusal">Why nothing was identified (a <see cref="DirectMessageOwnerTeamRefusal"/> code: not configured, not
/// found, ambiguous, unreadable, the Secure Record unit or team, or a Secure Record lookup that cannot be decided); <c>null</c>
/// when identified.</param>
/// <param name="Fault">The Dataverse fault, only when a read was unreadable.</param>
public sealed record DirectMessageOwnerTeamIdentity(Guid? BusinessUnitId, Guid? TeamId, string? Refusal, Exception? Fault)
{
    /// <summary>True when both ids are known: this is the team the configuration names.</summary>
    public bool IsIdentified =>
        Refusal is null && BusinessUnitId is { } bu && bu != Guid.Empty && TeamId is { } team && team != Guid.Empty;

    /// <summary>True only when a Direct setting is unset or blank: no Direct team can exist. No ids, no fault.</summary>
    public bool IsNotConfigured => Refusal == DirectMessageOwnerTeamRefusal.NotConfigured;

    internal static DirectMessageOwnerTeamIdentity NotConfigured { get; } =
        new(null, null, DirectMessageOwnerTeamRefusal.NotConfigured, null);
}
