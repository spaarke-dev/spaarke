using System.Text.Json.Serialization;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// unified-access-control-r2 task 144 (C10 part 1, GitHub #967) — the ONE definition of who owns a secure record:
/// a NAMED, NON-DEFAULT, MEMBERLESS owner team inside a Secure Record business unit that holds NO users.
/// </summary>
/// <remarks>
/// <para><b>Why not the business unit's default team any more.</b> Provisioning used to assign every secure root to
/// the Secure Record BU's DEFAULT owner team and checked only that one such team existed. Dataverse maintains a
/// default team's membership itself, from each user's <c>businessunitid</c>, and it cannot be curated — so an
/// administrator who moved any user into the Secure Record BU (a Change-BU, or registration given that BU's name)
/// silently made that user a member of the team that owns every secure record. Nothing checked. A named team's
/// membership changes only when someone adds a member on purpose, and this type proves it is empty.</para>
///
/// <para><b>Why the BU must also hold no users (hole 2).</b> A record owned by ANY team in the Secure Record BU has
/// that BU as its <c>owningbusinessunit</c>. A user placed in the BU whose ordinary roles carry Business Unit or
/// Deep depth reads every secure record by DEPTH, whoever the owner team is. A named team closes the membership
/// path only; the no-user invariant closes the depth path. Both are checked, and an unreadable answer to either
/// is a refusal, never "zero" (ADR-003).</para>
///
/// <para><b>Who uses this.</b> Provisioning (<c>ProvisionProjectEndpoint</c>) resolves through
/// <see cref="ResolveAsync"/> before any mutation; <c>SecureRecordIsolationCensusJob</c> re-checks the same
/// invariants on a schedule, because a Change-BU happens between provisioning calls and no BFF code can block it
/// (no plugins, ADR-002). <c>RecordOwnershipResolver</c> and the registration guard read the same two config keys
/// through the SDK and raw-HTTP seams they already had. The <c>can-manage-access</c> gate, asked for a record's owner
/// (task 150, round 46 item 4), tells the named team apart from any other owner through <see cref="IdentifyAsync"/> —
/// <see cref="ResolveAsync"/>'s own first two steps.</para>
///
/// <para><b>Placement</b> (CLAUDE.md §10/§11): a static helper beside <see cref="SecureContainerDecision"/>, not a
/// DI-registered service — it is three reads on the <see cref="DataverseWebApiClient"/> the provisioning endpoint
/// already injects, and the config keys it owns were previously duplicated in four places. No new interface (the
/// ADR-010 one-to-one ceiling is at its limit), no new package.</para>
/// </remarks>
public static class SecureRecordOwnerTeam
{
    // ── Configuration ────────────────────────────────────────────────────────

    /// <summary>Configuration key naming the canonical Secure Record business unit.</summary>
    /// <remarks>
    /// A NAME, not a GUID: the BU is created per environment, so no id is stable across tenants, and a customer
    /// who renames the BU must not need a redeploy. Moved here from <c>ProvisionProjectEndpoint</c> (task 144),
    /// where an alias keeps the pinning tests' references stable.
    /// </remarks>
    public const string BusinessUnitNameConfigKey = "SecureRecord:BusinessUnitName";

    /// <summary>Configuration key naming the secure owner team (task 144). Same section as its sibling.</summary>
    public const string OwnerTeamNameConfigKey = "SecureRecord:OwnerTeamName";

    /// <summary>The deployed Secure Record business unit's name (task 121 renamed it from <c>Secure Project</c>).</summary>
    /// <remarks>
    /// 🔴 A FAIL-CLOSED LOOKUP KEY. The live BU, this default, the config key, the pinning test and the operator
    /// runbook (docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md) move TOGETHER, or provisioning stops with
    /// "business unit not found".
    /// </remarks>
    public const string DefaultBusinessUnitName = "Secure Record";

    /// <summary>The secure owner team's name (owner decision F9, 2026-09-30).</summary>
    /// <remarks>
    /// Deliberately distinct from the BU's DEFAULT team, which carries the BU's own name (<c>Secure Record</c>),
    /// so the two can never be confused in MDA or in a census. 🔴 Also a fail-closed lookup key: the live team,
    /// this default, the pinning test and guide §4 move together.
    /// </remarks>
    public const string DefaultOwnerTeamName = "Secure Record Owners";

    /// <summary>Dataverse <c>teamtype</c> for an OWNER team (Owner = 0, Access = 1, Security Group = 2, Office Group = 3).</summary>
    public const int OwnerTeamType = 0;

    /// <summary>How many offending principals a refusal names. Enough to act on; the census job names them all.</summary>
    internal const int NamedPrincipalLimit = 5;

