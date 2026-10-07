namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// The spec NFR-05 standing assertion, isolated as a pure function so its FAILURE directions can themselves be
/// proven without a tenant (see <c>SecureBuRoleDepthAssertionTests</c> § perturbation).
/// </summary>
/// <remarks>
/// <para><b>Moved into the BFF by task 144 (2026-10-01).</b> It used to live in the test project, run only as a manual
/// live gate. Owner decision F2 added a read-only scheduled job (<c>SecureRecordIsolationCensusJob</c>) that runs the
/// same census between provisioning calls, so the evaluator — and the census composition in
/// <see cref="SecureBuRoleDepthCensusBuilder"/> — now has two runners and lives where both can reach it. One evaluator,
/// so the job and the manual gate cannot disagree about what "isolated" means.</para>
///
/// <para><b>The failure this guards</b> (design §5.2, confirmed end-to-end by task 046): a security
/// role granting <c>prvReadsprk_Project</c> at <c>Deep</c> depth, held from an ANCESTOR of the secure
/// business unit, reaches every secure project silently. There is no error, no denial and no log line —
/// a secure project simply appears in an ordinary user's list.</para>
///
/// <para><b>The assertion is about DEPTH, not about the BU</b> (design §5.1a-2). Reach is a property of <i>depth held
/// at an ancestor</i>, so this evaluator resolves reach against the live BU tree.</para>
///
/// <para><b>Clauses</b> (task 144 retargeted 2 and 3 and added 2b and 4):
/// <list type="number">
///   <item>No <b>non-administrator human</b> principal holds Read on a secure root table (project, matter, work
///   assignment) at a depth that reaches the secure BU.</item>
///   <item>The secure BU's NAMED owner team (<c>SecureRecord:OwnerTeamName</c>) resolves to exactly one non-default
///   Owner team (2b), and has <b>zero members of any kind</b> — it owns every secure record, so any member, human or
///   application user, reads all of them by ownership.</item>
///   <item>The <c>Secure Record Owner</c> role is held by that named team <b>alone</b> — not by the BU's default team,
///   not by any other team, not by any user.</item>
///   <item>The secure BU holds <b>zero systemusers</b> — enabled or disabled, human or application. A user there reads
///   every secure record by business-unit depth whoever owns them, which a named team alone does not prevent.</item>
///   <item>(Task 145, #1046.) The <c>Secure Record Owner</c> role — exactly one, in the secure BU — holds Read at User
///   (<c>Basic</c>) depth on <b>every table in the codified set</b> (<see cref="SecureRecordOwnerRoleSet"/>,
///   <c>config/secure-record-owner-role.json</c>). A missing one makes Dataverse refuse every assignment of that table
///   to the owner team, so the secure child fails closed; a wider depth makes the team's ownership reach further than
///   it owns. Each gap is its own finding, naming the table — the same rule as
///   <c>Set-SecureRecordOwnerRolePrivileges.ps1 -Verify</c>. A role that is only a REPLICA of one created in an
///   ancestor business unit is a finding and is not graded, as the script refuses it.</item>
/// </list></para>
///
/// <para><b>Why this is a standing assertion and not an audit.</b> Every clause above is a
/// configuration property any administrator can undo in one click, in an environment nobody is looking
/// at. An audit dated last month is not a control.</para>
/// </remarks>
public static class SecureBuRoleDepthAssertion
{
    /// <summary>
    /// The privileges whose depth decides secure-record visibility: Read on every table that carries
    /// <c>sprk_issecure</c>. Names verified against live metadata 2026-10-01 — the casing follows each table's SCHEMA
    /// name (<c>prvReadsprk_WorkAssignment</c>), and a census looks depths up by exact name.
    /// </summary>
    /// <remarks>
    /// <c>prvReadsprk_WorkAssignment</c> was added by task 144: work assignments carry <c>sprk_issecure</c> and become
    /// provisionable in that task, so a role reaching the secure BU on work assignments discloses a secure engagement
    /// just as surely as one reaching it on projects — and this list had not covered it.
    /// </remarks>
    public static readonly IReadOnlyList<string> GuardedPrivileges = new[]
    {
        "prvReadsprk_Project",
        "prvReadsprk_Matter",
        "prvReadsprk_WorkAssignment"
    };

    /// <summary>The role that legitimately anchors ownership inside the secure BU (design §5.1a).</summary>
    public const string SecureOwnerRoleName = "Secure Record Owner";

    /// <summary>
    /// The pinned administrative allow-list — the documented, accepted exception (spec Unresolved
    /// Questions, "Global read constraint"; design §5.2 "System Administrator / Customizer ... expected").
    /// </summary>
    /// <remarks>
    /// <para><b>Adding to this list requires editing this file.</b> That friction is the point: the
    /// allow-list IS the accepted-risk register, so growth in it must be as visible as a code change.</para>
    ///
    /// <para><b><c>Secure Record Owner</c> is deliberately NOT here.</b> It does not need to be: it is
    /// granted at User (<c>Basic</c>) depth, which reaches nothing by business unit, so the depth model
    /// exempts it structurally. Allow-listing it by name would instead hide the one change that matters —
    /// somebody widening it to <c>Local</c>, <c>Deep</c> or <c>Global</c>.</para>
    ///
    /// <para><b>Nor are <c>Service Reader</c> / <c>Service Writer</c>.</b> They hold Global read, but the
    /// documented exemption is about the PRINCIPAL (Microsoft platform application accounts), not the
    /// role — see <see cref="EffectiveGrant.PrincipalIsHuman"/>.</para>
    /// </remarks>
    public static readonly IReadOnlyList<string> AdministrativeRoleAllowList = new[]
    {
        "System Administrator",
        "System Customizer"
    };

    /// <summary>The loud not-run message for the default business-unit name.</summary>
    public static string InertMessage => InertMessageFor(SecureRecordOwnerTeam.DefaultBusinessUnitName);

