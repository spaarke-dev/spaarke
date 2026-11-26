using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using static Sprk.Bff.Api.Services.ExternalAccess.SecureRecordIsolationCensus;

namespace Sprk.Bff.Api.Services.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 191e (#2179 design v2, sections 3.2, 3.8, 3.13, 3.15): where a systemuser flagged
/// <c>sprk_isexternal = true</c> must live, read from Dataverse. It holds the directory reads that
/// <see cref="FlaggedUserPlacementJob"/> acts on and the placement-drift census that
/// <see cref="SecureRecordIsolationCensusJob"/> logs.
/// </summary>
/// <remarks>
/// <para><b>The invariant.</b> Every enabled, human, flagged systemuser sits in the <see cref="BusinessUnitName"/> business
/// unit (a direct child of the root that owns no rows) and holds exactly one role, <see cref="RoleName"/>, assigned from
/// that unit, at Basic depth on every lineage table and without <c>prvWriteUser</c>. It is a member of no owner team but
/// the unit's default team, owns no lineage row, and manages nobody while hierarchy security is on. The unit's default
/// team owns no lineage row and holds no role. <c>config/external-collaborator-role.json</c> (task 191d) is the definition
/// of the unit and the role; the two names below are pinned to it by a test.</para>
/// <para><b>Model 1 (BINDING, project CLAUDE.md §3).</b> "External" is ONLY a stored <c>sprk_isexternal</c> that reads
/// <c>true</c>. Blank, false, masked or unreadable is internal. Nothing here looks at a UPN, an email domain, a
/// <c>#EXT#</c> guest marker or a licence: a Model 1 customer's licensed core users are B2B guests and are not external.</para>
/// <para><b>Read-only, app-only, ids only.</b> Every read is the BFF's own (<see cref="IGenericEntityService"/>); none
/// selects a name, email or UPN of a user, so no finding or log line can carry one. A read that fails or is incomplete
/// throws (<see cref="SecureRecordIsolationCensus.RetrieveAllAsync"/>): nothing is graded or moved on a partial read.</para>
/// <para><b>Placement and §11</b>: notes/task-191e-flagged-user-placement.md. A static class, as
/// <see cref="SecureRecordIsolationCensus"/> is: both callers already hold the Dataverse seam, so no DI registration or
/// interface is added (ADR-010).</para>
/// </remarks>
internal static class ExternalCollaboratorPlacement
{
    /// <summary>The unit every flagged user lives in (<c>config/external-collaborator-role.json</c> businessUnit.name).</summary>
    internal const string BusinessUnitName = "External Collaborators";

    /// <summary>The one role a flagged user holds, assigned from that unit (<c>config/external-collaborator-role.json</c> roleName).</summary>
    internal const string RoleName = "Spaarke External Collaborator";

    /// <summary>The stored flag. Only a value that reads <c>true</c> is external (round 67).</summary>
    internal const string IsExternalColumn = "sprk_isexternal";

    /// <summary>The systemuser ↔ role relationship a role assignment writes.</summary>
    internal const string UserRoleRelationship = "systemuserroles_association";

    /// <summary>Ids per <c>In</c> condition.</summary>
    internal const int InChunkSize = 200;

    /// <summary>Rows read per owner chunk and table by the ownership check; one more is reported as "at least".</summary>
    internal const int OwnershipPageSize = 500;

    /// <summary>The access types a lineage-table privilege can carry (<c>prv{Access}{table}</c>).</summary>
    internal static readonly IReadOnlyList<string> AccessTypes =
        ["Read", "Write", "Append", "AppendTo", "Create", "Delete", "Share", "Assign"];

    /// <summary>The privilege that lets a user edit systemuser rows, its own flag included.</summary>
    internal const string WriteUserPrivilege = "prvWriteUser";

    /// <summary>Team type of an access team (record-level, template-masked): not an owner team.</summary>
    private const int AccessTeamType = 1;

    /// <summary>The columns every flagged-user read selects. Never a name, email or UPN.</summary>
    internal static readonly string[] FlaggedUserColumns =
        ["systemuserid", "businessunitid", "isdisabled", "applicationid", "accessmode", IsExternalColumn];

    // =====================================================================================================
    // Shared reads
    // =====================================================================================================

    /// <summary>Finds the unit: exactly one business unit of that name, a direct child of the root.</summary>
    /// <remarks>Two or more units of the name, or one that is not under the root, is <see cref="UnitState.Ambiguous"/>:
    /// which unit isolates flagged users cannot be decided, so nobody is moved.</remarks>
    internal static async Task<UnitResolution> ResolveUnitAsync(IGenericEntityService dataverse, CancellationToken ct)
    {
        var namedQuery = Query("businessunit", "businessunitid", "name", "parentbusinessunitid");
        namedQuery.Criteria.AddCondition("name", ConditionOperator.Equal, BusinessUnitName);
        var named = (await RetrieveAllAsync(dataverse, namedQuery, ct).ConfigureAwait(false))
            .Where(row => string.Equals(row.GetAttributeValue<string>("name"), BusinessUnitName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (named.Count == 0)
            return new UnitResolution(UnitState.Missing, null, []);

        var rootQuery = Query("businessunit", "businessunitid", "parentbusinessunitid");
        rootQuery.Criteria.AddCondition("parentbusinessunitid", ConditionOperator.Null);
        var roots = (await RetrieveAllAsync(dataverse, rootQuery, ct).ConfigureAwait(false))
            .Where(row => OptionalGuid(row, "parentbusinessunitid") is null)
            .Select(row => row.Id)
            .ToHashSet();
        if (roots.Count != 1)
        {
            throw new InvalidOperationException(
                $"The root business unit could not be identified ({roots.Count} units have no parent), so where '{BusinessUnitName}' " +
                "sits cannot be checked.");
        }

        var ids = named.Select(row => row.Id).ToArray();
        return named.Count == 1 && OptionalGuid(named[0], "parentbusinessunitid") is { } parent && roots.Contains(parent)
            ? new UnitResolution(UnitState.Found, named[0].Id, ids)
            : new UnitResolution(UnitState.Ambiguous, null, ids);
    }

    /// <summary>Finds the role copy that lives IN the unit (task 191d D-4). Anything but exactly one is unresolved.</summary>
    internal static async Task<RoleResolution> ResolveRoleAsync(IGenericEntityService dataverse, Guid unitId, CancellationToken ct)
    {
        var query = Query("role", "roleid", "name", "businessunitid");
        query.Criteria.AddCondition("name", ConditionOperator.Equal, RoleName);
        query.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, unitId);
        var rows = (await RetrieveAllAsync(dataverse, query, ct).ConfigureAwait(false))
            .Where(row => string.Equals(row.GetAttributeValue<string>("name"), RoleName, StringComparison.OrdinalIgnoreCase)
                          && OptionalGuid(row, "businessunitid") == unitId)
            .ToList();

        return rows.Count switch
        {
            1 => new RoleResolution(UnitState.Found, rows[0].Id),
            0 => new RoleResolution(UnitState.Missing, null),
            _ => new RoleResolution(UnitState.Ambiguous, null),
        };
    }

    /// <summary>Every business unit's copy of System Administrator, matched by template id (never by name).</summary>
    /// <exception cref="InvalidOperationException">No copy was returned: administrators cannot be told apart.</exception>
    internal static async Task<IReadOnlySet<Guid>> AdministratorRoleIdsAsync(IGenericEntityService dataverse, CancellationToken ct)
    {
        var template = SecuredColumnReadProof.SystemAdministratorRoleTemplateId;
        var query = Query("role", "roleid", "roletemplateid");
        query.Criteria.AddCondition("roletemplateid", ConditionOperator.Equal, template);
        var ids = (await RetrieveAllAsync(dataverse, query, ct).ConfigureAwait(false))
            .Where(row => OptionalGuid(row, "roletemplateid") == template)
            .Select(row => row.Id)
            .ToHashSet();

        return ids.Count > 0
            ? ids
            : throw new InvalidOperationException(
                "No System Administrator role copy was returned, so administrators cannot be told apart from other users.");
    }

    /// <summary>
    /// Every systemuser whose stored flag reads <c>true</c>. The filter is applied by Dataverse AND again here, so a value
    /// that is blank, false or masked (an absent attribute) never makes a user external.
    /// </summary>
    internal static async Task<IReadOnlyList<FlaggedUser>> ReadFlaggedUsersAsync(
        IGenericEntityService dataverse, IReadOnlyCollection<string> extraColumns, CancellationToken ct)
    {
        var query = Query("systemuser", FlaggedUserColumns.Concat(extraColumns).Distinct(StringComparer.Ordinal).ToArray());
        query.Criteria.AddCondition(IsExternalColumn, ConditionOperator.Equal, true);
        return (await RetrieveAllAsync(dataverse, query, ct).ConfigureAwait(false))
            .Select(FlaggedUser.From)
            .Where(user => user.IsDefinitelyExternal)
            .ToList();
    }

    /// <summary>The users of the unit whose stored flag does NOT read true (reported for an administrator; never moved).</summary>
    internal static async Task<IReadOnlyList<Guid>> ReadUnflaggedUsersInUnitAsync(
        IGenericEntityService dataverse, Guid unitId, CancellationToken ct)
    {
        var query = Query("systemuser", "systemuserid", "businessunitid", IsExternalColumn);
        query.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, unitId);
        return (await RetrieveAllAsync(dataverse, query, ct).ConfigureAwait(false))
            .Select(FlaggedUser.From)
            .Where(user => user.BusinessUnitId == unitId && !user.IsDefinitelyExternal)
            .Select(user => user.Id)
            .ToList();
    }

    /// <summary>One user's state as read now (the job's re-read before it writes, and its read-back).</summary>
    internal static async Task<FlaggedUser> ReadUserAsync(IGenericEntityService dataverse, Guid userId, CancellationToken ct) =>
        FlaggedUser.From(await dataverse.RetrieveAsync("systemuser", userId, FlaggedUserColumns, ct).ConfigureAwait(false));

    /// <summary>Directly assigned role copies per user.</summary>
    internal static Task<Dictionary<Guid, List<Guid>>> ReadDirectRolesAsync(
        IGenericEntityService dataverse, IReadOnlyCollection<Guid> userIds, CancellationToken ct) =>
        ReadPairsAsync(dataverse, "systemuserroles", "systemuserid", "roleid", userIds, ct);

    /// <summary>Team memberships per user.</summary>
    internal static Task<Dictionary<Guid, List<Guid>>> ReadMembershipsAsync(
        IGenericEntityService dataverse, IReadOnlyCollection<Guid> userIds, CancellationToken ct) =>
        ReadPairsAsync(dataverse, "teammembership", "systemuserid", "teamid", userIds, ct);

    /// <summary>Role copies per team.</summary>
    internal static Task<Dictionary<Guid, List<Guid>>> ReadTeamRolesAsync(
        IGenericEntityService dataverse, IReadOnlyCollection<Guid> teamIds, CancellationToken ct) =>
        ReadPairsAsync(dataverse, "teamroles", "teamid", "roleid", teamIds, ct);

    // =====================================================================================================
    // The placement-drift census (design 3.8 / 3.15): one CRITICAL finding per drift
    // =====================================================================================================

    /// <summary>
    /// Reads the directory and grades every enabled, human, flagged user and the unit's default team against the
    /// invariant (class remarks). Throws when any read fails or is incomplete; a census that could not be read is never
    /// reported clean.
    /// </summary>
    internal static async Task<PlacementCensusOutcome> EvaluateCensusAsync(IGenericEntityService dataverse, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dataverse);

        var unit = await ResolveUnitAsync(dataverse, ct).ConfigureAwait(false);
        var flagged = await ReadFlaggedUsersAsync(dataverse, ["positionid"], ct).ConfigureAwait(false);

        // The same "human" test the secure census uses: application users, support and non-interactive accounts and
        // disabled users are not graded (a disabled user cannot sign in; the placement job never moves one either).
        var humans = flagged.Where(user => SecureBuRoleDepthCensusBuilder.IsHumanPrincipal(
                user.HasApplicationId, user.IsDisabled ?? false, user.AccessMode ?? 0))
            .ToList();

        if (unit.State == UnitState.Missing)
        {
            // Inert, not graded: the unit is created by task 191i (owner go). Until then flagged users are where the
            // design's section 1 found them, and the job has nowhere to move them.
            return new PlacementCensusOutcome(PlacementCensusStatus.Inert, [], humans.Count);
        }

        if (unit.State == UnitState.Ambiguous)
        {
            return new PlacementCensusOutcome(PlacementCensusStatus.Findings,
            [
                new PlacementFinding(PlacementDriftKind.UnitAmbiguous, Guid.Empty,
                    $"{unit.Candidates.Count} business unit(s) are named '{BusinessUnitName}' ({string.Join(", ", unit.Candidates)}), " +
                    "or the one there is not a direct child of the root: which unit isolates flagged users cannot be decided, so no " +
                    "flagged user is moved and placement is not graded. Keep exactly one, directly under the root."),
            ], humans.Count);
        }

        var unitId = unit.UnitId!.Value;
        var role = await ResolveRoleAsync(dataverse, unitId, ct).ConfigureAwait(false);
        var defaultTeamIds = await ReadDefaultTeamIdsAsync(dataverse, unitId, ct).ConfigureAwait(false);
        var findings = new List<PlacementFinding>();

        if (role.State != UnitState.Found)
        {
            // Without exactly one role copy in the unit the job places nobody, so say so once; the per-user role findings
            // below then name every role a flagged user holds.
            findings.Add(new PlacementFinding(PlacementDriftKind.RoleUnresolved, Guid.Empty,
                role.State == UnitState.Missing
                    ? $"business unit '{BusinessUnitName}' ({unitId}) has no role '{RoleName}': no flagged user can be placed. " +
                      "Task 191d's script creates it."
                    : $"business unit '{BusinessUnitName}' ({unitId}) has more than one role '{RoleName}': which one a flagged user " +
                      "holds cannot be decided, so no flagged user is placed. Keep exactly one."));
        }

        if (humans.Count == 0 && defaultTeamIds.Count == 0)
        {
            return new PlacementCensusOutcome(
                findings.Count == 0 ? PlacementCensusStatus.Clean : PlacementCensusStatus.Findings, findings, 0);
        }

        var humanIds = humans.Select(user => user.Id).ToArray();

        // ── Roles held: directly, and through every team the user is in (the unit's default team included) ──
        var directRoles = await ReadDirectRolesAsync(dataverse, humanIds, ct).ConfigureAwait(false);
        var memberships = await ReadMembershipsAsync(dataverse, humanIds, ct).ConfigureAwait(false);
        var teamIds = memberships.Values.SelectMany(ids => ids).Concat(defaultTeamIds).ToHashSet();
        var teams = await ReadTeamsAsync(dataverse, teamIds, ct).ConfigureAwait(false);
        var teamRoles = await ReadTeamRolesAsync(dataverse, teamIds, ct).ConfigureAwait(false);
        var roleIds = directRoles.Values.SelectMany(ids => ids).Concat(teamRoles.Values.SelectMany(ids => ids)).ToHashSet();
        var roles = await ReadRolesAsync(dataverse, roleIds, ct).ConfigureAwait(false);
        var administrators = await AdministratorRoleIdsAsync(dataverse, ct).ConfigureAwait(false);

        // ── 1. Outside the unit ──
        // The job's own rule (FlaggedUserPlacementJob.Classify) decides whether turning writes on would move the user, so the
        // census never promises a move the job will not make.
        foreach (var user in humans.Where(user => user.BusinessUnitId != unitId))
        {
            var teamRoleIds = memberships.GetValueOrDefault(user.Id, [])
                .SelectMany(teamId => teamRoles.GetValueOrDefault(teamId, []))
                .ToList();
            var decision = FlaggedUserPlacementJob.Classify(
                user, unitId, role.RoleId, directRoles.GetValueOrDefault(user.Id, []), teamRoleIds, administrators);
            var why = decision switch
            {
                PlacementDecision.SkipAdministrator => "administrator",
                PlacementDecision.SkipAccessMode => "non-interactive",
                PlacementDecision.SkipUnreadable => "unreadable",
                _ => null,
            };

            findings.Add(new PlacementFinding(PlacementDriftKind.FlaggedUserOutsideExternalUnit, user.Id,
                $"flagged systemuser {user.Id} is in business unit {Describe(user.BusinessUnitId)}, not '{BusinessUnitName}' " +
                $"({unitId}): role depth and that unit's default team reach internal rows. " +
                (why is null
                    ? $"{FlaggedUserPlacementJob.WritesEnabledConfigKey} on lets the placement job move it."
                    : $"The placement job never moves this user ({why}): clear the {IsExternalColumn} flag or the role.")));
        }

        // ── 2. A role other than THE role from the unit, held directly ──
        foreach (var user in humans)
        {
            foreach (var roleId in directRoles.GetValueOrDefault(user.Id, []).Where(id => id != role.RoleId))
            {
                findings.Add(RoleFinding(user.Id, $"flagged systemuser {user.Id} holds", roleId, roles, unitId));
            }
        }

        // ── 2b. A role held by the unit's default team: every flagged user gets it ──
        foreach (var teamId in defaultTeamIds)
        {
            foreach (var roleId in teamRoles.GetValueOrDefault(teamId, []))
            {
                findings.Add(RoleFinding(teamId, $"the '{BusinessUnitName}' default team {teamId} holds (so every flagged user holds)",
                    roleId, roles, unitId));
            }
        }

        // ── 3. Another owner team ──
        foreach (var user in humans)
        {
            foreach (var teamId in memberships.GetValueOrDefault(user.Id, []).Where(id => !defaultTeamIds.Contains(id)))
            {
                var team = teams.GetValueOrDefault(teamId);
                if (team?.TeamType == AccessTeamType)
                    continue; // record-level access team: a share, decided by the share writers (design 3.4).

                findings.Add(new PlacementFinding(PlacementDriftKind.OtherOwnerTeamMembership, user.Id,
                    $"flagged systemuser {user.Id} is a member of team {teamId} ('{team?.Name ?? "unreadable"}', type " +
                    $"{team?.TeamType.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}, business unit " +
                    $"{Describe(team?.BusinessUnitId)}): rows that team owns, and its roles, reach the user. Remove the membership."));
            }
        }

        // ── 4 and 5. Depth above Basic on a lineage table, and prvWriteUser — on EVERY role held, THE role included ──
        var privileges = await ReadLineagePrivilegesAsync(dataverse, ct).ConfigureAwait(false);
        var (presentTables, ungradedTables) = LineageTablesIn(privileges.Values);
        var depths = await ReadDepthsAsync(dataverse, roles, privileges.Keys, ct).ConfigureAwait(false);
        foreach (var user in humans)
        {
            // Through teams: every team the user is a member of. A user in the unit is a member of its default team (a
            // teammembership row), so that team's roles count for exactly the users it reaches.
            var held = directRoles.GetValueOrDefault(user.Id, [])
                .Concat(memberships.GetValueOrDefault(user.Id, []).SelectMany(teamId => teamRoles.GetValueOrDefault(teamId, [])))
                .Distinct();
            foreach (var roleId in held)
            {
                var wide = new List<string>();
                var writeUser = false;
                foreach (var (privilegeId, name) in privileges)
                {
                    var depth = DepthOf(depths, roles.GetValueOrDefault(roleId), roleId, privilegeId);
                    if (depth == 0)
                        continue;

                    if (string.Equals(name, WriteUserPrivilege, StringComparison.OrdinalIgnoreCase))
                        writeUser = true;
                    else if (depth > (int)PrivilegeDepth.Basic)
                        wide.Add($"{name}={DepthName(depth)}");
                }

                var roleName = roles.GetValueOrDefault(roleId)?.Name ?? "unreadable";
                if (wide.Count > 0)
                {
                    wide.Sort(StringComparer.Ordinal);
                    findings.Add(new PlacementFinding(PlacementDriftKind.LineageDepthAboveBasic, user.Id,
                        $"flagged systemuser {user.Id} holds role {roleId} ('{roleName}') with depth above Basic on lineage tables: " +
                        $"{string.Join(", ", wide)}. A flagged user reaches a lineage row only through a share."));
                }

                if (writeUser)
                {
                    findings.Add(new PlacementFinding(PlacementDriftKind.WriteUserPrivilege, user.Id,
                        $"flagged systemuser {user.Id} holds role {roleId} ('{roleName}') with {WriteUserPrivilege}: it can clear its " +
                        $"own {IsExternalColumn} flag."));
                }
            }
        }

        // ── 6. A lineage row owned by a flagged user or by the unit's default team ──
        //    Only tables the environment has (a privilege of the table was returned): querying a table that does not exist
        //    throws, and an environment may legitimately lack a codified table (the secure census grades that as a coverage
        //    gap, not a failure). A missing table has no rows to own; it is listed as ungraded.
        await AddOwnershipFindingsAsync(dataverse, presentTables, humanIds.Concat(defaultTeamIds).ToHashSet(), defaultTeamIds,
            findings, ct).ConfigureAwait(false);

        // ── 7. Hierarchy security with a flagged manager ──
        await AddHierarchyFindingsAsync(dataverse, humans, findings, ct).ConfigureAwait(false);

        return new PlacementCensusOutcome(
            findings.Count == 0 ? PlacementCensusStatus.Clean : PlacementCensusStatus.Findings, findings, humans.Count)
        {
            UngradedTables = ungradedTables,
        };
    }

    /// <summary>
    /// The lineage-registry tables split by whether the environment returned any privilege of theirs (so the table exists):
    /// present ones are graded, the rest listed as ungraded.
    /// </summary>
    private static (IReadOnlyList<string> Present, IReadOnlyList<string> Ungraded) LineageTablesIn(IEnumerable<string> privilegeNames)
    {
        var names = privilegeNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var present = new List<string>();
        var ungraded = new List<string>();
        foreach (var table in LineageTables())
        {
            (AccessTypes.Any(access => names.Contains($"prv{access}{table}")) ? present : ungraded).Add(table);
        }

        return (present, ungraded);
    }

    private static IEnumerable<string> LineageTables() =>
        SecureChildLineage.Roots.Concat(SecureChildLineage.Children.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.Ordinal);

    private static PlacementFinding RoleFinding(
        Guid principalId, string holder, Guid roleId, IReadOnlyDictionary<Guid, CensusRole> roles, Guid unitId)
    {
        var role = roles.GetValueOrDefault(roleId);
        var fromAnotherUnit = role is null || role.BusinessUnitId != unitId;
        return new PlacementFinding(
            fromAnotherUnit ? PlacementDriftKind.RoleFromAnotherUnit : PlacementDriftKind.UnexpectedRole,
            principalId,
            fromAnotherUnit
                ? $"{holder} role {roleId} ('{role?.Name ?? "unreadable"}') assigned from business unit {Describe(role?.BusinessUnitId)}, " +
                  $"not '{BusinessUnitName}' ({unitId}): its depth is anchored in that unit and reopens it (matrix business units). " +
                  "Remove the assignment."
                : $"{holder} role {roleId} ('{role!.Name}') from '{BusinessUnitName}', which is not '{RoleName}': a flagged user holds " +
                  "that one role only. Remove the assignment.");
    }

    private static async Task AddOwnershipFindingsAsync(
        IGenericEntityService dataverse, IReadOnlyList<string> tables, IReadOnlySet<Guid> owners, IReadOnlySet<Guid> teamOwners,
        List<PlacementFinding> findings, CancellationToken ct)
    {
        if (owners.Count == 0)
            return;

        foreach (var table in tables)
        {
            var counts = new Dictionary<Guid, int>();
            var atLeast = false;
            foreach (var chunk in owners.Chunk(InChunkSize))
            {
                var query = new QueryExpression(table)
                {
                    ColumnSet = new ColumnSet("ownerid"),
                    NoLock = true,
                    PageInfo = new PagingInfo { Count = OwnershipPageSize, PageNumber = 1 },
                };
                query.Criteria.AddCondition("ownerid", ConditionOperator.In, chunk.Cast<object>().ToArray());

                ct.ThrowIfCancellationRequested();
                var page = await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
                atLeast |= page.MoreRecords;
                foreach (var row in page.Entities)
                {
                    if (OptionalGuid(row, "ownerid") is { } owner && owners.Contains(owner))
                        counts[owner] = counts.GetValueOrDefault(owner) + 1;
                }
            }

            foreach (var (owner, count) in counts.OrderBy(pair => pair.Key))
            {
                var who = teamOwners.Contains(owner)
                    ? $"the '{BusinessUnitName}' default team {owner}"
                    : $"flagged systemuser {owner}";
                findings.Add(new PlacementFinding(PlacementDriftKind.LineageRowOwnedByFlaggedPrincipal, owner,
                    $"{who} owns {(atLeast ? "at least " : string.Empty)}{count} {table} row(s): ownership gives access at any depth, " +
                    "whatever is shared. Re-own them through RecordOwnershipResolver to their I-6 team (task 191c)."));
            }
        }
    }

    private static async Task AddHierarchyFindingsAsync(
        IGenericEntityService dataverse, IReadOnlyList<FlaggedUser> humans, List<PlacementFinding> findings, CancellationToken ct)
    {
        if (humans.Count == 0)
            return;

        var organizations = await RetrieveAllAsync(dataverse,
            Query("organization", "organizationid", "ishierarchicalsecuritymodelenabled", "usepositionhierarchy"), ct).ConfigureAwait(false);
        if (organizations.Count != 1)
        {
            throw new InvalidOperationException(
                $"The organization row could not be read ({organizations.Count} returned), so whether hierarchy security is on is unknown.");
        }

        var organization = organizations[0];
        if (!organization.GetAttributeValue<bool>("ishierarchicalsecuritymodelenabled"))
            return;

        if (organization.GetAttributeValue<bool>("usepositionhierarchy"))
        {
            foreach (var user in humans.Where(user => user.PositionId is not null))
            {
                findings.Add(new PlacementFinding(PlacementDriftKind.FlaggedManagerUnderHierarchySecurity, user.Id,
                    $"hierarchy security (position model) is on and flagged systemuser {user.Id} holds position {user.PositionId}: " +
                    "positions above others reach their rows. Clear the position or turn hierarchy security off."));
            }

            return;
        }

        var reports = new Dictionary<Guid, int>();
        var managerIds = humans.Select(user => user.Id).ToHashSet();
        foreach (var chunk in managerIds.Chunk(InChunkSize))
        {
            var query = Query("systemuser", "systemuserid", "parentsystemuserid");
            query.Criteria.AddCondition("parentsystemuserid", ConditionOperator.In, chunk.Cast<object>().ToArray());
            foreach (var row in await RetrieveAllAsync(dataverse, query, ct).ConfigureAwait(false))
            {
                if (OptionalGuid(row, "parentsystemuserid") is { } manager && managerIds.Contains(manager))
                    reports[manager] = reports.GetValueOrDefault(manager) + 1;
            }
        }

        foreach (var (manager, count) in reports.OrderBy(pair => pair.Key))
        {
            findings.Add(new PlacementFinding(PlacementDriftKind.FlaggedManagerUnderHierarchySecurity, manager,
                $"hierarchy security (manager model) is on and flagged systemuser {manager} manages {count} user(s): a manager " +
                "reaches rows its reports own or are shared. Change the manager or turn hierarchy security off."));
        }
    }

    private static async Task<IReadOnlySet<Guid>> ReadDefaultTeamIdsAsync(IGenericEntityService dataverse, Guid unitId, CancellationToken ct)
    {
        var query = Query("team", "teamid", "businessunitid", "isdefault");
        query.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, unitId);
        query.Criteria.AddCondition("isdefault", ConditionOperator.Equal, true);
        return (await RetrieveAllAsync(dataverse, query, ct).ConfigureAwait(false))
            .Where(row => OptionalGuid(row, "businessunitid") == unitId && row.GetAttributeValue<bool>("isdefault"))
            .Select(row => row.Id)
            .ToHashSet();
    }

    private static async Task<Dictionary<Guid, CensusTeam>> ReadTeamsAsync(
        IGenericEntityService dataverse, IReadOnlyCollection<Guid> teamIds, CancellationToken ct)
    {
        var teams = new Dictionary<Guid, CensusTeam>();
        foreach (var row in await RetrieveInAsync(dataverse, "team", ["teamid", "name", "businessunitid", "isdefault", "teamtype"],
                     "teamid", teamIds, ct).ConfigureAwait(false))
        {
            teams[row.Id] = new CensusTeam(row.Id, row.GetAttributeValue<string>("name") ?? string.Empty,
                OptionalGuid(row, "businessunitid") ?? Guid.Empty, row.GetAttributeValue<bool>("isdefault"),
                row.GetAttributeValue<OptionSetValue>("teamtype")?.Value ?? -1, [], []);
        }

        return teams;
    }

    private static async Task<Dictionary<Guid, CensusRole>> ReadRolesAsync(
        IGenericEntityService dataverse, IReadOnlyCollection<Guid> roleIds, CancellationToken ct)
    {
        var roles = new Dictionary<Guid, CensusRole>();
        foreach (var row in await RetrieveInAsync(dataverse, "role", ["roleid", "name", "businessunitid", "parentrootroleid"],
                     "roleid", roleIds, ct).ConfigureAwait(false))
        {
            roles[row.Id] = new CensusRole(row.Id, row.GetAttributeValue<string>("name") ?? string.Empty,
                OptionalGuid(row, "businessunitid") ?? Guid.Empty, OptionalGuid(row, "parentrootroleid") ?? row.Id);
        }

        return roles;
    }

    /// <summary>
    /// Every privilege on a lineage-registry table (each access type) plus <see cref="WriteUserPrivilege"/>, id → name. A
    /// privilege name is <c>prv{Access}{SchemaName}</c>; Dataverse compares names case-insensitively, as this does.
    /// </summary>
    private static async Task<Dictionary<Guid, string>> ReadLineagePrivilegesAsync(IGenericEntityService dataverse, CancellationToken ct)
    {
        var wanted = LineageTables()
            .SelectMany(table => AccessTypes.Select(access => $"prv{access}{table}"))
            .Append(WriteUserPrivilege)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var privileges = new Dictionary<Guid, string>();
        foreach (var row in await RetrieveInAsync(dataverse, "privilege", ["privilegeid", "name"], "name", wanted.ToArray(), ct)
                     .ConfigureAwait(false))
        {
            var name = row.GetAttributeValue<string>("name");
            if (name is not null && wanted.Contains(name))
                privileges[row.Id] = name;
        }

        if (!privileges.Values.Any(name => string.Equals(name, WriteUserPrivilege, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"The privilege '{WriteUserPrivilege}' was not returned, so the census cannot grade who may edit a user's flag.");
        }

        return privileges;
    }

    /// <summary>(role id, privilege id) → depth mask, for each held role copy and its root role.</summary>
    private static async Task<Dictionary<(Guid RoleId, Guid PrivilegeId), int>> ReadDepthsAsync(
        IGenericEntityService dataverse, IReadOnlyDictionary<Guid, CensusRole> roles, IReadOnlyCollection<Guid> privilegeIds,
        CancellationToken ct)
    {
        var depths = new Dictionary<(Guid, Guid), int>();
        var roleIds = roles.Values.SelectMany(role => new[] { role.Id, role.RootRoleId }).ToHashSet();
        if (roleIds.Count == 0 || privilegeIds.Count == 0)
            return depths;

        foreach (var chunk in roleIds.Chunk(InChunkSize))
        {
            var query = Query("roleprivileges", "roleid", "privilegeid", "privilegedepthmask");
            query.Criteria.AddCondition("roleid", ConditionOperator.In, chunk.Cast<object>().ToArray());
            query.Criteria.AddCondition("privilegeid", ConditionOperator.In, privilegeIds.Cast<object>().ToArray());
            foreach (var row in await RetrieveAllAsync(dataverse, query, ct).ConfigureAwait(false))
            {
                depths[(RequiredGuid(row, "roleid"), RequiredGuid(row, "privilegeid"))] = row.GetAttributeValue<int>("privilegedepthmask");
            }
        }

        return depths;
    }

    /// <summary>The widest depth recorded for the role copy or its root (only the root copy carries privileges).</summary>
    private static int DepthOf(
        IReadOnlyDictionary<(Guid, Guid), int> depths, CensusRole? role, Guid roleId, Guid privilegeId) =>
        Math.Max(depths.GetValueOrDefault((roleId, privilegeId)),
            role is null ? 0 : depths.GetValueOrDefault((role.RootRoleId, privilegeId)));

    private static string DepthName(int mask) =>
        Enum.IsDefined(typeof(PrivilegeDepth), mask)
            ? ((PrivilegeDepth)mask).ToString()
            : mask.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Describe(Guid? id) => id is { } value && value != Guid.Empty ? value.ToString() : "(unreadable)";

    private static async Task<Dictionary<Guid, List<Guid>>> ReadPairsAsync(
        IGenericEntityService dataverse, string entity, string keyColumn, string valueColumn, IReadOnlyCollection<Guid> keys,
        CancellationToken ct)
    {
        var pairs = new Dictionary<Guid, List<Guid>>();
        var wanted = keys.ToHashSet();
        foreach (var row in await RetrieveInAsync(dataverse, entity, [keyColumn, valueColumn], keyColumn, keys, ct).ConfigureAwait(false))
        {
            var key = RequiredGuid(row, keyColumn);
            if (!wanted.Contains(key))
                continue;

            if (!pairs.TryGetValue(key, out var values))
                pairs[key] = values = [];
            values.Add(RequiredGuid(row, valueColumn));
        }

        return pairs;
    }

    private static async Task<List<Entity>> RetrieveInAsync<T>(
        IGenericEntityService dataverse, string entity, string[] columns, string inColumn, IReadOnlyCollection<T> values,
        CancellationToken ct)
    {
        var rows = new List<Entity>();
        foreach (var chunk in values.Distinct().Chunk(InChunkSize))
        {
            var query = Query(entity, columns);
            query.Criteria.AddCondition(inColumn, ConditionOperator.In, chunk.Cast<object>().ToArray());
            rows.AddRange(await RetrieveAllAsync(dataverse, query, ct).ConfigureAwait(false));
        }

        return rows;
    }
}