    private const string BusinessUnitEntitySet = "businessunits";
    private const string TeamEntitySet = "teams";
    private const string TeamMembershipEntitySet = "teammemberships";
    private const string SystemUserEntitySet = "systemusers";

    /// <summary>The configured Secure Record business-unit name, else <see cref="DefaultBusinessUnitName"/>.</summary>
    public static string BusinessUnitName(IConfiguration configuration) =>
        ConfiguredOrDefault(configuration, BusinessUnitNameConfigKey, DefaultBusinessUnitName);

    /// <summary>The configured secure owner team name, else <see cref="DefaultOwnerTeamName"/>.</summary>
    public static string OwnerTeamName(IConfiguration configuration) =>
        ConfiguredOrDefault(configuration, OwnerTeamNameConfigKey, DefaultOwnerTeamName);

    private static string ConfiguredOrDefault(IConfiguration configuration, string key, string fallback)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration[key] is { } configured && !string.IsNullOrWhiteSpace(configured)
            ? configured.Trim()
            : fallback;
    }

    // ── OData filters (internal so tests can pin each predicate — ADR-038 A2) ──

    /// <summary>The business unit, by name. Queried with <c>$top=2</c> so ambiguity is visible.</summary>
    internal static string BusinessUnitFilter(string businessUnitName) =>
        $"name eq '{EscapeODataStringLiteral(businessUnitName)}'";

    /// <summary>
    /// The named owner team: same BU, the configured name, an OWNER team, and NOT the default team. Every predicate
    /// is load-bearing — <c>isdefault eq false</c> is what makes it impossible for the retired default team to be
    /// selected even if someone renamed it to the configured name.
    /// </summary>
    internal static string NamedOwnerTeamFilter(Guid businessUnitId, string ownerTeamName) =>
        $"_businessunitid_value eq {businessUnitId} and name eq '{EscapeODataStringLiteral(ownerTeamName)}' " +
        $"and teamtype eq {OwnerTeamType} and isdefault eq false";

    /// <summary>Every membership row of the team, of ANY principal kind (human or application user).</summary>
    internal static string TeamMembershipFilter(Guid teamId) => $"teamid eq {teamId}";

    /// <summary>
    /// Every systemuser in the BU — enabled or disabled, human or application. No other predicate, deliberately:
    /// a disabled user can be re-enabled, and an application user reads by depth like anyone else.
    /// </summary>
    internal static string BusinessUnitUsersFilter(Guid businessUnitId) => $"_businessunitid_value eq {businessUnitId}";

    /// <summary>
    /// Resolves the Secure Record business unit and its named owner team, and proves the team has no members and
    /// the business unit no users. Any unreadable or ambiguous answer is a refusal (ADR-003). Never throws for a
    /// Dataverse fault — the fault is carried on the result so the caller can name it in its own error contract.
    /// </summary>
    public static async Task<SecureOwnerTeamResolution> ResolveAsync(
        DataverseWebApiClient dataverseClient,
        IConfiguration configuration,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dataverseClient);

        // 1 + 2. WHICH team: the business unit by name, then its named owner team (IdentifyAsync — one rule for every
        //        caller that needs to know which team that is).
        var identity = await IdentifyAsync(dataverseClient, configuration, ct).ConfigureAwait(false);
        var result = new SecureOwnerTeamResolution(
            identity.Refusal ?? SecureOwnerTeamStatus.BusinessUnitUnreadable, identity.BusinessUnitName,
            identity.BusinessUnitId, identity.OwnerTeamName, identity.OwnerTeamId,
            Array.Empty<Guid>(), Array.Empty<Guid>(), identity.Fault);
        if (!identity.IsIdentified)
            return result;

        var teamId = identity.OwnerTeamId!.Value;
        var buId = identity.BusinessUnitId!.Value;

        // 3. Zero members, of any principal kind. An unreadable count is NOT zero.
        List<IdRow> members;
        try
        {
            members = await dataverseClient.QueryAsync<IdRow>(
                TeamMembershipEntitySet, filter: TeamMembershipFilter(teamId), select: "systemuserid",
                top: NamedPrincipalLimit, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return result with { Status = SecureOwnerTeamStatus.OwnerTeamMembershipUnreadable, Fault = ex };
        }

        if (members.Count > 0)
        {
            return result with
            {
                Status = SecureOwnerTeamStatus.OwnerTeamHasMembers,
                MemberIds = members.Select(m => m.systemuserid ?? Guid.Empty).ToArray()
            };
        }

        // 4. Zero systemusers in the business unit — enabled or disabled, human or application.
        List<IdRow> users;
        try
        {
            users = await dataverseClient.QueryAsync<IdRow>(
                SystemUserEntitySet, filter: BusinessUnitUsersFilter(buId), select: "systemuserid",
                top: NamedPrincipalLimit, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return result with { Status = SecureOwnerTeamStatus.BusinessUnitUsersUnreadable, Fault = ex };
        }

        if (users.Count > 0)
        {
            return result with
            {
                Status = SecureOwnerTeamStatus.BusinessUnitHasUsers,
                BusinessUnitUserIds = users.Select(u => u.systemuserid ?? Guid.Empty).ToArray()
            };
        }

        return result with { Status = SecureOwnerTeamStatus.Resolved };
    }

    /// <summary>
    /// WHICH team is the Secure Record owner team — the business unit by its configured name, then its NAMED, non-default
    /// owner team — WITHOUT the two invariants <see cref="ResolveAsync"/> also proves (no members, no users). For a caller
    /// that must tell that team apart from another owner (task 150, round 46 item 4: the Access ribbon's "finished?"
    /// question), not one that assigns to it: only <see cref="ResolveAsync"/>'s <see cref="SecureOwnerTeamStatus.Resolved"/>
    /// may be used to assign. Steps 1 and 2 of <see cref="ResolveAsync"/>, which calls this — one rule, never a copy.
    /// </summary>
    /// <remarks>Never throws for a Dataverse fault: an unreadable, absent or ambiguous answer is a refusal carried on the
    /// result (<see cref="SecureOwnerTeamIdentity.Refusal"/>), with the fault. Ambiguity is never resolved by picking one.</remarks>
    internal static async Task<SecureOwnerTeamIdentity> IdentifyAsync(
        DataverseWebApiClient dataverseClient,
        IConfiguration configuration,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dataverseClient);

        var buName = BusinessUnitName(configuration);
        var teamName = OwnerTeamName(configuration);
        var identity = new SecureOwnerTeamIdentity(
            SecureOwnerTeamStatus.BusinessUnitUnreadable, buName, null, teamName, null, null);

        // 1. The business unit, by name. $top=2: one row is the answer, two make ambiguity a state we can refuse.
        List<IdRow> businessUnits;
        try
        {
            businessUnits = await dataverseClient.QueryAsync<IdRow>(
                BusinessUnitEntitySet, filter: BusinessUnitFilter(buName), select: "businessunitid,name",
                top: 2, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return identity with { Refusal = SecureOwnerTeamStatus.BusinessUnitUnreadable, Fault = ex };
        }

        if (businessUnits.Count == 0)
            return identity with { Refusal = SecureOwnerTeamStatus.BusinessUnitNotFound };
        if (businessUnits.Count > 1)
            return identity with { Refusal = SecureOwnerTeamStatus.BusinessUnitAmbiguous };
        if (businessUnits[0].businessunitid is not { } buId || buId == Guid.Empty)
            return identity with { Refusal = SecureOwnerTeamStatus.BusinessUnitNotFound };

        identity = identity with { BusinessUnitId = buId };

        // 2. The NAMED owner team. Never the default team: isdefault eq false is in the filter, and a missing or
        //    duplicated named team refuses rather than falling back to anything.
        List<IdRow> teams;
        try
        {
            teams = await dataverseClient.QueryAsync<IdRow>(
                TeamEntitySet, filter: NamedOwnerTeamFilter(buId, teamName), select: "teamid,name",
                top: 2, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return identity with { Refusal = SecureOwnerTeamStatus.OwnerTeamUnreadable, Fault = ex };
        }

        if (teams.Count == 0)
            return identity with { Refusal = SecureOwnerTeamStatus.OwnerTeamNotFound };
        if (teams.Count > 1)
            return identity with { Refusal = SecureOwnerTeamStatus.OwnerTeamAmbiguous };
        if (teams[0].teamid is not { } teamId || teamId == Guid.Empty)
            return identity with { Refusal = SecureOwnerTeamStatus.OwnerTeamNotFound };

        return identity with { Refusal = null, OwnerTeamId = teamId, OwnerTeamName = teams[0].name ?? teamName };
    }

    /// <summary>
    /// Whether a record whose <c>owningbusinessunit</c> is <paramref name="owningBusinessUnitId"/> is owned INSIDE the Secure
    /// Record business unit <paramref name="secureBusinessUnitId"/> — by the named owner team or by any OTHER team there (in
    /// practice the retired default team, before task 144's migration). <c>null</c> when the record was read without its
    /// owning business unit: that cannot be told. The ONE rule behind provisioning's <c>owned_by_other_secure_team</c>
    /// refusal and the <c>can-manage-access</c> owner answer (task 150, round 53 item 2), so the Access ribbon hides Make
    /// Secure on exactly the records that refusal would answer.
    /// </summary>
    internal static bool? IsInSecureBusinessUnit(Guid? owningBusinessUnitId, Guid secureBusinessUnitId) =>
        owningBusinessUnitId is { } bu && bu != Guid.Empty ? bu == secureBusinessUnitId : null;

    /// <summary>Escapes a string for an OData single-quoted literal.</summary>
    internal static string EscapeODataStringLiteral(string value) => value.Replace("'", "''");

    /// <summary>
    /// The columns the four reads project. One DTO; each read fills the columns it selected. Internal (not private) so a
    /// test can answer the reads through the client's virtual <c>QueryAsync</c> seam (ADR-038 §4).
    /// </summary>
    internal sealed class IdRow
    {
        [JsonPropertyName("businessunitid")]
        public Guid? businessunitid { get; set; }

        [JsonPropertyName("teamid")]
        public Guid? teamid { get; set; }

        [JsonPropertyName("systemuserid")]
        public Guid? systemuserid { get; set; }

        [JsonPropertyName("name")]
        public string? name { get; set; }
    }
}

/// <summary>Why <see cref="SecureRecordOwnerTeam.ResolveAsync"/> did or did not resolve an owner team.</summary>
public enum SecureOwnerTeamStatus
{
    /// <summary>One BU, one named owner team, zero members, zero BU users. The only usable outcome.</summary>
    Resolved,

    /// <summary>No business unit has the configured name.</summary>
    BusinessUnitNotFound,

    /// <summary>More than one business unit has the configured name.</summary>
    BusinessUnitAmbiguous,

    /// <summary>The business-unit read faulted.</summary>
    BusinessUnitUnreadable,

    /// <summary>No non-default owner team with the configured name exists in the BU.</summary>
    OwnerTeamNotFound,

    /// <summary>More than one matches — refused rather than choosing one.</summary>
    OwnerTeamAmbiguous,

    /// <summary>The team read faulted.</summary>
    OwnerTeamUnreadable,

    /// <summary>The named team has at least one member (human or application user).</summary>
    OwnerTeamHasMembers,

    /// <summary>The membership read faulted — NOT read as zero.</summary>
    OwnerTeamMembershipUnreadable,

    /// <summary>At least one systemuser (any kind) sits in the Secure Record BU.</summary>
    BusinessUnitHasUsers,

    /// <summary>The BU-user read faulted — NOT read as zero.</summary>
    BusinessUnitUsersUnreadable
}

/// <summary>
/// The outcome of <see cref="SecureRecordOwnerTeam.IdentifyAsync"/>: WHICH team is the Secure Record owner team, or why it
/// could not be told (a <see cref="SecureOwnerTeamStatus"/> refusal of steps 1–2, with the fault behind an unreadable one).
/// Not an assignment target: the invariants are <see cref="SecureRecordOwnerTeam.ResolveAsync"/>'s.
/// </summary>
internal sealed record SecureOwnerTeamIdentity(
    SecureOwnerTeamStatus? Refusal,
    string BusinessUnitName,
    Guid? BusinessUnitId,
    string OwnerTeamName,
    Guid? OwnerTeamId,
    Exception? Fault)
{
    /// <summary>True when exactly one business unit and exactly one named owner team were found.</summary>
    public bool IsIdentified =>
        Refusal is null && BusinessUnitId is { } bu && bu != Guid.Empty && OwnerTeamId is { } team && team != Guid.Empty;
}

/// <summary>The outcome of <see cref="SecureRecordOwnerTeam.ResolveAsync"/>.</summary>
/// <param name="Status">The verdict. Only <see cref="SecureOwnerTeamStatus.Resolved"/> may be used to assign.</param>
/// <param name="BusinessUnitName">The configured BU name that was looked up.</param>
/// <param name="BusinessUnitId">The resolved BU, when step 1 succeeded.</param>
/// <param name="OwnerTeamName">The configured team name, or the resolved team's own name.</param>
/// <param name="OwnerTeamId">The resolved named team, when step 2 succeeded.</param>
/// <param name="MemberIds">Members found (up to the named-principal limit) when the team is not empty.</param>
/// <param name="BusinessUnitUserIds">Users found (up to the limit) when the BU is not empty.</param>
/// <param name="Fault">The Dataverse fault behind an <c>*Unreadable</c> status.</param>
public sealed record SecureOwnerTeamResolution(
    SecureOwnerTeamStatus Status,
    string BusinessUnitName,
    Guid? BusinessUnitId,
    string OwnerTeamName,
    Guid? OwnerTeamId,
    IReadOnlyList<Guid> MemberIds,
    IReadOnlyList<Guid> BusinessUnitUserIds,
    Exception? Fault)
{
    /// <summary>True only for <see cref="SecureOwnerTeamStatus.Resolved"/>.</summary>
    public bool IsResolved => Status == SecureOwnerTeamStatus.Resolved && OwnerTeamId is { } id && id != Guid.Empty;
}
