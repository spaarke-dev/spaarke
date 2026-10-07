using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 109 — the ONE organization-membership read: two NAMED sets and a fault
/// that cannot be mistaken for absence. Closes ISS-019 (#998), ISS-020 (#999) and the READ half of ISS-026
/// (#1006); owner decisions D-2 (end date) and D-10 (start date).
/// </summary>
/// <remarks>
/// <para><b>Two instruments, chosen per claim.</b></para>
/// <list type="bullet">
/// <item><b>Pure members</b> (<c>MembershipConfersOn</c>, <c>ProjectOrganizationMemberships</c>,
/// <c>BuildOrganizationMembershipFilter</c>, <c>GrantOrganizationConfers</c>) carry the date and state
/// contract. They are <c>internal</c> + <c>InternalsVisibleTo</c>, extracted to be assertable — the pattern
/// ADR-038 Amendment A2 sanctions.</item>
/// <item><b>The REAL <see cref="ExternalParticipationService"/> and <see cref="AccessibleRecordSetService"/>
/// over a real HTTP transport</b> for every claim about what a FAULT does. That is deliberate and it is the
/// lesson of ISS-019 itself: task 043 documented and unit-tested the veto's fail-closed as held, using a
/// double that THREW where the real query returned an empty list. A double here would assert the fake again.
/// The transport is an in-memory ASP.NET Core server (<c>UseTestServer</c>) standing in for the Dataverse
/// Web API — a real test double server, the replacement ADR-038 §7 names for ban B1, not a
/// <c>Mock&lt;HttpMessageHandler&gt;</c>. Membership resolution, the standing-grant reader and the No
/// Access reader are module-boundary interfaces and are substituted at that boundary (ADR-038 §4).</item>
/// </list>
/// </remarks>
public class OrganizationMembershipReadTests
{
    // ═════════════════════════════════════════════════════════════════════════════
    // Pure contract — dates (owner D-2 part 1 + D-10)
    // ═════════════════════════════════════════════════════════════════════════════

    private static readonly DateOnly Today = new(2026, 9, 30);

    /// <summary>
    /// 🔴 Both null branches are load-bearing: a membership with no start or no end date is an ordinary
    /// current membership. Task 107's inversion (null ⇒ nothing) is correct for a GRANT's expiry and would
    /// silently revoke every open-ended membership here.
    /// </summary>
    [Fact]
    public void MembershipConfersOn_NullStartOrNullEnd_IsUnboundedAndConfers()
    {
        ExternalParticipationService.MembershipConfersOn(null, null, Today).Should().BeTrue(
            "no start and no end date is an ordinary current membership");
        ExternalParticipationService.MembershipConfersOn(null, Today.AddDays(30), Today).Should().BeTrue(
            "a NULL start date means no start bound — not 'never started'");
        ExternalParticipationService.MembershipConfersOn(Today.AddDays(-30), null, Today).Should().BeTrue(
            "a NULL end date means no end bound — not 'already ended'");
    }

    /// <summary>
    /// Both columns are Date Only, so both boundaries are INCLUSIVE: access holds ON the start date
    /// (D-10, "confers as of the access date") and THROUGH the end date (D-2, ExpiryPredicate's <c>ge</c>).
    /// </summary>
    [Fact]
    public void MembershipConfersOn_StartTodayOrEndToday_Confers()
    {
        ExternalParticipationService.MembershipConfersOn(Today, null, Today).Should().BeTrue(
            "access begins ON the start date, not the day after");
        ExternalParticipationService.MembershipConfersOn(null, Today, Today).Should().BeTrue(
            "access holds THROUGH the end date — the same boundary task 117's writer deactivates after");
    }

    [Fact]
    public void MembershipConfersOn_EndDatePassed_ConfersNothing()
    {
        ExternalParticipationService.MembershipConfersOn(null, Today.AddDays(-1), Today).Should().BeFalse(
            "owner D-2 part 1: a membership whose end date has passed stops conferring");
    }

