namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// The spec NFR-05 standing assertion, isolated as a pure function so its FAILURE directions can
/// themselves be proven without a tenant (see <c>SecureBuRoleDepthAssertionTests</c> § perturbation).
/// </summary>
/// <remarks>
/// <para><b>The failure this guards</b> (design §5.2, confirmed end-to-end by task 046): a security
/// role granting <c>prvReadsprk_Project</c> at <c>Deep</c> depth, held from an ANCESTOR of the secure
/// business unit, reaches every secure project silently. There is no error, no denial and no log line —
/// a secure project simply appears in an ordinary user's list. <c>Spaarke Basic User</c> held exactly
/// that (Deep at the root BU) and every secure project was readable by every ordinary user.</para>
///
/// <para><b>The assertion is about DEPTH, not about the BU</b> (design §5.1a-2, restated 2026-08-25).
/// "Which roles are scoped to the secure BU?" is the wrong question and would have passed while the
/// hole was wide open: <c>Spaarke Basic User</c> never mentions the secure BU anywhere and reaches it
/// anyway. Reach is a property of <i>depth held at an ancestor</i>, so this evaluator resolves reach
/// against the live BU tree.</para>
///
/// <para><b>Three clauses</b>, per the amended NFR-05 (spec NFR-05 + design §5.1a-2 restatement):
/// <list type="number">
///   <item>No <b>non-administrator human</b> principal holds <c>prvReadsprk_Project</c> /
///   <c>prvReadsprk_Matter</c> at a depth that reaches the secure BU.</item>
///   <item>The secure BU's default owner team has <b>zero human members</b> — it owns every secure
///   project, so a member would read all of them by ownership (design §5.1a).</item>
///   <item>The <c>Secure Project Owner</c> role is held by that team <b>alone</b>.</item>
/// </list></para>
///
/// <para><b>Why this is a standing assertion and not an audit.</b> Every clause above is a
/// configuration property any administrator can undo in one click, in an environment nobody is looking
/// at. The census in design §5.2 was true on 2026-08-20 and had already drifted by the time it was
/// re-run. An audit dated last month is not a control.</para>
/// </remarks>
public static class SecureBuRoleDepthAssertion
{
    /// <summary>
    /// The privileges whose depth decides secure-project visibility. Both are asserted because
    /// <c>sprk_matter</c> carries <c>sprk_issecure</c> too (design §5.1a: three entities carry it as of
    /// 2026-08-25) and a Matter reached is a secure engagement disclosed just as surely as a Project.
    /// </summary>
    public static readonly IReadOnlyList<string> GuardedPrivileges = new[]
    {
        "prvReadsprk_Project",
        "prvReadsprk_Matter"
    };

    /// <summary>
    /// The secure business unit, by name. BOTH spellings are pinned deliberately: dev provisioned
    /// <c>Secure Project</c> (singular — live metadata, 2026-09-09) while design §5.2's target topology
    /// names <c>Secure Projects</c> (plural). Matching only one would have made this assertion silently
    /// inert in exactly the environment it was written for.
    /// </summary>
    public static readonly IReadOnlyList<string> SecureBusinessUnitNames = new[]
    {
        "Secure Projects",
        "Secure Project"
    };

    /// <summary>The role that legitimately anchors ownership inside the secure BU (design §5.1a).</summary>
    public const string SecureOwnerRoleName = "Secure Project Owner";

