using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text;
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
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Caching;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 132 (defect C12) — no access cache stores a result produced by a failed read; caller
/// cancellation propagates and a timeout is a fault; and a legitimate empty answer IS still cached.
/// </summary>
/// <remarks>
/// <para><b>The defect.</b> Four caches could not tell a failed read from a real answer, so a 429, a timeout or a
/// client abort was stored as "no grants / no teams / no rights" for 60 seconds to 15 minutes — and every retry hit the
/// cache. Each section below drives the PRODUCTION classifier into a fault and asserts what was stored, and drives it to
/// a real empty answer and asserts that WAS stored (the over-correction case).</para>
/// <para><b>Instruments (ADR-038).</b> The grant set and the access snapshot are exercised through their real HTTP
/// reads against an in-memory ASP.NET Core server (<c>UseTestServer</c>) standing in for the Dataverse Web API — ADR-038
/// §7's named replacement for ban B1, and the instrument task 109 used for the same service: the production status →
/// answer/fault mapping runs, so mapping a 429 to an answer reddens these tests (perturbation 19). The ONE override is
/// <c>DataverseAccessDataSource.GetDataverseTokenViaOBOAsync</c> (an internal virtual seam): an MSAL exchange cannot be
/// driven offline, and every Dataverse read after it stays production code. The identity and membership caches sit on
/// <c>IDataverseService</c>, substituted at that module boundary. No <c>Mock&lt;HttpMessageHandler&gt;</c>, no reflection,
/// no sleeps: the two timeout cases use a short client timeout against a server that never answers.</para>
/// <para>Eviction (criteria 13, 14, 23) is in <c>AccessCacheInvalidationTests</c>.</para>
/// </remarks>
public sealed class AccessCacheFaultCachingTests
{
    private const string Tenant = "11111111-2222-3333-4444-555555555555";
    private const string FakeServiceUrl = "https://fake-dataverse.crm.dynamics.com";

