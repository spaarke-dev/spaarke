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
    public async Task<DirectMessageOwnerTeamResolution> ResolveDirectMessageOwnerTeamAsync(CancellationToken ct)
    {
        // Configuration only — no read, nothing cached: these are the cache key.
        var buName = Configured(BusinessUnitNameConfigKey);
        var teamName = Configured(OwnerTeamNameConfigKey);
        if (buName is null || teamName is null)
            return DirectMessageOwnerTeamResolution.Refused(DirectMessageOwnerTeamRefusal.NotConfigured);

        if (string.Equals(buName, SecureRecordOwnerTeam.BusinessUnitName(_configuration), StringComparison.OrdinalIgnoreCase))
            return DirectMessageOwnerTeamResolution.Refused(DirectMessageOwnerTeamRefusal.BusinessUnitIsSecureRecord);

        var lookup = new NamedOwnerTeamLookup(buName, teamName);
        if (Fresh(lookup) is { } cached)
            return cached;

        await _evaluation.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Another caller may have evaluated while this one waited.
            if (Fresh(lookup) is { } evaluated)
                return evaluated;

            var verdict = await EvaluateAsync(lookup, ct).ConfigureAwait(false);
            if (verdict.Fault is null)
                Volatile.Write(ref _cached, new CachedVerdict(lookup, verdict, _time.GetUtcNow()));

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
    /// <c>true</c> when the team's OWN row proves it is not the Direct message owner team: another name, its business unit's
    /// default team, or not an Owner team — none of which <see cref="ResolveDirectMessageOwnerTeamAsync"/> can ever select.
    /// <c>false</c> means "it could be" (the full resolution decides), including an unset configuration, an empty id and a team
    /// row that is not found. One read, not cached. A Dataverse fault propagates.
    /// </summary>
    /// <remarks>Mirrors <c>SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync</c>. Names compare trimmed and
    /// case-insensitively, as Dataverse's own <c>name</c> equality does. Any doubt answers <c>false</c>, which costs only the full
    /// resolution — never a wrong "not the Direct team".</remarks>
    public async Task<bool> CannotBeDirectOwnerTeamAsync(Guid teamId, CancellationToken ct)
    {
        if (teamId == Guid.Empty || Configured(OwnerTeamNameConfigKey) is not { } teamName)
            return false;

        var rows = await _dataverse.QueryAsync<TeamRow>(
            TeamEntitySet, filter: $"teamid eq {teamId}", select: "name,isdefault,teamtype",
            top: 1, cancellationToken: ct).ConfigureAwait(false);
        if (rows.Count == 0)
            return false;

        var team = rows[0];
        if (team.name is { } name && !string.Equals(name.Trim(), teamName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (team.isdefault == true)
            return true;
        return team.teamtype is { } type && type != SecureRecordOwnerTeam.OwnerTeamType;
    }

    /// <summary>The trimmed setting, or <c>null</c> when it is unset or blank (no default).</summary>
    private string? Configured(string key) =>
        _configuration[key] is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    /// <summary>The cached verdict for <paramref name="lookup"/> while it is younger than <see cref="CacheLifetime"/>.</summary>
    private DirectMessageOwnerTeamResolution? Fresh(NamedOwnerTeamLookup lookup)
    {
        var cached = Volatile.Read(ref _cached);
        return cached is not null
               && cached.Lookup == lookup
               && _time.GetUtcNow() - cached.At < CacheLifetime
            ? cached.Verdict
            : null;
    }

    /// <summary>
    /// The checks, in order: the business unit and its named team (the shared lookup's steps 1–2; a not-found name is told
    /// apart from the unit's default team), the unit's place in the tree (not the root, a direct child of the root), then the
    /// shared lookup's invariants (no member, no user). Never throws for a Dataverse fault.
    /// </summary>
    private async Task<DirectMessageOwnerTeamResolution> EvaluateAsync(NamedOwnerTeamLookup lookup, CancellationToken ct)
    {
        var identity = await SecureRecordOwnerTeam.IdentifyNamedAsync(_dataverse, lookup, ct).ConfigureAwait(false);
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
    /// The unit has no NAMED, non-default Owner team with the configured name. When its DEFAULT team carries that name, the
    /// setting names the default team — its own refusal. A fault reading that is still a refusal (not found), not cached.
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
            return DirectMessageOwnerTeamResolution.Refused(DirectMessageOwnerTeamRefusal.OwnerTeamNotFound, ex);
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

    private sealed record CachedVerdict(NamedOwnerTeamLookup Lookup, DirectMessageOwnerTeamResolution Verdict, DateTimeOffset At);

    /// <summary>A business unit's id and parent. Internal so a test can answer the read through <c>QueryAsync</c>.</summary>
    internal sealed class BusinessUnitRow
    {
        [JsonPropertyName("businessunitid")]
        public Guid? businessunitid { get; set; }

        [JsonPropertyName("_parentbusinessunitid_value")]
        public Guid? parentbusinessunitid { get; set; }
    }

    /// <summary>The team columns <see cref="CannotBeDirectOwnerTeamAsync"/> reads. Internal for the same reason.</summary>
    internal sealed class TeamRow
    {
        [JsonPropertyName("name")]
        public string? name { get; set; }

        [JsonPropertyName("isdefault")]
        public bool? isdefault { get; set; }

        [JsonPropertyName("teamtype")]
        public int? teamtype { get; set; }
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

    /// <summary>The configured business unit is the Secure Record business unit.</summary>
    public const string BusinessUnitIsSecureRecord = Prefix + "bu_is_secure_record";

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
/// The answer of <see cref="DirectMessageOwnerTeam.ResolveDirectMessageOwnerTeamAsync"/>: the team, or a refusal.
/// </summary>
/// <param name="TeamId">The Direct message owner team, when resolved.</param>
/// <param name="Refusal">A <see cref="DirectMessageOwnerTeamRefusal"/> code, when not.</param>
/// <param name="Fault">The Dataverse fault behind an unreadable refusal (never cached).</param>
public sealed record DirectMessageOwnerTeamResolution(Guid? TeamId, string? Refusal, Exception? Fault)
{
    /// <summary>True only for a resolved team. Only then may the team be used to assign.</summary>
    public bool IsResolved => Refusal is null && TeamId is { } id && id != Guid.Empty;

    internal static DirectMessageOwnerTeamResolution Resolved(Guid teamId) => new(teamId, null, null);

    internal static DirectMessageOwnerTeamResolution Refused(string refusal, Exception? fault = null) => new(null, refusal, fault);
}