    [Fact]
    public void MembershipConfersOn_StartDateInTheFuture_ConfersNothing()
    {
        ExternalParticipationService.MembershipConfersOn(Today.AddDays(1), null, Today).Should().BeFalse(
            "owner D-10: a not-yet-started membership confers nothing yet");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Pure contract — the two named sets
    // ═════════════════════════════════════════════════════════════════════════════

    private static readonly Guid OrgA = Guid.Parse("a1000000-0000-0000-0000-00000000000a");
    private static readonly Guid OrgB = Guid.Parse("b2000000-0000-0000-0000-00000000000b");

    private static ExternalParticipationService.ContactOrgRow Row(
        Guid org, DateOnly? start = null, DateOnly? end = null, int? state = 0, int? orgState = 0, bool orgExpanded = true)
        => new()
        {
            OrganizationId = org,
            StartDate = start,
            EndDate = end,
            StateCode = state,
            Organization = orgExpanded ? new ExternalParticipationService.OrganizationStateRow { StateCode = orgState } : null,
        };

    [Fact]
    public void ProjectOrganizationMemberships_EndedByDateButActive_IsAWallSubjectButConfersNothing()
    {
        var sets = ExternalParticipationService.ProjectOrganizationMemberships(
            new[] { Row(OrgA, end: Today.AddDays(-1)), Row(OrgB) }, Today);

        sets.ConferringOrganizationIds.Should().BeEquivalentTo(new[] { OrgB },
            "a date-ended membership confers nothing even while its statecode is active (D-2 part 1)");
        sets.WallSubjectOrganizationIds.Should().BeEquivalentTo(new[] { OrgA, OrgB },
            "the org-keyed ethical wall KEEPS binding a former member (D-2 part 2)");
        sets.Unreadable.Should().BeFalse();
    }

    [Fact]
    public void ProjectOrganizationMemberships_StartDateInTheFuture_IsAWallSubjectButConfersNothing()
    {
        var sets = ExternalParticipationService.ProjectOrganizationMemberships(
            new[] { Row(OrgA, start: Today.AddDays(1)), Row(OrgB, start: Today) }, Today);

        sets.ConferringOrganizationIds.Should().BeEquivalentTo(new[] { OrgB },
            "a not-yet-started membership confers nothing; one starting TODAY does (D-10)");
        sets.WallSubjectOrganizationIds.Should().BeEquivalentTo(new[] { OrgA, OrgB },
            "the wall is statecode-only at BOTH ends of the range — a not-yet-started member is still walled (D-10)");
    }

    [Fact]
    public void ProjectOrganizationMemberships_InactiveOrganization_IsAWallSubjectButConfersNothing()
    {
        var sets = ExternalParticipationService.ProjectOrganizationMemberships(
            new[] { Row(OrgA, orgState: 1), Row(OrgB, orgState: 0) }, Today);

        sets.ConferringOrganizationIds.Should().BeEquivalentTo(new[] { OrgB },
            "ISS-026 read guard: a membership under an INACTIVE organization confers nothing; an active one is unaffected");
        sets.WallSubjectOrganizationIds.Should().Contain(OrgA,
            "deactivating a firm must not lift the ethical wall off its members");
    }

    [Fact]
    public void ProjectOrganizationMemberships_NullStatecodes_AreActive()
    {
        var sets = ExternalParticipationService.ProjectOrganizationMemberships(
            new[] { Row(OrgA, state: null, orgState: null) }, Today);

        sets.ConferringOrganizationIds.Should().BeEquivalentTo(new[] { OrgA },
            "a NULL statecode is ACTIVE — ExternalGrantRow.IsActive's semantics, on the junction row and on the organization");
        sets.WallSubjectOrganizationIds.Should().BeEquivalentTo(new[] { OrgA });
    }

    [Fact]
    public void ProjectOrganizationMemberships_OrganizationStateDidNotComeBack_ConfersNothingButStillBindsTheWall()
    {
        var sets = ExternalParticipationService.ProjectOrganizationMemberships(
            new[] { Row(OrgA, orgExpanded: false) }, Today);

        sets.ConferringOrganizationIds.Should().BeEmpty(
            "an organization whose state is unknown must not confer — unknown is not active (ADR-003 fail-closed)");
        sets.WallSubjectOrganizationIds.Should().BeEquivalentTo(new[] { OrgA });
    }

    [Fact]
    public void ProjectOrganizationMemberships_InactiveJunctionRow_IsInNeitherSet()
    {
        var sets = ExternalParticipationService.ProjectOrganizationMemberships(
            new[] { Row(OrgA, state: 1) }, Today);

        sets.ConferringOrganizationIds.Should().BeEmpty();
        sets.WallSubjectOrganizationIds.Should().BeEmpty(
            "a deactivated junction row is not a membership on either axis — re-decided in code, not trusted from the $filter");
    }

    /// <summary>
    /// The junction <c>$filter</c> defines the WALL set, so it must carry no date term at either end.
    /// <c>ExternalAccessQueryIntegrityGuardTests</c> pins that the call site uses this builder, and
    /// <see cref="AssertEveryJunctionReadSentExactlyTheWallFilter"/> that the request sends it unaltered.
    /// </summary>
    [Fact]
    public void BuildOrganizationMembershipFilter_IsStatecodeOnly_WithNoDateTerm()
    {
        var contactId = Guid.Parse("c0000000-0000-0000-0000-000000000001");

        var filter = ExternalParticipationService.BuildOrganizationMembershipFilter(contactId);

        filter.Should().Contain($"_sprk_contact_value eq {contactId}");
        filter.Should().Contain("(statecode eq 0 or statecode eq null)",
            "a NULL statecode is ACTIVE; a bare `statecode eq 0` would drop such a row from the wall");
        filter.Should().NotContain("sprk_enddate",
            "a date bound in the $filter would narrow the WALL — a fail-OPEN change to a veto (D-2 part 2)");
        filter.Should().NotContain("sprk_startdate",
            "the wall is not start-date-bounded either (D-10)");
    }

    [Fact]
    public void GrantOrganizationConfers_OnlyAnActiveOrNoOrganizationLetsAGrantRowConfer()
    {
        static ExternalParticipationService.OrganizationStateRow Org(int? state) => new() { StateCode = state };

        ExternalParticipationService.GrantOrganizationConfers(null, null).Should().BeTrue(
            "a contact-keyed grant with no organization lookup is unaffected by ISS-026");
        ExternalParticipationService.GrantOrganizationConfers(OrgA, Org(0)).Should().BeTrue(
            "an active organization is unaffected");
        ExternalParticipationService.GrantOrganizationConfers(OrgA, Org(1)).Should().BeFalse(
            "ISS-026: a grant whose organization is INACTIVE confers nothing");
        ExternalParticipationService.GrantOrganizationConfers(OrgA, null).Should().BeFalse(
            "an organization whose state did not come back is not known to be active");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // The real read over a real transport
    // ═════════════════════════════════════════════════════════════════════════════

    private static readonly Guid ContactId = Guid.Parse("c1000000-0000-0000-0000-000000000001");

    private static readonly Guid OrgCurrent = Guid.Parse("0c000000-0000-0000-0000-000000000001");
    private static readonly Guid OrgEnded = Guid.Parse("0e000000-0000-0000-0000-000000000002");
    private static readonly Guid OrgNotStarted = Guid.Parse("05000000-0000-0000-0000-000000000003");
    private static readonly Guid OrgInactive = Guid.Parse("01000000-0000-0000-0000-000000000004");

    private static readonly Guid DirectProject = Guid.Parse("9d000000-0000-0000-0000-000000000001");
    private static readonly Guid FirmInactiveProject = Guid.Parse("9f000000-0000-0000-0000-000000000002");
    private static readonly Guid CurrentOrgProject = Guid.Parse("9c000000-0000-0000-0000-000000000003");
    private static readonly Guid EndedOrgProject = Guid.Parse("9e000000-0000-0000-0000-000000000004");
    private static readonly Guid NotStartedOrgProject = Guid.Parse("95000000-0000-0000-0000-000000000005");
    private static readonly Guid InactiveOrgProject = Guid.Parse("91000000-0000-0000-0000-000000000006");

    /// <summary>
    /// The additive ORG-GRANT term (the one with NO standing-grant gate — the term whose exposure the
    /// earlier "exposure is nil" claim missed) confers only through a CURRENT membership of an ACTIVE
    /// organization, and a contact-keyed grant carrying an inactive firm confers nothing (ISS-026).
    /// </summary>
    [Fact]
    public async Task GetGrantSetAsync_OrgGrantTerm_ConfersOnlyThroughCurrentMembershipsOfActiveOrganizations()
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        SeedWorld(dataverse);
        var participations = RealParticipationService(dataverse);

        var grants = await participations.GetGrantSetAsync(ContactId, CancellationToken.None);

        grants.Projects.Select(p => p.ProjectId).Should().BeEquivalentTo(
            new[] { DirectProject, CurrentOrgProject },
            "ended (D-2), not-yet-started (D-10) and inactive-organization (ISS-026) memberships confer no org " +
            "grant, and a contact grant whose firm is inactive confers nothing — while a plain contact grant " +
            "and a current, active membership are unaffected");
    }

    /// <summary>
    /// 🔴 The ethical wall OVER-matches, by design (owner D-2 part 2, D-10): an org-keyed deny row still
    /// binds a member whose membership ended by date, has not yet started, or sits under an inactive
    /// organization — none of which CONFERS anything any more.
    /// </summary>
    [Theory]
    [InlineData("ended")]
    [InlineData("not-yet-started")]
    [InlineData("inactive-organization")]
    public async Task ComposeAsync_OrgKeyedDenyRow_StillMatchesAMemberWhoseMembershipNoLongerConfers(string membership)
    {
        var wallOrg = membership switch
        {
            "ended" => OrgEnded,
            "not-yet-started" => OrgNotStarted,
            _ => OrgInactive,
        };

        await using var dataverse = await FakeDataverse.StartAsync();
        SeedWorld(dataverse);
        var denyList = new OrgKeyedDenyList((wallOrg, DirectProject));
        var standing = StandingReader();
        var sut = RealEvaluator(RealParticipationService(dataverse), denyList, standing.Object);

        var set = await sut.ComposeAsync(ContactPrincipal(), AccessibleRecordSetService.ProjectEntity, CancellationToken.None);

        set.Contains(DirectProject).Should().BeFalse(
            $"an org-keyed deny row keeps binding a member whose membership is {membership} — the wall is " +
            "statecode-only, so it matches MORE subjects than confer");
        set.Contains(CurrentOrgProject).Should().BeTrue("records the wall does not name are unaffected");
        denyList.SubjectOrganizationsSeen.Should().BeEquivalentTo(
            new[] { OrgCurrent, OrgEnded, OrgNotStarted, OrgInactive },
            "the veto is handed the WALL set — every statecode-active membership");

        // …and the SERVER-side half of the same claim. This test server does not evaluate OData, so a date
        // term added to the junction $filter at the call site would date-bound the wall in production while
        // every assertion above stayed green.
        AssertEveryJunctionReadSentExactlyTheWallFilter(dataverse);
    }

    /// <summary>
    /// 🔴 ISS-019 (#998) — THE assertion of this task. A junction QUERY fault used to be swallowed into an
    /// empty list, which the veto read as "belongs to no organization": the wall's organization axis
    /// silently stopped matching. Now it denies every queried candidate — and, from the SAME read, the
    /// additive terms contribute nothing.
    /// </summary>
    [Theory]
    [InlineData("500")]
    [InlineData("403")]
    [InlineData("timeout")]
    public async Task ComposeAsync_JunctionQueryFaults_DeniesEveryCandidateAndTheAdditiveTermsContributeNothing(string fault)
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        SeedWorld(dataverse);
        var denyList = new OrgKeyedDenyList();
        var standing = StandingReader();
        var sut = RealEvaluator(RealParticipationService(dataverse, TimeSpan.FromMilliseconds(500)), denyList, standing.Object);

        // Control: the same world, healthy, keeps its access — so the empty set below is caused by the fault.
        var healthy = await sut.ComposeAsync(ContactPrincipal(), AccessibleRecordSetService.ProjectEntity, CancellationToken.None);
        healthy.Contains(DirectProject).Should().BeTrue("control: a healthy junction leaves the direct grant standing");
        healthy.Contains(CurrentOrgProject).Should().BeTrue("control: and the org grant through a current membership");

        dataverse.ClearRequests();
        standing.Invocations.Clear();
        denyList.Calls = 0;
        if (fault == "timeout")
        {
            dataverse.JunctionHangs = true;
        }
        else
        {
            dataverse.JunctionFault = (HttpStatusCode)int.Parse(fault);
        }

        var faulted = await sut.ComposeAsync(ContactPrincipal(), AccessibleRecordSetService.ProjectEntity, CancellationToken.None);

        faulted.RecordIds.Should().BeEmpty(
            $"a junction {fault} must deny EVERY queried candidate — including the direct grant — instead of reading " +
            "as 'belongs to no organization' and letting the wall's organization axis stop matching");
        denyList.Calls.Should().Be(0,
            "the veto is decided by the Unreadable outcome before the deny list is even consulted");

        // The additive direction, from the same fault.
        dataverse.Requests.Should().NotContain(r => r.IsOrgGrantQuery,
            "the org-grant term must not even ask for org grants when memberships are unreadable — a fault must not grant");
        standing.Verify(s => s.ReadForOrganizationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "the org-expansion term must contribute nothing on a faulted read");
    }