    private static readonly Guid ContactId = Guid.Parse("13200000-0000-0000-0000-0000000000c1");
    private static readonly Guid DirectProject = Guid.Parse("13200000-0000-0000-0000-0000000000a1");
    private static readonly Guid OrgProject = Guid.Parse("13200000-0000-0000-0000-0000000000a2");
    private static readonly Guid Organization = Guid.Parse("13200000-0000-0000-0000-0000000000b1");

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════
    // (a) GRANT SET — ExternalParticipationService (criteria 1-5)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Criterion 1 — a non-2xx (a 429 throttle included) is a FAULT: nothing granted, nothing stored, re-queried.</summary>
    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(403)]
    public async Task GrantSet_GrantQueryAnswersNon2xx_IsNotCached_ComposesNoGrants_AndTheNextCallReQueries(int status)
    {
        await using var world = await GrantWorld.StartAsync();
        world.Dataverse.ContactGrants.Add(DirectProject);
        world.Dataverse.ContactGrantStatus = (HttpStatusCode)status;

        var faulted = await world.Service.GetGrantSetAsync(ContactId);

        faulted.Faulted.Should().BeTrue($"a {status} is a failed read, not an answer");
        faulted.Projects.Should().BeEmpty("fail closed: the faulted request composes no grant-derived access");
        (await world.CachedEntryAsync()).Should().BeNull("a fault-derived set is never stored");

        world.Dataverse.ContactGrantStatus = null; // Dataverse recovers
        var next = await world.Service.GetGrantSetAsync(ContactId);

        world.ContactGrantReads.Should().Be(2, "the next call re-queries instead of serving the fault");
        next.Projects.Select(p => p.ProjectId).Should().Equal(DirectProject);
    }

    /// <summary>
    /// Criterion 2 — a thrown read (here a body that does not parse) and an HttpClient TIMEOUT are faults too. The timeout
    /// arrives as a TaskCanceledException with the caller's token NOT cancelled: it must not escape as a cancellation.
    /// </summary>
    [Theory]
    [InlineData("malformed-body")]
    [InlineData("timeout")]
    public async Task GrantSet_GrantQueryThrowsOrTimesOut_IsAFault_NotCached_NotPropagated(string fault)
    {
        await using var world = await GrantWorld.StartAsync(timeout: fault == "timeout" ? TimeSpan.FromMilliseconds(400) : null);
        world.Dataverse.ContactGrants.Add(DirectProject);
        world.Dataverse.ContactGrantMalformed = fault == "malformed-body";
        world.Dataverse.ContactGrantHangs = fault == "timeout";

        var act = () => world.Service.GetGrantSetAsync(ContactId, CancellationToken.None);
        var faulted = (await act.Should().NotThrowAsync("a fault is reported on the set, never thrown")).Subject;

        faulted.Faulted.Should().BeTrue();
        faulted.Projects.Should().BeEmpty();
        (await world.CachedEntryAsync()).Should().BeNull();

        world.Dataverse.ContactGrantMalformed = false;
        world.Dataverse.ContactGrantHangs = false;
        (await world.Service.GetGrantSetAsync(ContactId)).Projects.Select(p => p.ProjectId).Should().Equal(DirectProject);
        world.ContactGrantReads.Should().Be(2);
    }

    /// <summary>
    /// Criterion 3 — the CALLER's cancellation propagates out of GetGrantSetAsync, and nothing is cached. The same
    /// window inside the membership-junction sub-read is pinned by
    /// <c>OrganizationMembershipReadTests.GetGrantSetAsync_CallerCancelsDuringTheJunctionRead_PropagatesTheCancellation_AndCachesNothing</c>.
    /// </summary>
    [Fact]
    public async Task GrantSet_CallerCancelsDuringTheGrantQuery_PropagatesOperationCanceled_AndCachesNothing()
    {
        await using var world = await GrantWorld.StartAsync();
        world.Dataverse.ContactGrants.Add(DirectProject);
        world.Dataverse.ContactGrantHangs = true;
        using var cts = new CancellationTokenSource();

        var call = world.Service.GetGrantSetAsync(ContactId, cts.Token);
        await world.Dataverse.ContactGrantRequestSeen.Task; // the read is in flight — no sleep
        cts.Cancel();

        await call.Invoking(async c => await c).Should().ThrowAsync<OperationCanceledException>(
            "a client abort is the caller's cancellation, not 'no grants'");
        (await world.CachedEntryAsync()).Should().BeNull();
    }

    /// <summary>
    /// Criterion 4 — an organization-grant read fault, or task 109's junction Unreadable outcome, returns the
    /// successfully-read DIRECT grants for this request and caches nothing. The control (no fault) proves the
    /// organization term is live in this world, so the fault case is not vacuous.
    /// </summary>
    [Theory]
    [InlineData("org-grant-query")]
    [InlineData("junction")]
    public async Task GrantSet_OrganizationTermFaults_ReturnsTheDirectGrants_AndCachesNothing(string faultedRead)
    {
        await using var world = await GrantWorld.StartAsync();
        world.Dataverse.ContactGrants.Add(DirectProject);
        world.Dataverse.Memberships.Add(Organization);
        world.Dataverse.OrgGrants.Add((OrgProject, Organization));
        world.Dataverse.OrgGrantStatus = faultedRead == "org-grant-query" ? HttpStatusCode.InternalServerError : null;
        world.Dataverse.JunctionStatus = faultedRead == "junction" ? HttpStatusCode.InternalServerError : null;

        var partial = await world.Service.GetGrantSetAsync(ContactId);

        partial.Projects.Select(p => p.ProjectId).Should().Equal(new[] { DirectProject },
            "the direct grants were read successfully and still apply to this request");
        partial.Faulted.Should().BeTrue("a set missing its organization grants because a read failed is not an answer");
        (await world.CachedEntryAsync()).Should().BeNull("a partial set is not stored for 60 s");

        world.Dataverse.OrgGrantStatus = null;
        world.Dataverse.JunctionStatus = null;
        var complete = await world.Service.GetGrantSetAsync(ContactId);
        complete.Projects.Select(p => p.ProjectId).Should().BeEquivalentTo(new[] { DirectProject, OrgProject },
            "control: with the read healthy the organization term contributes");
        complete.Faulted.Should().BeFalse();
        (await world.CachedEntryAsync()).Should().NotBeNull("control: a complete set is cached");
    }

    /// <summary>Criterion 5 — the negative over-correction case: zero rows from a SUCCESSFUL read is an answer, and is cached.</summary>
    [Fact]
    public async Task GrantSet_SuccessfulReadOfZeroRows_IsAnAnswer_CachedAndServedAsAHit()
    {
        await using var world = await GrantWorld.StartAsync();

        var first = await world.Service.GetGrantSetAsync(ContactId);
        (await world.CachedEntryAsync()).Should().NotBeNull("a contact with zero grants is an answer");
        var second = await world.Service.GetGrantSetAsync(ContactId);

        first.Faulted.Should().BeFalse();
        first.Projects.Should().BeEmpty();
        second.Projects.Should().BeEmpty();
        world.ContactGrantReads.Should().Be(1, "the second call is a cache HIT");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════
    // (b) IDENTITY — IdentityNormalizationService (criteria 6, 7, 24)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Criterion 6 — a fault on EACH sub-path produces an identity that is returned (fail soft, as before) but NOT
    /// cached; the next resolve re-reads. One case per path, plus a timeout on the team read (a TaskCanceledException
    /// with the caller's token NOT cancelled is a fault, not a cancellation — the constraint's rule, on this cache), plus
    /// the PRODUCTION organization resolver's own query failing: before task 132 that resolver swallowed the fault
    /// itself, so the identity layer never saw it and cached "member of no organization" (verifier r1, seed S10 — the
    /// "organizations" case alone uses a resolver double and cannot see that adapter).
    /// </summary>
    [Theory]
    [InlineData("systemuser-row")]
    [InlineData("teams")]
    [InlineData("contact-binding")]
    [InlineData("account")]
    [InlineData("organizations")]
    [InlineData("organizations-real-resolver")]
    [InlineData("teams-timeout")]
    public async Task Identity_AFaultOnEachSubPath_IsReturnedButNotCached_AndTheNextResolveReReads(string path)
    {
        var world = new IdentityWorld
        {
            LinkedContact = path != "contact-binding",
            RealOrganizationResolver = path == "organizations-real-resolver",
        };
        world.Fail(path);

        var act = () => world.Service.ResolveAsync(IdentityWorld.UserId, CancellationToken.None);
        var identity = (await act.Should().NotThrowAsync("each path fails soft (FR-1A.5)")).Subject;

        identity.IsFaulted.Should().BeTrue($"the {path} read failed and the identity must say so");
        (await world.CachedAsync()).Should().BeNull("an identity resolved over a failed read is never cached");

        await world.Service.ResolveAsync(IdentityWorld.UserId, CancellationToken.None);
        world.SystemUserReads.Should().Be(2, "the next resolve re-reads instead of serving the fault");
    }

    /// <summary>Criterion 7 — legitimate absence (no teams, no linked or bound contact, no organizations) IS cached.</summary>
    [Fact]
    public async Task Identity_SuccessfulResolutionWithNothingFound_IsAnAnswer_AndIsCached()
    {
        var world = new IdentityWorld { LinkedContact = false, TeamIds = Array.Empty<Guid>(), OrganizationIds = Array.Empty<Guid>() };

        var identity = await world.Service.ResolveAsync(IdentityWorld.UserId, CancellationToken.None);
        (await world.CachedAsync()).Should().NotBeNull("a user with no teams, no contact and no organizations is an answer");
        await world.Service.ResolveAsync(IdentityWorld.UserId, CancellationToken.None);

        identity.IsFaulted.Should().BeFalse();
        identity.ContactId.Should().BeNull();
        identity.ContactUnreadable.Should().BeFalse("read and found absent is not unreadable (criterion 16's distinction)");
        identity.TeamIds.Should().BeEmpty();
        world.SystemUserReads.Should().Be(1, "the second resolve is a cache HIT");
        world.BindingReads.Should().Be(1, "precondition: the binding lookup ran and SUCCEEDED with no match");
    }

    /// <summary>
    /// Criterion 24 (identity) — an entry written under the pre-fix version is not served. The control proves the seed
    /// is readable, so the negative cannot pass merely because the seed failed to deserialize.
    /// </summary>
    [Fact]
    public async Task Identity_AnEntryCachedUnderThePreFixVersion_IsNotServed()
    {
        IdentityNormalizationService.CacheVersion.Should().Be(2, "task 132 bumped it 1 → 2 so no pre-fix (possibly fault-derived) entry is served");
        var poisoned = new PersonIdentity(IdentityWorld.UserId, PrimaryEmail: "pre-fix@example.test");

        var control = new IdentityWorld();
        await control.Cache.SetAsync(Tenant, IdentityNormalizationService.CacheResource,
            IdentityNormalizationService.CacheId(IdentityWorld.UserId), IdentityNormalizationService.CacheVersion, poisoned);
        (await control.Service.ResolveAsync(IdentityWorld.UserId, CancellationToken.None)).PrimaryEmail
            .Should().Be("pre-fix@example.test", "control: an entry under the current version is served");
        control.SystemUserReads.Should().Be(0);

        var world = new IdentityWorld();
        await world.Cache.SetAsync(Tenant, IdentityNormalizationService.CacheResource,
            IdentityNormalizationService.CacheId(IdentityWorld.UserId), 1, poisoned);
        var identity = await world.Service.ResolveAsync(IdentityWorld.UserId, CancellationToken.None);

        world.SystemUserReads.Should().Be(1, "a v1 entry is a MISS");
        identity.PrimaryEmail.Should().Be(IdentityWorld.Email);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════
    // (c) MEMBERSHIP — MembershipResolverService (criteria 8, 24)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Criterion 8 (systemuser path) — a response built on a FAULTED identity (the team read failed: every team-owned
    /// record hidden) is not cached; one built on a CLEAN identity with zero matches is. Observed as whether the second
    /// resolve runs the membership query again.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Membership_SystemUserPath_CachesOnlyAResponseBuiltOnACleanIdentity(bool identityFaulted)
    {
        var world = new MembershipWorld();
        if (identityFaulted)
        {
            world.Identity.Fail("teams");
        }

        await world.ResolveProjectsAsync();
        await world.ResolveProjectsAsync();

        world.MembershipQueries.Should().Be(identityFaulted ? 2 : 1, identityFaulted
            ? "a response built over a failed team read is never cached — it hid every team-owned record for 10 + 5 minutes"
            : "a clean identity with zero matches is an answer and is cached");
    }

    /// <summary>
    /// Criterion 8 (people-targeting surface) — the human/application-user check that decides whether Created By binds
    /// is a read too. When it FAILS, the response is built without Created By (fail soft, as before) and is NOT cached;
    /// when it reads a human, the response is cached (verifier r1, seed S15).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Membership_PeopleTargeting_CachesOnlyAResponseBuiltOnAReadApplicationUserCheck(bool checkFaulted)
    {
        var world = new MembershipWorld();
        if (checkFaulted)
        {
            world.Identity.Fail("application-user-check");
        }

        await world.Resolver.ResolveAsync(IdentityWorld.UserId, MembershipWorld.Project, MembershipResolveOptions.People, CancellationToken.None);
        await world.Resolver.ResolveAsync(IdentityWorld.UserId, MembershipWorld.Project, MembershipResolveOptions.People, CancellationToken.None);

        world.MembershipQueries.Should().Be(checkFaulted ? 2 : 1, checkFaulted
            ? "a response built while the human/application-user check could not be read is never cached"
            : "control: with the check read (a human), the people-targeting response is an answer and is cached");
    }

    /// <summary>
    /// Criterion 8 (contact path) — the key already encodes the inputs (contactId + OrganizationIds via the options
    /// hash), so a resolve with an EMPTY organization list cannot satisfy a later resolve with the real ids. The control
    /// (the same options twice) proves this path does cache, so the negative is not vacuous.
    /// </summary>
    [Fact]
    public async Task Membership_ContactPath_AnEmptyOrganizationListDoesNotSatisfyALaterResolveWithTheRealIds()
    {
        var world = new MembershipWorld();

        await world.Resolver.ResolveByContactAsync(ContactId, MembershipWorld.Matter, new MembershipResolveOptions(), CancellationToken.None);
        await world.Resolver.ResolveByContactAsync(ContactId, MembershipWorld.Matter, new MembershipResolveOptions(), CancellationToken.None);
        world.MembershipQueries.Should().Be(1, "control: identical inputs are a cache HIT");

        await world.Resolver.ResolveByContactAsync(ContactId, MembershipWorld.Matter,
            new MembershipResolveOptions(OrganizationIds: new[] { Organization }), CancellationToken.None);
        world.MembershipQueries.Should().Be(2, "the real organization ids are a different question — a MISS, never the empty-list answer");
    }

    /// <summary>Criterion 24 (membership) — an entry under the pre-fix version is not served; the control proves the seed is readable.</summary>
    [Fact]
    public async Task Membership_AnEntryCachedUnderThePreFixVersion_IsNotServed()
    {
        // At least 5: task 132 bumped it 4 → 5 so no pre-fix (possibly fault-derived) entry is served. Later bumps keep
        // that guarantee (task 172 / #1011 bumped 5 → 6 because the Owner column's query semantics changed), so this pins
        // the floor rather than one value; the v4 seed below is still unreachable.
        MembershipResolverService.CacheVersion.Should().BeGreaterThanOrEqualTo(5, "task 132 bumped it 4 → 5 so no pre-fix (possibly fault-derived) entry is served");

        // Learn the production cache id from a real write (the id embeds a private options hash).
        var probe = new MembershipWorld();
        await probe.ResolveProjectsAsync();
        var written = probe.Writes.Should().ContainSingle(w => w.Resource == MembershipResolverService.CacheResource).Subject;
        var poisoned = new MembershipResponse(
            MembershipWorld.Project, new PersonIdentity(IdentityWorld.UserId), new[] { OrgProject },
            new Dictionary<string, IReadOnlyList<Guid>>(), 1, DateTimeOffset.UtcNow.AddMinutes(5));

        var control = new MembershipWorld();
        await control.InnerCache.SetAsync(Tenant, written.Resource, written.Id, MembershipResolverService.CacheVersion, poisoned);
        (await control.ResolveProjectsAsync()).Ids.Should().Equal(new[] { OrgProject }, "control: the current version is served");
        control.MembershipQueries.Should().Be(0);

        var world = new MembershipWorld();
        await world.InnerCache.SetAsync(Tenant, written.Resource, written.Id, 4, poisoned);
        var response = await world.ResolveProjectsAsync();

        world.MembershipQueries.Should().Be(1, "a v4 entry is a MISS");
        response.Ids.Should().BeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════
    // (d) ACCESS SNAPSHOTS — DataverseAccessDataSource classified, CachedAccessDataSource gated (criterion 9)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Criterion 9, record path — every fault and the DEGRADED probe-derived answer are marked faulted by the
    /// production classifier, keep EXACTLY the rights they carry today, and are not cached; the next call re-asks.
    /// </summary>
    [Theory]
    [InlineData("obo-failure", AccessRights.None)]
    [InlineData("user-lookup-500", AccessRights.None)]
    [InlineData("user-lookup-429", AccessRights.None)]
    [InlineData("user-lookup-malformed", AccessRights.None)]
    [InlineData("rpa-unanswered-probe-readable", AccessRights.Read)]
    [InlineData("rpa-unanswered-probe-500", AccessRights.None)]
    [InlineData("rpa-unanswered-probe-timeout", AccessRights.None)]
    public async Task Snapshot_RecordPath_AFaultOrADegradedAnswer_IsNotCached_AndKeepsTodaysRights(
        string fault, AccessRights expectedRights)
    {
        await using var world = await SnapshotWorld.StartAsync(
            timeout: fault.EndsWith("timeout", StringComparison.Ordinal) ? TimeSpan.FromMilliseconds(400) : null);
        world.Arrange(fault);

        var snapshot = await world.Cached.GetRecordAccessAsync(SnapshotWorld.Oid, SnapshotWorld.MatterSet, SnapshotWorld.RecordId, "user-token");

        snapshot.AccessRights.Should().Be(expectedRights, "the faulted request receives exactly the rights it received before task 132");
        snapshot.Faulted.Should().BeTrue($"'{fault}' is not a complete Dataverse answer");
        (await world.CachedRecordEntryAsync()).Should().BeNull("a faulted or degraded snapshot is never stored");

        var before = world.Dataverse.TotalRequests;
        await world.Cached.GetRecordAccessAsync(SnapshotWorld.Oid, SnapshotWorld.MatterSet, SnapshotWorld.RecordId, "user-token");
        if (fault != "obo-failure")
        {
            world.Dataverse.TotalRequests.Should().BeGreaterThan(before, "the next call asks Dataverse again");
        }
        else
        {
            world.Source.OboCalls.Should().Be(2, "the next call attempts the exchange again");
        }
    }

    /// <summary>
    /// Criterion 9, record path — a LEGITIMATE None is an answer and IS cached for the TTL: RetrievePrincipalAccess
    /// answered with no rights, a probe refused with 403 / 404, a user lookup that succeeded and found no systemuser.
    /// </summary>
    [Theory]
    [InlineData("rpa-answered-no-rights")]
    [InlineData("rpa-unanswered-probe-403")]
    [InlineData("rpa-unanswered-probe-404")]
    [InlineData("no-such-systemuser")]
    public async Task Snapshot_RecordPath_ALegitimateNone_IsCached_AndServedAsAHit(string answer)
    {
        await using var world = await SnapshotWorld.StartAsync();
        world.Arrange(answer);

        var snapshot = await world.Cached.GetRecordAccessAsync(SnapshotWorld.Oid, SnapshotWorld.MatterSet, SnapshotWorld.RecordId, "user-token");
        (await world.CachedRecordEntryAsync()).Should().NotBeNull($"'{answer}' is Dataverse's answer and is cached");
        var before = world.Dataverse.TotalRequests;
        var hit = await world.Cached.GetRecordAccessAsync(SnapshotWorld.Oid, SnapshotWorld.MatterSet, SnapshotWorld.RecordId, "user-token");

        snapshot.AccessRights.Should().Be(AccessRights.None);
        snapshot.Faulted.Should().BeFalse();
        hit.AccessRights.Should().Be(AccessRights.None);
        world.Dataverse.TotalRequests.Should().Be(before, "the second call is a cache HIT");
    }

    /// <summary>
    /// Criterion 9, document path (<c>GetUserAccessAsync</c>) — the same classification, including the two sub-reads
    /// this path adds (teams, roles) and the document probe. Timeout here is the user lookup hanging.
    /// </summary>
    [Theory]
    [InlineData("user-lookup-500", AccessRights.None)]
    [InlineData("user-lookup-timeout", AccessRights.None)]
    [InlineData("rpa-unanswered-probe-readable", AccessRights.Read)]
    [InlineData("rpa-unanswered-probe-500", AccessRights.None)]
    [InlineData("teams-500", AccessRights.Read | AccessRights.Write)]
    [InlineData("roles-500", AccessRights.Read | AccessRights.Write)]
    [InlineData("obo-failure", AccessRights.None)]
    public async Task Snapshot_DocumentPath_AFaultOrADegradedAnswer_IsNotCached_AndKeepsTodaysRights(
        string fault, AccessRights expectedRights)
    {
        await using var world = await SnapshotWorld.StartAsync(
            timeout: fault.EndsWith("timeout", StringComparison.Ordinal) ? TimeSpan.FromMilliseconds(400) : null);
        world.Arrange(fault);

        var snapshot = await world.Cached.GetUserAccessAsync(SnapshotWorld.Oid, SnapshotWorld.DocumentId.ToString(), "user-token");

        snapshot.AccessRights.Should().Be(expectedRights);
        snapshot.Faulted.Should().BeTrue($"'{fault}' is not a complete Dataverse answer");
        (await world.CachedDocumentEntryAsync()).Should().BeNull();
    }

    /// <summary>Criterion 9, document path — an answer (RPA answered, with rights or with none) IS cached.</summary>
    [Theory]
    [InlineData("rpa-answered-no-rights", AccessRights.None)]
    [InlineData("healthy", AccessRights.Read | AccessRights.Write)]
    public async Task Snapshot_DocumentPath_AnAnswer_IsCached_AndServedAsAHit(string answer, AccessRights expectedRights)
    {
        await using var world = await SnapshotWorld.StartAsync();
        world.Arrange(answer);

        var snapshot = await world.Cached.GetUserAccessAsync(SnapshotWorld.Oid, SnapshotWorld.DocumentId.ToString(), "user-token");
        (await world.CachedDocumentEntryAsync()).Should().NotBeNull();
        var before = world.Dataverse.TotalRequests;
        var hit = await world.Cached.GetUserAccessAsync(SnapshotWorld.Oid, SnapshotWorld.DocumentId.ToString(), "user-token");

        snapshot.Faulted.Should().BeFalse();
        snapshot.AccessRights.Should().Be(expectedRights);
        hit.AccessRights.Should().Be(expectedRights);
        world.Dataverse.TotalRequests.Should().Be(before, "the second call is a cache HIT");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════
    // DENY-VETO SUBJECT (criterion 16) — an UNKNOWN subject denies; an ABSENT one checks nothing
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Criterion 16. A systemuser holds record R through membership; their linked contact C is on R's No Access List.
    /// (a) contact read: R is vetoed — the control that the wall binds this person. (b) contact UNREADABLE (the reads
    /// that decide it failed): before task 132 the veto had no subject and checked nothing, so R composed — the traced
    /// fail-open. Now every candidate is denied. (c) genuinely unlinked (read, absent): composes exactly as before, R
    /// kept, the deny reader never consulted — 7 of 8 dev systemusers are in this state and lose nothing.
    /// (d) On an entity type grants do not target (here <c>sprk_event</c>) the contact is never the veto subject — not
    /// even when it is KNOWN — so an unreadable one composes exactly as a known one does there (verifier r1, item 11:
    /// the unreadable branch used to deny every candidate on every type).
    /// </summary>
    [Theory]
    // Batch 4 integration (task 143, owner N3): on a NON-secure record a contact-subject entry removes only the
    // contact-sourced contribution; the systemuser keeps its own membership term, so R (held through membership)
    // composes. Task 132's unknown-contact branch still denies every candidate (fail closed).
    [InlineData("contact-read", AccessibleRecordSetService.ProjectEntity, true)]
    [InlineData("contact-unreadable", AccessibleRecordSetService.ProjectEntity, false)]
    [InlineData("genuinely-unlinked", AccessibleRecordSetService.ProjectEntity, true)]
    [InlineData("contact-read", "sprk_event", true)]
    [InlineData("contact-unreadable", "sprk_event", true)]
    public async Task DenyVeto_SystemUserPlane_AnUnreadableContactDenies_AnAbsentOneComposesAsBefore(
        string state, string entityType, bool expectComposed)
    {
        var record = Guid.Parse("13200000-0000-0000-0000-0000000000e1");
        var denyList = new ContactDenyList(ContactId, record);
        var evaluator = DenyVetoWorld.Evaluator(record, denyList);

        var principal = new WorkforcePrincipal
        {
            Kind = WorkforcePrincipalKind.SystemUser,
            SystemUserId = IdentityWorld.UserId,
            ContactId = state == "contact-read" ? ContactId : null,
            ContactUnreadable = state == "contact-unreadable",
            Oid = IdentityWorld.Oid.ToString("D"),
            TenantId = Tenant,
        };

        var set = await evaluator.ComposeAsync(principal, entityType, CancellationToken.None);

        set.Contains(record).Should().Be(expectComposed);
        if (state == "genuinely-unlinked" || entityType != AccessibleRecordSetService.ProjectEntity)
        {
            denyList.Calls.Should().Be(0, "no contact subject — nothing to check, exactly as before task 132");
        }
    }

    /// <summary>
    /// Criterion 16, the link in the chain: the resolver marks the principal's contact UNREADABLE when the identity's
    /// deciding reads failed (or the identity threw), and NOT when the contact was read as absent.
    /// </summary>
    [Theory]
    [InlineData("systemuser-row-faulted", true)]
    [InlineData("identity-threw", true)]
    [InlineData("read-and-absent", false)]
    public async Task WorkforceResolver_MarksTheContactUnreadable_OnlyWhenItsReadsFailed(string identityState, bool expectUnreadable)
    {
        var identity = new Mock<IIdentityNormalizationService>();
        var setup = identity.Setup(i => i.ResolveAsync(IdentityWorld.UserId, It.IsAny<CancellationToken>()));
        switch (identityState)
        {
            case "systemuser-row-faulted":
                setup.ReturnsAsync(new PersonIdentity(IdentityWorld.UserId) { Faults = IdentityReadFaults.SystemUser });
                break;
            case "identity-threw":
                setup.ThrowsAsync(new InvalidOperationException("simulated identity resolution failure"));
                break;
            default:
                setup.ReturnsAsync(new PersonIdentity(IdentityWorld.UserId));
                break;
        }

        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "systemuser"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var rows = new EntityCollection();
                rows.Entities.Add(new Entity("systemuser") { Id = IdentityWorld.UserId });
                return rows;
            });
        var resolver = new WorkforcePrincipalResolver(
            identity.Object,
            dataverse.Object,
            MemoryTenantCache(),
            IdentityBindingTestKit.Binder(new InMemoryContactIdentityStore()),
            new ConfigurationBuilder().Build(), // inline link writes off — the resolver reads nothing more
            NullLogger<WorkforcePrincipalResolver>.Instance);

        var resolution = await resolver.ResolveAsync(
            IdentityBindingTestKit.WorkforceUser(IdentityWorld.Oid, IdentityBindingTestKit.CustomerTenant), CancellationToken.None);

        resolution.Principal!.Kind.Should().Be(WorkforcePrincipalKind.SystemUser);
        resolution.Principal.ContactId.Should().BeNull();
        resolution.Principal.ContactUnreadable.Should().Be(expectUnreadable);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════
    // TTLs (criterion 12)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Criterion 12 — the identity, membership and impersonated-root-set TTLs are the owner-approved 2 minutes, and the
    /// stacked worst case for an outside-BFF change (identity + membership) is within the owner's bound (rounds 3 R3/R4:
    /// "minutes, never hourly", ≤ 5 min) and within <c>caching-architecture.md</c>'s "authorization cache TTLs ≤ 2 min".
    /// </summary>
    [Fact]
    public void Ttls_AreTheOwnerApprovedBound_AndTheStackedWorstCaseIsWithinFiveMinutes()
    {
        var bound = TimeSpan.FromMinutes(2);

        IdentityNormalizationService.CacheTtl.Should().Be(bound);
        MembershipResolverService.CacheTtl.Should().Be(bound);
        ImpersonatedRootSetSource.CacheTtl.Should().Be(bound);
        CachedAccessDataSource.ResourceAccessTtl.Should().Be(TimeSpan.FromSeconds(60));
        (IdentityNormalizationService.CacheTtl + MembershipResolverService.CacheTtl).Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(5),
            "an entry built at the end of the identity's life lives another membership TTL (stacking)");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Harness
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    private static ITenantCache MemoryTenantCache()
        => new TenantCache(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<TenantCache>.Instance);

    /// <summary>
    /// A request carrying the caller's <c>tid</c> (the caches are skipped without one). Field-backed on purpose: the
    /// framework <c>HttpContextAccessor</c> keeps the context in an AsyncLocal, which does not flow back out of the
    /// async world builders below.
    /// </summary>
    private static IHttpContextAccessor RequestWithTenant() => new FixedHttpContextAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("tid", Tenant) }, "test")),
        },
    };

    private sealed class FixedHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private static IConfiguration ServiceUrlConfig(params (string Key, string Value)[] extra)
    {
        var settings = new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = FakeServiceUrl };
        foreach (var (key, value) in extra)
        {
            settings[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static AccessToken Token => new("test-token-not-a-credential", DateTimeOffset.UtcNow.AddHours(1));

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(Token);
    }

    // ── grant set world ─────────────────────────────────────────────────────────────────────────────────────

    private sealed class GrantWorld : IAsyncDisposable
    {
        private GrantWorld(GrantDataverse dataverse, ITenantCache cache, ExternalParticipationService service)
        {
            Dataverse = dataverse;
            Cache = cache;
            Service = service;
        }

        public GrantDataverse Dataverse { get; }

        public ITenantCache Cache { get; }

        public ExternalParticipationService Service { get; }

        public int ContactGrantReads => Dataverse.Requests.Count(r => r == "contact-grants");

        public static async Task<GrantWorld> StartAsync(TimeSpan? timeout = null)
        {
            var dataverse = await GrantDataverse.StartAsync();
            var cache = MemoryTenantCache();
            var service = new ExternalParticipationService(
                dataverse.CreateClient(timeout ?? TimeSpan.FromSeconds(30)),
                cache,
                ServiceUrlConfig(),
                new StaticTokenCredential(),
                RequestWithTenant(),
                NullLogger<ExternalParticipationService>.Instance,
                filing: Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.NoFilingEntities());
            return new GrantWorld(dataverse, cache, service);
        }

        public Task<string?> CachedEntryAsync()
            => Cache.GetStringAsync(Tenant, ExternalParticipationService.ExternalAccessResource, ContactId.ToString(),
                ExternalParticipationService.CacheVersion);

        public ValueTask DisposeAsync() => Dataverse.DisposeAsync();
    }

    /// <summary>
    /// The grant read's three collections over an in-memory server, in Dataverse's JSON shape (lookups as
    /// <c>_x_value</c>, the expanded <c>sprk_Organization</c> carrying its <c>statecode</c>). Faults per read.
    /// </summary>
    private sealed class GrantDataverse : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private GrantDataverse(WebApplication app) => _app = app;

        public List<Guid> ContactGrants { get; } = new();
        public List<Guid> Memberships { get; } = new();
        public List<(Guid Project, Guid Organization)> OrgGrants { get; } = new();

        public HttpStatusCode? ContactGrantStatus { get; set; }
        public bool ContactGrantMalformed { get; set; }
        public bool ContactGrantHangs { get; set; }
        public HttpStatusCode? OrgGrantStatus { get; set; }
        public HttpStatusCode? JunctionStatus { get; set; }

        public TaskCompletionSource ContactGrantRequestSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<string> Requests { get; } = new();

        public static async Task<GrantDataverse> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseTestServer();
            var app = builder.Build();
            var fake = new GrantDataverse(app);
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
            if (collection == "sprk_contactorganizations")
            {
                Requests.Enqueue("junction");
                if (JunctionStatus is { } js)
                {
                    context.Response.StatusCode = (int)js;
                    return;
                }

                await WriteAsync(context, "{\"value\":[" + string.Join(",", Memberships.Select(o =>
                    $"{{\"_sprk_organization_value\":\"{o}\",\"sprk_startdate\":null,\"sprk_enddate\":null,\"statecode\":0,\"sprk_Organization\":{{\"statecode\":0}}}}")) + "]}");
                return;
            }

            if (filter.Contains("_sprk_contact_value eq null", StringComparison.Ordinal))
            {
                Requests.Enqueue("org-grants");
                if (OrgGrantStatus is { } os)
                {
                    context.Response.StatusCode = (int)os;
                    return;
                }

                await WriteAsync(context, "{\"value\":[" + string.Join(",", OrgGrants.Select(g =>
                    $"{{\"_sprk_project_value\":\"{g.Project}\",\"sprk_accesslevel\":100000001,\"_sprk_organization_value\":\"{g.Organization}\",\"sprk_Organization\":{{\"statecode\":0}}}}")) + "]}");
                return;
            }

            Requests.Enqueue("contact-grants");
            ContactGrantRequestSeen.TrySetResult();
            if (ContactGrantHangs)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(30), context.RequestAborted); }
                catch (OperationCanceledException) { }
                return;
            }

            if (ContactGrantStatus is { } cs)
            {
                context.Response.StatusCode = (int)cs;
                await context.Response.WriteAsync("{\"error\":{\"code\":\"0x80072321\",\"message\":\"simulated\"}}");
                return;
            }

            if (ContactGrantMalformed)
            {
                await WriteAsync(context, "{\"value\":[ this is not json");
                return;
            }

            await WriteAsync(context, "{\"value\":[" + string.Join(",", ContactGrants.Select(p =>
                $"{{\"_sprk_project_value\":\"{p}\",\"sprk_accesslevel\":100000001,\"_sprk_organization_value\":null,\"sprk_Organization\":null}}")) + "]}");
        }

        private static Task WriteAsync(HttpContext context, string json)
        {
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync(json);
        }
    }

    // ── identity world ──────────────────────────────────────────────────────────────────────────────────────

    private sealed class IdentityWorld
    {
        public static readonly Guid UserId = Guid.Parse("13200000-0000-0000-0000-0000000000d1");
        public static readonly Guid Oid = Guid.Parse("13200000-0000-0000-0000-0000000000d2");
        public const string Email = "user@customer.example";
        private static readonly Guid BusinessUnit = Guid.Parse("13200000-0000-0000-0000-0000000000d3");
        private static readonly Guid Team = Guid.Parse("13200000-0000-0000-0000-0000000000d4");
        private static readonly Guid Account = Guid.Parse("13200000-0000-0000-0000-0000000000d5");

        private readonly Dictionary<string, Exception> _faults = new();

        public IdentityWorld(ITenantCache? cache = null)
        {
            Cache = cache ?? MemoryTenantCache();

            Dataverse
                .Setup(d => d.RetrieveAsync("systemuser", UserId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns(() => Answer("systemuser-row", () =>
                {
                    var row = new Entity("systemuser", UserId)
                    {
                        ["internalemailaddress"] = Email,
                        ["businessunitid"] = new EntityReference("businessunit", BusinessUnit),
                        ["azureactivedirectoryobjectid"] = Oid,
                    };
                    if (LinkedContact)
                    {
                        row["sprk_primarycontact"] = new EntityReference("contact", ContactId);
                    }

                    return row;
                }));

            Dataverse
                .Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "teammembership"), It.IsAny<CancellationToken>()))
                .Returns(() => Answer(_faults.ContainsKey("teams-timeout") ? "teams-timeout" : "teams", () =>
                {
                    var rows = new EntityCollection();
                    foreach (var team in TeamIds)
                    {
                        rows.Entities.Add(new Entity("teammembership") { ["teamid"] = team });
                    }

                    return rows;
                }));

            Dataverse
                .Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "contact"), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    BindingReads++;
                    return Answer("contact-binding", () => new EntityCollection());
                });

            Dataverse
                .Setup(d => d.RetrieveAsync("contact", ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns(() => Answer("account", () => new Entity("contact", ContactId)
                {
                    ["parentcustomerid"] = new EntityReference("account", Account),
                }));

            Dataverse
                .Setup(d => d.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    MembershipQueries++;
                    return Task.FromResult(new EntityCollection());
                });

            // The PRODUCTION organization resolver's own query (used when RealOrganizationResolver is set). Registered
            // after the general FetchExpression setup, so it answers the organization fetch only.
            Dataverse
                .Setup(d => d.RetrieveMultipleAsync(
                    It.Is<FetchExpression>(f => f.Query.Contains("<entity name='sprk_organization'>", StringComparison.Ordinal)),
                    It.IsAny<CancellationToken>()))
                .Returns(() => Answer("organizations-real-resolver", () =>
                {
                    var rows = new EntityCollection();
                    foreach (var organization in OrganizationIds)
                    {
                        rows.Entities.Add(new Entity("sprk_organization", organization));
                    }

                    return rows;
                }));

            // The people-targeting surface's human/application-user check (ApplicationUserCheck) — a systemuser read
            // of the applicationid column alone. Registered after the identity's systemuser-row setup, so it answers
            // that read only; healthy, it returns no applicationid (a human).
            Dataverse
                .Setup(d => d.RetrieveAsync("systemuser", UserId,
                    It.Is<string[]>(c => c.Length == 1 && c[0] == ApplicationUserCheck.ApplicationIdAttribute),
                    It.IsAny<CancellationToken>()))
                .Returns(() => Answer("application-user-check", () => new Entity("systemuser", UserId)));

            Organizations
                .Setup(o => o.ResolveOrganizationsAsync(UserId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .Returns(() => _faults.TryGetValue("organizations", out var ex)
                    ? Task.FromException<IReadOnlyList<Guid>>(ex)
                    : Task.FromResult<IReadOnlyList<Guid>>(OrganizationIds));
        }

        public Mock<IDataverseService> Dataverse { get; } = new();

        public Mock<IIdentityOrganizationResolver> Organizations { get; } = new();

        public ITenantCache Cache { get; }

        public bool LinkedContact { get; set; } = true;

        /// <summary>
        /// When set, the identity's organization resolver is the PRODUCTION <see cref="OrganizationMembershipResolver"/>
        /// (lookup field configured) over the substituted Dataverse, instead of the seam double — so a fault in its
        /// own query must REACH the identity layer through the production adapter (verifier r1, seed S10).
        /// </summary>
        public bool RealOrganizationResolver { get; set; }

        public IReadOnlyList<Guid> TeamIds { get; set; } = new[] { Team };

        public IReadOnlyList<Guid> OrganizationIds { get; set; } = new[] { Organization };

        public int BindingReads { get; private set; }

        public int MembershipQueries { get; private set; }

        public int SystemUserReads => Dataverse.Invocations.Count(i =>
            i.Method.Name == nameof(IDataverseService.RetrieveAsync) && (string)i.Arguments[0] == "systemuser");

        public IdentityNormalizationService Service => new(
            Dataverse.Object,
            Cache,
            new[] { RealOrganizationResolver ? ProductionOrganizationResolver() : Organizations.Object },
            Options.Create(new MembershipOptions()),
            NullLogger<IdentityNormalizationService>.Instance,
            RequestWithTenant());

        private IIdentityOrganizationResolver ProductionOrganizationResolver()
        {
            var options = new MembershipOptions();
            options.OrganizationLookup.UserLookupField = "sprk_owneruser";
            return new OrganizationMembershipResolver(
                Dataverse.Object,
                Mock.Of<IOptionsMonitor<MembershipOptions>>(m => m.CurrentValue == options),
                NullLogger<OrganizationMembershipResolver>.Instance);
        }

        public void Fail(string path)
            => _faults[path] = path == "teams-timeout"
                // An HttpClient timeout: a TaskCanceledException whose token is NOT the caller's.
                ? new TaskCanceledException("simulated Dataverse timeout")
                : new InvalidOperationException($"simulated {path} read failure");

        public Task<PersonIdentity?> CachedAsync()
            => Cache.GetAsync<PersonIdentity>(Tenant, IdentityNormalizationService.CacheResource,
                IdentityNormalizationService.CacheId(UserId), IdentityNormalizationService.CacheVersion);

        private Task<T> Answer<T>(string path, Func<T> value)
            => _faults.TryGetValue(path, out var ex) ? Task.FromException<T>(ex) : Task.FromResult(value());
    }

    // ── membership world ────────────────────────────────────────────────────────────────────────────────────

    private sealed class MembershipWorld
    {
        public const string Project = "sprk_project";
        public const string Matter = "sprk_matter";

        public MembershipWorld()
        {
            InnerCache = MemoryTenantCache();
            Recording = new RecordingTenantCache(InnerCache);
            Identity = new IdentityWorld(Recording);

            var discovery = new Mock<IMembershipFieldDiscoveryService>();
            discovery
                .Setup(d => d.DiscoverAsync(Project, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DiscoveryResult(Project, DateTimeOffset.UtcNow, new[]
                {
                    new MembershipDescriptor("ownerid", "owner", "SystemUser", "systemuser", "test"),
                    new MembershipDescriptor("owningteam", "owningTeam", "Team", "team", "test"),
                }, Array.Empty<IgnoredField>(), Array.Empty<IgnoredField>()));
            discovery
                .Setup(d => d.DiscoverAsync(Matter, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DiscoveryResult(Matter, DateTimeOffset.UtcNow, new[]
                {
                    new MembershipDescriptor("sprk_assignedattorney1", "assignedAttorney1", "Contact", "contact", "test"),
                    new MembershipDescriptor("sprk_assignedlawfirm1", "assignedLawFirm1", "Organization", "sprk_organization", "test"),
                }, Array.Empty<IgnoredField>(), Array.Empty<IgnoredField>()));

            var options = new MembershipOptions();
            options.AccessConferringRoles.Entities[Matter] = new List<AccessConferringColumn>
            {
                new() { Field = "sprk_assignedattorney1", IdentityType = "Contact" },
                new() { Field = "sprk_assignedlawfirm1", IdentityType = "Organization" },
            };

            Resolver = new MembershipResolverService(
                discovery.Object,
                Identity.Service,
                Identity.Dataverse.Object,
                Recording,
                Options.Create(options),
                NullLogger<MembershipResolverService>.Instance,
                RequestWithTenant());
        }

        public ITenantCache InnerCache { get; }

        public RecordingTenantCache Recording { get; }

        public IdentityWorld Identity { get; }

        public MembershipResolverService Resolver { get; }

        public int MembershipQueries => Identity.MembershipQueries;

        public IReadOnlyCollection<(string Resource, string Id)> Writes => Recording.Writes.ToList();

        /// <summary>The authorization path's options (the systemuser plane composes with AccessConferringOnly).</summary>
        public Task<MembershipResponse> ResolveProjectsAsync()
            => Resolver.ResolveAsync(IdentityWorld.UserId, Project,
                new MembershipResolveOptions(AccessConferringOnly: true), CancellationToken.None);
    }

    /// <summary>The production cache with every write's (resource, id) recorded — to learn a private key composition.</summary>
    private sealed class RecordingTenantCache(ITenantCache inner) : ITenantCache
    {
        public ConcurrentQueue<(string Resource, string Id)> Writes { get; } = new();

        public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
            => inner.GetAsync<T>(tenantId, resource, id, version, cacheInstance, ct);

        public Task SetAsync<T>(string tenantId, string resource, string id, int version, T value, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
        {
            Writes.Enqueue((resource, id));
            return inner.SetAsync(tenantId, resource, id, version, value, ttl, cacheInstance, ct);
        }

        public Task RemoveAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
            => inner.RemoveAsync(tenantId, resource, id, version, cacheInstance, ct);

        public Task<T> GetOrCreateAsync<T>(string tenantId, string resource, string id, int version, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
            => inner.GetOrCreateAsync(tenantId, resource, id, version, factory, ttl, cacheInstance, ct);

        public Task<string?> GetStringAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
            => inner.GetStringAsync(tenantId, resource, id, version, cacheInstance, ct);

        public Task SetStringAsync(string tenantId, string resource, string id, int version, string value, TimeSpan? ttl = null, TimeSpan? slidingExpiration = null, string cacheInstance = "default", CancellationToken ct = default)
        {
            Writes.Enqueue((resource, id));
            return inner.SetStringAsync(tenantId, resource, id, version, value, ttl, slidingExpiration, cacheInstance, ct);
        }

        public Task RefreshAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
            => inner.RefreshAsync(tenantId, resource, id, version, cacheInstance, ct);

        public Task SetSlidingAsync<T>(string tenantId, string resource, string id, int version, T value, TimeSpan slidingExpiration, string cacheInstance = "default", CancellationToken ct = default)
        {
            Writes.Enqueue((resource, id));
            return inner.SetSlidingAsync(tenantId, resource, id, version, value, slidingExpiration, cacheInstance, ct);
        }
    }

    // ── snapshot world ──────────────────────────────────────────────────────────────────────────────────────

    private sealed class SnapshotWorld : IAsyncDisposable
    {
        public const string Oid = "13200000-0000-0000-0000-0000000000f0";
        public const string MatterSet = "sprk_matters";
        public static readonly Guid RecordId = Guid.Parse("13200000-0000-0000-0000-0000000000f1");
        public static readonly Guid DocumentId = Guid.Parse("13200000-0000-0000-0000-0000000000f2");

        private SnapshotWorld(AccessDataverse dataverse, SeamAccessDataSource source, ITenantCache cache)
        {
            Dataverse = dataverse;
            Source = source;
            Cache = cache;
            Cached = new CachedAccessDataSource(source, cache, RequestWithTenant(), NullLogger<CachedAccessDataSource>.Instance);
        }

        public AccessDataverse Dataverse { get; }

        public SeamAccessDataSource Source { get; }

        public ITenantCache Cache { get; }

        public CachedAccessDataSource Cached { get; }

        public static async Task<SnapshotWorld> StartAsync(TimeSpan? timeout = null)
        {
            var dataverse = await AccessDataverse.StartAsync();
            var source = new SeamAccessDataSource(dataverse.CreateClient(timeout ?? TimeSpan.FromSeconds(30)));
            return new SnapshotWorld(dataverse, source, MemoryTenantCache());
        }

        /// <summary>Puts the fake Dataverse (and the OBO seam) into the named state.</summary>
        public void Arrange(string state)
        {
            switch (state)
            {
                case "healthy":
                    break;
                case "obo-failure":
                    Source.OboFails = true;
                    break;
                case "user-lookup-500":
                    Dataverse.Lookup = Behaviour.Status(500);
                    break;
                case "user-lookup-429":
                    Dataverse.Lookup = Behaviour.Status(429);
                    break;
                case "user-lookup-malformed":
                    Dataverse.Lookup = Behaviour.Body("{\"value\":[ not json");
                    break;
                case "user-lookup-timeout":
                    Dataverse.Lookup = Behaviour.Hang();
                    break;
                case "no-such-systemuser":
                    Dataverse.Lookup = Behaviour.Body("{\"value\":[]}");
                    break;
                case "rpa-answered-no-rights":
                    Dataverse.Rpa = Behaviour.Body("{\"AccessRights\":\"\"}");
                    break;
                case "rpa-unanswered-probe-readable":
                    Dataverse.Rpa = Behaviour.Status(429);
                    break;
                case "rpa-unanswered-probe-500":
                    Dataverse.Rpa = Behaviour.Status(500);
                    Dataverse.Probe = Behaviour.Status(500);
                    break;
                case "rpa-unanswered-probe-timeout":
                    Dataverse.Rpa = Behaviour.Status(503);
                    Dataverse.Probe = Behaviour.Hang();
                    break;
                case "rpa-unanswered-probe-403":
                    Dataverse.Rpa = Behaviour.Status(500);
                    Dataverse.Probe = Behaviour.Status(403);
                    break;
                case "rpa-unanswered-probe-404":
                    Dataverse.Rpa = Behaviour.Status(500);
                    Dataverse.Probe = Behaviour.Status(404);
                    break;
                case "teams-500":
                    Dataverse.Teams = Behaviour.Status(500);
                    break;
                case "roles-500":
                    Dataverse.Roles = Behaviour.Status(500);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown snapshot state.");
            }
        }

        public Task<string?> CachedRecordEntryAsync()
            => Cache.GetStringAsync(Tenant, CachedAccessDataSource.RecordAccessResource,
                CachedAccessDataSource.RecordAccessCacheId(MatterSet, Oid, CachedAccessDataSource.RecordIdSegment(RecordId)),
                CachedAccessDataSource.CacheVersion);

        public Task<string?> CachedDocumentEntryAsync()
            => Cache.GetStringAsync(Tenant, CachedAccessDataSource.DocumentAccessResource,
                CachedAccessDataSource.DocumentAccessCacheId("obo", Oid, DocumentId.ToString()),
                CachedAccessDataSource.CacheVersion);

        public ValueTask DisposeAsync() => Dataverse.DisposeAsync();
    }

    /// <summary>
    /// The PRODUCTION data source with its one non-drivable step — the MSAL OBO exchange — replaced; every Dataverse
    /// read, and the classification of its answer, is production code.
    /// </summary>
    private sealed class SeamAccessDataSource : DataverseAccessDataSource
    {
        public SeamAccessDataSource(HttpClient client)
            : base(
                Mock.Of<IDataverseService>(),
                client,
                ServiceUrlConfig(("Graph:ManagedIdentity:Enabled", "true")),
                NullLogger<DataverseAccessDataSource>.Instance,
                credential: new StaticTokenCredential())
        {
        }

        public bool OboFails { get; set; }

        public int OboCalls { get; private set; }

        internal override Task<string> GetDataverseTokenViaOBOAsync(string userAccessToken, CancellationToken ct = default)
        {
            OboCalls++;
            return OboFails
                ? Task.FromException<string>(new InvalidOperationException("simulated OBO exchange failure"))
                : Task.FromResult("obo-dataverse-token");
        }
    }

    /// <summary>One read's scripted answer.</summary>
    private sealed record Behaviour(int StatusCode, string? Json, bool Hangs)
    {
        public static Behaviour Status(int status) => new(status, "{\"error\":{\"code\":\"0x80072321\",\"message\":\"simulated\"}}", false);
        public static Behaviour Body(string json) => new(200, json, false);
        public static Behaviour Hang() => new(200, null, true);
    }

    /// <summary>
    /// The Web API surface <see cref="DataverseAccessDataSource"/> reads, over an in-memory server: the oid lookup,
    /// RetrievePrincipalAccess, the record/document probes, and the team/role sub-reads. Healthy by default.
    /// </summary>
    private sealed class AccessDataverse : IAsyncDisposable
    {
        private const string SystemUserId = "13200000-0000-0000-0000-0000000000f9";
        private readonly WebApplication _app;
        private int _requests;

        private AccessDataverse(WebApplication app) => _app = app;

        public Behaviour Lookup { get; set; } = Behaviour.Body($"{{\"value\":[{{\"systemuserid\":\"{SystemUserId}\",\"fullname\":\"Test User\"}}]}}");
        public Behaviour Rpa { get; set; } = Behaviour.Body("{\"AccessRights\":\"ReadAccess,WriteAccess\"}");
        public Behaviour Probe { get; set; } = Behaviour.Body("{\"createdon\":\"2026-10-01T00:00:00Z\"}");
        public Behaviour Teams { get; set; } = Behaviour.Body("{\"value\":[{\"teamid\":\"t-1\",\"name\":\"Team\"}]}");
        public Behaviour Roles { get; set; } = Behaviour.Body("{\"value\":[{\"roleid\":\"r-1\",\"name\":\"Role\"}]}");

        public int TotalRequests => Volatile.Read(ref _requests);

        public static async Task<AccessDataverse> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseTestServer();
            var app = builder.Build();
            var fake = new AccessDataverse(app);
            app.Map("/api/data/v9.2/{**rest}", (HttpContext context) => fake.HandleAsync(context));
            await app.StartAsync();
            return fake;
        }

        public HttpClient CreateClient(TimeSpan timeout)
        {
            var client = _app.GetTestClient();
            // DataverseAccessDataSource addresses relative URLs, as its DI registration's BaseAddress does.
            client.BaseAddress = new Uri("http://localhost/api/data/v9.2/");
            client.Timeout = timeout;
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private async Task HandleAsync(HttpContext context)
        {
            Interlocked.Increment(ref _requests);
            var path = Uri.UnescapeDataString(context.Request.Path.Value ?? string.Empty)["/api/data/v9.2/".Length..];

            var behaviour =
                path.Contains("RetrievePrincipalAccess", StringComparison.Ordinal) ? Rpa
                : path.EndsWith("/teammembership_association", StringComparison.Ordinal) ? Teams
                : path.EndsWith("/systemuserroles_association", StringComparison.Ordinal) ? Roles
                : path == "systemusers" ? Lookup
                : Probe;

            if (behaviour.Hangs)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(30), context.RequestAborted); }
                catch (OperationCanceledException) { }
                return;
            }

            context.Response.StatusCode = behaviour.StatusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(behaviour.Json ?? "{}", Encoding.UTF8);
        }
    }

    // ── deny-veto world ─────────────────────────────────────────────────────────────────────────────────────

    private static class DenyVetoWorld
    {
        public static AccessibleRecordSetService Evaluator(Guid record, INoAccessListReader denyList)
        {
            var membership = new Mock<IMembershipResolverService>();
            membership
                .Setup(m => m.ResolveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, string entity, MembershipResolveOptions? _, CancellationToken _) => new MembershipResponse(
                    entity, new PersonIdentity(IdentityWorld.UserId), new[] { record },
                    new Dictionary<string, IReadOnlyList<Guid>>(), 1, DateTimeOffset.UtcNow.AddMinutes(2)));

            return new AccessibleRecordSetService(
                membership.Object,
                new QuietParticipations(),
                Mock.Of<ISubjectStandingGrantReader>(),
                denyList,
                // Batch 4 integration (task 143): the identity store the secure-record veto reads status-first.
                Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.UnlinkedIdentityStore(),
                Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.InternalSystemUsers(),
                Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.NoFilingEntities(),
                NullLogger<AccessibleRecordSetService>.Instance);
        }
    }

    /// <summary>The production participation service with its reads answered "nothing relevant" — no grants, no flags,
    /// no organizations — so the only thing that can remove the record is the deny veto.</summary>
    private sealed class QuietParticipations : ExternalParticipationService
    {
        public QuietParticipations()
            : base(new HttpClient(), cache: null!, configuration: null!, credential: null!,
                httpContextAccessor: null!, logger: NullLogger<ExternalParticipationService>.Instance,
                filing: Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.NoFilingEntities())
        {
        }

        public override Task<ExternalGrantSet> GetGrantSetAsync(Guid contactId, CancellationToken ct = default)
            => Task.FromResult(ExternalGrantSet.Empty);

        public override Task<IReadOnlyDictionary<Guid, RootRecordFlags>> GetRootRecordFlagsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, RootRecordFlags>>(recordIds.Distinct().ToDictionary(id => id, _ => RootRecordFlags.None));

        internal override Task<ActiveOrgMemberships> ReadOrganizationMembershipsAsync(Guid contactId, CancellationToken ct = default)
            => Task.FromResult(ActiveOrgMemberships.None);

        public override Task<IReadOnlyDictionary<Guid, ReferencedOrganizations>> GetReferencedOrganizationIdsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, ReferencedOrganizations>>(recordIds.Distinct().ToDictionary(id => id, _ => ReferencedOrganizations.None));

        internal override Task<ContactRecordState> QueryContactStateAsync(Guid contactId, CancellationToken ct)
            => Task.FromResult(ContactRecordState.Active);
    }

    /// <summary>A No Access List with one contact × record entry, at its module boundary.</summary>
    private sealed class ContactDenyList(Guid subjectContact, Guid deniedRecord) : INoAccessListReader
    {
        public int Calls { get; private set; }

        public Task<NoAccessListResult> GetDeniedRecordsAsync(
            Guid? contactId, IReadOnlyCollection<Guid> organizationIds,
            IReadOnlyCollection<NoAccessCandidateRecord> candidates, CancellationToken ct = default)
        {
            Calls++;
            var denied = contactId == subjectContact
                ? candidates.Where(c => c.RecordId == deniedRecord).Select(c => c.RecordId).ToHashSet()
                : new HashSet<Guid>();
            return Task.FromResult(new NoAccessListResult
            {
                DeniedRecordIds = denied,
                DenyingEntryIds = denied.ToDictionary(id => id, _ => (IReadOnlyList<Guid>)Array.Empty<Guid>()),
            });
        }

        // Batch 4 integration (task 143): the systemuser plane asks with the multi-subject shape. The same one entry —
        // a CONTACT subject (so on a non-secure record it removes the contact-sourced contribution, owner N3).
        public Task<NoAccessListResult> GetDeniedRecordsAsync(
            NoAccessSubjects subjects, IReadOnlyCollection<NoAccessCandidateRecord> candidates, CancellationToken ct = default)
        {
            Calls++;
            var denied = subjects.ContactIds.Contains(subjectContact)
                ? candidates.Where(c => c.RecordId == deniedRecord).Select(c => c.RecordId).ToHashSet()
                : new HashSet<Guid>();
            return Task.FromResult(new NoAccessListResult
            {
                DeniedRecordIds = denied,
                DenyingEntryIds = denied.ToDictionary(id => id, _ => (IReadOnlyList<Guid>)Array.Empty<Guid>()),
                DenyingSubjectKinds = denied.ToDictionary(id => id, _ => NoAccessSubjectKinds.Contact),
            });
        }
    }
}
