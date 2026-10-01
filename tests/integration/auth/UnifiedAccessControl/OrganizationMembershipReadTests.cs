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
    /// A caller who abandons the request mid-junction-read loses only the org-grant term — never the
    /// direct grants beside it.
    /// </summary>
    /// <remarks>
    /// Pins a regression this task would otherwise have introduced (its code-review finding W3). The
    /// junction query also runs INSIDE the grant-set read, whose catch-all turns any escaping exception into
    /// an EMPTY grant set — direct grants included — that is then cached. So the query reports the caller's
    /// cancellation as an unreadable read rather than rethrowing it; only the evaluator's entry rethrows.
    /// </remarks>
    [Fact]
    public async Task GetGrantSetAsync_CallerCancelsDuringTheJunctionRead_KeepsTheDirectGrants()
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        SeedWorld(dataverse);
        dataverse.JunctionHangs = true;
        var participations = RealParticipationService(dataverse);

        using var abandoned = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var grants = await participations.GetGrantSetAsync(ContactId, abandoned.Token);

        grants.Projects.Select(p => p.ProjectId).Should().Contain(DirectProject,
            "a cancelled membership read must cost the org-grant term only, not the contact's own grants");
        grants.Projects.Select(p => p.ProjectId).Should().NotContain(CurrentOrgProject,
            "and the org-grant term contributes nothing — the read was not completed");
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
            membership.Object, participations, standing, denyList, NullLogger<AccessibleRecordSetService>.Instance);
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

    private sealed record MembershipSpec(Guid OrganizationId, DateOnly? Start, DateOnly? End);

    private sealed record GrantSpec(Guid ProjectId, Guid? OrganizationId);

    /// <summary>One request as the server received it, decoded.</summary>
    private sealed record SeenRequest(string Collection, string Filter, string Select, string Expand)
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
                collection, filter, context.Request.Query["$select"].ToString(), context.Request.Query["$expand"].ToString());
            lock (_gate) { _requests.Add(seen); }

            switch (collection)
            {
                case "sprk_contactorganizations":
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
                    await WriteValueAsync(context, ids.Select(id => new Dictionary<string, object?>
                    {
                        ["sprk_projectid"] = id,
                        ["sprk_issecure"] = false,
                        ["sprk_accesspermission"] = 100000000,
                        ["_sprk_assignedlawfirm1_value"] = null,
                        ["_sprk_assignedlawfirm2_value"] = null,
                    }));
                    return;

                default:
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
            }
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
