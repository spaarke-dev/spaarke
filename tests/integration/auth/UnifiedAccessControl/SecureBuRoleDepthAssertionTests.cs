using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// The spec NFR-05 STANDING assertion: no security role lets a non-administrator human reach the
/// secure-projects business unit, the secure owner team holds no humans, and the owner role has not
/// escaped that team.
/// </summary>
/// <remarks>
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
    /// <c>Secure Project Owner</c> at User depth on the owner team is the design §5.1a steady state and
    /// must pass. It is exempted STRUCTURALLY (Basic reaches no business unit), never by name — see the
    /// next test for why that distinction is load-bearing.
    /// </summary>
    [Fact]
    public void Evaluate_WhenTheSecureOwnerRoleIsBasicDepthOnTheOwnerTeam_Passes()
    {
        var census = new SecureBuRoleDepthCensus(
            BusinessUnits,
            new[] { Grant(SecureBuRoleDepthAssertion.SecureOwnerRoleName, PrivilegeDepth.Basic, SecureBu, "Secure Project team", isHuman: false) },
            new[] { new SecureOwnerRoleHolder("Secure Project", IsSecureOwnerTeam: true) },
            Array.Empty<string>());

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
        var census = new SecureBuRoleDepthCensus(
            BusinessUnits,
            new[] { Grant(SecureBuRoleDepthAssertion.SecureOwnerRoleName, PrivilegeDepth.Local, SecureBu, "Contract Paralegal", isHuman: true) },
            new[]
            {
                new SecureOwnerRoleHolder("Secure Project", IsSecureOwnerTeam: true),
                new SecureOwnerRoleHolder("Contract Paralegal", IsSecureOwnerTeam: false)
            },
            Array.Empty<string>());

        var outcome = SecureBuRoleDepthAssertion.Evaluate(census);

        outcome.Findings.Select(f => f.Verdict).Should().BeEquivalentTo(new[]
        {
            SecureBuVerdict.HumanPrincipalReachesSecureBusinessUnit,
            SecureBuVerdict.SecureOwnerRoleHeldBeyondOwnerTeam
        });
    }

    /// <summary>
    /// The owner team owns EVERY secure project, so one human member reads all of them by ownership —
    /// no depth is involved and narrowing the role would not relax it (design §5.1a). This clause is the
    /// half of NFR-05 that the depth model structurally cannot see.
    /// </summary>
    [Fact]
    public void Evaluate_WhenTheSecureOwnerTeamHasAHumanMember_ReportsOwnerTeamHasHumanMembers()
    {
        var census = new SecureBuRoleDepthCensus(
            BusinessUnits,
            new[] { Grant(SecureBuRoleDepthAssertion.SecureOwnerRoleName, PrivilegeDepth.Basic, SecureBu, "Secure Project team", isHuman: false) },
            new[] { new SecureOwnerRoleHolder("Secure Project", IsSecureOwnerTeam: true) },
            new[] { "Contract Paralegal" });

        var outcome = SecureBuRoleDepthAssertion.Evaluate(census);

        outcome.Verdict.Should().Be(SecureBuVerdict.OwnerTeamHasHumanMembers);
        outcome.Message.Should().Contain("Contract Paralegal");
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
        var census = new SecureBuRoleDepthCensus(
            new[]
            {
                new BusinessUnitNode(RootBu, "Spaarke", null),
                new BusinessUnitNode(SiblingBu, "Spaarke Business Unit 1", RootBu)
            },
            new[] { Grant("Spaarke Basic User", PrivilegeDepth.Deep, RootBu, "Test User 1", isHuman: true) },
            Array.Empty<SecureOwnerRoleHolder>(),
            Array.Empty<string>());

        var outcome = SecureBuRoleDepthAssertion.Evaluate(census);

        outcome.IsInert.Should().BeTrue();
        outcome.Passed.Should().BeFalse("inert is not a pass");
        outcome.Message.Should().Contain(
            "Secure Projects BU not found — NFR-05 assertion inert; UAT environment setup pending");
    }

    /// <summary>
    /// A census with no grants is what a mistyped privilege name, a broken join, or a swallowed query
    /// error all look like — and all three would otherwise present as "nothing reaches the secure BU".
    /// Refusing to render a verdict is the fail-CLOSED direction acceptance criterion 4 requires.
    /// </summary>
    [Fact]
    public void Evaluate_WhenTheCensusContainsNoGrantsAtAll_RefusesToRenderAVerdict()
    {
        var census = new SecureBuRoleDepthCensus(
            BusinessUnits,
            Array.Empty<EffectiveGrant>(),
            Array.Empty<SecureOwnerRoleHolder>(),
            Array.Empty<string>());

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
    /// Reads the live role-depth census and asserts NFR-05's three clauses.
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

        var privilegeFilter = string.Join(
            " or ",
            SecureBuRoleDepthAssertion.GuardedPrivileges.Select(name => $"name eq '{name}'"));

        var privileges = (await GetAllAsync(http, $"privileges?$select=name,privilegeid&$filter={Uri.EscapeDataString(privilegeFilter)}"))
            .ToDictionary(row => GuidOf(row, "privilegeid"), row => Text(row, "name") ?? string.Empty);

        if (privileges.Count != SecureBuRoleDepthAssertion.GuardedPrivileges.Count)
        {
            throw new InvalidOperationException(
                "Expected to resolve "
                + string.Join(" / ", SecureBuRoleDepthAssertion.GuardedPrivileges)
                + $" but the environment returned {privileges.Count} privilege row(s): "
                + string.Join(", ", privileges.Values)
                + ". A missing privilege means the entity is absent or renamed; grading a partial census "
                + "would under-report reach, so this fails instead.");
        }

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
            .Select(row => new RoleCopy(
                GuidOf(row, "roleid"),
                Text(row, "name") ?? string.Empty,
                GuidOf(row, "_businessunitid_value"),
                OptionalGuidOf(row, "_parentrootroleid_value") ?? GuidOf(row, "roleid")))
            .ToDictionary(role => role.Id);

        var users = (await GetAllAsync(
                http,
                "systemusers?$select=fullname,domainname,systemuserid,accessmode,applicationid,isdisabled"
                + "&$expand=systemuserroles_association($select=roleid)"))
            .Select(ReadUser)
            .ToDictionary(user => user.Id);

        var teams = (await GetAllAsync(
                http,
                "teams?$select=name,teamid,isdefault,_businessunitid_value"
                + "&$expand=teamroles_association($select=roleid),teammembership_association($select=systemuserid)"))
            .Select(row => new TeamRow(
                GuidOf(row, "teamid"),
                Text(row, "name") ?? string.Empty,
                GuidOf(row, "_businessunitid_value"),
                Flag(row, "isdefault"),
                Ids(row, "teamroles_association", "roleid"),
                Ids(row, "teammembership_association", "systemuserid")))
            .ToArray();

        var grants = new List<EffectiveGrant>();

        foreach (var user in users.Values)
        {
            foreach (var roleId in user.RoleIds)
            {
                AddGrants(grants, roles, depthByRootRole, roleId, anchorOverride: null, user, heldViaTeam: null);
            }
        }

        foreach (var team in teams)
        {
            foreach (var roleId in team.RoleIds)
            {
                foreach (var memberId in team.MemberIds)
                {
                    if (users.TryGetValue(memberId, out var member))
                    {
                        AddGrants(grants, roles, depthByRootRole, roleId, team.BusinessUnitId, member, team.Name);
                    }
                }
            }
        }

        var secureBu = businessUnits.FirstOrDefault(
            bu => SecureBuRoleDepthAssertion.SecureBusinessUnitNames.Any(
                name => string.Equals(bu.Name, name, StringComparison.OrdinalIgnoreCase)));

        var ownerRoleCopyIds = roles.Values
            .Where(role => string.Equals(role.Name, SecureBuRoleDepthAssertion.SecureOwnerRoleName, StringComparison.OrdinalIgnoreCase))
            .Select(role => role.Id)
            .ToHashSet();

        var ownerRoleHolders = new List<SecureOwnerRoleHolder>();
        foreach (var user in users.Values.Where(u => u.RoleIds.Any(ownerRoleCopyIds.Contains)))
        {
            ownerRoleHolders.Add(new SecureOwnerRoleHolder($"user '{user.Name}' ({user.DomainName ?? "no domain"})", IsSecureOwnerTeam: false));
        }

        var ownerTeamHumanMembers = new List<string>();
        foreach (var team in teams)
        {
            var isSecureOwnerTeam = secureBu is not null && team.IsDefault && team.BusinessUnitId == secureBu.Id;

            if (team.RoleIds.Any(ownerRoleCopyIds.Contains))
            {
                ownerRoleHolders.Add(new SecureOwnerRoleHolder($"team '{team.Name}'", isSecureOwnerTeam));
            }

            if (isSecureOwnerTeam)
            {
                ownerTeamHumanMembers.AddRange(team.MemberIds
                    .Where(id => users.TryGetValue(id, out var member) && member.IsHuman)
                    .Select(id => $"{users[id].Name} ({users[id].DomainName ?? "no domain"})"));
            }
        }

        return new SecureBuRoleDepthCensus(businessUnits, grants, ownerRoleHolders, ownerTeamHumanMembers);
    }

    private static void AddGrants(
        List<EffectiveGrant> grants,
        IReadOnlyDictionary<Guid, RoleCopy> roles,
        IReadOnlyDictionary<(Guid RoleId, string Privilege), PrivilegeDepth> depthByRootRole,
        Guid roleId,
        Guid? anchorOverride,
        UserRow principal,
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

    /// <summary>
    /// A human principal: a real, enabled, interactive person. Everything else — application users,
    /// Microsoft platform accounts (<c>#</c>-prefixed), support and non-interactive access modes,
    /// disabled users — is the documented Global-read exception (design §5.2).
    /// </summary>
    private static UserRow ReadUser(JsonElement row)
    {
        var name = Text(row, "fullname") ?? string.Empty;
        var accessMode = Number(row, "accessmode");

        var isHuman =
            Text(row, "applicationid") is null
            && !Flag(row, "isdisabled")
            && !name.StartsWith('#')
            && accessMode is not 3 and not 4; // 3 = Support User, 4 = Non-interactive.

        return new UserRow(
            GuidOf(row, "systemuserid"),
            name,
            Text(row, "domainname"),
            isHuman,
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
            + $"'{SecureBuRoleDepthAssertion.SecureOwnerRoleName}', {census.SecureOwnerTeamHumanMembers.Count} "
            + $"human member(s) of the secure owner team. Verdict: {outcome.Verdict}.")
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

    private sealed record RoleCopy(Guid Id, string Name, Guid BusinessUnitId, Guid RootRoleId);

    private sealed record UserRow(Guid Id, string Name, string? DomainName, bool IsHuman, IReadOnlyList<Guid> RoleIds);

    private sealed record TeamRow(
        Guid Id,
        string Name,
        Guid BusinessUnitId,
        bool IsDefault,
        IReadOnlyList<Guid> RoleIds,
        IReadOnlyList<Guid> MemberIds);

    // ── Perturbation fixture: the live dev topology as of 2026-09-09 ──────────────────────────────

    private static readonly Guid RootBu = Guid.Parse("00000000-0000-0000-0000-0000000000a0");
    private static readonly Guid SecureBu = Guid.Parse("00000000-0000-0000-0000-0000000000b0");
    private static readonly Guid SiblingBu = Guid.Parse("00000000-0000-0000-0000-0000000000c0");

    /// <summary>Root, with the secure BU and an ordinary BU as siblings beneath it — the live dev shape.</summary>
    private static readonly BusinessUnitNode[] BusinessUnits =
    {
        new(RootBu, "Spaarke", null),
        new(SecureBu, "Secure Project", RootBu),
        new(SiblingBu, "Spaarke Business Unit 1", RootBu)
    };

    private static SecureBuRoleDepthCensus CensusWith(params EffectiveGrant[] grants) =>
        new(BusinessUnits, grants, Array.Empty<SecureOwnerRoleHolder>(), Array.Empty<string>());

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