    /// <summary>
    /// 🔴 Task 135 (defect C1) — the SAME junction query fault, on the CIAM plane, through
    /// <see cref="CiamContactPrincipalStrategy"/>. Before task 135 the CIAM strategy never read the junction for
    /// the wall at all, so neither this fault nor a healthy organization-keyed entry could touch a ciamlogin.com
    /// caller. Now the strategy's scope comes from the evaluator, so a query fault the read reports as
    /// Unreadable (task 109) removes every candidate there too.
    /// </summary>
    /// <remarks>
    /// Driven over the real transport for the reason this file's header gives: a double that returned
    /// <c>Failed</c> would assert the double. The contact is resolved through the shared identity binder
    /// (task 141) over an in-memory identity store binding <see cref="CiamOid"/> to <see cref="ContactId"/>;
    /// identity resolution has its own suite (tests/integration/auth/UnifiedAccessControl/IdentityBinding), so
    /// the transport under test here is the junction read alone.
    /// </remarks>
    [Theory]
    [InlineData("500")]
    [InlineData("403")]
    public async Task CiamStrategy_JunctionQueryFaults_RemovesEveryCandidateFromTheCiamPrincipal(string fault)
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        SeedWorld(dataverse);
        var denyList = new OrgKeyedDenyList();
        var participations = RealParticipationService(dataverse);
        var identities = new InMemoryContactIdentityStore();
        identities.AddContact(ContactId, oid: CiamOid, plane: IdentityPlaneMarker.External);
        var strategy = new CiamContactPrincipalStrategy(
            IdentityBindingTestKit.Binder(identities),
            RealEvaluator(participations, denyList, StandingReader().Object),
            NullLogger<CiamContactPrincipalStrategy>.Instance);

        // Control: healthy, the CIAM principal holds the direct grant and the current org grant.
        var healthy = await ResolveCiamAsync(strategy);
        healthy.GetAccessibleProjectIds().Should().Contain(new[] { DirectProject, CurrentOrgProject },
            "control: a healthy junction leaves the CIAM contact's direct and current-org grants standing");

        dataverse.ClearRequests();
        denyList.Calls = 0;
        dataverse.JunctionFault = (HttpStatusCode)int.Parse(fault);

        var faulted = await ResolveCiamAsync(strategy);