/// <summary>Whether a name-keyed directory object was found once, not at all, or more than once (or misplaced).</summary>
internal enum UnitState
{
    Found,
    Missing,
    Ambiguous,
}

/// <summary>The External Collaborators unit as read: its id only when <see cref="UnitState.Found"/>.</summary>
internal sealed record UnitResolution(UnitState State, Guid? UnitId, IReadOnlyList<Guid> Candidates);

/// <summary>The External Collaborator role copy in that unit: its id only when <see cref="UnitState.Found"/>.</summary>
internal sealed record RoleResolution(UnitState State, Guid? RoleId);

/// <summary>
/// One systemuser as the placement code reads it: ids and flags, never a name. A nullable value is one Dataverse did not
/// return; every decision treats it as unknown and fails closed.
/// </summary>
internal sealed record FlaggedUser(
    Guid Id, Guid? BusinessUnitId, bool? IsDisabled, bool HasApplicationId, int? AccessMode, bool? IsExternal, Guid? PositionId)
{
    /// <summary>Only a stored value that reads <c>true</c> (round 67). Blank, false and an absent (masked) value are internal.</summary>
    public bool IsDefinitelyExternal => IsExternal == true;

    internal static FlaggedUser From(Entity row) => new(
        row.Id,
        SecureRecordIsolationCensus.OptionalGuid(row, "businessunitid"),
        row.Attributes.TryGetValue("isdisabled", out var disabled) && disabled is bool d ? d : null,
        SecureRecordIsolationCensus.OptionalGuid(row, "applicationid") is { } app && app != Guid.Empty,
        row.Attributes.TryGetValue("accessmode", out var mode) && mode is OptionSetValue m ? m.Value : null,
        row.Attributes.TryGetValue(ExternalCollaboratorPlacement.IsExternalColumn, out var flag) && flag is bool f ? f : null,
        SecureRecordIsolationCensus.OptionalGuid(row, "positionid"));
}