    /// <summary>
    /// The pinned administrative allow-list — the documented, accepted exception (spec Unresolved
    /// Questions, "Global read constraint"; design §5.2 "System Administrator / Customizer ... expected").
    /// </summary>
    /// <remarks>
    /// <para><b>Adding to this list requires editing this file.</b> That friction is the point: the
    /// allow-list IS the accepted-risk register, so growth in it must be as visible as a code change.</para>
    ///
    /// <para><b><c>Secure Project Owner</c> is deliberately NOT here.</b> It does not need to be: it is
    /// granted at User (<c>Basic</c>) depth, which reaches nothing by business unit, so the depth model
    /// exempts it structurally. Allow-listing it by name would instead hide the one change that matters —
    /// somebody widening it to <c>Local</c>, <c>Deep</c> or <c>Global</c>.</para>
    ///
    /// <para><b>Nor are <c>Service Reader</c> / <c>Service Writer</c>.</b> They hold Global read, but the
    /// documented exemption is about the PRINCIPAL (Microsoft platform application accounts), not the
    /// role — see <see cref="EffectiveGrant.PrincipalIsHuman"/>. Allow-listing the role name would exempt
    /// a human who was ever granted it, which is precisely a hole.</para>
    /// </remarks>
    public static readonly IReadOnlyList<string> AdministrativeRoleAllowList = new[]
    {
        "System Administrator",
        "System Customizer"
    };

    /// <summary>
    /// The exact wording spec NFR-05 requires in the pre-UAT state, so an inert run is legible in the
    /// build log rather than looking like a pass.
    /// </summary>
    public const string InertMessage =
        "Secure Projects BU not found — NFR-05 assertion inert; UAT environment setup pending. "
        + "No business unit named 'Secure Projects' or 'Secure Project' exists in the target "
        + "environment, so the role-depth assertion has nothing to resolve reach against and is NOT "
        + "asserting anything on this run. This is not a pass. Create the secure business unit per "
        + "design §5.2 and re-run.";

    /// <summary>Evaluates the whole NFR-05 assertion against a census read from a live environment.</summary>
    public static SecureBuAssertionOutcome Evaluate(SecureBuRoleDepthCensus census)
    {
        ArgumentNullException.ThrowIfNull(census);

        var secureBu = census.BusinessUnits.FirstOrDefault(
            bu => SecureBusinessUnitNames.Any(
                name => string.Equals(bu.Name, name, StringComparison.OrdinalIgnoreCase)));

        if (secureBu is null)
        {
            // The loud NOT-RUN. It is not a pass: Passed is false for this verdict, and the caller is
            // contracted to print the message so the inert state is visible in every run.
            return new SecureBuAssertionOutcome(new[]
            {
                new SecureBuFinding(SecureBuVerdict.SecureBusinessUnitNotFound, InertMessage)
            });
        }

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
        findings.AddRange(EvaluateOwnerTeamMembershipClause(census));
        findings.AddRange(EvaluateOwnerRoleContainmentClause(census));

        return new SecureBuAssertionOutcome(findings);
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
                // nothing it does not already hold, so reporting it would be noise that buries the
                // principals for whom the team IS the only grant path.
                continue;
            }