        dataverse.Requests.Should().Contain(r => r.Collection == "sprk_contactorganizations",
            "precondition: the CIAM composition read the junction for the wall");
        faulted.GetAccessibleProjectIds().Should().BeEmpty(
            $"a junction {fault} on the CIAM plane must deny EVERY candidate — the direct grant included — " +
            "instead of reading as 'belongs to no organization'");
        denyList.Calls.Should().Be(0,
            "the veto is decided by the Unreadable outcome before the deny list is even consulted");
    }

    private static async Task<CallerPrincipal> ResolveCiamAsync(CiamContactPrincipalStrategy strategy)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim("oid", CiamOid), new Claim("iss", "https://spaarketest.ciamlogin.com/tid/v2.0") },
                "Ciam")),
        };

        var resolution = await strategy.ResolveAsync(context, CancellationToken.None);
        resolution.IsResolved.Should().BeTrue("precondition: the CIAM contact resolves by its bound oid");
        return resolution.Principal!;
    }

    /// <summary>The CIAM caller's stable oid, bound to <see cref="ContactId"/> in the test's identity store.</summary>
    private const string CiamOid = "c1a00000-0000-0000-0000-0000000000c1";

    /// <summary>
    /// The path that PRE-DATES ISS-019 still denies: when the REAL junction entry cannot acquire its token or
    /// its API url, every queried candidate is denied. Task 109's new fault reporting must not have weakened it.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the real entry.</b> Every other test of this path overrides
    /// <c>ReadOrganizationMembershipsAsync</c> with a double that THROWS, so only the evaluator's catch is
    /// pinned. A try/catch added INSIDE the real entry that maps the acquisition fault to
    /// <see cref="ActiveOrgMemberships.None"/> — "belongs to no organization", ISS-019's exact shape — passed
    /// every one of them (verifier finding, 2026-10-01). That is the blind spot this file's header names: a
    /// double that throws where production does not.</para>
    /// <para><b>Why the fault is CONFINED to the entry's acquisition.</b> The flag read and the
    /// org-reference read share the same token and API url, and each fails closed on its own; on a grant-set
    /// cache miss the grant read would fail to an empty set too. A fault that struck every read would
    /// therefore deny everything whatever the junction entry did, and the test would pass under the very
    /// weakening it exists to catch. So the grant set is served from the production cache, the fault is armed
    /// for ONE acquisition, and the preconditions below prove both that it struck the junction entry and that
    /// the later reads acquired normally.</para>
    /// </remarks>
    [Theory]
    [InlineData("token")]
    [InlineData("api-url")]
    public async Task ComposeAsync_JunctionEntryCannotAcquireItsTokenOrApiUrl_DeniesEveryCandidate(string fault)
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        SeedWorld(dataverse);
        var cache = new TenantCache(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<TenantCache>.Instance);

        // "token": tokens live under the service's 5-minute refresh margin, so it never caches one and the
        // junction entry's acquisition really reaches the credential. "api-url": a long-lived token, cached by
        // the control, so the junction entry's ONLY configuration read is GetDataverseApiUrl's.
        var credential = new ArmableTokenCredential(fault == "token" ? TimeSpan.FromMinutes(1) : TimeSpan.FromHours(1));
        var serviceUrl = new ArmableServiceUrl();
        var denyList = new OrgKeyedDenyList();
        var sut = RealEvaluator(
            RealParticipationService(dataverse, cache: cache, credential: credential, serviceUrl: serviceUrl),
            denyList,
            StandingReader().Object);

        // Control: healthy, keeps its access — and warms the grant-set cache, so the candidates below exist
        // whatever the junction entry does.
        var healthy = await sut.ComposeAsync(ContactPrincipal(), AccessibleRecordSetService.ProjectEntity, CancellationToken.None);
        healthy.Contains(DirectProject).Should().BeTrue("control: a healthy entry leaves the direct grant standing");
        healthy.Contains(CurrentOrgProject).Should().BeTrue("control: and the org grant through a current membership");

        dataverse.ClearRequests();
        denyList.Calls = 0;
        if (fault == "token")
        {
            credential.FailNextRequest = true;
        }
        else
        {
            serviceUrl.FailNextRead = true;
        }

        var faulted = await sut.ComposeAsync(ContactPrincipal(), AccessibleRecordSetService.ProjectEntity, CancellationToken.None);

        // Preconditions — the fault struck the junction entry's acquisition, and nothing else.
        (fault == "token" ? credential.Failures : serviceUrl.Failures).Should().Be(1,
            "precondition: the armed acquisition fault fired exactly once");
        dataverse.Requests.Should().NotContain(r => r.Collection == "sprk_externalrecordaccesses",
            "precondition: the grant set came from the cache, so its candidates exist independently of the fault");
        dataverse.Requests.Should().NotContain(r => r.Collection == "sprk_contactorganizations",
            "precondition: the fault struck BEFORE the junction query was sent — acquisition, not the query (the " +
            "query-fault path is ISS-019's, pinned above)");
        dataverse.Requests.Should().Contain(r => r.Collection == "sprk_projects",
            "precondition: the later flag read acquired its token and url normally and was sent — nothing but the " +
            "junction entry failed, so no other read's fail-closed can deny on this test's behalf");

        faulted.RecordIds.Should().BeEmpty(
            $"a junction entry that cannot acquire its {fault} must deny EVERY queried candidate — the direct grant " +
            "included — rather than reading as 'belongs to no organization'");
        denyList.Calls.Should().Be(0,
            "the veto is decided by the Unreadable outcome before the deny list is even consulted");
    }

    /// <summary>
    /// A caller who abandons the request mid-junction-read gets the cancellation back — not a grant set — and
    /// nothing is cached, so the contact's direct grants are not lost beyond the abandoned request.
    /// </summary>
    /// <remarks>
    /// <para>Task 109's code-review finding W3 pinned the OPPOSITE here ("keeps the direct grants"): the grant-set
    /// read's catch-all used to turn any escaping exception into an EMPTY grant set that was then cached, so the
    /// junction query reported the caller's cancellation as Failed instead of rethrowing it. Task 132 (C12) made that
    /// catch rethrow the caller's cancellation and stopped any faulted set from being cached, which removed W3's
    /// premise — and left a window where a client abort during the junction read RETURNED a faulted set instead of
    /// propagating (task 132 criterion 3; verifier round r1). The query now rethrows.</para>
    /// <para>W3's real concern — a client abort must never cache "no access at all" — is what the last two assertions
    /// keep: no entry is written, and the next request reads the direct grants. The cancel is fired only once the
    /// junction request is in flight (no timer), so the window under test is exactly the junction read.</para>
    /// </remarks>
    [Fact]
    public async Task GetGrantSetAsync_CallerCancelsDuringTheJunctionRead_PropagatesTheCancellation_AndCachesNothing()
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        SeedWorld(dataverse);
        dataverse.JunctionHangs = true;
        var cache = RealCache();
        var participations = RealParticipationService(dataverse, cache: cache);
        using var abandoned = new CancellationTokenSource();

        var call = participations.GetGrantSetAsync(ContactId, abandoned.Token);
        await dataverse.JunctionRequestSeen.Task; // the junction read is in flight — the grant query already answered
        abandoned.Cancel();

        await call.Invoking(async c => await c).Should().ThrowAsync<OperationCanceledException>(
            "a client abort is the caller's cancellation — not a grant set, faulted or otherwise");
        (await cache.GetStringAsync(RequestTenant, ExternalParticipationService.ExternalAccessResource,
                ContactId.ToString(), ExternalParticipationService.CacheVersion))
            .Should().BeNull("an abandoned read is never stored as the contact's grant set");

        dataverse.JunctionHangs = false;
        var next = await participations.GetGrantSetAsync(ContactId, CancellationToken.None);
        next.Projects.Select(p => p.ProjectId).Should().Contain(DirectProject,
            "the contact's direct grants are lost to the abandoned request only — the next request reads them");
    }

    /// <summary>
    /// Exactly ONE junction read per resolution, and it serves BOTH named sets (task 043's single-snapshot
    /// invariant, NFR-02). Counted on the wire, with the grant set served from the production cache so the
    /// count is the evaluator's resolution alone.
    /// </summary>
    /// <remarks>
    /// The grant set's own org-grant term reads the junction too, inside <c>GetGrantSetAsync</c>, and that
    /// read is cached for 60 s with the rest of the grant set — task 043's accounting counts it under "grant
    /// set", and it is unchanged by task 109 apart from now using the conferring set. On a cache MISS a
    /// composition therefore sees one more junction request; that is the pre-existing org-grant read, not a
    /// second read for either consumer below.
    /// </remarks>
    [Fact]
    public async Task ComposeAsync_OneJunctionReadPerResolution_ServesTheConferringAndTheWallSets()
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        SeedWorld(dataverse);
        var cache = new TenantCache(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<TenantCache>.Instance);
        var denyList = new OrgKeyedDenyList();
        var standing = StandingReader();
        var sut = RealEvaluator(RealParticipationService(dataverse, cache: cache), denyList, standing.Object);

        await sut.ComposeAsync(ContactPrincipal(), AccessibleRecordSetService.ProjectEntity, CancellationToken.None);
        dataverse.ClearRequests();
        standing.Invocations.Clear();
        denyList.SubjectOrganizationsSeen.Clear();

        await sut.ComposeAsync(ContactPrincipal(), AccessibleRecordSetService.ProjectEntity, CancellationToken.None);

        dataverse.Requests.Should().NotContain(r => r.Collection == "sprk_externalrecordaccesses",
            "precondition: the grant set came from the cache, so every junction request below is the evaluator's");
        var junctionReads = dataverse.Requests.Where(r => r.Collection == "sprk_contactorganizations").ToList();
        junctionReads.Should().ContainSingle("ONE junction read per resolution — a second would re-open the two-snapshot hazard");
        junctionReads[0].Select.Should().Contain("sprk_startdate").And.Contain("sprk_enddate");
        junctionReads[0].Expand.Should().Contain("sprk_Organization");
        AssertEveryJunctionReadSentExactlyTheWallFilter(dataverse);

        // Both consumers fed from that one read, each with its own set.
        standing.Verify(s => s.ReadForOrganizationAsync(OrgCurrent, It.IsAny<CancellationToken>()), Times.Once,
            "the org-expansion term reads the CONFERRING set");
        standing.Verify(s => s.ReadForOrganizationAsync(
                It.Is<Guid>(id => id == OrgEnded || id == OrgNotStarted || id == OrgInactive), It.IsAny<CancellationToken>()),
            Times.Never,
            "ended, not-yet-started and inactive-organization memberships derive nothing (D-2, D-10, ISS-026)");
        denyList.SubjectOrganizationsSeen.Should().BeEquivalentTo(
            new[] { OrgCurrent, OrgEnded, OrgNotStarted, OrgInactive },
            "the veto reads the WALL set from the same response");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Task 137 (#1060, defect C5) — over the same real transport
    // ═════════════════════════════════════════════════════════════════════════════

    public static TheoryData<string> BothContactPlanes => new() { "ciam", "workforce" };

    /// <summary>
    /// Task 137 verifies task 109 on BOTH contact planes (#1006 read half, #999, D-10): through the real read, the
    /// CIAM principal and the workforce contact-only composition hold exactly the direct grant and the org grant of
    /// the CURRENT membership of an ACTIVE organization — never the ended, the not-yet-started or the
    /// inactive-organization one, nor a contact grant carrying an inactive firm.
    /// </summary>
    [Theory]
    [MemberData(nameof(BothContactPlanes))]
    public async Task Task109Guards_OnBothPlanes_ConferOnlyThroughCurrentMembershipsOfActiveOrganizations(string plane)
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        SeedWorld(dataverse);
        var world = new RequestScopedWorld(dataverse);

        var projects = await world.ProjectIdsAsync(plane);

        projects.Should().BeEquivalentTo(new[] { DirectProject, CurrentOrgProject },
            $"{plane}: ended (#999 / D-2), not-yet-started (D-10) and inactive-organization (#1006) memberships confer " +
            "no org grant, and a contact grant whose firm is inactive confers nothing");
    }

    /// <summary>
    /// Task 137 verifies task 109's other half on the CIAM plane: the ethical wall's ORGANIZATION axis keeps binding a
    /// FORMER member (ended, not yet started, inactive organization) — the wall set is statecode-only.
    /// </summary>
    [Theory]
    [InlineData("ended")]
    [InlineData("not-yet-started")]
    [InlineData("inactive-organization")]
    public async Task Task109Guards_OnTheCiamPlane_TheOrgKeyedWallStillBindsAFormerMember(string membership)
    {
        var wallOrg = membership switch { "ended" => OrgEnded, "not-yet-started" => OrgNotStarted, _ => OrgInactive };
        await using var dataverse = await FakeDataverse.StartAsync();
        SeedWorld(dataverse);
        var world = new RequestScopedWorld(dataverse, new OrgKeyedDenyList((wallOrg, DirectProject)));

        var projects = await world.ProjectIdsAsync("ciam");

        projects.Should().NotContain(DirectProject, $"CIAM: a {membership} membership is still a wall subject");
        projects.Should().Contain(CurrentOrgProject, "records the wall does not name are unaffected");
    }

    /// <summary>
    /// C5: a contact deactivated AFTER sign-in loses every contact-sourced record on its NEXT request — on both planes —
    /// while the grant cache is WARM (the second request reads no grant row) and the identity layer still says
    /// "active" (the CIAM identity store keeps the contact active; the workforce principal is pre-resolved, as a warm
    /// 10-minute identity cache would hand it over). Only the live contact-state read changed.
    /// </summary>
    [Theory]
    [MemberData(nameof(BothContactPlanes))]
    public async Task InactiveContact_AfterSignIn_LosesEveryRecordOnTheNextRequest_WithWarmCaches(string plane)
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        SeedWorld(dataverse);
        var world = new RequestScopedWorld(dataverse, cache: RealCache());

        var before = await world.ProjectIdsAsync(plane);
        before.Should().BeEquivalentTo(new[] { DirectProject, CurrentOrgProject }, "control: the active contact's access");
        var grantReads = GrantReads(dataverse);

        dataverse.ContactStateCode = 1; // deactivated in Dataverse — nothing else changes

        var after = await world.ProjectIdsAsync(plane);

        after.Should().BeEmpty($"{plane}: an inactive contact confers nothing through any contact-sourced term");
        GrantReads(dataverse).Should().Be(grantReads,
            "the grant set came from the WARM cache — the live state read, not a cache expiry, removed the access");
        dataverse.Requests.Count(r => r.Collection.StartsWith("contacts(", StringComparison.Ordinal)).Should().Be(2,
            "the contact's state is read live once per request, never cached across requests");
    }

    /// <summary>C5, fail closed: a contact-state read that faults confers nothing on either plane (the control is healthy).</summary>
    [Theory]
    [MemberData(nameof(BothContactPlanes))]
    public async Task ContactStateReadFaults_ConfersNothing(string plane)
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        SeedWorld(dataverse);
        var world = new RequestScopedWorld(dataverse);
        (await world.ProjectIdsAsync(plane)).Should().NotBeEmpty("control: a readable active contact keeps its access");

        dataverse.ContactStateFault = HttpStatusCode.InternalServerError;

        (await world.ProjectIdsAsync(plane)).Should().BeEmpty($"{plane}: an unreadable contact state is not Active");
    }

    /// <summary>
    /// C5, fail closed (ADR-003 constraint "a missing row is treated as INACTIVE"): a contact-state read that answers
    /// 404 — the contact row is gone — confers nothing on either plane. The twin of
    /// <see cref="ContactStateReadFaults_ConfersNothing"/>: 404 has its own branch in the production read, so a regression
    /// that maps it to Active would otherwise go unnoticed (task 137 r3, verifier finding 1).
    /// </summary>
    [Theory]
    [MemberData(nameof(BothContactPlanes))]
    public async Task ContactStateReadAnswers404_TheMissingRowConfersNothing(string plane)
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        SeedWorld(dataverse);
        var world = new RequestScopedWorld(dataverse);
        (await world.ProjectIdsAsync(plane)).Should().NotBeEmpty("control: a readable active contact keeps its access");

        dataverse.ContactStateFault = HttpStatusCode.NotFound;

        (await world.ProjectIdsAsync(plane)).Should().BeEmpty($"{plane}: a missing contact row is not Active");
        dataverse.Requests.Count(r => r.Collection.StartsWith("contacts(", StringComparison.Ordinal)).Should().Be(2,
            "the 404 came from the live contact-state read itself, once per request");
    }

    /// <summary>
    /// C5: an INACTIVE root confers nothing to a contact on either plane — the state rides the existing batched flag
    /// read — and reactivating it restores the access with no other change (a read-time rule, not a grant write).
    /// </summary>
    [Theory]
    [MemberData(nameof(BothContactPlanes))]
    public async Task InactiveProject_ConfersNothing_AndReactivationRestoresIt(string plane)
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        SeedWorld(dataverse);
        var world = new RequestScopedWorld(dataverse);

        dataverse.InactiveProjects.Add(DirectProject);
        var inactive = await world.ProjectIdsAsync(plane);

        inactive.Should().NotContain(DirectProject, $"{plane}: an active grant on an inactive project confers nothing");
        inactive.Should().Contain(CurrentOrgProject, "only the inactive root is affected");
        dataverse.Requests.Where(r => r.Collection == "sprk_projects" && r.Select.Contains("sprk_issecure")).Should().NotBeEmpty().And.OnlyContain(
            r => r.Select.Split(',', StringSplitOptions.None).Contains("statecode"),
            "the root's state is selected by the SAME batched flag read");

        dataverse.InactiveProjects.Clear();
        (await world.ProjectIdsAsync(plane)).Should().Contain(DirectProject, "reactivation restores access — no data repair");
    }

    // ── Task 150 (round 26 item 2(a)): an EMPTY sprk_issecure over the REAL flag read ─────────────────
    //
    // EmptySecureFlagFailsClosedTests pins the row mapping (FlagsFrom). These pin the WIRE PATH: the production
    // GetRootRecordFlagsAsync deserializing a row whose field-secured flag the BFF identity could not read, whichever
    // shape Dataverse sends it in, and the read-time evaluator acting on the answer. A call site that defaulted the
    // flag before the mapping (`row.sprk_issecure ?? false`) passes every mapping test and fails these.

    public static TheoryData<string> EmptySecureFlagShapes => new() { "omitted", "null" };

    public static TheoryData<string, string> EmptySecureFlagShapesOnBothPlanes => new()
    {
        { "omitted", "ciam" },
        { "omitted", "workforce" },
        { "null", "ciam" },
        { "null", "workforce" },
    };

    private static EmptyFlagShape ShapeOf(string shape)
        => shape == "omitted" ? EmptyFlagShape.Omitted : EmptyFlagShape.Null;

    [Theory]
    [MemberData(nameof(EmptySecureFlagShapes))]
    public async Task GetRootRecordFlagsAsync_TheRealReadOfAnEmptySecureFlag_IsUnreadable(string shape)
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        dataverse.EmptySecureFlags[DirectProject] = ShapeOf(shape);
        var participations = RealParticipationService(dataverse);

        var flags = await participations.GetRootRecordFlagsAsync(
            AccessibleRecordSetService.ProjectEntity, new[] { DirectProject, CurrentOrgProject }, CancellationToken.None);

        flags[DirectProject].Should().Be(RootRecordFlags.Unreadable,
            $"a flag {shape} on the wire is a masked value, and unknown is the fail-closed answer (round 17 item 3)");
        flags[CurrentOrgProject].IsUnreadable.Should().BeFalse("control: a row whose flag reads false is mapped as stored");
        flags[CurrentOrgProject].IsSecure.Should().BeFalse();

        var flagRead = dataverse.Requests.Single(r => r.Collection == "sprk_projects");
        flagRead.Select.Split(',').Should().Contain("sprk_issecure", "the flag was asked for — it came back empty");
    }

    [Theory]
    [MemberData(nameof(EmptySecureFlagShapesOnBothPlanes))]
    public async Task ComposeAsync_ARecordWhoseSecureFlagReadsEmptyOnTheWire_IsSuppressed(string shape, string plane)
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        SeedWorld(dataverse);
        var world = new RequestScopedWorld(dataverse);
        (await world.ProjectIdsAsync(plane)).Should().Contain(DirectProject,
            "control: with its flag readable the directly granted project is accessible");

        dataverse.EmptySecureFlags[DirectProject] = ShapeOf(shape);
        var masked = await world.ProjectIdsAsync(plane);

        masked.Should().NotContain(DirectProject,
            $"{plane}: an empty flag is Unreadable — every contact-sourced right on the record is removed");
        masked.Should().Contain(CurrentOrgProject, "only the record whose flag came back empty is affected");
    }

    // ── Task 137 r2: the PRODUCTION organization-member page read, over the transport ─────────────────

    private static readonly Guid FanOutOrg = Guid.Parse("0f000000-0000-0000-0000-000000000137");
    private static readonly Guid OtherOrg = Guid.Parse("0f000000-0000-0000-0000-000000000138");
    private const string RequestTenant = "11111111-2222-3333-4444-555555555555";

    /// <summary>
    /// Task 137 r2 (verifier finding 1): the ONE invalidation routine's organization fan-out, through the REAL
    /// <c>ReadOrganizationMemberPageAsync</c> — every other fan-out test substitutes that read. An organization of
    /// 1,203 active members is three server pages of 500: the request asks for 500 a page (<c>Prefer</c>), sends
    /// exactly <see cref="ExternalOrganizationMembership.ActiveMembersFilter"/> on every page, follows the server's
    /// opaque <c>@odata.nextLink</c> verbatim, and every member's cached grant set is removed — nobody else's.
    /// </summary>
    /// <remarks>
    /// A defect in this read is otherwise invisible: the routine catches it, logs a warning and lets the members
    /// fall back to the 60-second TTL, so a broken read and a working one look the same to every write endpoint.
    /// The fake server honours <c>odata.maxpagesize</c> as Dataverse does (absent, it answers one page of up to
    /// 5,000), and its skip tokens are unguessable, so the page count and the tokens are the client's own doing.
    /// </remarks>
    [Fact]
    public async Task OrgFanOut_TheRealMemberPageRead_PagesTheActiveMembersFilterToTheEnd_AndInvalidatesEveryMember()
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        var members = Enumerable.Range(1, 1203).Select(i => new Guid($"c0000000-0000-0000-0000-{i:D12}")).ToArray();
        var otherMember = Guid.Parse("c0000000-0000-0000-ffff-000000000001");
        dataverse.OrganizationMembers[FanOutOrg] = members;
        dataverse.OrganizationMembers[OtherOrg] = new[] { otherMember };

        var cache = RealCache();
        foreach (var contact in members.Append(otherMember))
        {
            await SeedGrantSetAsync(cache, contact);
        }

        var sut = RealParticipationService(dataverse, cache: cache);

        var result = await sut.InvalidateGrantSetsAsync(Array.Empty<Guid>(), new[] { FanOutOrg }, CancellationToken.None);

        result.OrganizationsNotFullyExpanded.Should().BeEmpty("all three pages were read");
        result.ContactCount.Should().Be(members.Length);
        result.RemovalsSucceeded.Should().Be(members.Length, "one tenant (the request's tid) × every member");
        result.RemovalsFailed.Should().Be(0);

        foreach (var contact in members)
        {
            (await CachedGrantSetAsync(cache, contact)).Should().BeNull($"member {contact} of the organization is invalidated");
        }

        (await CachedGrantSetAsync(cache, otherMember)).Should().NotBeNull("another organization's member is untouched");

        var reads = MemberPageReads(dataverse);
        reads.Should().HaveCount(3, "1,203 members at 500 a page — the client asked for 500 and followed every nextLink");
        reads.Should().OnlyContain(
            r => r.Filter == ExternalOrganizationMembership.ActiveMembersFilter(FanOutOrg),
            "every page — the first and each nextLink — carries exactly the shared ACTIVE-members $filter");
        reads.Should().OnlyContain(r => r.Select == ExternalOrganizationMembership.MemberSelect);
        reads.Should().OnlyContain(r => r.Prefer == $"odata.maxpagesize={ExternalParticipationService.OrganizationMemberPageSize}");
        reads.Select(r => r.SkipToken).Should().Equal(
            new[] { "" }.Concat(dataverse.IssuedSkipTokens),
            "page one is the built query; every later page is the server's own @odata.nextLink, sent back verbatim");
    }

    /// <summary>
    /// Task 137 r2: a member page the server FAILS is a fault, never an empty page — the organization is reported as
    /// not fully expanded, the members already read are invalidated, the rest keep their entry (to the TTL) and the
    /// routine does not throw. Over the real read, so <c>EnsureSuccessStatusCode</c> is what decides it.
    /// </summary>
    [Fact]
    public async Task OrgFanOut_TheRealMemberPageRead_AFailedSecondPage_ReportsTheGap_AndKeepsWhatWasRead()
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        var members = Enumerable.Range(1, 700).Select(i => new Guid($"c1000000-0000-0000-0000-{i:D12}")).ToArray();
        dataverse.OrganizationMembers[FanOutOrg] = members;
        dataverse.MemberPageFaultAt = 1;

        var cache = RealCache();
        foreach (var contact in members)
        {
            await SeedGrantSetAsync(cache, contact);
        }

        var sut = RealParticipationService(dataverse, cache: cache);

        var result = await sut.InvalidateGrantSetsAsync(Array.Empty<Guid>(), new[] { FanOutOrg }, CancellationToken.None);

        result.OrganizationsNotFullyExpanded.Should().Equal(new[] { FanOutOrg }, "a failed page is a gap, reported");
        result.ContactCount.Should().Be(500, "page one's members were read and invalidated");
        MemberPageReads(dataverse).Should().HaveCount(2, "the walk stopped at the failed page");

        foreach (var contact in members.Take(500))
        {
            (await CachedGrantSetAsync(cache, contact)).Should().BeNull();
        }

        foreach (var contact in members.Skip(500))
        {
            (await CachedGrantSetAsync(cache, contact)).Should().NotBeNull("an unread member expires on the TTL instead");
        }
    }

    private static List<SeenRequest> MemberPageReads(FakeDataverse dataverse)
        => dataverse.Requests
            .Where(r => r.Collection == ExternalOrganizationMembership.EntitySet
                        && r.Filter.StartsWith("_sprk_organization_value eq ", StringComparison.Ordinal))
            .ToList();

    private static Task SeedGrantSetAsync(ITenantCache cache, Guid contactId)
        => cache.SetAsync(RequestTenant, ExternalParticipationService.ExternalAccessResource, contactId.ToString(),
            ExternalParticipationService.CacheVersion, new List<int> { 1 }, TimeSpan.FromMinutes(5));

    private static Task<List<int>?> CachedGrantSetAsync(ITenantCache cache, Guid contactId)
        => cache.GetAsync<List<int>>(RequestTenant, ExternalParticipationService.ExternalAccessResource,
            contactId.ToString(), ExternalParticipationService.CacheVersion);

    private static int GrantReads(FakeDataverse dataverse)
        => dataverse.Requests.Count(r => r.Collection == "sprk_externalrecordaccesses");

    private static TenantCache RealCache()
        => new(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<TenantCache>.Instance);

    /// <summary>
    /// The real participation service and evaluator over the fake transport, where every call of
    /// <see cref="ProjectIdsAsync"/> is a NEW request (a fresh <see cref="HttpContext"/> carrying the tid), so nothing
    /// request-scoped leaks between them — exactly as two HTTP requests would not share one.
    /// </summary>
    private sealed class RequestScopedWorld
    {
        private readonly HttpContextAccessor _accessor = new();
        private readonly AccessibleRecordSetService _evaluator;

        public RequestScopedWorld(FakeDataverse dataverse, INoAccessListReader? denyList = null, ITenantCache? cache = null)
        {
            var participations = new ExternalParticipationService(
                dataverse.CreateClient(TimeSpan.FromSeconds(30)),
                cache ?? Mock.Of<ITenantCache>(),
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = FakeServiceUrl })
                    .Build(),
                new StaticTokenCredential(),
                _accessor,
                NullLogger<ExternalParticipationService>.Instance);
            _evaluator = RealEvaluator(participations, denyList ?? new OrgKeyedDenyList(), StandingReader().Object);
        }

        public async Task<IReadOnlyCollection<Guid>> ProjectIdsAsync(string plane)
        {
            _accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[]
                    {
                        new Claim("tid", "11111111-2222-3333-4444-555555555555"),
                        new Claim("oid", CiamOid),
                        new Claim("iss", "https://spaarketest.ciamlogin.com/tid/v2.0"),
                    },
                    "test")),
            };

            if (plane == "workforce")
            {
                var set = await _evaluator.ComposeAsync(
                    ContactPrincipal(), AccessibleRecordSetService.ProjectEntity, CancellationToken.None);
                return set.RecordIds.ToList();
            }

            var identities = new InMemoryContactIdentityStore();
            identities.AddContact(ContactId, oid: CiamOid, plane: IdentityPlaneMarker.External);
            var strategy = new CiamContactPrincipalStrategy(
                IdentityBindingTestKit.Binder(identities), _evaluator, NullLogger<CiamContactPrincipalStrategy>.Instance);
            var resolution = await strategy.ResolveAsync(_accessor.HttpContext, CancellationToken.None);
            resolution.IsResolved.Should().BeTrue("precondition: the CIAM identity layer still resolves the contact");
            return resolution.Principal!.GetAccessibleProjectIds().ToList();
        }
    }

    // ── world ────────────────────────────────────────────────────────────────────────────────────

    private static void SeedWorld(FakeDataverse dataverse)
    {
        // Relative to the REAL clock: the production read compares against today in UTC. Offsets of 3 days
        // keep the outcome stable across a midnight crossing; the exact day boundaries are pinned above.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        dataverse.OrganizationStates[OrgCurrent] = 0;
        dataverse.OrganizationStates[OrgEnded] = 0;
        dataverse.OrganizationStates[OrgNotStarted] = 0;
        dataverse.OrganizationStates[OrgInactive] = 1;

        dataverse.Memberships.Add(new MembershipSpec(OrgCurrent, Start: null, End: null));
        dataverse.Memberships.Add(new MembershipSpec(OrgEnded, Start: null, End: today.AddDays(-3)));
        dataverse.Memberships.Add(new MembershipSpec(OrgNotStarted, Start: today.AddDays(3), End: null));
        dataverse.Memberships.Add(new MembershipSpec(OrgInactive, Start: null, End: null));

        dataverse.ContactGrants.Add(new GrantSpec(DirectProject, OrganizationId: null));
        dataverse.ContactGrants.Add(new GrantSpec(FirmInactiveProject, OrganizationId: OrgInactive));

        dataverse.OrgGrants.Add(new GrantSpec(CurrentOrgProject, OrgCurrent));
        dataverse.OrgGrants.Add(new GrantSpec(EndedOrgProject, OrgEnded));
        dataverse.OrgGrants.Add(new GrantSpec(NotStartedOrgProject, OrgNotStarted));
        dataverse.OrgGrants.Add(new GrantSpec(InactiveOrgProject, OrgInactive));
    }

    private static WorkforcePrincipal ContactPrincipal() => new()
    {
        Kind = WorkforcePrincipalKind.ContactOnly,
        ContactId = ContactId,
        Oid = "0a000000-0000-0000-0000-0000000000a1",
        TenantId = "11111111-2222-3333-4444-555555555555",
    };

    private const string FakeServiceUrl = "https://fake-dataverse.crm.dynamics.com";

    private static ExternalParticipationService RealParticipationService(
        FakeDataverse dataverse,
        TimeSpan? timeout = null,
        ITenantCache? cache = null,
        TokenCredential? credential = null,
        ArmableServiceUrl? serviceUrl = null)
    {
        var configuration = serviceUrl is not null
            ? new ConfigurationBuilder().Add(serviceUrl).Build()
            : new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Dataverse:ServiceUrl"] = FakeServiceUrl,
                })
                .Build();

        // With a cache, the request carries a tid claim (the grant-set cache is skipped without one).
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(cache is null
            ? null
            : new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim("tid", "11111111-2222-3333-4444-555555555555") }, "test")),
            });

        return new ExternalParticipationService(
            dataverse.CreateClient(timeout ?? TimeSpan.FromSeconds(30)),
            cache ?? Mock.Of<ITenantCache>(),
            configuration,
            credential ?? new StaticTokenCredential(),
            accessor.Object,
            NullLogger<ExternalParticipationService>.Instance);
    }

    private static AccessibleRecordSetService RealEvaluator(
        ExternalParticipationService participations, INoAccessListReader denyList, ISubjectStandingGrantReader standing)
    {
        // No walk produces records here — org baselines are NotHeld — so the resolver contributes nothing.
        var membership = new Mock<IMembershipResolverService>();
        return new AccessibleRecordSetService(
            membership.Object, participations, standing, denyList,
            Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.UnlinkedIdentityStore(),
            Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.InternalSystemUsers(),
            NullLogger<AccessibleRecordSetService>.Instance);
    }

    private static Mock<ISubjectStandingGrantReader> StandingReader()
    {
        var standing = new Mock<ISubjectStandingGrantReader>();
        standing.Setup(s => s.ReadForContactAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StandingGrantState.NotHeld);
        standing.Setup(s => s.ReadForOrganizationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StandingGrantState.NotHeld);
        return standing;
    }

    /// <summary>
    /// The junction <c>$filter</c> the SERVER received is EXACTLY <c>BuildOrganizationMembershipFilter</c>'s —
    /// nothing appended, nothing inlined.
    /// </summary>
    /// <remarks>
    /// That filter defines the FR-23 WALL set, which must stay <c>statecode</c>-only (owner D-2 part 2, D-10).
    /// The builder test pins the builder; <c>ExternalAccessQueryIntegrityGuardTests</c> pins that the call site
    /// names it. Neither sees a term APPENDED after the builder call —
    /// <c>$"?$filter={BuildOrganizationMembershipFilter(contactId)} and (sprk_enddate eq null or …)"</c> — which
    /// date-bounds the wall server-side, a fail-OPEN change to a veto, while every projection assertion stays
    /// green because this test server does not evaluate OData (verifier finding, 2026-10-01). Asserting the
    /// wire catches that edit in any form, on any line.
    /// </remarks>
    private static void AssertEveryJunctionReadSentExactlyTheWallFilter(FakeDataverse dataverse)
    {
        var filters = dataverse.Requests
            .Where(r => r.Collection == "sprk_contactorganizations")
            .Select(r => r.Filter)
            .ToList();

        filters.Should().NotBeEmpty("precondition: the junction was read");
        filters.Should().OnlyContain(
            f => f == ExternalParticipationService.BuildOrganizationMembershipFilter(ContactId),
            "the junction $filter defines the WALL set and must reach the server exactly as the statecode-only " +
            "builder emits it — a date term appended at the call site would narrow the ethical wall (D-2 part 2, D-10)");
    }

    /// <summary>The two shapes a masked (field-secured, unreadable) <c>sprk_issecure</c> arrives in.</summary>
    private enum EmptyFlagShape
    {
        /// <summary>The property is absent from the row.</summary>
        Omitted,

        /// <summary>The property is present with a JSON null.</summary>
        Null,
    }

    private sealed record MembershipSpec(Guid OrganizationId, DateOnly? Start, DateOnly? End);

    private sealed record GrantSpec(Guid ProjectId, Guid? OrganizationId);

    /// <summary>One request as the server received it, decoded (task 137 r2: plus its <c>Prefer</c> header and
    /// <c>$skiptoken</c>, for the organization-member page read).</summary>
    private sealed record SeenRequest(
        string Collection, string Filter, string Select, string Expand, string Prefer = "", string SkipToken = "")
    {
        public bool IsOrgGrantQuery =>
            Collection == "sprk_externalrecordaccesses" && Filter.Contains("_sprk_contact_value eq null", StringComparison.Ordinal);
    }

    /// <summary>
    /// The deny list at its module boundary: org-subject × record entries. Records which subject
    /// organizations the veto handed it, so a test can see WHICH named set reached the wall.
    /// </summary>
    private sealed class OrgKeyedDenyList : INoAccessListReader
    {
        private readonly (Guid SubjectOrg, Guid RecordId)[] _entries;

        public OrgKeyedDenyList(params (Guid SubjectOrg, Guid RecordId)[] entries) => _entries = entries;

        public HashSet<Guid> SubjectOrganizationsSeen { get; } = new();

        public int Calls { get; set; }

        public Task<NoAccessListResult> GetDeniedRecordsAsync(
            Guid? contactId, IReadOnlyCollection<Guid> organizationIds,
            IReadOnlyCollection<NoAccessCandidateRecord> candidates, CancellationToken ct = default)
        {
            Calls++;
            SubjectOrganizationsSeen.UnionWith(organizationIds);

            var denied = candidates
                .Where(c => _entries.Any(e => e.RecordId == c.RecordId && organizationIds.Contains(e.SubjectOrg)))
                .Select(c => c.RecordId)
                .ToHashSet();

            return Task.FromResult(new NoAccessListResult
            {
                DeniedRecordIds = denied,
                DenyingEntryIds = denied.ToDictionary(id => id, _ => (IReadOnlyList<Guid>)Array.Empty<Guid>()),
            });
        }

        /// <summary>
        /// Task 143: the three-subject overload the systemuser plane calls. The entries here are organization-subject,
        /// so a denial is reported with that subject kind — what the systemuser plane splits on (owner N3).
        /// </summary>
        public async Task<NoAccessListResult> GetDeniedRecordsAsync(
            NoAccessSubjects subjects, IReadOnlyCollection<NoAccessCandidateRecord> candidates, CancellationToken ct = default)
        {
            var result = await GetDeniedRecordsAsync(
                subjects.ContactIds.FirstOrDefault(), subjects.OrganizationIds, candidates, ct);
            return new NoAccessListResult
            {
                DeniedRecordIds = result.DeniedRecordIds,
                DenyingEntryIds = result.DenyingEntryIds,
                DenyingSubjectKinds = result.DeniedRecordIds.ToDictionary(id => id, _ => NoAccessSubjectKinds.Organization),
            };
        }
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static AccessToken Token => new("test-token-not-a-credential", DateTimeOffset.UtcNow.AddHours(1));

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(Token);
    }

    /// <summary>
    /// A credential that fails the NEXT token request once it is armed, and issues tokens of a chosen lifetime
    /// otherwise (a lifetime under the service's 5-minute refresh margin means every acquisition reaches it).
    /// </summary>
    private sealed class ArmableTokenCredential : TokenCredential
    {
        private readonly TimeSpan _lifetime;

        public ArmableTokenCredential(TimeSpan lifetime) => _lifetime = lifetime;

        public bool FailNextRequest { get; set; }

        public int Failures { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            if (FailNextRequest)
            {
                FailNextRequest = false;
                Failures++;
                throw new Azure.Identity.AuthenticationFailedException("armed test fault: token acquisition failed");
            }

            return new AccessToken("test-token-not-a-credential", DateTimeOffset.UtcNow.Add(_lifetime));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>
    /// Configuration carrying <c>Dataverse:ServiceUrl</c>, whose NEXT read of that key comes back missing once
    /// armed — the service's "Dataverse:ServiceUrl is required" fault, for exactly one acquisition.
    /// </summary>
    private sealed class ArmableServiceUrl : ConfigurationProvider, IConfigurationSource
    {
        private const string Key = "Dataverse:ServiceUrl";

        public ArmableServiceUrl() => Data[Key] = FakeServiceUrl;

        public bool FailNextRead { get; set; }

        public int Failures { get; private set; }

        public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

        public override bool TryGet(string key, out string? value)
        {
            if (FailNextRead && string.Equals(key, Key, StringComparison.OrdinalIgnoreCase))
            {
                FailNextRead = false;
                Failures++;
                value = null;
                return false;
            }

            return base.TryGet(key, out value);
        }
    }

    /// <summary>
    /// An in-memory Dataverse Web API: the three collections this path reads, answering in Dataverse's
    /// JSON shape (lookups as <c>_x_value</c>, Date Only columns as <c>yyyy-MM-dd</c>, the expanded
    /// <c>sprk_Organization</c> carrying its own <c>statecode</c> — the live shape, captured 2026-09-30).
    /// It does not evaluate OData beyond what the assertions need: which org ids an org-grant query asked
    /// for, and which record ids a root read asked for.
    /// </summary>
    private sealed class FakeDataverse : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly List<SeenRequest> _requests = new();
        private readonly object _gate = new();

        private FakeDataverse(WebApplication app) => _app = app;

        public Dictionary<Guid, int?> OrganizationStates { get; } = new();
        public List<MembershipSpec> Memberships { get; } = new();
        public List<GrantSpec> ContactGrants { get; } = new();
        public List<GrantSpec> OrgGrants { get; } = new();

        /// <summary>Non-null: every junction request answers with this status.</summary>
        public HttpStatusCode? JunctionFault { get; set; }

        /// <summary>The junction request never answers, so the client's timeout fires.</summary>
        public bool JunctionHangs { get; set; }

        /// <summary>Completes when a junction request arrives — so a test can act while that read is in flight.</summary>
        public TaskCompletionSource JunctionRequestSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<SeenRequest> Requests
        {
            get { lock (_gate) { return _requests.ToList(); } }
        }

        public void ClearRequests()
        {
            lock (_gate) { _requests.Clear(); }
        }

        public static async Task<FakeDataverse> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseTestServer();

            var app = builder.Build();
            var fake = new FakeDataverse(app);
            app.MapGet("/api/data/v9.2/{collection}", (HttpContext context, string collection) => fake.HandleAsync(context, collection));
            await app.StartAsync();
            return fake;
        }

        public HttpClient CreateClient(TimeSpan timeout)
        {
            var client = _app.GetTestClient();
            client.Timeout = timeout;
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private async Task HandleAsync(HttpContext context, string collection)
        {
            var filter = context.Request.Query["$filter"].ToString();
            var seen = new SeenRequest(
                collection, filter, context.Request.Query["$select"].ToString(), context.Request.Query["$expand"].ToString(),
                context.Request.Headers["Prefer"].ToString(), context.Request.Query["$skiptoken"].ToString());
            lock (_gate) { _requests.Add(seen); }

            switch (collection)
            {
                case "sprk_contactorganizations" when filter.StartsWith("_sprk_organization_value eq ", StringComparison.Ordinal):
                    await WriteMemberPageAsync(context, filter, seen);
                    return;

                case "sprk_contactorganizations":
                    JunctionRequestSeen.TrySetResult();
                    if (JunctionHangs)
                    {
                        try { await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted); }
                        catch (OperationCanceledException) { }
                        return;
                    }

                    if (JunctionFault is { } status)
                    {
                        context.Response.StatusCode = (int)status;
                        return;
                    }

                    await WriteValueAsync(context, Memberships.Select(m => new Dictionary<string, object?>
                    {
                        ["_sprk_organization_value"] = m.OrganizationId,
                        ["sprk_startdate"] = m.Start?.ToString("yyyy-MM-dd"),
                        ["sprk_enddate"] = m.End?.ToString("yyyy-MM-dd"),
                        ["statecode"] = 0,
                        ["sprk_Organization"] = Organization(m.OrganizationId),
                    }));
                    return;

                case "sprk_externalrecordaccesses":
                    var rows = seen.IsOrgGrantQuery
                        ? OrgGrants.Where(g => filter.Contains($"_sprk_organization_value eq {g.OrganizationId}", StringComparison.Ordinal))
                        : ContactGrants;
                    await WriteValueAsync(context, rows.Select(g => new Dictionary<string, object?>
                    {
                        ["_sprk_project_value"] = g.ProjectId,
                        ["sprk_accesslevel"] = (int)ExternalAccessLevel.Collaborate,
                        ["_sprk_organization_value"] = g.OrganizationId,
                        ["sprk_Organization"] = g.OrganizationId is { } org ? Organization(org) : null,
                    }));
                    return;

                case "sprk_projects":
                    var ids = Regex.Matches(filter, @"sprk_projectid eq ([0-9a-fA-F-]{36})")
                        .Select(m => m.Groups[1].Value)
                        .Distinct();
                    await WriteValueAsync(context, ids.Select(id =>
                    {
                        var row = new Dictionary<string, object?>
                        {
                            ["sprk_projectid"] = id,
                            ["sprk_issecure"] = false,
                            ["sprk_accesspermission"] = 100000000,
                            // Task 137: the root's own state rides the flag read (the live column, Active = 0).
                            ["statecode"] = InactiveProjects.Contains(Guid.Parse(id)) ? 1 : 0,
                            ["_sprk_assignedlawfirm1_value"] = null,
                            ["_sprk_assignedlawfirm2_value"] = null,
                        };

                        // Task 150: a field-secured column the caller cannot read is masked — Dataverse leaves the
                        // property out of the row, or returns it as null. Both shapes are served as they arrive live.
                        if (EmptySecureFlags.TryGetValue(Guid.Parse(id), out var shape))
                        {
                            if (shape == EmptyFlagShape.Omitted)
                            {
                                row.Remove("sprk_issecure");
                            }
                            else
                            {
                                row["sprk_issecure"] = null;
                            }
                        }

                        return row;
                    }));
                    return;

                case var single when single.StartsWith("contacts(", StringComparison.Ordinal):
                    // Task 137: the LIVE contact-state read — contacts({id})?$select=statecode, one row.
                    if (ContactStateFault is { } contactFault)
                    {
                        context.Response.StatusCode = (int)contactFault;
                        return;
                    }

                    await context.Response.WriteAsJsonAsync(new Dictionary<string, object?> { ["statecode"] = ContactStateCode });
                    return;

                default:
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
            }
        }

        /// <summary>Task 137: the contact's own <c>statecode</c> the live read returns (Active by default).</summary>
        public int? ContactStateCode { get; set; } = 0;

        /// <summary>Task 137: non-null — the contact-state read answers with this status.</summary>
        public HttpStatusCode? ContactStateFault { get; set; }

        /// <summary>Task 137: projects whose own <c>statecode</c> is Inactive.</summary>
        public HashSet<Guid> InactiveProjects { get; } = new();

        /// <summary>
        /// Task 150 (round 26 item 2(a)): projects whose <c>sprk_issecure</c> comes back EMPTY, in the given shape —
        /// what field-level security does to a column the reading identity may not read. Absent: <c>false</c>.
        /// </summary>
        public Dictionary<Guid, EmptyFlagShape> EmptySecureFlags { get; } = new();

        /// <summary>Task 137 r2: each organization's ACTIVE member contacts (the organization -> members read).</summary>
        public Dictionary<Guid, Guid[]> OrganizationMembers { get; } = new();

        /// <summary>Task 137 r2: non-null — the member page with this zero-based index answers 503.</summary>
        public int? MemberPageFaultAt { get; set; }

        /// <summary>Task 137 r2: every <c>$skiptoken</c> this server put in an <c>@odata.nextLink</c>, in order.</summary>
        public List<string> IssuedSkipTokens { get; } = new();

        private readonly Dictionary<string, int> _skipTokenOffsets = new(StringComparer.Ordinal);

        /// <summary>
        /// Task 137 r2: one page of an organization's members, as Dataverse pages it — <c>Prefer:
        /// odata.maxpagesize</c> honoured (absent, Dataverse's own 5,000), and the next page addressed by an ABSOLUTE
        /// <c>@odata.nextLink</c> repeating the query plus an opaque <c>$skiptoken</c>. The token is unguessable, so a
        /// client that pages correctly is one that sends the server's link back.
        /// </summary>
        private async Task WriteMemberPageAsync(HttpContext context, string filter, SeenRequest seen)
        {
            var organizationId = Guid.Parse(Regex.Match(filter, @"^_sprk_organization_value eq ([0-9a-fA-F-]{36})").Groups[1].Value);
            var all = OrganizationMembers.TryGetValue(organizationId, out var ids) ? ids : Array.Empty<Guid>();

            int offset;
            lock (_gate)
            {
                if (seen.SkipToken.Length == 0)
                {
                    offset = 0;
                }
                else if (!_skipTokenOffsets.TryGetValue(seen.SkipToken, out offset))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest; // a token this server never issued
                    return;
                }
            }

            var pageSizeMatch = Regex.Match(seen.Prefer, @"odata\.maxpagesize=(\d+)");
            var pageSize = pageSizeMatch.Success ? int.Parse(pageSizeMatch.Groups[1].Value) : 5000;
            if (MemberPageFaultAt is { } faultAt && offset / pageSize == faultAt)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            var body = new Dictionary<string, object?>
            {
                ["value"] = all.Skip(offset).Take(pageSize)
                    .Select(id => new Dictionary<string, object?> { ["_sprk_contact_value"] = id })
                    .ToList(),
            };

            if (offset + pageSize < all.Length)
            {
                var token = $"cookie-{Guid.NewGuid():N}";
                lock (_gate)
                {
                    _skipTokenOffsets[token] = offset + pageSize;
                    IssuedSkipTokens.Add(token);
                }

                body["@odata.nextLink"] =
                    $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}" +
                    $"?$filter={Uri.EscapeDataString(filter)}&$select={Uri.EscapeDataString(seen.Select)}" +
                    $"&$skiptoken={Uri.EscapeDataString(token)}";
            }

            await context.Response.WriteAsJsonAsync(body);
        }

        private Dictionary<string, object?> Organization(Guid organizationId) => new()
        {
            ["sprk_organizationid"] = organizationId,
            ["statecode"] = OrganizationStates.TryGetValue(organizationId, out var state) ? state : 0,
        };

        private static Task WriteValueAsync(HttpContext context, IEnumerable<Dictionary<string, object?>> rows)
            => context.Response.WriteAsJsonAsync(new Dictionary<string, object> { ["value"] = rows.ToList() });
    }
}