/// <summary>The kinds of placement drift the census reports, each CRITICAL (design 3.8).</summary>
internal enum PlacementDriftKind
{
    /// <summary>More than one unit carries the name, or it is not under the root.</summary>
    UnitAmbiguous,

    /// <summary>The unit has no copy of THE role, or more than one: nobody can be placed.</summary>
    RoleUnresolved,

    /// <summary>A flagged user outside the unit.</summary>
    FlaggedUserOutsideExternalUnit,

    /// <summary>A role assigned from another business unit (matrix): its depth is anchored there.</summary>
    RoleFromAnotherUnit,

    /// <summary>A role from the unit that is not THE External Collaborator role.</summary>
    UnexpectedRole,

    /// <summary>Local, Deep or Global on a lineage table.</summary>
    LineageDepthAboveBasic,

    /// <summary><c>prvWriteUser</c> at any depth.</summary>
    WriteUserPrivilege,

    /// <summary>A member of an owner team other than the unit's default team.</summary>
    OtherOwnerTeamMembership,

    /// <summary>A lineage row owned by a flagged user or by the unit's default team.</summary>
    LineageRowOwnedByFlaggedPrincipal,

    /// <summary>Hierarchy security is on and a flagged user manages someone (or holds a position).</summary>
    FlaggedManagerUnderHierarchySecurity,
}

/// <summary>One drift: its kind, the principal it names (a systemuser or team id; empty for the unit) and the message.</summary>
internal sealed record PlacementFinding(PlacementDriftKind Kind, Guid PrincipalId, string Message);

/// <summary>The placement census statuses.</summary>
internal static class PlacementCensusStatus
{
    /// <summary>Graded, no drift.</summary>
    public const string Clean = "clean";

    /// <summary>Graded, at least one drift.</summary>
    public const string Findings = "findings";

    /// <summary>Not graded: the unit does not exist yet (task 191i creates it).</summary>
    public const string Inert = "inert";

    /// <summary>Could not be read.</summary>
    public const string Error = "error";
}

/// <summary>A graded placement census.</summary>
internal sealed record PlacementCensusOutcome(string Status, IReadOnlyList<PlacementFinding> Findings, int FlaggedUsers)
{
    /// <summary>Lineage tables this environment does not have (no privilege of theirs returned): not graded for ownership.</summary>
    public IReadOnlyList<string> UngradedTables { get; init; } = [];
}
