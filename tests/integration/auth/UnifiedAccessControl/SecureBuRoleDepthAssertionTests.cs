using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// The spec NFR-05 STANDING assertion: no security role lets a non-administrator human reach the
/// Secure Record business unit, the business unit holds no users, its NAMED owner team resolves and has no members
/// of any kind, and the owner role has not escaped that team (clauses retargeted by task 144, #967).
/// </summary>
/// <remarks>
/// <para><b>Task 144 moved the evaluator into the BFF</b> (<see cref="SecureBuRoleDepthAssertion"/>,
/// <see cref="SecureBuRoleDepthCensusBuilder"/>) so the read-only <c>SecureRecordIsolationCensusJob</c> runs the same
/// census on a schedule (owner decision F2). These tests still own it: the perturbations prove every FAIL direction,
/// and the live test is the manual NFR-05 gate.</para>
///
/// <para><b>Standing, not an audit.</b> Design §5.2's role-depth census was true on 2026-08-20 and had
/// drifted before it was re-read. Every clause here is a configuration property one administrator can
/// undo in one click, in an environment nobody is watching — which is exactly how the original hole
/// (<c>Spaarke Basic User</c> holding <c>Deep</c> at the root BU) appeared. A role edit that re-opens
/// secure projects must turn a build red, not ship.</para>
///
/// <para><b>Two layers, deliberately</b> — the same shape as the NFR-04 negative canary (task 034),
/// for the same reason:
/// <list type="number">
///   <item><b>Perturbation (always runs, no tenant).</b> Feeds <see cref="SecureBuRoleDepthAssertion"/>
///   each fail-OPEN topology and asserts it reports a violation. An assertion nobody has watched fail
///   is an assertion nobody has verified — and this one cannot be watched fail in CI, because no
///   pipeline in this repo can reach Dataverse.</item>
///   <item><b>Live tenant.</b> The real census: business-unit tree, role-privilege depths, direct and
///   team-held assignments, owner-team membership. Gated on
///   <see cref="EnvironmentUrlVariable"/>; a query FAILURE is a test failure, never a skip.</item>
/// </list></para>
///
/// <para><b>Relationship to <c>ImpersonationNegativeCanaryTests</c> (task 034).</b> Different subject,
/// same gating pattern. The canary asserts impersonation is doing something; this asserts role
/// topology. Design §5.1a-2 notes the *empirical* form (provision a secure project, attempt an
/// impersonated read as a known non-admin) is the stronger proof; that is task 047's subject. This
/// assertion is the configuration form, and its job is to catch the edit BEFORE anyone has to notice a
/// disclosure — it enumerates every principal, which an empirical probe of one fixture user cannot.</para>
/// </remarks>
public class SecureBuRoleDepthAssertionTests
{
    private readonly ITestOutputHelper _output;

    public SecureBuRoleDepthAssertionTests(ITestOutputHelper output) => _output = output;

    /// <summary>Dataverse environment URL, e.g. <c>https://spaarkedev1.crm.dynamics.com</c>.</summary>
    public const string EnvironmentUrlVariable = "SPAARKE_NFR05_DATAVERSE_URL";

    /// <summary>
    /// Falls back to the NFR-04 canary's URL variable so an operator running the auth suite against a
    /// live environment configures ONE variable, not two (root CLAUDE.md §11 — default to reuse).
    /// </summary>
    public const string FallbackEnvironmentUrlVariable = ImpersonationCanaryEnvironment.ServiceUrlVariable;

    /// <summary>
    /// Asserts that this run IS an NFR-05 run, so an unconfigured environment becomes a FAILURE rather
    /// than a not-run. Set by an operator, or by any pipeline that gains Dataverse credentials.
    /// </summary>
    public const string RequiredVariable = "SPAARKE_NFR05_REQUIRED";

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    // Layer 1 — PERTURBATION. Runs everywhere, needs no tenant, no credential.
    //
    // These are the tests that make the gate real today. They prove each FAIL direction of the
    // assertion, so a future refactor that softens the reachability model turns this file red
    // immediately rather than at the next disclosure.
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// THE load-bearing perturbation, and the exact §5.2 topology: an ordinary role granting Deep from
    /// an ANCESTOR of the secure BU. The role never mentions the secure BU; reach comes from where the
    /// depth is anchored. A test that enumerated "roles scoped to the secure BU" would have passed here
    /// while every secure project was readable.
    /// </summary>
    [Fact]
    public void Evaluate_WhenDeepIsHeldByAHumanAtAnAncestorOfTheSecureBu_ReportsNotIsolated()
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(CensusWith(
            Grant("Spaarke Basic User", PrivilegeDepth.Deep, RootBu, "Test User 1", isHuman: true)));