            if (administrative)
            {
                // An administrative role reaching the secure BU through TEAM membership is not a
                // per-identity designation at all. Dataverse's default business-unit owner team holds
                // every user in that BU automatically and cannot be curated, so one role assignment on
                // it silently promotes every current AND future member. Live dev 2026-09-09: the root
                // `Spaarke` default owner team holds System Administrator, which is why a user with
                // ZERO directly-assigned roles reads the secure project. The allow-list must not
                // launder that into "they are administrators, so it is fine".
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
                    + $", which REACHES '{secureBu.Name}' ({secureBu.Id}). Every secure project is "
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
    /// Clause 2 — the owner team owns EVERY secure project, so at any depth a human member reads all of
    /// them by ownership. The hazard is what the team owns, not the role's depth (design §5.1a).
    /// </summary>
    private static IEnumerable<SecureBuFinding> EvaluateOwnerTeamMembershipClause(
        SecureBuRoleDepthCensus census)
    {
        if (census.SecureOwnerTeamHumanMembers.Count == 0)
        {
            yield break;
        }

        yield return new SecureBuFinding(
            SecureBuVerdict.OwnerTeamHasHumanMembers,
            $"The secure-project owner team has {census.SecureOwnerTeamHumanMembers.Count} HUMAN "
            + $"member(s): {string.Join(", ", census.SecureOwnerTeamHumanMembers)}. That team owns every "
            + "secure project, so each member reads all of them by ownership — the ownership-derived "
            + "access design §5.1a forbids. The team exists to hold ownership, not to confer access; "
            + "narrowing its role's depth does NOT relax this.");
    }

    /// <summary>Clause 3 — the ownership anchor must stay an anchor, not become a grant surface.</summary>
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
            $"'{SecureOwnerRoleName}' is held by {strays.Length} principal(s) other than the secure "
            + $"owner team: {string.Join(", ", strays)}. Design §5.1a assigns it to the owner team ALONE "
            + "— it exists so Dataverse will accept an assignment to that team, not to grant read to "
            + "anyone. A holder of this role reads whatever the secure team owns.");
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
    /// which is why clause 2 exists separately. An UNRECOGNISED depth mask fails closed: an unknown
    /// value is exactly what a future platform change would introduce, and guessing "narrow" would make
    /// this assertion quietly stop asserting.</para>
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
/// and team assignment by the query layer.
/// </summary>
/// <param name="RoleName">The role's name (shared by every business-unit copy of the role).</param>
/// <param name="PrivilegeName">One of <see cref="SecureBuRoleDepthAssertion.GuardedPrivileges"/>.</param>
/// <param name="Depth">The depth the ROOT role grants; child copies inherit it.</param>
/// <param name="AnchorBusinessUnitId">The BU the depth is evaluated from — see <c>Reaches</c>.</param>
/// <param name="PrincipalName">Display name, for the build log.</param>
/// <param name="PrincipalDomainName">UPN, for the build log.</param>
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
public sealed record SecureOwnerRoleHolder(string PrincipalName, bool IsSecureOwnerTeam);

/// <summary>Everything the assertion needs, read from a live environment by the query layer.</summary>
public sealed record SecureBuRoleDepthCensus(
    IReadOnlyList<BusinessUnitNode> BusinessUnits,
    IReadOnlyList<EffectiveGrant> Grants,
    IReadOnlyList<SecureOwnerRoleHolder> SecureOwnerRoleHolders,
    IReadOnlyList<string> SecureOwnerTeamHumanMembers);

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

    /// <summary>The secure owner team has human members, who therefore read everything it owns.</summary>
    OwnerTeamHasHumanMembers,

    /// <summary>The secure owner role escaped the owner team.</summary>
    SecureOwnerRoleHeldBeyondOwnerTeam
}

/// <summary>One violation, with the operator-actionable message that belongs in the build log.</summary>
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
    /// a run's headline verdict must not depend on which principal the census happened to enumerate
    /// first. A live dev run produces 16 findings across two verdicts; the summary line has to name the
    /// worse one.
    /// </remarks>
    public SecureBuVerdict Verdict => Findings.Count == 0
        ? SecureBuVerdict.Isolated
        : Findings.OrderBy(f => Severity(f.Verdict)).First().Verdict;

    private static int Severity(SecureBuVerdict verdict) => verdict switch
    {
        SecureBuVerdict.HumanPrincipalReachesSecureBusinessUnit => 0,
        SecureBuVerdict.OwnerTeamHasHumanMembers => 1,
        SecureBuVerdict.AdministrativeRoleHeldByTeam => 2,
        SecureBuVerdict.SecureOwnerRoleHeldBeyondOwnerTeam => 3,
        SecureBuVerdict.VacuousCensus => 4,
        SecureBuVerdict.SecureBusinessUnitNotFound => 5,
        _ => 6
    };

    /// <summary>
    /// True when the assertion could not assert anything because the secure BU does not exist. This is
    /// NOT a pass — the caller prints <see cref="Message"/> and halts as not-run.
    /// </summary>
    public bool IsInert => Verdict == SecureBuVerdict.SecureBusinessUnitNotFound;

    /// <summary>Every finding, newline-separated, for the failure message.</summary>
    public string Message => Findings.Count == 0
        ? "No principal reaches the secure business unit; the owner team is clean."
        : string.Join(
            Environment.NewLine + Environment.NewLine,
            Findings.Select((f, i) => $"[{i + 1}/{Findings.Count}] {f.Verdict}: {f.Message}"));
}