    /// <summary>
    /// The exact wording spec NFR-05 requires in the pre-UAT state, so an inert run is legible in the
    /// build log rather than looking like a pass.
    /// </summary>
    public static string InertMessageFor(string secureBusinessUnitName) =>
        "Secure Record BU not found — NFR-05 assertion inert; UAT environment setup pending. "
        + $"No business unit named '{secureBusinessUnitName}' exists in the target environment, so the role-depth "
        + "assertion has nothing to resolve reach against and is NOT asserting anything on this run. "
        + "This is not a pass. Create the secure business unit per design §5.2 and re-run. "
        + "⚠️ If the environment DOES have a secure BU but it is still called 'Secure Project', the "
        + "2026-09-29 rename (task 121) is only half applied — rename the live BU rather than widening "
        + "the name here, and see docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md §3a.";

    /// <summary>
    /// Every business unit carrying the configured Secure Record name. Exactly one is the only gradeable shape: the
    /// evaluator and <see cref="SecureBuRoleDepthCensusBuilder"/> both resolve through here, so neither can quietly
    /// grade the first of two.
    /// </summary>
    public static IReadOnlyList<BusinessUnitNode> SecureBusinessUnitsNamed(
        IReadOnlyList<BusinessUnitNode> businessUnits, string secureBusinessUnitName)
    {
        ArgumentNullException.ThrowIfNull(businessUnits);

        return businessUnits
            .Where(bu => string.Equals(bu.Name, secureBusinessUnitName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <summary>Evaluates the whole NFR-05 assertion against a census read from a live environment.</summary>
    public static SecureBuAssertionOutcome Evaluate(SecureBuRoleDepthCensus census)
    {
        ArgumentNullException.ThrowIfNull(census);

        var secureBus = SecureBusinessUnitsNamed(census.BusinessUnits, census.SecureBusinessUnitName);

        if (secureBus.Count == 0)
        {
            // The loud NOT-RUN. It is not a pass: Passed is false for this verdict, and the caller is
            // contracted to print the message so the inert state is visible in every run.
            return new SecureBuAssertionOutcome(new[]
            {
                new SecureBuFinding(
                    SecureBuVerdict.SecureBusinessUnitNotFound, InertMessageFor(census.SecureBusinessUnitName))
            });
        }

        if (secureBus.Count > 1)
        {
            // Task 144 (verifier round 2): two business units carry the configured name. Grading the first would let
            // the second — which may hold users, or be reached by depth — report "isolated". Provisioning, the
            // ownership resolver and registration all REFUSE on this ambiguity; the census refuses to render a verdict
            // the same way, and it is a finding (never inert, never a pass).
            return new SecureBuAssertionOutcome(new[]
            {
                new SecureBuFinding(
                    SecureBuVerdict.SecureBusinessUnitAmbiguous,
                    $"{secureBus.Count} business units are named '{census.SecureBusinessUnitName}' "
                    + $"(SecureRecord:BusinessUnitName): {string.Join(", ", secureBus.Select(bu => $"{bu.Id} (parent {bu.ParentId?.ToString() ?? "none"})"))}. "
                    + "Which one holds the secure records cannot be decided, so NONE is graded — isolation is UNKNOWN, "
                    + "not proven. Provisioning, the record-ownership resolver and registration refuse on the same "
                    + "ambiguity. Rename every business unit that is not the Secure Record BU; do not move users.")
            });
        }

        var secureBu = secureBus[0];

        // A census with no depth grants at all means the QUERY is wrong, not that the environment is
        // safe: prvReadsprk_Project is held by System Administrator in every environment that has the
        // entity. Refusing to render a verdict stops a mistyped privilege name from presenting as a
        // clean bill of health — the same guard as the NFR-04 canary's vacuous baseline.
        if (census.Grants.Count == 0)
        {
            return new SecureBuAssertionOutcome(new[]
            {
                new SecureBuFinding(
                    SecureBuVerdict.VacuousCensus,
                    "The role-depth census returned ZERO grants for "
                    + string.Join(" / ", GuardedPrivileges)
                    + ". That is a broken query, not an isolated environment: these privileges are held "
                    + "by System Administrator in every environment that has the entity. Check the "
                    + "privilege names, the roleprivileges join, and that child-BU role copies were "
                    + "resolved to their parent root role (privileges hang off the ROOT role only). Do "
                    + "NOT read this as a pass.")
            });
        }

        var findings = new List<SecureBuFinding>();
        findings.AddRange(EvaluateDepthClause(census, secureBu));
        findings.AddRange(EvaluateNoUserBusinessUnitClause(census, secureBu));
        findings.AddRange(EvaluateOwnerTeamClause(census, secureBu));
        findings.AddRange(EvaluateOwnerRoleContainmentClause(census));
        findings.AddRange(EvaluateOwnerRoleCoverageClause(census));

        return new SecureBuAssertionOutcome(findings);
    }

    /// <summary>
    /// Clause 5 (task 145) — the owner role covers the codified set. Fails closed on everything it cannot grade: an
    /// empty set, or anything but exactly one owner role in the secure BU, is a finding rather than a quiet pass.
    /// </summary>
    private static IEnumerable<SecureBuFinding> EvaluateOwnerRoleCoverageClause(SecureBuRoleDepthCensus census)
    {
        var coverage = census.OwnerRoleCoverage;

        if (coverage.Tables.Count == 0)
        {
            yield return new SecureBuFinding(
                SecureBuVerdict.SecureOwnerRoleLacksCodifiedPrivilege,
                "The census carries NO tables from the codified set (config/secure-record-owner-role.json), so whether "
                + $"'{SecureOwnerRoleName}' can own secure children is UNKNOWN, not proven. The set lists the root "
                + "tables at minimum; an empty one means it was not loaded.");
            yield break;
        }

        if (coverage.OwnerRoleCount != 1)
        {
            yield return new SecureBuFinding(
                SecureBuVerdict.SecureOwnerRoleLacksCodifiedPrivilege,
                $"The secure business unit holds {coverage.OwnerRoleCount} role(s) named '{SecureOwnerRoleName}'; "
                + "exactly one is required, so its coverage of the codified set cannot be graded. Create it in the "
                + "secure business unit per docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md §5.2 (one role, never a "
                + "replica of a role from an ancestor business unit).");
            yield break;
        }

        if (coverage.InheritedFromRootRoleId is { } rootRoleId)
        {
            // The one owner-role copy in the secure BU is a REPLICA: the role was created in an ANCESTOR business unit,
            // so Dataverse replicated it into every business unit beneath that ancestor and keeps its privileges on the
            // root copy outside the secure BU. Set-SecureRecordOwnerRolePrivileges.ps1 and the provisioning handler
            // design (S4) refuse this shape before grading anything; the census refuses it the same way.
            yield return new SecureBuFinding(
                SecureBuVerdict.SecureOwnerRoleLacksCodifiedPrivilege,
                $"The '{SecureOwnerRoleName}' role in the secure business unit is a REPLICA of root role {rootRoleId}, "
                + "so it was created in an ANCESTOR business unit and exists in every business unit beneath that one. "
                + "Its coverage of the codified set is not graded: setup guide §5.2 creates the role IN the secure "
                + "business unit, and Set-SecureRecordOwnerRolePrivileges.ps1 refuses a replica the same way. Recreate "
                + "it in the secure business unit and move the owner team onto it.");
            yield break;
        }

        foreach (var table in coverage.Tables)
        {
            if (!table.ExistsInEnvironment)
            {
                yield return new SecureBuFinding(
                    SecureBuVerdict.SecureOwnerRoleLacksCodifiedPrivilege,
                    $"config/secure-record-owner-role.json requires {table.PrivilegeName} (table {table.LogicalName}), "
                    + "but this environment has no privilege of exactly that name: the table is not installed, was "
                    + "renamed, or the file mis-cases it (casing follows the schema name; compare "
                    + $"EntityDefinitions(LogicalName='{table.LogicalName}')/Privileges). The owner team cannot own its "
                    + "rows here until that is resolved.");
                continue;
            }

            if (table.HeldDepth is null)
            {
                yield return new SecureBuFinding(
                    SecureBuVerdict.SecureOwnerRoleLacksCodifiedPrivilege,
                    $"'{SecureOwnerRoleName}' lacks {table.PrivilegeName} (table {table.LogicalName}), which "
                    + "config/secure-record-owner-role.json requires. Dataverse refuses to make the secure owner team "
                    + $"the owner of a {table.LogicalName} row, so every secure {table.LogicalName} write FAILS CLOSED "
                    + "(not an exposure). Fix: scripts/Set-SecureRecordOwnerRolePrivileges.ps1 -Apply, then the guide "
                    + "§5.4 strip (AddPrivilegesRole re-injects the SharePoint four), then -Verify.");
                continue;
            }

            if (table.HeldDepth != PrivilegeDepth.Basic)
            {
                yield return new SecureBuFinding(
                    SecureBuVerdict.SecureOwnerRoleLacksCodifiedPrivilege,
                    $"'{SecureOwnerRoleName}' holds {table.PrivilegeName} (table {table.LogicalName}) at "
                    + $"{table.HeldDepth} depth; the codified set requires {SecureRecordOwnerRoleSet.RequiredDepth} "
                    + "(User) only. A wider depth lets the owner team read beyond what it owns. Narrowing an existing "
                    + "grant is an owner decision (setup guide §5.1); the script will not do it.");
            }
        }
    }

    /// <summary>Clause 1 — the load-bearing one. Nothing else here would have caught the §5.2 hole.</summary>
    private static IEnumerable<SecureBuFinding> EvaluateDepthClause(
        SecureBuRoleDepthCensus census,
        BusinessUnitNode secureBu)
    {
        foreach (var grant in census.Grants)
        {
            if (!grant.PrincipalIsHuman)
            {
                // Application users and Microsoft platform accounts holding Global read are the
                // documented, accepted exception (design §5.2). The exemption is on the PRINCIPAL.
                continue;
            }

            if (!Reaches(grant.Depth, grant.AnchorBusinessUnitId, secureBu.Id, census.BusinessUnits))
            {
                continue;
            }

            var administrative =
                AdministrativeRoleAllowList.Contains(grant.RoleName, StringComparer.OrdinalIgnoreCase);

            if (administrative && grant.HeldViaTeam is null)
            {
                continue; // A deliberate per-identity administrator. The accepted exception.
            }

            if (administrative && HoldsTheSameRoleDirectly(census, grant))
            {
                // The identity is independently a designated administrator; the team path confers
                // nothing it does not already hold.
                continue;
            }

            if (administrative)
            {
                // An administrative role reaching the secure BU through TEAM membership is not a
                // per-identity designation at all. Dataverse's default business-unit owner team holds
                // every user in that BU automatically and cannot be curated, so one role assignment on
                // it silently promotes every current AND future member.
                yield return new SecureBuFinding(
                    SecureBuVerdict.AdministrativeRoleHeldByTeam,
                    $"'{grant.PrincipalName}' ({grant.PrincipalDomainName ?? "no domain"}) reaches the "
                        + $"secure business unit '{secureBu.Name}' through the ALLOW-LISTED administrative "
                        + $"role '{grant.RoleName}' ({grant.PrivilegeName} @ {grant.Depth}, anchored at BU "
                        + $"{grant.AnchorBusinessUnitId}) — but holds it via team '{grant.HeldViaTeam}', "
                        + "not by direct assignment. The allow-list exempts a DELIBERATE per-identity "
                        + "administrator; a role on a team promotes every current and future member at "
                        + "once, and a default business-unit owner team's membership is platform-managed "
                        + "and cannot be curated. Either assign the administrative role directly to the "
                        + "identities that should hold it, or remove it from the team.");
                continue;
            }

            yield return new SecureBuFinding(
                SecureBuVerdict.HumanPrincipalReachesSecureBusinessUnit,
                $"SECURE PROJECTS ARE NOT ISOLATED: '{grant.PrincipalName}' "
                    + $"({grant.PrincipalDomainName ?? "no domain"}) — a non-administrator human — holds "
                    + $"'{grant.RoleName}' granting {grant.PrivilegeName} at {grant.Depth} depth anchored "
                    + $"at business unit {grant.AnchorBusinessUnitId}"
                    + (grant.HeldViaTeam is null ? string.Empty : $" (via team '{grant.HeldViaTeam}')")
                    + $", which REACHES '{secureBu.Name}' ({secureBu.Id}). Every secure record of that type is "
                    + "readable by this principal, with no error and no log line. Fix per design §5.2: "
                    + "move the principal (or the team) into a subtree that is not an ancestor of the "
                    + "secure BU, or narrow the depth. This is a MERGE GATE (spec NFR-05).");
        }
    }

    /// <summary>
    /// Whether the same principal also holds the same role by DIRECT assignment — i.e. whether the
    /// team-conferred copy is redundant. Identity is matched on domain name where present, because a
    /// display name is not unique (dev has three enabled <c>Ralph Schroeder</c> identities).
    /// </summary>
    private static bool HoldsTheSameRoleDirectly(SecureBuRoleDepthCensus census, EffectiveGrant grant) =>
        census.Grants.Any(other =>
            other.HeldViaTeam is null
            && string.Equals(other.RoleName, grant.RoleName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                other.PrincipalDomainName ?? other.PrincipalName,
                grant.PrincipalDomainName ?? grant.PrincipalName,
                StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Clause 4 (task 144) — the no-user business unit. Every record owned by ANY team in the secure BU has that BU as
    /// its owning business unit, so a user placed there whose roles carry Business Unit or Deep depth reads every
    /// secure record by DEPTH, whoever the owner team is. Any systemuser of any kind fails it: a disabled user can be
    /// re-enabled, and an application user reads by depth like anyone else.
    /// </summary>
    private static IEnumerable<SecureBuFinding> EvaluateNoUserBusinessUnitClause(
        SecureBuRoleDepthCensus census,
        BusinessUnitNode secureBu)
    {
        if (census.SecureBusinessUnitUsers.Count == 0)
        {
            yield break;
        }

        yield return new SecureBuFinding(
            SecureBuVerdict.SecureBusinessUnitHasUsers,
            $"The secure business unit '{secureBu.Name}' holds {census.SecureBusinessUnitUsers.Count} systemuser(s): "
            + $"{string.Join(", ", census.SecureBusinessUnitUsers)}. It must hold none. A user there reads every "
            + "secure record by business-unit depth whoever owns it — a named owner team does not prevent that. "
            + "Moving a user is an owner decision; this census reports, it does not move anyone.");
    }

    /// <summary>
    /// Clause 2 — the NAMED owner team (task 144) owns EVERY secure record, so any member — human or application
    /// user — reads all of them by ownership. No depth is involved, and narrowing the role would not relax it. A
    /// missing or duplicated named team fails too: membership cannot be asserted for a team that does not resolve.
    /// </summary>
    private static IEnumerable<SecureBuFinding> EvaluateOwnerTeamClause(
        SecureBuRoleDepthCensus census,
        BusinessUnitNode secureBu)
    {
        if (census.SecureOwnerTeamMatches != 1)
        {
            yield return new SecureBuFinding(
                SecureBuVerdict.SecureOwnerTeamNotResolved,
                $"The secure business unit '{secureBu.Name}' has {census.SecureOwnerTeamMatches} non-default Owner "
                + $"team(s) named '{census.SecureOwnerTeamName}'; exactly one is required. Provisioning refuses "
                + "without it and will not fall back to the business unit's default team, whose membership follows "
                + "every user placed in the business unit and cannot be curated. Create it per "
                + "docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md §4.");
            yield break;
        }

        if (census.SecureOwnerTeamMembers.Count == 0)
        {
            yield break;
        }

        yield return new SecureBuFinding(
            SecureBuVerdict.OwnerTeamHasMembers,
            $"The secure owner team '{census.SecureOwnerTeamName}' has {census.SecureOwnerTeamMembers.Count} "
            + $"member(s): {string.Join(", ", census.SecureOwnerTeamMembers)}. That team owns every secure record, "
            + "so each member — human or application user — reads all of them by ownership, the ownership-derived "
            + "access design §5.1a forbids. Narrowing its role's depth does NOT relax this.");
    }

    /// <summary>
    /// Clause 3 — the ownership anchor must stay an anchor, not become a grant surface. Since task 144 the only
    /// legitimate holder is the NAMED owner team: the business unit's default team holding it is a finding too.
    /// </summary>
    private static IEnumerable<SecureBuFinding> EvaluateOwnerRoleContainmentClause(
        SecureBuRoleDepthCensus census)
    {
        var strays = census.SecureOwnerRoleHolders
            .Where(holder => !holder.IsSecureOwnerTeam)
            .Select(holder => holder.PrincipalName)
            .ToArray();

        if (strays.Length == 0)
        {
            yield break;
        }

        yield return new SecureBuFinding(
            SecureBuVerdict.SecureOwnerRoleHeldBeyondOwnerTeam,
            $"'{SecureOwnerRoleName}' is held by {strays.Length} principal(s) other than the named secure "
            + $"owner team '{census.SecureOwnerTeamName}': {string.Join(", ", strays)}. It is assigned to that team "
            + "ALONE — it exists so Dataverse will accept an assignment to that team, not to grant read to anyone. "
            + "A holder of this role reads whatever it owns, and on the business unit's default team it reads "
            + "nothing yet keeps the retired owner a legal assignment target.");
    }

    /// <summary>
    /// Whether a privilege at <paramref name="depth"/>, anchored at <paramref name="anchorBusinessUnitId"/>,
    /// reaches <paramref name="secureBusinessUnitId"/>.
    /// </summary>
    /// <remarks>
    /// <para>The anchor is the ROLE COPY's business unit for a directly-assigned role (Dataverse only
    /// lets a user hold the copy that lives in their own BU) and the TEAM's business unit for a
    /// team-assigned role (team privileges are evaluated against the team's BU, not the member's).</para>
    /// <para><c>Basic</c> reaches nothing by business unit — it matches only records the principal owns,
    /// which is why clause 2 exists separately. An UNRECOGNISED depth mask fails closed.</para>
    /// </remarks>
    public static bool Reaches(
        PrivilegeDepth depth,
        Guid anchorBusinessUnitId,
        Guid secureBusinessUnitId,
        IReadOnlyList<BusinessUnitNode> businessUnits)
    {
        ArgumentNullException.ThrowIfNull(businessUnits);

        return depth switch
        {
            PrivilegeDepth.Basic => false,
            PrivilegeDepth.Local => anchorBusinessUnitId == secureBusinessUnitId,
            PrivilegeDepth.Deep => anchorBusinessUnitId == secureBusinessUnitId
                || IsAncestor(anchorBusinessUnitId, secureBusinessUnitId, businessUnits),
            PrivilegeDepth.Global => true,
            _ => true
        };
    }

    /// <summary>True when <paramref name="candidateAncestorId"/> is a strict ancestor of <paramref name="nodeId"/>.</summary>
    private static bool IsAncestor(
        Guid candidateAncestorId,
        Guid nodeId,
        IReadOnlyList<BusinessUnitNode> businessUnits)
    {
        var byId = new Dictionary<Guid, BusinessUnitNode>();
        foreach (var bu in businessUnits)
        {
            byId[bu.Id] = bu;
        }

        var seen = new HashSet<Guid>();
        var current = byId.TryGetValue(nodeId, out var start) ? start.ParentId : null;

        while (current is { } parentId && seen.Add(parentId))
        {
            if (parentId == candidateAncestorId)
            {
                return true;
            }

            current = byId.TryGetValue(parentId, out var parent) ? parent.ParentId : null;
        }

        return false;
    }
}

/// <summary>
/// Composes raw directory rows into a <see cref="SecureBuRoleDepthCensus"/> — the ONE composition both census
/// readers use (the scheduled job over the SDK, the manual NFR-05 gate over the Web API), so the rules for anchors,
/// team-held grants, who counts as human and which team is "the" owner team cannot drift between them.
/// </summary>
public static class SecureBuRoleDepthCensusBuilder
{
    /// <summary>
    /// A human principal: a real, enabled, interactive person. Everything else — application users,
    /// Microsoft platform accounts (<c>#</c>-prefixed), support and non-interactive access modes,
    /// disabled users — is the documented Global-read exception for clause 1 (design §5.2). Clauses 2 and 4 count
    /// EVERY principal regardless.
    /// </summary>
    public static bool IsHumanPrincipal(bool hasApplicationId, bool isDisabled, string fullName, int accessMode) =>
        !hasApplicationId
        && !isDisabled
        && !fullName.StartsWith('#')
        && accessMode is not 3 and not 4; // 3 = Support User, 4 = Non-interactive.

    /// <summary>Builds the census from directory rows.</summary>
    /// <param name="secureBusinessUnitName">The configured Secure Record BU name.</param>
    /// <param name="secureOwnerTeamName">The configured named owner team.</param>
    /// <param name="businessUnits">The BU tree.</param>
    /// <param name="depthByRootRole">(root role id, privilege name) → depth, for the guarded privileges.</param>
    /// <param name="roles">Every role copy.</param>
    /// <param name="users">Every systemuser, with directly-assigned role copy ids.</param>
    /// <param name="teams">Every team, with role copy ids and member ids.</param>
    /// <param name="ownerRoleSet">The codified set the owner role must cover (task 145) — the readers pass
    /// <see cref="SecureRecordOwnerRoleSet.Embedded"/>; <paramref name="depthByRootRole"/> must carry its privileges
    /// (<see cref="CensusPrivilegeNames"/>).</param>
    /// <param name="resolvedPrivilegeNames">Every privilege name the environment returned, exactly as returned. A
    /// codified privilege not among them is reported by clause 5 as not present in the environment.</param>
    public static SecureBuRoleDepthCensus Build(
        string secureBusinessUnitName,
        string secureOwnerTeamName,
        IReadOnlyList<BusinessUnitNode> businessUnits,
        IReadOnlyDictionary<(Guid RootRoleId, string Privilege), PrivilegeDepth> depthByRootRole,
        IReadOnlyList<CensusRole> roles,
        IReadOnlyList<CensusUser> users,
        IReadOnlyList<CensusTeam> teams,
        SecureRecordOwnerRoleSet ownerRoleSet,
        IReadOnlyCollection<string> resolvedPrivilegeNames)
    {
        ArgumentNullException.ThrowIfNull(ownerRoleSet);
        ArgumentNullException.ThrowIfNull(resolvedPrivilegeNames);

        var rolesById = roles.ToDictionary(r => r.Id);
        var usersById = users.ToDictionary(u => u.Id);
        var grants = new List<EffectiveGrant>();

        foreach (var user in users)
        {
            foreach (var roleId in user.RoleIds)
            {
                AddGrants(grants, rolesById, depthByRootRole, roleId, anchorOverride: null, user, heldViaTeam: null);
            }
        }

        foreach (var team in teams)
        {
            foreach (var roleId in team.RoleIds)
            {
                foreach (var memberId in team.MemberIds)
                {
                    if (usersById.TryGetValue(memberId, out var member))
                    {
                        AddGrants(grants, rolesById, depthByRootRole, roleId, team.BusinessUnitId, member, team.Name);
                    }
                }
            }
        }

        // EVERY business unit carrying the configured name — never the first of several (task 144, verifier round 2).
        // With exactly one this is the Secure Record BU; with two or more the evaluator refuses to grade
        // (SecureBusinessUnitAmbiguous), and the census still lists users and named teams across ALL of them, so its
        // summary over-reports rather than hiding whatever sits in the second one.
        var secureBuIds = SecureBuRoleDepthAssertion.SecureBusinessUnitsNamed(businessUnits, secureBusinessUnitName)
            .Select(bu => bu.Id)
            .ToHashSet();

        var namedTeams = teams
            .Where(t => secureBuIds.Contains(t.BusinessUnitId)
                        && !t.IsDefault
                        && t.TeamType == SecureRecordOwnerTeam.OwnerTeamType
                        && string.Equals(t.Name, secureOwnerTeamName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var ownerTeamId = namedTeams.Count == 1 ? namedTeams[0].Id : (Guid?)null;

        var ownerRoleCopyIds = roles
            .Where(role => string.Equals(role.Name, SecureBuRoleDepthAssertion.SecureOwnerRoleName, StringComparison.OrdinalIgnoreCase))
            .Select(role => role.Id)
            .ToHashSet();

        var ownerRoleHolders = new List<SecureOwnerRoleHolder>();
        foreach (var user in users.Where(u => u.RoleIds.Any(ownerRoleCopyIds.Contains)))
        {
            ownerRoleHolders.Add(new SecureOwnerRoleHolder($"user '{user.Name}' ({user.DomainName ?? "no domain"})", IsSecureOwnerTeam: false));
        }

        foreach (var team in teams.Where(t => t.RoleIds.Any(ownerRoleCopyIds.Contains)))
        {
            var label = team.IsDefault ? $"team '{team.Name}' (the business unit's DEFAULT team)" : $"team '{team.Name}'";
            ownerRoleHolders.Add(new SecureOwnerRoleHolder(label, IsSecureOwnerTeam: ownerTeamId == team.Id));
        }

        var ownerTeamMembers = ownerTeamId is not null
            ? namedTeams[0].MemberIds.Select(id => Describe(id, usersById)).ToArray()
            : Array.Empty<string>();

        var secureBuUsers = users
            .Where(u => u.BusinessUnitId is { } buId && secureBuIds.Contains(buId))
            .Select(u => Describe(u.Id, usersById))
            .ToArray();

        return new SecureBuRoleDepthCensus(
            secureBusinessUnitName,
            secureOwnerTeamName,
            businessUnits,
            grants,
            ownerRoleHolders,
            namedTeams.Count,
            ownerTeamMembers,
            secureBuUsers,
            OwnerRoleCoverage(ownerRoleSet, resolvedPrivilegeNames, depthByRootRole, roles, secureBuIds));
    }

    /// <summary>
    /// Every privilege a census reader must resolve: the guarded root-table Reads (clause 1) and every privilege of the
    /// codified set (clause 5). Distinct, in a stable order.
    /// </summary>
    public static IReadOnlyList<string> CensusPrivilegeNames(SecureRecordOwnerRoleSet ownerRoleSet)
    {
        ArgumentNullException.ThrowIfNull(ownerRoleSet);

        return SecureBuRoleDepthAssertion.GuardedPrivileges
            .Concat(ownerRoleSet.Tables.Select(t => t.PrivilegeName))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Refuses a census whose privilege read did not return every GUARDED root-table Read EXACTLY (ordinal). Those
    /// drive clause 1 — a missing one would under-report reach — so the whole census stops.
    /// </summary>
    /// <remarks>
    /// <b>Only the guarded three throw.</b> A codified-set privilege the environment lacks (a table not installed
    /// there, a rename, or a mis-cased file entry — Dataverse matches names case-insensitively, the census looks depths
    /// up exactly) is a clause-5 FINDING, passed to the builder as "not resolved". Throwing on it would blind clauses
    /// 1-4 — the exposure checks — on every run, in every environment missing one optional table.
    /// </remarks>
    public static void RequireGuardedPrivilegesResolved(IReadOnlyCollection<string> returned)
    {
        ArgumentNullException.ThrowIfNull(returned);

        var unresolved = SecureBuRoleDepthAssertion.GuardedPrivileges
            .Where(name => !returned.Contains(name, StringComparer.Ordinal))
            .ToArray();
        if (unresolved.Length == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Expected to resolve {string.Join(" / ", SecureBuRoleDepthAssertion.GuardedPrivileges)} exactly, but the "
            + $"environment returned ({string.Join(", ", returned)}); missing: {string.Join(", ", unresolved)}. A missing "
            + "privilege means an entity is absent or renamed; grading a partial census would under-report reach, so "
            + "this fails instead.");
    }

    /// <summary>
    /// The owner role's coverage of the codified set: which role(s) named <c>Secure Record Owner</c> live in the secure
    /// BU, and — when exactly one does — the depth its ROOT copy holds on each table's Read privilege.
    /// </summary>
    private static SecureOwnerRoleCoverage OwnerRoleCoverage(
        SecureRecordOwnerRoleSet ownerRoleSet,
        IReadOnlyCollection<string> resolvedPrivilegeNames,
        IReadOnlyDictionary<(Guid RootRoleId, string Privilege), PrivilegeDepth> depthByRootRole,
        IReadOnlyList<CensusRole> roles,
        IReadOnlySet<Guid> secureBuIds)
    {
        var ownerRoleCopiesInSecureBu = roles
            .Where(role => secureBuIds.Contains(role.BusinessUnitId)
                           && string.Equals(role.Name, SecureBuRoleDepthAssertion.SecureOwnerRoleName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var ownerRootRoleIds = ownerRoleCopiesInSecureBu
            .Select(role => role.RootRoleId)
            .Distinct()
            .ToArray();

        // A role created IN the secure BU is its own root (roleid == parentrootroleid). One created in an ANCESTOR
        // reaches the secure BU only as a replica whose root copy lives elsewhere — the shape the script refuses.
        var inheritedFromRootRoleId = ownerRootRoleIds.Length == 1
                                      && ownerRoleCopiesInSecureBu.All(role => role.Id != role.RootRoleId)
            ? ownerRootRoleIds[0]
            : (Guid?)null;

        var tables = ownerRoleSet.Tables
            .Select(table => new OwnerRoleTableCoverage(
                table.LogicalName,
                table.PrivilegeName,
                ownerRootRoleIds.Length == 1
                && depthByRootRole.TryGetValue((ownerRootRoleIds[0], table.PrivilegeName), out var depth)
                    ? depth
                    : null,
                resolvedPrivilegeNames.Contains(table.PrivilegeName, StringComparer.Ordinal)))
            .ToArray();

        return new SecureOwnerRoleCoverage(ownerRootRoleIds.Length, tables, inheritedFromRootRoleId);
    }

    private static string Describe(Guid userId, IReadOnlyDictionary<Guid, CensusUser> usersById)
    {
        if (!usersById.TryGetValue(userId, out var user))
        {
            return $"systemuser {userId} (not in the user census)";
        }

        var kind = user.IsHuman ? "human" : "non-human/disabled";
        return $"{user.Name} ({user.DomainName ?? "no domain"}; {kind})";
    }

    private static void AddGrants(
        List<EffectiveGrant> grants,
        IReadOnlyDictionary<Guid, CensusRole> roles,
        IReadOnlyDictionary<(Guid RootRoleId, string Privilege), PrivilegeDepth> depthByRootRole,
        Guid roleId,
        Guid? anchorOverride,
        CensusUser principal,
        string? heldViaTeam)
    {
        if (!roles.TryGetValue(roleId, out var role))
        {
            return;
        }

        foreach (var privilege in SecureBuRoleDepthAssertion.GuardedPrivileges)
        {
            if (!depthByRootRole.TryGetValue((role.RootRoleId, privilege), out var depth))
            {
                continue;
            }

            grants.Add(new EffectiveGrant(
                role.Name,
                privilege,
                depth,
                anchorOverride ?? role.BusinessUnitId,
                principal.Name,
                principal.DomainName,
                principal.IsHuman,
                heldViaTeam));
        }
    }
}

/// <summary>One security-role copy. Only the ROOT copy (<c>roleid == parentrootroleid</c>) carries privileges.</summary>
public sealed record CensusRole(Guid Id, string Name, Guid BusinessUnitId, Guid RootRoleId);

/// <summary>One systemuser, with the role copies assigned to it directly.</summary>
public sealed record CensusUser(
    Guid Id, string Name, string? DomainName, bool IsHuman, Guid? BusinessUnitId, IReadOnlyList<Guid> RoleIds);

/// <summary>One team, with its role copies and its members.</summary>
public sealed record CensusTeam(
    Guid Id, string Name, Guid BusinessUnitId, bool IsDefault, int TeamType,
    IReadOnlyList<Guid> RoleIds, IReadOnlyList<Guid> MemberIds);

/// <summary>Dataverse <c>roleprivileges.privilegedepthmask</c> values.</summary>
public enum PrivilegeDepth
{
    /// <summary>User — matches only records the principal owns. Reaches no business unit.</summary>
    Basic = 1,

    /// <summary>Business Unit — matches records owned in the anchor BU only.</summary>
    Local = 2,

    /// <summary>Parent: Child Business Units — the anchor BU and every descendant.</summary>
    Deep = 4,

    /// <summary>Organization — everything.</summary>
    Global = 8
}

/// <summary>One node of the live business-unit tree. <c>ParentId</c> is null for the root BU.</summary>
public sealed record BusinessUnitNode(Guid Id, string Name, Guid? ParentId);

/// <summary>
/// One principal's effective grant of one guarded privilege, already resolved through role replication
/// and team assignment by the census builder.
/// </summary>
/// <param name="RoleName">The role's name (shared by every business-unit copy of the role).</param>
/// <param name="PrivilegeName">One of <see cref="SecureBuRoleDepthAssertion.GuardedPrivileges"/>.</param>
/// <param name="Depth">The depth the ROOT role grants; child copies inherit it.</param>
/// <param name="AnchorBusinessUnitId">The BU the depth is evaluated from — see <c>Reaches</c>.</param>
/// <param name="PrincipalName">Display name, for the log.</param>
/// <param name="PrincipalDomainName">UPN, for the log.</param>
/// <param name="PrincipalIsHuman">False for application users, disabled users and platform accounts.</param>
/// <param name="HeldViaTeam">Team name when held through a team; null for direct user assignment.</param>
public sealed record EffectiveGrant(
    string RoleName,
    string PrivilegeName,
    PrivilegeDepth Depth,
    Guid AnchorBusinessUnitId,
    string PrincipalName,
    string? PrincipalDomainName,
    bool PrincipalIsHuman,
    string? HeldViaTeam);

/// <summary>A principal holding <see cref="SecureBuRoleDepthAssertion.SecureOwnerRoleName"/>.</summary>
/// <param name="PrincipalName">Who holds it, for the log.</param>
/// <param name="IsSecureOwnerTeam">True only for the NAMED secure owner team (task 144) — never the default team.</param>
public sealed record SecureOwnerRoleHolder(string PrincipalName, bool IsSecureOwnerTeam);

/// <summary>Everything the assertion needs, composed by <see cref="SecureBuRoleDepthCensusBuilder"/>.</summary>
/// <param name="SecureBusinessUnitName">The configured Secure Record BU name the census resolved against.</param>
/// <param name="SecureOwnerTeamName">The configured named owner team.</param>
/// <param name="BusinessUnits">The BU tree.</param>
/// <param name="Grants">Every effective grant of a guarded privilege.</param>
/// <param name="SecureOwnerRoleHolders">Every holder of the owner role.</param>
/// <param name="SecureOwnerTeamMatches">How many non-default Owner teams in the secure BU carry the configured name
/// (across every BU bearing the secure name, when that name is ambiguous).</param>
/// <param name="SecureOwnerTeamMembers">Every member of the named team, of any kind (empty unless it resolved).</param>
/// <param name="SecureBusinessUnitUsers">Every systemuser in the secure BU, of any kind (across every BU bearing the
/// secure name, when that name is ambiguous).</param>
/// <param name="OwnerRoleCoverage">The owner role's coverage of the codified set (clause 5, task 145).</param>
public sealed record SecureBuRoleDepthCensus(
    string SecureBusinessUnitName,
    string SecureOwnerTeamName,
    IReadOnlyList<BusinessUnitNode> BusinessUnits,
    IReadOnlyList<EffectiveGrant> Grants,
    IReadOnlyList<SecureOwnerRoleHolder> SecureOwnerRoleHolders,
    int SecureOwnerTeamMatches,
    IReadOnlyList<string> SecureOwnerTeamMembers,
    IReadOnlyList<string> SecureBusinessUnitUsers,
    SecureOwnerRoleCoverage OwnerRoleCoverage);

/// <summary>The <c>Secure Record Owner</c> role's coverage of the codified set (task 145).</summary>
/// <param name="OwnerRoleCount">How many distinct roles of that name live in the secure BU. Only 1 is gradeable.</param>
/// <param name="Tables">One entry per codified table, in file order; <c>HeldDepth</c> is null when the role lacks it
/// (or when <paramref name="OwnerRoleCount"/> is not 1).</param>
/// <param name="InheritedFromRootRoleId">When the one owner role in the secure BU is a REPLICA of a role created in an
/// ancestor business unit (<c>roleid != parentrootroleid</c>), that root role's id; null when the role is the secure
/// BU's own. A replica is a clause-5 finding and is not graded.</param>
public sealed record SecureOwnerRoleCoverage(
    int OwnerRoleCount, IReadOnlyList<OwnerRoleTableCoverage> Tables, Guid? InheritedFromRootRoleId = null);

/// <summary>One codified table and the depth the owner role holds on its Read privilege.</summary>
/// <param name="LogicalName">The codified table.</param>
/// <param name="PrivilegeName">Its Read privilege, as the codified set names it.</param>
/// <param name="HeldDepth">The depth the owner role holds; null when it lacks it.</param>
/// <param name="ExistsInEnvironment">False when the environment has no privilege of exactly this name (table not
/// installed, renamed, or the file mis-cases it).</param>
public sealed record OwnerRoleTableCoverage(
    string LogicalName, string PrivilegeName, PrivilegeDepth? HeldDepth, bool ExistsInEnvironment = true);

/// <summary>Why the assertion reached the verdict it did.</summary>
public enum SecureBuVerdict
{
    /// <summary>Nothing reaches the secure BU. The only passing verdict.</summary>
    Isolated,

    /// <summary>The secure BU does not exist — the assertion is INERT, and says so loudly.</summary>
    SecureBusinessUnitNotFound,

    /// <summary>The census returned no grants at all, so the query is broken. Never a pass.</summary>
    VacuousCensus,

    /// <summary>A non-administrator human reaches the secure BU. The §5.2 hole.</summary>
    HumanPrincipalReachesSecureBusinessUnit,

    /// <summary>An allow-listed administrative role reaches it through bulk team membership.</summary>
    AdministrativeRoleHeldByTeam,

    /// <summary>The named secure owner team has members (any kind), who therefore read everything it owns.</summary>
    OwnerTeamHasMembers,

    /// <summary>The secure owner role escaped the named owner team.</summary>
    SecureOwnerRoleHeldBeyondOwnerTeam,

    /// <summary>A systemuser (any kind) sits in the secure BU and reads by depth (task 144).</summary>
    SecureBusinessUnitHasUsers,

    /// <summary>The named owner team is missing or ambiguous (task 144).</summary>
    SecureOwnerTeamNotResolved,

    /// <summary>
    /// Two or more business units carry the configured Secure Record name, so none is graded (task 144). Never a pass
    /// and never inert — isolation is unknown.
    /// </summary>
    SecureBusinessUnitAmbiguous,

    /// <summary>
    /// The <c>Secure Record Owner</c> role lacks Read at User depth on a codified table, holds it wider, or cannot be
    /// graded (task 145). Fail-CLOSED, not an exposure: the secure child of that table cannot be owned by the team.
    /// </summary>
    SecureOwnerRoleLacksCodifiedPrivilege
}

/// <summary>One violation, with the operator-actionable message that belongs in the log.</summary>
public sealed record SecureBuFinding(SecureBuVerdict Verdict, string Message);

/// <summary>The assertion's result. Empty findings is the only pass.</summary>
public sealed record SecureBuAssertionOutcome(IReadOnlyList<SecureBuFinding> Findings)
{
    /// <summary>True only when no clause was violated.</summary>
    public bool Passed => Findings.Count == 0;

    /// <summary>
    /// The most severe verdict, or <see cref="SecureBuVerdict.Isolated"/> when clean.
    /// </summary>
    /// <remarks>
    /// Ordered by severity rather than by position, because clause order is an implementation detail and
    /// a run's headline verdict must not depend on which principal the census happened to enumerate first.
    /// </remarks>
    public SecureBuVerdict Verdict => Findings.Count == 0
        ? SecureBuVerdict.Isolated
        : Findings.OrderBy(f => Severity(f.Verdict)).First().Verdict;

    private static int Severity(SecureBuVerdict verdict) => verdict switch
    {
        SecureBuVerdict.HumanPrincipalReachesSecureBusinessUnit => 0,
        SecureBuVerdict.SecureBusinessUnitHasUsers => 1,
        SecureBuVerdict.OwnerTeamHasMembers => 2,
        SecureBuVerdict.AdministrativeRoleHeldByTeam => 3,
        SecureBuVerdict.SecureOwnerRoleHeldBeyondOwnerTeam => 4,
        SecureBuVerdict.SecureOwnerTeamNotResolved => 5,
        SecureBuVerdict.SecureBusinessUnitAmbiguous => 6,
        SecureBuVerdict.SecureOwnerRoleLacksCodifiedPrivilege => 7, // fail-closed: ranks below every exposure
        SecureBuVerdict.VacuousCensus => 8,
        SecureBuVerdict.SecureBusinessUnitNotFound => 9,
        _ => 10
    };

    /// <summary>
    /// True when the assertion could not assert anything because the secure BU does not exist. This is
    /// NOT a pass — the caller prints <see cref="Message"/> and halts as not-run.
    /// </summary>
    public bool IsInert => Verdict == SecureBuVerdict.SecureBusinessUnitNotFound;

    /// <summary>Every finding, newline-separated, for the failure message.</summary>
    public string Message => Findings.Count == 0
        ? "No principal reaches the secure business unit; the business unit holds no users; the named owner team is "
          + "resolved, memberless and the only holder of its role; the role holds Read at User depth on every table "
          + "in the codified set."
        : string.Join(
            Environment.NewLine + Environment.NewLine,
            Findings.Select((f, i) => $"[{i + 1}/{Findings.Count}] {f.Verdict}: {f.Message}"));
}