        outcome.Passed.Should().BeFalse(
            "Deep held at the root BU reaches every descendant, and the secure BU is a descendant");
        outcome.Verdict.Should().Be(SecureBuVerdict.HumanPrincipalReachesSecureBusinessUnit);
        outcome.Message.Should().Contain("SECURE PROJECTS ARE NOT ISOLATED");
        outcome.Message.Should().Contain("Test User 1", "the offending principal belongs in the build log");
        outcome.Message.Should().Contain("Spaarke Basic User", "so does the role");
        outcome.Message.Should().Contain("Deep", "and the depth");
    }

    /// <summary>
    /// The passing shape design §5.2 chose (Fix A, validated in dev 2026-08-25): the SAME Deep depth,
    /// held in a SIBLING subtree, reaches nothing. The insight the fix rests on is "do not let ordinary
    /// users sit at or above the secure BU", not "reduce the depth" — so a gate that failed Deep
    /// unconditionally would be a wall, and would push the environment toward the wrong remedy.
    /// </summary>
    [Fact]
    public void Evaluate_WhenDeepIsHeldByAHumanInASiblingSubtree_Passes()
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(CensusWith(
            Grant("Spaarke Basic User", PrivilegeDepth.Deep, SiblingBu, "Test User 1", isHuman: true)));

        outcome.Passed.Should().BeTrue(outcome.Message);
        outcome.Verdict.Should().Be(SecureBuVerdict.Isolated);
    }

    /// <summary>Global reaches everything, wherever it is anchored — the second half of the §5.2 census.</summary>
    [Fact]
    public void Evaluate_WhenGlobalIsHeldByANonAdministratorHuman_ReportsNotIsolated()
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(CensusWith(
            Grant("Spaarke Reporting Access Viewer", PrivilegeDepth.Global, SiblingBu, "Jane Analyst", isHuman: true)));

        outcome.Verdict.Should().Be(SecureBuVerdict.HumanPrincipalReachesSecureBusinessUnit);
    }

    /// <summary>
    /// The documented, accepted exception (spec Unresolved Questions; design §5.2): Microsoft platform
    /// and application principals hold Global read. The exemption is on the PRINCIPAL, so it survives
    /// somebody renaming the role, and does not extend to a human who acquires the same role.
    /// </summary>
    [Fact]
    public void Evaluate_WhenGlobalIsHeldOnlyByApplicationPrincipals_Passes()
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(CensusWith(
            Grant("Service Reader", PrivilegeDepth.Global, RootBu, "# RelevanceSearch", isHuman: false),
            Grant("Service Writer", PrivilegeDepth.Global, RootBu, "# mi-bff-api-dev", isHuman: false)));

        outcome.Passed.Should().BeTrue(outcome.Message);
    }

    /// <summary>The pinned administrative allow-list, applied to a deliberate per-identity assignment.</summary>
    [Fact]
    public void Evaluate_WhenAnAllowListedAdministrativeRoleIsAssignedDirectly_Passes()
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(CensusWith(
            Grant("System Administrator", PrivilegeDepth.Global, RootBu, "Ralph Schroeder", isHuman: true)));

        outcome.Passed.Should().BeTrue(outcome.Message);
    }

    /// <summary>
    /// The allow-list must not be reachable in bulk. A default business-unit owner team contains every
    /// user in that BU automatically and its membership cannot be curated, so one role assignment on it
    /// promotes every current and future member. This is live in dev today (root <c>Spaarke</c> default
    /// team holds System Administrator), which is why a user with zero directly-assigned roles reads the
    /// secure project — and why allow-listing by role name alone would have gone green on it.
    /// </summary>
    [Fact]
    public void Evaluate_WhenAnAllowListedAdministrativeRoleIsHeldThroughATeam_ReportsAdministrativeRoleHeldByTeam()
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(CensusWith(
            Grant("System Administrator", PrivilegeDepth.Global, RootBu, "Jake Schroeder", isHuman: true, viaTeam: "Spaarke")));

        outcome.Passed.Should().BeFalse("bulk team membership is not a per-identity administrator designation");
        outcome.Verdict.Should().Be(SecureBuVerdict.AdministrativeRoleHeldByTeam);
        outcome.Message.Should().Contain("Spaarke", "the team that conferred it belongs in the message");
    }

    /// <summary>
    /// <c>Secure Record Owner</c> at User depth on the owner team is the design §5.1a steady state and
    /// must pass. It is exempted STRUCTURALLY (Basic reaches no business unit), never by name — see the
    /// next test for why that distinction is load-bearing.
    /// </summary>
    [Fact]
    public void Evaluate_WhenTheSecureOwnerRoleIsBasicDepthOnTheOwnerTeam_Passes()
    {
        var census = Census(
            grants: new[] { Grant(SecureBuRoleDepthAssertion.SecureOwnerRoleName, PrivilegeDepth.Basic, SecureBu, "Secure Record Owners team", isHuman: false) },
            holders: new[] { new SecureOwnerRoleHolder($"team '{OwnerTeamName}'", IsSecureOwnerTeam: true) });

        var outcome = SecureBuRoleDepthAssertion.Evaluate(census);

        outcome.Passed.Should().BeTrue(outcome.Message);
    }

    /// <summary>
    /// Widening the owner role from User to Business Unit depth, and handing it to a human, must fail on
    /// BOTH counts: the depth now reaches the secure BU, and the role escaped the owner team. Pins that
    /// the role is not on the allow-list — allow-listing it by name would have hidden exactly this edit.
    /// </summary>
    [Fact]
    public void Evaluate_WhenTheSecureOwnerRoleIsWidenedAndGivenToAHuman_ReportsBothViolations()
    {
        var census = Census(
            grants: new[] { Grant(SecureBuRoleDepthAssertion.SecureOwnerRoleName, PrivilegeDepth.Local, SecureBu, "Contract Paralegal", isHuman: true) },
            holders: new[]
            {
                new SecureOwnerRoleHolder($"team '{OwnerTeamName}'", IsSecureOwnerTeam: true),
                new SecureOwnerRoleHolder("Contract Paralegal", IsSecureOwnerTeam: false)
            });

        var outcome = SecureBuRoleDepthAssertion.Evaluate(census);

        outcome.Findings.Select(f => f.Verdict).Should().BeEquivalentTo(new[]
        {
            SecureBuVerdict.HumanPrincipalReachesSecureBusinessUnit,
            SecureBuVerdict.SecureOwnerRoleHeldBeyondOwnerTeam
        });
    }

    /// <summary>
    /// The NAMED owner team owns EVERY secure record, so one member reads all of them by ownership —
    /// no depth is involved and narrowing the role would not relax it (design §5.1a). This clause is the
    /// half of NFR-05 that the depth model structurally cannot see. Task 144: ANY member fails it, human or
    /// application user — both are seeded, and each must be named.
    /// </summary>
    [Fact]
    public void Evaluate_WhenTheNamedOwnerTeamHasAnyMember_ReportsOwnerTeamHasMembers_NamingEach()
    {
        var census = Census(
            grants: new[] { Grant(SecureBuRoleDepthAssertion.SecureOwnerRoleName, PrivilegeDepth.Basic, SecureBu, "Secure Record Owners team", isHuman: false) },
            holders: new[] { new SecureOwnerRoleHolder($"team '{OwnerTeamName}'", IsSecureOwnerTeam: true) },
            members: new[] { "Contract Paralegal (human)", "# integration-app (application user)" });

        var outcome = SecureBuRoleDepthAssertion.Evaluate(census);

        outcome.Verdict.Should().Be(SecureBuVerdict.OwnerTeamHasMembers);
        outcome.Message.Should().Contain("Contract Paralegal").And.Contain("# integration-app");
    }

    /// <summary>The other direction of clause 2: the same census with the team empty passes.</summary>
    [Fact]
    public void Evaluate_WhenTheNamedOwnerTeamIsEmpty_DoesNotReportMembers()
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(Census(
            grants: new[] { Grant(SecureBuRoleDepthAssertion.SecureOwnerRoleName, PrivilegeDepth.Basic, SecureBu, "Secure Record Owners team", isHuman: false) },
            holders: new[] { new SecureOwnerRoleHolder($"team '{OwnerTeamName}'", IsSecureOwnerTeam: true) }));

        outcome.Passed.Should().BeTrue(outcome.Message);
    }

    /// <summary>
    /// Clause 2b (task 144): the named owner team must resolve to exactly one non-default Owner team. A census of a
    /// team that does not exist would report "no members" vacuously — so a missing or duplicated team is itself a
    /// failure, never a pass.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Evaluate_WhenTheNamedOwnerTeamDoesNotResolveToExactlyOne_ReportsSecureOwnerTeamNotResolved(int matches)
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(Census(
            grants: new[] { Grant("Spaarke Basic User", PrivilegeDepth.Deep, SiblingBu, "Test User 1", isHuman: true) },
            matches: matches));

        outcome.Verdict.Should().Be(SecureBuVerdict.SecureOwnerTeamNotResolved);
        outcome.Message.Should().Contain(OwnerTeamName).And.Contain($"has {matches} non-default");
    }

    /// <summary>The other direction of clause 2b: exactly one named team, no finding.</summary>
    [Fact]
    public void Evaluate_WhenTheNamedOwnerTeamResolvesToExactlyOne_DoesNotReportIt()
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(Census(
            grants: new[] { Grant("Spaarke Basic User", PrivilegeDepth.Deep, SiblingBu, "Test User 1", isHuman: true) },
            matches: 1));

        outcome.Findings.Select(f => f.Verdict).Should().NotContain(SecureBuVerdict.SecureOwnerTeamNotResolved);
    }

    /// <summary>
    /// Clause 4 (task 144) — the no-user business unit. A user in the secure BU reads every secure record by
    /// business-unit DEPTH whoever owns it, which a named team alone does not prevent. Any kind fails it: a disabled
    /// user can be re-enabled and an application user reads by depth like anyone else — each is named.
    /// </summary>
    [Fact]
    public void Evaluate_WhenAnySystemUserSitsInTheSecureBusinessUnit_ReportsSecureBusinessUnitHasUsers_NamingEach()
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(Census(
            grants: new[] { Grant("Spaarke Basic User", PrivilegeDepth.Deep, SiblingBu, "Test User 1", isHuman: true) },
            users: new[] { "Moved Attorney (human)", "Old Paralegal (disabled)", "# sync-app (application user)" }));

        outcome.Verdict.Should().Be(SecureBuVerdict.SecureBusinessUnitHasUsers);
        outcome.Message.Should().Contain("Moved Attorney").And.Contain("Old Paralegal").And.Contain("# sync-app");
    }

    /// <summary>The other direction of clause 4: no user in the BU, no finding.</summary>
    [Fact]
    public void Evaluate_WhenTheSecureBusinessUnitHoldsNoUsers_DoesNotReportUsers()
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(Census(
            grants: new[] { Grant("Spaarke Basic User", PrivilegeDepth.Deep, SiblingBu, "Test User 1", isHuman: true) }));

        outcome.Findings.Select(f => f.Verdict).Should().NotContain(SecureBuVerdict.SecureBusinessUnitHasUsers);
        outcome.Passed.Should().BeTrue(outcome.Message);
    }

    /// <summary>
    /// Clause 3, retargeted (task 144): the owner role on the business unit's DEFAULT team is a finding — it keeps the
    /// retired owner a legal assignment target. The cutover in guide §4 ends with the role on the named team alone.
    /// </summary>
    [Fact]
    public void Evaluate_WhenTheOwnerRoleIsStillOnTheDefaultTeam_ReportsSecureOwnerRoleHeldBeyondOwnerTeam()
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(Census(
            grants: new[] { Grant("Spaarke Basic User", PrivilegeDepth.Deep, SiblingBu, "Test User 1", isHuman: true) },
            holders: new[]
            {
                new SecureOwnerRoleHolder($"team '{OwnerTeamName}'", IsSecureOwnerTeam: true),
                new SecureOwnerRoleHolder("team 'Secure Record' (the business unit's DEFAULT team)", IsSecureOwnerTeam: false)
            }));

        outcome.Verdict.Should().Be(SecureBuVerdict.SecureOwnerRoleHeldBeyondOwnerTeam);
        outcome.Message.Should().Contain("DEFAULT team");
    }

    /// <summary>
    /// Two business units carry the configured Secure Record name (task 144, verifier round 2). The census around the
    /// FIRST one is clean, so an evaluator that graded the first match would report isolated while the second — which
    /// could hold users or be reached by depth — went unexamined. Provisioning, the resolver and registration refuse on
    /// this ambiguity; the assertion must too: a finding naming both, never a pass and never inert.
    /// </summary>
    [Fact]
    public void Evaluate_WhenTwoBusinessUnitsCarryTheSecureName_ReportsAmbiguous_NeverIsolated()
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(Census(
            grants: new[] { Grant("Spaarke Basic User", PrivilegeDepth.Deep, SiblingBu, "Test User 1", isHuman: true) },
            businessUnits: BusinessUnits.Append(new BusinessUnitNode(SecondSecureNamedBu, "Secure Record", SiblingBu)).ToArray()));

        outcome.Passed.Should().BeFalse("which business unit holds the secure records cannot be decided");
        outcome.IsInert.Should().BeFalse("an ambiguous secure BU is an unknown isolation state, not an absent one");
        outcome.Verdict.Should().Be(SecureBuVerdict.SecureBusinessUnitAmbiguous);
        outcome.Message.Should().Contain(SecureBu.ToString()).And.Contain(SecondSecureNamedBu.ToString());
    }

    // ── The shared census builder (the job and the live gate compose through it) ─────────────────────

    /// <summary>
    /// The builder's half of the ambiguity rule: with two business units carrying the secure name it lists the users
    /// of BOTH, not of whichever came first — the census over-reports rather than hiding the second unit.
    /// </summary>
    [Fact]
    public void Build_WhenTwoBusinessUnitsCarryTheSecureName_ListsUsersInEachOfThem_NotJustTheFirst()
    {
        var userInSecond = Guid.NewGuid();
        var someRole = Guid.NewGuid();

        var census = SecureBuRoleDepthCensusBuilder.Build(
            SecureRecordOwnerTeam.DefaultBusinessUnitName,
            OwnerTeamName,
            BusinessUnits.Append(new BusinessUnitNode(SecondSecureNamedBu, "Secure Record", SiblingBu)).ToArray(),
            new Dictionary<(Guid, string), PrivilegeDepth> { [(someRole, "prvReadsprk_Project")] = PrivilegeDepth.Basic },
            new[] { new CensusRole(someRole, "Spaarke Basic User", RootBu, someRole) },
            new[] { new CensusUser(userInSecond, "Second Unit User", "second@spaarke.com", IsHuman: true, SecondSecureNamedBu, new[] { someRole }) },
            Array.Empty<CensusTeam>(),
            SecureRecordOwnerRoleSet.Embedded,
            SecureBuRoleDepthCensusBuilder.CensusPrivilegeNames(SecureRecordOwnerRoleSet.Embedded));

        census.SecureBusinessUnitUsers.Should().ContainSingle().Which.Should().Contain("Second Unit User");
        SecureBuRoleDepthAssertion.Evaluate(census).Verdict.Should().Be(SecureBuVerdict.SecureBusinessUnitAmbiguous);
    }

    /// <summary>
    /// The builder decides which team is THE owner team: the non-default Owner team with the configured name in the
    /// secure BU. The default team holding the owner role is therefore a stray, every member of the named team is
    /// counted whatever its kind, and every user in the BU is listed — enabled or disabled, human or application.
    /// </summary>
    [Fact]
    public void Build_MarksOnlyTheNamedTeamAsTheOwner_AndCountsEveryMemberAndEveryBusinessUnitUser()
    {
        var ownerRole = Guid.NewGuid();
        var namedTeam = Guid.NewGuid();
        var defaultTeam = Guid.NewGuid();
        var humanMember = Guid.NewGuid();
        var appMember = Guid.NewGuid();
        var disabledInBu = Guid.NewGuid();

        var census = SecureBuRoleDepthCensusBuilder.Build(
            SecureRecordOwnerTeam.DefaultBusinessUnitName,
            OwnerTeamName,
            BusinessUnits,
            new Dictionary<(Guid, string), PrivilegeDepth> { [(ownerRole, "prvReadsprk_Project")] = PrivilegeDepth.Basic },
            new[] { new CensusRole(ownerRole, SecureBuRoleDepthAssertion.SecureOwnerRoleName, SecureBu, ownerRole) },
            new[]
            {
                new CensusUser(humanMember, "Member Human", "member@spaarke.com", IsHuman: true, SiblingBu, Array.Empty<Guid>()),
                new CensusUser(appMember, "# member-app", null, IsHuman: false, RootBu, Array.Empty<Guid>()),
                new CensusUser(disabledInBu, "Disabled In Bu", "old@spaarke.com", IsHuman: false, SecureBu, Array.Empty<Guid>())
            },
            new[]
            {
                new CensusTeam(defaultTeam, "Secure Record", SecureBu, IsDefault: true, TeamType: 0, new[] { ownerRole }, Array.Empty<Guid>()),
                new CensusTeam(namedTeam, OwnerTeamName, SecureBu, IsDefault: false, TeamType: 0, new[] { ownerRole }, new[] { humanMember, appMember })
            },
            SecureRecordOwnerRoleSet.Embedded,
            SecureBuRoleDepthCensusBuilder.CensusPrivilegeNames(SecureRecordOwnerRoleSet.Embedded));

        census.SecureOwnerTeamMatches.Should().Be(1);
        census.SecureOwnerRoleHolders.Should().ContainSingle(h => h.IsSecureOwnerTeam)
            .Which.PrincipalName.Should().Contain(OwnerTeamName);
        census.SecureOwnerRoleHolders.Should().ContainSingle(h => !h.IsSecureOwnerTeam)
            .Which.PrincipalName.Should().Contain("DEFAULT");
        census.SecureOwnerTeamMembers.Should().HaveCount(2, "an application-user member counts exactly like a human one");
        census.SecureBusinessUnitUsers.Should().ContainSingle().Which.Should().Contain("Disabled In Bu");

        var outcome = SecureBuRoleDepthAssertion.Evaluate(census);
        outcome.Findings.Select(f => f.Verdict).Should().Contain(new[]
        {
            SecureBuVerdict.OwnerTeamHasMembers,
            SecureBuVerdict.SecureBusinessUnitHasUsers,
            SecureBuVerdict.SecureOwnerRoleHeldBeyondOwnerTeam
        });
    }

    /// <summary>
    /// The work-assignment Read privilege is guarded (task 144): work assignments carry <c>sprk_issecure</c> and are
    /// provisionable, so a role reaching the secure BU on them discloses secure work just as surely. The casing is the
    /// schema name's, verified live — the census looks depths up by exact name.
    /// </summary>
    [Fact]
    public void GuardedPrivileges_CoverEveryTableThatCarriesTheSecureFlag()
    {
        SecureBuRoleDepthAssertion.GuardedPrivileges.Should().BeEquivalentTo(
            "prvReadsprk_Project", "prvReadsprk_Matter", "prvReadsprk_WorkAssignment");
    }

    // ── Clause 5 (task 145, #1046): the owner role covers the codified set ───────────────────────────

    /// <summary>
    /// THE clause-5 perturbation. The owner role holds Read at Basic on every codified table but one; Dataverse would
    /// refuse every assignment of that table's rows to the owner team, so secure children of it fail closed. The census
    /// must name the table and its privilege — and only that table.
    /// </summary>
    [Fact]
    public void Evaluate_WhenTheOwnerRoleLacksOneCodifiedTable_ReportsItNamingTheTable()
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(Census(
            grants: new[] { Grant("Spaarke Basic User", PrivilegeDepth.Deep, SiblingBu, "Test User 1", isHuman: true) },
            coverage: CoverageOfTheWholeSet(overrides: ("sprk_todo", null))));

        outcome.Passed.Should().BeFalse("a secure To Do cannot be owned by the team");
        outcome.Verdict.Should().Be(SecureBuVerdict.SecureOwnerRoleLacksCodifiedPrivilege);
        outcome.Findings.Should().ContainSingle()
            .Which.Message.Should().Contain("prvReadsprk_Todo").And.Contain("sprk_todo").And.Contain("FAILS CLOSED");
    }

    /// <summary>The other direction: every codified table at Basic on exactly one owner role passes clause 5.</summary>
    [Fact]
    public void Evaluate_WhenTheOwnerRoleCoversTheWholeSetAtBasic_DoesNotReportCoverage()
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(Census(
            grants: new[] { Grant("Spaarke Basic User", PrivilegeDepth.Deep, SiblingBu, "Test User 1", isHuman: true) },
            coverage: CoverageOfTheWholeSet()));

        outcome.Passed.Should().BeTrue(outcome.Message);
    }

    /// <summary>
    /// A codified table held WIDER than Basic is a finding too, as <c>-Verify</c> treats it: the team's ownership would
    /// reach further than it owns, and the depth model of clause 1 does not see it (the team is not a human).
    /// </summary>
    [Fact]
    public void Evaluate_WhenTheOwnerRoleHoldsACodifiedTableWiderThanBasic_ReportsTheDepth()
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(Census(
            grants: new[] { Grant("Spaarke Basic User", PrivilegeDepth.Deep, SiblingBu, "Test User 1", isHuman: true) },
            coverage: CoverageOfTheWholeSet(overrides: ("sprk_document", PrivilegeDepth.Local))));

        outcome.Verdict.Should().Be(SecureBuVerdict.SecureOwnerRoleLacksCodifiedPrivilege);
        outcome.Message.Should().Contain("prvReadsprk_Document").And.Contain("Local");
    }

    /// <summary>
    /// No owner role in the secure BU, or two, cannot be graded — a finding, never a pass. The script refuses the same
    /// shapes ("exactly one role of the configured name lives in it").
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Evaluate_WhenTheSecureBuDoesNotHoldExactlyOneOwnerRole_ReportsCoverageUngradeable(int ownerRoles)
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(Census(
            grants: new[] { Grant("Spaarke Basic User", PrivilegeDepth.Deep, SiblingBu, "Test User 1", isHuman: true) },
            coverage: CoverageOfTheWholeSet(ownerRoles)));

        outcome.Findings.Should().ContainSingle()
            .Which.Message.Should().Contain($"holds {ownerRoles} role(s) named");
    }

    /// <summary>An empty coverage list is a set that was never loaded — UNKNOWN, refused as a pass.</summary>
    [Fact]
    public void Evaluate_WhenTheCensusCarriesNoCodifiedTables_RefusesToPassClauseFive()
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(Census(
            grants: new[] { Grant("Spaarke Basic User", PrivilegeDepth.Deep, SiblingBu, "Test User 1", isHuman: true) },
            coverage: new SecureOwnerRoleCoverage(1, Array.Empty<OwnerRoleTableCoverage>())));

        outcome.Verdict.Should().Be(SecureBuVerdict.SecureOwnerRoleLacksCodifiedPrivilege);
        outcome.Message.Should().Contain("UNKNOWN");
    }

    /// <summary>
    /// The builder grades the owner role's ROOT copy in the secure BU against the set: a role holding Basic on every
    /// codified privilege except the invoice one yields exactly one gap, naming <c>sprk_invoice</c>.
    /// </summary>
    [Fact]
    public void Build_GradesTheOwnerRoleInTheSecureBuAgainstTheCodifiedSet()
    {
        var ownerRole = Guid.NewGuid();
        var namedTeam = Guid.NewGuid();
        var set = SecureRecordOwnerRoleSet.Embedded;
        var depths = set.Tables
            .Where(t => t.LogicalName != "sprk_invoice")
            .ToDictionary(t => (ownerRole, t.PrivilegeName), _ => PrivilegeDepth.Basic);

        var census = SecureBuRoleDepthCensusBuilder.Build(
            SecureRecordOwnerTeam.DefaultBusinessUnitName,
            OwnerTeamName,
            BusinessUnits,
            depths,
            new[] { new CensusRole(ownerRole, SecureBuRoleDepthAssertion.SecureOwnerRoleName, SecureBu, ownerRole) },
            Array.Empty<CensusUser>(),
            new[] { new CensusTeam(namedTeam, OwnerTeamName, SecureBu, IsDefault: false, TeamType: 0, new[] { ownerRole }, Array.Empty<Guid>()) },
            set,
            SecureBuRoleDepthCensusBuilder.CensusPrivilegeNames(set));

        census.OwnerRoleCoverage.OwnerRoleCount.Should().Be(1);
        census.OwnerRoleCoverage.InheritedFromRootRoleId.Should().BeNull("the role was created in the secure BU");
        census.OwnerRoleCoverage.Tables.Should().HaveCount(set.Tables.Count);
        census.OwnerRoleCoverage.Tables.Should().ContainSingle(t => t.HeldDepth == null)
            .Which.LogicalName.Should().Be("sprk_invoice");
    }

    /// <summary>
    /// Only the owner role IN the secure BU is graded. A role carrying the same name in a sibling business unit — here
    /// one that even covers the whole set — is a different role: counting it would make the secure BU look like it holds
    /// two owner roles, and grading it would hide the secure role's real gap.
    /// </summary>
    [Fact]
    public void Build_WhenASameNamedRoleLivesInASiblingBu_GradesOnlyTheSecureBuRole()
    {
        var ownerRole = Guid.NewGuid();
        var siblingRole = Guid.NewGuid();
        var namedTeam = Guid.NewGuid();
        var set = SecureRecordOwnerRoleSet.Embedded;
        var depths = set.Tables
            .Where(t => t.LogicalName != "sprk_invoice")
            .ToDictionary(t => (ownerRole, t.PrivilegeName), _ => PrivilegeDepth.Basic);
        foreach (var table in set.Tables)
        {
            depths[(siblingRole, table.PrivilegeName)] = PrivilegeDepth.Basic;
        }

        var census = SecureBuRoleDepthCensusBuilder.Build(
            SecureRecordOwnerTeam.DefaultBusinessUnitName,
            OwnerTeamName,
            BusinessUnits,
            depths,
            new[]
            {
                new CensusRole(siblingRole, SecureBuRoleDepthAssertion.SecureOwnerRoleName, SiblingBu, siblingRole),
                new CensusRole(ownerRole, SecureBuRoleDepthAssertion.SecureOwnerRoleName, SecureBu, ownerRole)
            },
            Array.Empty<CensusUser>(),
            new[] { new CensusTeam(namedTeam, OwnerTeamName, SecureBu, IsDefault: false, TeamType: 0, new[] { ownerRole }, Array.Empty<Guid>()) },
            set,
            SecureBuRoleDepthCensusBuilder.CensusPrivilegeNames(set));

        census.OwnerRoleCoverage.OwnerRoleCount.Should().Be(1, "the sibling BU's role is not in the secure BU");
        census.OwnerRoleCoverage.Tables.Should().ContainSingle(t => t.HeldDepth == null)
            .Which.LogicalName.Should().Be("sprk_invoice", "the SECURE BU's role is the one graded, and it lacks invoice");
    }

    /// <summary>
    /// A <c>Secure Record Owner</c> role created in the ROOT business unit reaches the secure BU only as a replica (its
    /// copy there has <c>roleid != parentrootroleid</c>). The script and the provisioning handler design refuse that
    /// shape; the census must too — a finding naming the root role, even though the root copy covers the whole set.
    /// </summary>
    [Fact]
    public void Build_WhenTheSecureBuRoleIsAReplicaOfAnAncestorRole_ReportsIt_AndDoesNotGradeIt()
    {
        var rootCopy = Guid.NewGuid();
        var secureCopy = Guid.NewGuid();
        var siblingCopy = Guid.NewGuid();
        var namedTeam = Guid.NewGuid();
        var set = SecureRecordOwnerRoleSet.Embedded;

        var census = SecureBuRoleDepthCensusBuilder.Build(
            SecureRecordOwnerTeam.DefaultBusinessUnitName,
            OwnerTeamName,
            BusinessUnits,
            set.Tables.ToDictionary(t => (rootCopy, t.PrivilegeName), _ => PrivilegeDepth.Basic),
            new[]
            {
                new CensusRole(rootCopy, SecureBuRoleDepthAssertion.SecureOwnerRoleName, RootBu, rootCopy),
                new CensusRole(secureCopy, SecureBuRoleDepthAssertion.SecureOwnerRoleName, SecureBu, rootCopy),
                new CensusRole(siblingCopy, SecureBuRoleDepthAssertion.SecureOwnerRoleName, SiblingBu, rootCopy)
            },
            Array.Empty<CensusUser>(),
            new[] { new CensusTeam(namedTeam, OwnerTeamName, SecureBu, IsDefault: false, TeamType: 0, new[] { secureCopy }, Array.Empty<Guid>()) },
            set,
            SecureBuRoleDepthCensusBuilder.CensusPrivilegeNames(set));

        census.OwnerRoleCoverage.OwnerRoleCount.Should().Be(1);
        census.OwnerRoleCoverage.InheritedFromRootRoleId.Should().Be(rootCopy);

        var finding = SecureBuRoleDepthAssertion.Evaluate(census with
        {
            Grants = new[] { Grant("Spaarke Basic User", PrivilegeDepth.Deep, SiblingBu, "Test User 1", isHuman: true) }
        }).Findings.Should().ContainSingle().Subject;
        finding.Verdict.Should().Be(SecureBuVerdict.SecureOwnerRoleLacksCodifiedPrivilege);
        finding.Message.Should().Contain("REPLICA").And.Contain(rootCopy.ToString());
    }

    /// <summary>
    /// Clause 5 fails CLOSED, so it must never headline a run in which someone can READ secure records. Seeded with a
    /// clause-1 exposure and a clause-5 gap together, the headline is the exposure — the verdict the job heartbeat and
    /// the live gate's summary line print.
    /// </summary>
    [Fact]
    public void Evaluate_WhenAnExposureAndACodifiedGapCoexist_HeadlinesTheExposure()
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(Census(
            grants: new[] { Grant("Spaarke Basic User", PrivilegeDepth.Deep, RootBu, "Test User 1", isHuman: true) },
            coverage: CoverageOfTheWholeSet(overrides: ("sprk_invoice", null))));

        outcome.Findings.Select(f => f.Verdict).Should().BeEquivalentTo(new[]
        {
            SecureBuVerdict.HumanPrincipalReachesSecureBusinessUnit,
            SecureBuVerdict.SecureOwnerRoleLacksCodifiedPrivilege
        });
        outcome.Verdict.Should().Be(SecureBuVerdict.HumanPrincipalReachesSecureBusinessUnit);
    }

    /// <summary>
    /// The same ranking against EVERY verdict that means exposure or an unknown isolation state: each outranks a clause-5
    /// finding, whichever was enumerated first.
    /// </summary>
    [Theory]
    [InlineData(SecureBuVerdict.HumanPrincipalReachesSecureBusinessUnit)]
    [InlineData(SecureBuVerdict.SecureBusinessUnitHasUsers)]
    [InlineData(SecureBuVerdict.OwnerTeamHasMembers)]
    [InlineData(SecureBuVerdict.AdministrativeRoleHeldByTeam)]
    [InlineData(SecureBuVerdict.SecureOwnerRoleHeldBeyondOwnerTeam)]
    [InlineData(SecureBuVerdict.SecureOwnerTeamNotResolved)]
    [InlineData(SecureBuVerdict.SecureBusinessUnitAmbiguous)]
    public void Verdict_RanksACodifiedGapBelowEveryExposure(SecureBuVerdict exposure)
    {
        var outcome = new SecureBuAssertionOutcome(new[]
        {
            new SecureBuFinding(SecureBuVerdict.SecureOwnerRoleLacksCodifiedPrivilege, "gap"),
            new SecureBuFinding(exposure, "exposure")
        });

        outcome.Verdict.Should().Be(exposure);
    }

    /// <summary>
    /// The ONE list's own invariants, read through the embedded copy every runner uses: it is for the role and BU this
    /// assertion names, and it covers every root table that carries <c>sprk_issecure</c> — a set without one of them
    /// would let provisioning's assignment of that root fail while clause 5 reported "covered".
    /// </summary>
    [Fact]
    public void EmbeddedSet_IsForThisRoleAndBu_AndCoversEveryGuardedRootTable()
    {
        var set = SecureRecordOwnerRoleSet.Embedded;

        set.RoleName.Should().Be(SecureBuRoleDepthAssertion.SecureOwnerRoleName);
        set.BusinessUnitName.Should().Be(SecureRecordOwnerTeam.DefaultBusinessUnitName);
        set.Tables.Where(t => t.Kind == "root").Select(t => t.PrivilegeName)
            .Should().BeEquivalentTo(SecureBuRoleDepthAssertion.GuardedPrivileges);
        set.Tables.Should().OnlyContain(t => t.Kind == "root" || t.Kind == "child");
    }

    /// <summary>
    /// The C# reader refuses what the script refuses (justified beyond the task's test list: the census now PARSES the
    /// file, and a parse that accepted a wider access type or an empty list would grade a widened role as covered).
    /// </summary>
    [Theory]
    [InlineData("""{"schemaVersion":1,"roleName":"r","businessUnitName":"b","access":"Write","depth":"Basic","tables":[{"logicalName":"a","privilegeName":"p","reason":"x","evidence":"y"}]}""", "access 'Write'")]
    [InlineData("""{"schemaVersion":1,"roleName":"r","businessUnitName":"b","access":"Read","depth":"Deep","tables":[{"logicalName":"a","privilegeName":"p","reason":"x","evidence":"y"}]}""", "depth 'Deep'")]
    [InlineData("""{"schemaVersion":1,"roleName":"r","businessUnitName":"b","access":"Read","depth":"Basic","tables":[]}""", "lists no tables")]
    [InlineData("""{"schemaVersion":1,"roleName":"r","businessUnitName":"b","access":"Read","depth":"Basic","tables":[{"logicalName":"a","privilegeName":"p","reason":"x","evidence":""}]}""", "has no 'evidence'")]
    [InlineData("""{"schemaVersion":1,"roleName":"r","businessUnitName":"b","access":"Read","depth":"Basic","tables":[{"logicalName":"a","privilegeName":"p","reason":"x","evidence":"y"},{"logicalName":"a","privilegeName":"p","reason":"x","evidence":"y"}]}""", "twice")]
    [InlineData("""{"schemaVersion":2,"roleName":"r","businessUnitName":"b","access":"Read","depth":"Basic","tables":[{"logicalName":"a","privilegeName":"p","reason":"x","evidence":"y"}]}""", "schemaVersion")]
    public void Parse_RefusesWhatTheScriptRefuses(string json, string reason)
    {
        var act = () => SecureRecordOwnerRoleSet.Parse(json);

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain(reason);
    }

    /// <summary>
    /// The guarded root-table Reads drive clause 1, so a census that did not resolve each of them EXACTLY stops — a
    /// mis-cased or missing one would under-report reach. Extra rows (the codified set's privileges) are expected.
    /// </summary>
    [Fact]
    public void RequireGuardedPrivilegesResolved_WhenAGuardedNameIsMisCasedOrMissing_Throws_AndPassesWithExtraRows()
    {
        var misCased = () => SecureBuRoleDepthCensusBuilder.RequireGuardedPrivilegesResolved(
            new[] { "prvReadsprk_Project", "prvReadsprk_matter", "prvReadsprk_WorkAssignment" });
        var missing = () => SecureBuRoleDepthCensusBuilder.RequireGuardedPrivilegesResolved(
            new[] { "prvReadsprk_Project", "prvReadsprk_WorkAssignment" });
        var withExtras = () => SecureBuRoleDepthCensusBuilder.RequireGuardedPrivilegesResolved(
            SecureBuRoleDepthAssertion.GuardedPrivileges.Append("prvReadsprk_Todo").ToArray());

        misCased.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("missing: prvReadsprk_Matter");
        missing.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("missing: prvReadsprk_Matter");
        withExtras.Should().NotThrow();
    }

    /// <summary>
    /// A codified privilege the environment does not have by that EXACT name (a table not installed there, a rename, a
    /// mis-cased file entry) is a clause-5 finding naming it — never a throw, which would blind clauses 1-4 (the
    /// exposure checks) on every run in that environment.
    /// </summary>
    [Fact]
    public void Build_WhenACodifiedPrivilegeIsNotInTheEnvironment_ReportsItAsAFinding_NotAThrow()
    {
        var ownerRole = Guid.NewGuid();
        var namedTeam = Guid.NewGuid();
        var set = SecureRecordOwnerRoleSet.Embedded;
        var present = set.Tables.Where(t => t.LogicalName != "sprk_memo").ToArray();

        var census = SecureBuRoleDepthCensusBuilder.Build(
            SecureRecordOwnerTeam.DefaultBusinessUnitName,
            OwnerTeamName,
            BusinessUnits,
            present.ToDictionary(t => (ownerRole, t.PrivilegeName), _ => PrivilegeDepth.Basic),
            new[] { new CensusRole(ownerRole, SecureBuRoleDepthAssertion.SecureOwnerRoleName, SecureBu, ownerRole) },
            Array.Empty<CensusUser>(),
            new[] { new CensusTeam(namedTeam, OwnerTeamName, SecureBu, IsDefault: false, TeamType: 0, new[] { ownerRole }, Array.Empty<Guid>()) },
            set,
            present.Select(t => t.PrivilegeName).ToArray());

        var gap = census.OwnerRoleCoverage.Tables.Should().ContainSingle(t => !t.ExistsInEnvironment).Subject;
        gap.LogicalName.Should().Be("sprk_memo");

        var finding = SecureBuRoleDepthAssertion.Evaluate(census with
        {
            Grants = new[] { Grant("Spaarke Basic User", PrivilegeDepth.Deep, SiblingBu, "Test User 1", isHuman: true) }
        }).Findings.Should().ContainSingle().Subject;
        finding.Verdict.Should().Be(SecureBuVerdict.SecureOwnerRoleLacksCodifiedPrivilege);
        finding.Message.Should().Contain("prvReadsprk_Memo").And.Contain("no privilege of exactly that name");
    }

    /// <summary>
    /// A designated administrator who ALSO picks the same role up from a team is not a second finding —
    /// the team path confers nothing they do not already hold. Reporting it would bury the principals
    /// for whom the team is the only grant path, which is the whole point of the check.
    /// </summary>
    [Fact]
    public void Evaluate_WhenADirectAdministratorAlsoInheritsTheSameRoleFromATeam_DoesNotReportIt()
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(CensusWith(
            Grant("System Administrator", PrivilegeDepth.Global, RootBu, "Ralph Schroeder", isHuman: true),
            Grant("System Administrator", PrivilegeDepth.Global, RootBu, "Ralph Schroeder", isHuman: true, viaTeam: "Spaarke")));

        outcome.Passed.Should().BeTrue(outcome.Message);
    }

    /// <summary>
    /// …but the redundancy check must match the PRINCIPAL, not just the role. Dev has three enabled
    /// identities displaying as <c>Ralph Schroeder</c>; letting one identity's direct assignment excuse
    /// another's team-conferred one would silently exempt the very case being hunted.
    /// </summary>
    [Fact]
    public void Evaluate_WhenADifferentIdentityHoldsTheRoleDirectly_StillReportsTheTeamHeldGrant()
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(CensusWith(
            Grant("System Administrator", PrivilegeDepth.Global, RootBu, "Ralph Schroeder", isHuman: true),
            Grant("System Administrator", PrivilegeDepth.Global, RootBu, "Jake Schroeder", isHuman: true, viaTeam: "Spaarke")));

        outcome.Verdict.Should().Be(SecureBuVerdict.AdministrativeRoleHeldByTeam);
        outcome.Message.Should().Contain("Jake Schroeder");
        outcome.Message.Should().NotContain("'Ralph Schroeder'");
    }

    /// <summary>
    /// The headline verdict is the most SEVERE finding, not the first one enumerated. A live dev run
    /// produces sixteen findings across two verdicts and the summary line has to name the worse of them;
    /// tying it to census order would make the headline depend on which principal Dataverse happened to
    /// return first.
    /// </summary>
    [Fact]
    public void Evaluate_WhenBothATeamHeldAdminGrantAndAnOrdinaryGrantReach_ReportsTheMoreSevereVerdict()
    {
        var outcome = SecureBuRoleDepthAssertion.Evaluate(CensusWith(
            Grant("System Administrator", PrivilegeDepth.Global, RootBu, "Jake Schroeder", isHuman: true, viaTeam: "Spaarke"),
            Grant("Spaarke Basic User", PrivilegeDepth.Deep, RootBu, "Test User 1", isHuman: true)));

        outcome.Findings.Should().HaveCount(2);
        outcome.Findings[0].Verdict.Should().Be(
            SecureBuVerdict.AdministrativeRoleHeldByTeam, "census order puts the team grant first");
        outcome.Verdict.Should().Be(
            SecureBuVerdict.HumanPrincipalReachesSecureBusinessUnit, "but severity order wins");
    }

    /// <summary>
    /// The pre-UAT state: no secure BU, so the assertion has nothing to resolve reach against. It halts
    /// as NOT RUN with the exact wording spec NFR-05 requires, and <c>Passed</c> is FALSE — an inert
    /// assertion is not a passing one, and the message refuses to be read as one.
    /// </summary>
    [Fact]
    public void Evaluate_WhenTheSecureBusinessUnitDoesNotExist_ReportsInertWithTheLoudWarning()
    {
        var census = Census(
            grants: new[] { Grant("Spaarke Basic User", PrivilegeDepth.Deep, RootBu, "Test User 1", isHuman: true) },
            businessUnits: new[]
            {
                new BusinessUnitNode(RootBu, "Spaarke", null),
                new BusinessUnitNode(SiblingBu, "Spaarke Business Unit 1", RootBu)
            });

        var outcome = SecureBuRoleDepthAssertion.Evaluate(census);

        outcome.IsInert.Should().BeTrue();
        outcome.Passed.Should().BeFalse("inert is not a pass");
        outcome.Message.Should().Contain(
            "Secure Record BU not found — NFR-05 assertion inert; UAT environment setup pending");
    }

    /// <summary>
    /// A census with no grants is what a mistyped privilege name, a broken join, or a swallowed query
    /// error all look like — and all three would otherwise present as "nothing reaches the secure BU".
    /// Refusing to render a verdict is the fail-CLOSED direction acceptance criterion 4 requires.
    /// </summary>
    [Fact]
    public void Evaluate_WhenTheCensusContainsNoGrantsAtAll_RefusesToRenderAVerdict()
    {
        var census = Census(grants: Array.Empty<EffectiveGrant>());

        var outcome = SecureBuRoleDepthAssertion.Evaluate(census);

        outcome.Passed.Should().BeFalse();
        outcome.Verdict.Should().Be(SecureBuVerdict.VacuousCensus);
        outcome.Message.Should().Contain("broken query");
    }

    /// <summary>
    /// User (Basic) depth matches only records the principal owns, so it reaches no business unit —
    /// the negative control that makes the diagnosis precise (design §5.1a-2: Support User at Basic was
    /// correctly DENIED on the same record that Deep-at-root returned).
    /// </summary>
    [Fact]
    public void Reaches_WhenDepthIsBasic_DoesNotReachAnyBusinessUnit()
    {
        SecureBuRoleDepthAssertion.Reaches(PrivilegeDepth.Basic, SecureBu, SecureBu, BusinessUnits)
            .Should().BeFalse();
    }

    /// <summary>
    /// Business Unit (Local) depth covers the anchor BU only — it does NOT inherit downward. Pins that
    /// Local at an ancestor is not silently treated as Deep, which is the mitigation design §5.1a-2
    /// calls Fix B (narrowing <c>Spaarke Basic User</c> from Deep to Local).
    /// </summary>
    [Fact]
    public void Reaches_WhenDepthIsLocalAtAnAncestor_DoesNotReachTheDescendant()
    {
        SecureBuRoleDepthAssertion.Reaches(PrivilegeDepth.Local, RootBu, SecureBu, BusinessUnits)
            .Should().BeFalse();

        SecureBuRoleDepthAssertion.Reaches(PrivilegeDepth.Local, SecureBu, SecureBu, BusinessUnits)
            .Should().BeTrue("Local anchored AT the secure BU does reach it");
    }

    /// <summary>
    /// Deep reaches a GRANDCHILD too, not only a direct child. Design §5.2's target topology nests
    /// per-project secure BUs under the secure BU, so a one-level ancestor check would go blind exactly
    /// when the environment becomes the shape the design is aiming at.
    /// </summary>
    [Fact]
    public void Reaches_WhenDeepIsHeldAtAGrandparent_ReachesTheGrandchild()
    {
        var perProjectBu = Guid.Parse("00000000-0000-0000-0000-0000000000f1");
        var tree = BusinessUnits.Append(new BusinessUnitNode(perProjectBu, "Secure Project 42", SecureBu)).ToArray();

        SecureBuRoleDepthAssertion.Reaches(PrivilegeDepth.Deep, RootBu, perProjectBu, tree)
            .Should().BeTrue();
    }

    /// <summary>
    /// An unrecognised depth mask fails CLOSED. A future platform change that introduced one must make
    /// this assertion loud, not silently narrow — the mode in which a security gate stops asserting is
    /// the mode nobody notices.
    /// </summary>
    [Fact]
    public void Reaches_WhenTheDepthMaskIsUnrecognised_FailsClosed()
    {
        SecureBuRoleDepthAssertion.Reaches((PrivilegeDepth)16, SiblingBu, SecureBu, BusinessUnits)
            .Should().BeTrue("an unknown depth must be treated as reaching, not as narrow");
    }

    /// <summary>
    /// Acceptance criterion 4, the configuration half: when the run is DECLARED an NFR-05 run, an absent
    /// environment is a failure with the setup contract, never a quiet non-run. The check is a pure
    /// function of the two values so it is provable without touching process environment state.
    /// </summary>
    [Fact]
    public void ResolveEnvironmentUrl_WhenTheRunIsRequiredButUnconfigured_ThrowsWithTheSetupContract()
    {
        var act = () => ResolveEnvironmentUrl(configuredUrl: null, required: true);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().ContainAll(EnvironmentUrlVariable, RequiredVariable);
    }

    /// <summary>A configured URL is normalised (trailing slash removed) and returned for both branches.</summary>
    [Fact]
    public void ResolveEnvironmentUrl_WhenConfigured_ReturnsTheNormalisedUrl()
    {
        ResolveEnvironmentUrl("https://spaarkedev1.crm.dynamics.com/", required: false)
            .Should().Be("https://spaarkedev1.crm.dynamics.com");
    }

    /// <summary>An unparseable URL is rejected up front rather than producing an opaque request failure.</summary>
    [Fact]
    public void ResolveEnvironmentUrl_WhenTheUrlIsNotAbsolute_Throws()
    {
        var act = () => ResolveEnvironmentUrl("spaarkedev1", required: false);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain(EnvironmentUrlVariable);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    // Layer 2 — LIVE TENANT. The standing assertion itself.
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads the live role-depth census and asserts every NFR-05 clause, including task 145's clause 5 (the owner role
    /// covers <c>config/secure-record-owner-role.json</c>).
    /// </summary>
    /// <remarks>
    /// <para><b>Failure modes are not symmetric here.</b> A query error is a FAILURE (the reader throws;
    /// nothing catches), an absent secure BU is a loud NOT-RUN, and a violation is a build-breaking
    /// failure naming the principal, role, privilege and depth. The one outcome that cannot happen is a
    /// quiet pass on an environment the test could not actually read.</para>
    ///
    /// <para><b>Why it is opt-in.</b> No pipeline in this repo holds a Dataverse credential (see
    /// <c>tests/integration/auth/README.md</c> § "What is NOT yet wired" — the same open decision task
    /// 034 escalated). Set <c>SPAARKE_NFR05_DATAVERSE_URL</c> (or the canary's URL variable) plus an
    /// ambient Azure credential to run it; set <c>SPAARKE_NFR05_REQUIRED=true</c> to make an
    /// unconfigured run a failure.</para>
    /// </remarks>
    [Fact]
    [Trait("Category", "LiveDataverseRoleDepth")]
    public async Task SecureBusinessUnitRoleDepth_InTheTargetEnvironment_IsReachableByNoNonAdministratorHuman()
    {
        var required = string.Equals(
            Environment.GetEnvironmentVariable(RequiredVariable), "true", StringComparison.OrdinalIgnoreCase);

        var configured = Environment.GetEnvironmentVariable(EnvironmentUrlVariable)
            ?? Environment.GetEnvironmentVariable(FallbackEnvironmentUrlVariable);

        if (configured is null && !required)
        {
            // NOT RUN — xUnit 2.9 offers no dynamic skip, and a permanently red test is a deleted test.
            // The not-run state is made loud rather than invisible.
            _output.WriteLine(
                "NFR-05 role-depth assertion NOT RUN: neither " + EnvironmentUrlVariable + " nor "
                + FallbackEnvironmentUrlVariable + " is set, so no environment was inspected. This is "
                + "NOT a pass. Set the variable and an ambient Azure credential to run it, or set "
                + RequiredVariable + "=true to make an unconfigured run a failure.");
            return;
        }

        var serviceUrl = ResolveEnvironmentUrl(configured, required);
        var census = await ReadCensusAsync(serviceUrl);
        var outcome = SecureBuRoleDepthAssertion.Evaluate(census);

        _output.WriteLine(Summarise(serviceUrl, census, outcome));

        if (outcome.IsInert)
        {
            // The loud skip, printed above and echoed to the runner's stdout so it survives a console
            // logger that only prints output for failing tests.
            Console.WriteLine(SecureBuRoleDepthAssertion.InertMessage);
            return;
        }

        outcome.Passed.Should().BeTrue(outcome.Message);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    // Live census reader. Every failure throws; nothing is swallowed and nothing degrades to "empty".
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates and normalises the environment URL, or throws with the setup contract. Pure in its
    /// inputs so the required-but-unconfigured branch is provable without process environment state.
    /// </summary>
    internal static string ResolveEnvironmentUrl(string? configuredUrl, bool required)
    {
        if (string.IsNullOrWhiteSpace(configuredUrl))
        {
            throw new InvalidOperationException(
                "The NFR-05 role-depth assertion was declared mandatory for this run ("
                + RequiredVariable + "=true) but no target environment is configured. This is a FAILURE, "
                + "not a skip: an unrun standing assertion is an open gate.\n\n"
                + "Set " + EnvironmentUrlVariable + " (or " + FallbackEnvironmentUrlVariable + ") to the "
                + "Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com, and provide an "
                + "ambient Azure credential (az login / workload identity) whose identity can read "
                + "businessunits, roles, roleprivileges, systemusers and teams.");
        }

        var trimmed = configuredUrl.Trim().TrimEnd('/');

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                $"{EnvironmentUrlVariable} is not an absolute URL: '{configuredUrl}'.");
        }

        return trimmed;
    }

    /// <summary>
    /// Reads everything NFR-05 needs from live Dataverse and resolves it into a census.
    /// </summary>
    /// <remarks>
    /// <para><b>Role replication.</b> A security role exists once per business unit; only the ROOT copy
    /// (<c>roleid == parentrootroleid</c>) carries <c>roleprivileges</c> rows, and the copies inherit.
    /// Depth is therefore looked up through <c>parentrootroleid</c> while the ANCHOR business unit comes
    /// from the copy the principal actually holds. Reading depth per-copy would find nothing and produce
    /// a vacuous census — which is why <see cref="SecureBuRoleDepthAssertion"/> refuses to grade one.</para>
    ///
    /// <para><b>Team-held roles.</b> Dataverse evaluates a team-assigned privilege against the TEAM's
    /// business unit, not the member's, so the anchor differs from the direct-assignment case. Default
    /// business-unit owner teams contain every user in their BU automatically.</para>
    /// </remarks>
    private static async Task<SecureBuRoleDepthCensus> ReadCensusAsync(string serviceUrl)
    {
        var token = await new DefaultAzureCredential().GetTokenAsync(
            new TokenRequestContext(new[] { $"{serviceUrl}/.default" }),
            CancellationToken.None);

        using var http = new HttpClient { BaseAddress = new Uri($"{serviceUrl}/api/data/v9.2/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        http.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        http.DefaultRequestHeaders.Add("OData-Version", "4.0");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var businessUnits = (await GetAllAsync(http, "businessunits?$select=name,businessunitid,_parentbusinessunitid_value"))
            .Select(row => new BusinessUnitNode(
                GuidOf(row, "businessunitid"),
                Text(row, "name") ?? string.Empty,
                OptionalGuidOf(row, "_parentbusinessunitid_value")))
            .ToArray();

        // Clause 1's guarded Reads plus every privilege of the codified owner-role set (clause 5, task 145), read from
        // the ONE list the BFF embeds — the same composition and the same exact-name check as the scheduled job.
        var ownerRoleSet = SecureRecordOwnerRoleSet.Embedded;
        var privilegeNames = SecureBuRoleDepthCensusBuilder.CensusPrivilegeNames(ownerRoleSet);
        var privilegeFilter = string.Join(" or ", privilegeNames.Select(name => $"name eq '{name}'"));

        var privileges = (await GetAllAsync(http, $"privileges?$select=name,privilegeid&$filter={Uri.EscapeDataString(privilegeFilter)}"))
            .ToDictionary(row => GuidOf(row, "privilegeid"), row => Text(row, "name") ?? string.Empty);

        SecureBuRoleDepthCensusBuilder.RequireGuardedPrivilegesResolved(privileges.Values.ToArray());

        var depthFilter = string.Join(" or ", privileges.Keys.Select(id => $"privilegeid eq {id}"));
        var rolePrivileges = await GetAllAsync(
            http,
            $"roleprivilegescollection?$select=roleid,privilegeid,privilegedepthmask&$filter={Uri.EscapeDataString(depthFilter)}");

        // (root role id, privilege name) -> depth
        var depthByRootRole = new Dictionary<(Guid RoleId, string Privilege), PrivilegeDepth>();
        foreach (var row in rolePrivileges)
        {
            var privilegeName = privileges[GuidOf(row, "privilegeid")];
            depthByRootRole[(GuidOf(row, "roleid"), privilegeName)] = (PrivilegeDepth)Number(row, "privilegedepthmask");
        }

        var roles = (await GetAllAsync(http, "roles?$select=name,roleid,_businessunitid_value,_parentrootroleid_value"))
            .Select(row => new CensusRole(
                GuidOf(row, "roleid"),
                Text(row, "name") ?? string.Empty,
                GuidOf(row, "_businessunitid_value"),
                OptionalGuidOf(row, "_parentrootroleid_value") ?? GuidOf(row, "roleid")))
            .ToArray();

        // _businessunitid_value is read for clause 4 (task 144): every systemuser in the secure BU, of any kind.
        var users = (await GetAllAsync(
                http,
                "systemusers?$select=fullname,domainname,systemuserid,accessmode,applicationid,isdisabled,_businessunitid_value"
                + "&$expand=systemuserroles_association($select=roleid)"))
            .Select(ReadUser)
            .ToArray();

        // teamtype is read so the builder can pick the NAMED Owner team, never an access team or the default team.
        var teams = (await GetAllAsync(
                http,
                "teams?$select=name,teamid,isdefault,teamtype,_businessunitid_value"
                + "&$expand=teamroles_association($select=roleid),teammembership_association($select=systemuserid)"))
            .Select(row => new CensusTeam(
                GuidOf(row, "teamid"),
                Text(row, "name") ?? string.Empty,
                GuidOf(row, "_businessunitid_value"),
                Flag(row, "isdefault"),
                Number(row, "teamtype"),
                Ids(row, "teamroles_association", "roleid"),
                Ids(row, "teammembership_association", "systemuserid")))
            .ToArray();

        // ONE composition, shared with the scheduled census job (task 144): anchors, team-held grants, who counts as
        // human and which team is THE owner team are decided in the BFF's builder, not re-derived here.
        return SecureBuRoleDepthCensusBuilder.Build(
            Environment.GetEnvironmentVariable(SecureBuNameVariable) ?? SecureRecordOwnerTeam.DefaultBusinessUnitName,
            Environment.GetEnvironmentVariable(OwnerTeamNameVariable) ?? SecureRecordOwnerTeam.DefaultOwnerTeamName,
            businessUnits,
            depthByRootRole,
            roles,
            users,
            teams,
            ownerRoleSet,
            privileges.Values.ToArray());
    }

    /// <summary>Optional override of the Secure Record BU name for a live run (default: the compiled default).</summary>
    public const string SecureBuNameVariable = "SPAARKE_NFR05_SECURE_BU_NAME";

    /// <summary>Optional override of the named owner team for a live run (default: the compiled default).</summary>
    public const string OwnerTeamNameVariable = "SPAARKE_NFR05_SECURE_OWNER_TEAM_NAME";

    /// <summary>
    /// One systemuser row. Who counts as a human is the shared rule
    /// (<see cref="SecureBuRoleDepthCensusBuilder.IsHumanPrincipal"/>), so the job and this gate cannot disagree.
    /// </summary>
    private static CensusUser ReadUser(JsonElement row)
    {
        var name = Text(row, "fullname") ?? string.Empty;

        return new CensusUser(
            GuidOf(row, "systemuserid"),
            name,
            Text(row, "domainname"),
            SecureBuRoleDepthCensusBuilder.IsHumanPrincipal(
                hasApplicationId: Text(row, "applicationid") is not null,
                isDisabled: Flag(row, "isdisabled"),
                fullName: name,
                accessMode: Number(row, "accessmode")),
            OptionalGuidOf(row, "_businessunitid_value"),
            Ids(row, "systemuserroles_association", "roleid"));
    }

    private static string Summarise(string serviceUrl, SecureBuRoleDepthCensus census, SecureBuAssertionOutcome outcome)
    {
        var humanGrants = census.Grants.Count(g => g.PrincipalIsHuman);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"NFR-05 role-depth census for {serviceUrl}: {census.BusinessUnits.Count} business unit(s), "
            + $"{census.Grants.Count} effective grant(s) of "
            + $"{string.Join(" / ", SecureBuRoleDepthAssertion.GuardedPrivileges)} ({humanGrants} held by "
            + $"humans), {census.SecureOwnerRoleHolders.Count} holder(s) of "
            + $"'{SecureBuRoleDepthAssertion.SecureOwnerRoleName}', {census.SecureOwnerTeamMatches} team(s) named "
            + $"'{census.SecureOwnerTeamName}' with {census.SecureOwnerTeamMembers.Count} member(s) of any kind, "
            + $"{census.SecureBusinessUnitUsers.Count} systemuser(s) in '{census.SecureBusinessUnitName}', "
            + $"{census.OwnerRoleCoverage.OwnerRoleCount} owner role(s) in it covering "
            + $"{census.OwnerRoleCoverage.Tables.Count(t => t.HeldDepth == PrivilegeDepth.Basic)} of "
            + $"{census.OwnerRoleCoverage.Tables.Count} codified table(s) at Basic. "
            + $"Verdict: {outcome.Verdict}.")
            + Environment.NewLine + outcome.Message;
    }

    // ── OData plumbing ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads every page of a collection. Truncation is never acceptable here: a census that stopped at
    /// page one could omit the one principal that reaches the secure BU and report isolation.
    /// </summary>
    private static async Task<IReadOnlyList<JsonElement>> GetAllAsync(HttpClient http, string query)
    {
        var rows = new List<JsonElement>();
        var next = query;

        while (next is not null)
        {
            using var response = await http.GetAsync(next);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"The NFR-05 census query '{next}' failed with {(int)response.StatusCode} "
                    + $"{response.ReasonPhrase}. A query failure is a FAILURE, never a skip: the "
                    + $"assertion cannot report isolation about an environment it could not read.\n{body}");
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            rows.AddRange(document.RootElement.GetProperty("value").EnumerateArray().Select(Clone));

            next = document.RootElement.TryGetProperty("@odata.nextLink", out var link)
                ? link.GetString()
                : null;
        }

        return rows;
    }

    private static JsonElement Clone(JsonElement element) => JsonDocument.Parse(element.GetRawText()).RootElement;

    private static string? Text(JsonElement row, string property) =>
        row.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Guid GuidOf(JsonElement row, string property) =>
        Guid.TryParse(Text(row, property), out var id)
            ? id
            : throw new InvalidOperationException(
                $"The NFR-05 census expected a GUID in '{property}' but the row did not carry one: {row.GetRawText()}");

    private static Guid? OptionalGuidOf(JsonElement row, string property) =>
        Guid.TryParse(Text(row, property), out var id) ? id : null;

    private static int Number(JsonElement row, string property) =>
        row.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    private static bool Flag(JsonElement row, string property) =>
        row.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static IReadOnlyList<Guid> Ids(JsonElement row, string collection, string property) =>
        row.TryGetProperty(collection, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
                .Select(item => OptionalGuidOf(item, property))
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToArray()
            : Array.Empty<Guid>();

    // ── Perturbation fixture: the live dev topology as of 2026-09-09 ──────────────────────────────

    private const string OwnerTeamName = SecureRecordOwnerTeam.DefaultOwnerTeamName;

    private static readonly Guid RootBu = Guid.Parse("00000000-0000-0000-0000-0000000000a0");
    private static readonly Guid SecureBu = Guid.Parse("00000000-0000-0000-0000-0000000000b0");
    private static readonly Guid SiblingBu = Guid.Parse("00000000-0000-0000-0000-0000000000c0");

    /// <summary>A second business unit that also carries the secure name — listed AFTER the real one.</summary>
    private static readonly Guid SecondSecureNamedBu = Guid.Parse("00000000-0000-0000-0000-0000000000d0");

    /// <summary>Root, with the secure BU and an ordinary BU as siblings beneath it — the live dev shape.</summary>
    private static readonly BusinessUnitNode[] BusinessUnits =
    {
        new(RootBu, "Spaarke", null),
        new(SecureBu, "Secure Record", RootBu),
        new(SiblingBu, "Spaarke Business Unit 1", RootBu)
    };

    /// <summary>
    /// A census of the dev topology in which every task-144 clause holds (one named team, no members, no BU users),
    /// so a test perturbs exactly the one property it is about.
    /// </summary>
    private static SecureBuRoleDepthCensus CensusWith(params EffectiveGrant[] grants) => Census(grants: grants);

    private static SecureBuRoleDepthCensus Census(
        IReadOnlyList<EffectiveGrant> grants,
        IReadOnlyList<SecureOwnerRoleHolder>? holders = null,
        int matches = 1,
        IReadOnlyList<string>? members = null,
        IReadOnlyList<string>? users = null,
        IReadOnlyList<BusinessUnitNode>? businessUnits = null,
        SecureOwnerRoleCoverage? coverage = null) =>
        new(
            SecureRecordOwnerTeam.DefaultBusinessUnitName,
            OwnerTeamName,
            businessUnits ?? BusinessUnits,
            grants,
            holders ?? Array.Empty<SecureOwnerRoleHolder>(),
            matches,
            members ?? Array.Empty<string>(),
            users ?? Array.Empty<string>(),
            coverage ?? CoverageOfTheWholeSet());

    /// <summary>
    /// One owner role covering every table of the codified set at Basic — task 145's steady state — with the depth of
    /// any table named in <paramref name="overrides"/> replaced (null = the role lacks it). Built FROM the embedded set,
    /// so the fixture never carries a second copy of the list.
    /// </summary>
    private static SecureOwnerRoleCoverage CoverageOfTheWholeSet(
        int ownerRoles = 1, params (string LogicalName, PrivilegeDepth? Depth)[] overrides) =>
        new(ownerRoles, SecureRecordOwnerRoleSet.Embedded.Tables
            .Select(t => new OwnerRoleTableCoverage(
                t.LogicalName,
                t.PrivilegeName,
                overrides.Any(o => o.LogicalName == t.LogicalName)
                    ? overrides.First(o => o.LogicalName == t.LogicalName).Depth
                    : PrivilegeDepth.Basic))
            .ToArray());

    private static EffectiveGrant Grant(
        string roleName,
        PrivilegeDepth depth,
        Guid anchorBusinessUnitId,
        string principalName,
        bool isHuman,
        string? viaTeam = null) =>
        new(roleName, "prvReadsprk_Project", depth, anchorBusinessUnitId, principalName,
            $"{principalName.Replace(" ", ".", StringComparison.Ordinal)}@spaarke.com", isHuman, viaTeam);
}
