using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;

using static Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory;
namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// Tests for the principal-agnostic caller resolution spine (teams-app-r1 task 025 · R2 FR-22):
///   • <see cref="CallerPrincipalResolver.DeterminePlane"/> — plane selection by token issuer/tenant
///     (a CIAM token routes to the CIAM strategy; a workforce token routes to the workforce strategy;
///     never cross-selected).
///   • <see cref="WorkforcePrincipalStrategy"/> — the workforce caller's record scope is EXACTLY the
///     accessible-record-set (R2 NFR-08: not all projects), sourced from IAccessibleRecordSetService
///     for entity <c>sprk_project</c>; a resolver deny short-circuits with ProblemDetails.
///   • <see cref="CiamContactPrincipalStrategy"/> — resolves the contact through the oid binding
///     (<see cref="ContactIdentityBinder"/>, task 141; each deny carries the binding decision's own reason
///     code) and, since task 135, takes its record scope from the unified evaluator.
/// </summary>
public class CallerPrincipalResolverTests
{
    private const string CiamTenantId = "11111111-1111-1111-1111-111111111111";
    private const string WorkforceTenantId = "22222222-2222-2222-2222-222222222222";

    private static CallerPrincipalResolver CreateResolver(params ICallerPrincipalStrategy[] strategies)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ciam:TenantId"] = CiamTenantId })
            .Build();
        return new CallerPrincipalResolver(strategies, config, Mock.Of<ILogger<CallerPrincipalResolver>>());
    }

    private static ClaimsPrincipal Principal(params (string type, string value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.type, c.value)), "TestAuth"));

    // ── Plane selection ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeterminePlane_CiamIssuer_SelectsCiamContact()
    {
        var resolver = CreateResolver();
        var user = Principal(
            ("iss", $"https://spaarke.ciamlogin.com/{CiamTenantId}/v2.0"),
            ("tid", CiamTenantId));

        resolver.DeterminePlane(user).Should().Be(CallerPrincipalPlane.CiamContact);
    }

    [Fact]
    public void DeterminePlane_WorkforceIssuerAndForeignTenant_SelectsWorkforce()
    {
        var resolver = CreateResolver();
        var user = Principal(
            ("iss", $"https://login.microsoftonline.com/{WorkforceTenantId}/v2.0"),
            ("tid", WorkforceTenantId));

        resolver.DeterminePlane(user).Should().Be(CallerPrincipalPlane.Workforce);
    }

    [Fact]
    public void DeterminePlane_ConfiguredCiamTenantWithoutCiamIssuer_SelectsCiamContact()
    {
        // Belt-and-suspenders: even without a ciamlogin issuer string, a token whose tid IS the
        // configured CIAM tenant is the CIAM plane.
        var resolver = CreateResolver();
        var user = Principal(("tid", CiamTenantId));

        resolver.DeterminePlane(user).Should().Be(CallerPrincipalPlane.CiamContact);
    }

    [Fact]
    public void DeterminePlane_NoIssuerOrTenantClaims_DefaultsToWorkforce()
    {
        var resolver = CreateResolver();
        resolver.DeterminePlane(Principal()).Should().Be(CallerPrincipalPlane.Workforce);
    }

    [Fact]
    public async Task ResolveAsync_RoutesToTheStrategyForTheDetectedPlane()
    {
        // A CIAM token must be resolved by the CIAM strategy, a workforce token by the workforce
        // strategy — never cross-selected.
        var ciam = new StubStrategy(CallerPrincipalPlane.CiamContact);
        var workforce = new StubStrategy(CallerPrincipalPlane.Workforce);
        var resolver = CreateResolver(ciam, workforce);

        var ciamCtx = new DefaultHttpContext
        {
            User = Principal(("iss", $"https://x.ciamlogin.com/{CiamTenantId}/v2.0"))
        };
        await resolver.ResolveAsync(ciamCtx, CancellationToken.None);
        ciam.Called.Should().BeTrue();
        workforce.Called.Should().BeFalse();

        ciam.Called = false;
        var wfCtx = new DefaultHttpContext
        {
            User = Principal(("iss", $"https://login.microsoftonline.com/{WorkforceTenantId}/v2.0"),
                ("tid", WorkforceTenantId))
        };
        await resolver.ResolveAsync(wfCtx, CancellationToken.None);
        workforce.Called.Should().BeTrue();
        ciam.Called.Should().BeFalse();
    }

    // ── Workforce strategy: Tier-2 record scope = accessible set (NFR-08) ────────────────────────

    [Fact]
    public async Task WorkforceStrategy_SystemUser_ScopesToMembershipSetOnly_NotAllProjects()
    {
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var suid = Guid.NewGuid();
        var contactId = Guid.NewGuid();

        var resolver = new Mock<IWorkforcePrincipalResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkforcePrincipalResolution.ForSystemUser(
                suid, contactId, Guid.NewGuid().ToString(), WorkforceTenantId));

        var accessible = new Mock<IAccessibleRecordSetService>();
        accessible.Setup(s => s.ComposeAsync(
                It.IsAny<WorkforcePrincipal>(), "sprk_project", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessibleRecordSet
            {
                PrincipalKind = WorkforcePrincipalKind.SystemUser,
                EntityType = "sprk_project",
                Rights = RightsOf(p1, p2),
                Sources = new AccessibleRecordSetSources(true, false, false)
            });
        // Task 028: the strategy now composes matter + work-assignment roots too — empty here.
        accessible.Setup(s => s.ComposeAsync(
                It.IsAny<WorkforcePrincipal>(), It.Is<string>(e => e != "sprk_project"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkforcePrincipal _, string entity, CancellationToken _) => new AccessibleRecordSet
            {
                PrincipalKind = WorkforcePrincipalKind.SystemUser,
                EntityType = entity,
                Rights = RightsOf(),
                Sources = new AccessibleRecordSetSources(true, false, false)
            });

        var strategy = new WorkforcePrincipalStrategy(
            resolver.Object, accessible.Object, Mock.Of<ILogger<WorkforcePrincipalStrategy>>());

        var ctx = new DefaultHttpContext { User = Principal(("email", "staff@contoso.com")) };
        var result = await strategy.ResolveAsync(ctx, CancellationToken.None);

        result.IsResolved.Should().BeTrue();
        result.Principal!.Plane.Should().Be(CallerPrincipalPlane.Workforce);
        result.Principal.SystemUserId.Should().Be(suid);
        result.Principal.GetAccessibleProjectIds().Should().BeEquivalentTo(new[] { p1, p2 },
            "a workforce systemuser sees ONLY its ADR-034 membership set, never all projects (NFR-08)");
        result.Principal.HasProjectAccess(Guid.NewGuid()).Should().BeFalse();
        // Record scope is composed for sprk_project — the Tier-2 predicate, not an unfiltered list.
        accessible.Verify(s => s.ComposeAsync(
            It.IsAny<WorkforcePrincipal>(), "sprk_project", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WorkforceStrategy_Contact_ScopesToGrantSet()
    {
        var grant = Guid.NewGuid();
        var contactId = Guid.NewGuid();

        var resolver = new Mock<IWorkforcePrincipalResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkforcePrincipalResolution.ForContact(
                contactId, Guid.NewGuid().ToString(), WorkforceTenantId));

        var accessible = new Mock<IAccessibleRecordSetService>();
        accessible.Setup(s => s.ComposeAsync(
                It.IsAny<WorkforcePrincipal>(), "sprk_project", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessibleRecordSet
            {
                PrincipalKind = WorkforcePrincipalKind.ContactOnly,
                EntityType = "sprk_project",
                Rights = RightsOf(grant),
                Sources = new AccessibleRecordSetSources(false, true, false)
            });
        // Task 028: the strategy now composes matter + work-assignment roots too — empty here.
        accessible.Setup(s => s.ComposeAsync(
                It.IsAny<WorkforcePrincipal>(), It.Is<string>(e => e != "sprk_project"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkforcePrincipal _, string entity, CancellationToken _) => new AccessibleRecordSet
            {
                PrincipalKind = WorkforcePrincipalKind.ContactOnly,
                EntityType = entity,
                Rights = RightsOf(),
                Sources = new AccessibleRecordSetSources(false, true, false)
            });

        var strategy = new WorkforcePrincipalStrategy(
            resolver.Object, accessible.Object, Mock.Of<ILogger<WorkforcePrincipalStrategy>>());

        var result = await strategy.ResolveAsync(new DefaultHttpContext { User = Principal() }, CancellationToken.None);

        result.IsResolved.Should().BeTrue();
        result.Principal!.ContactId.Should().Be(contactId);
        result.Principal.GetAccessibleProjectIds().Should().BeEquivalentTo(new[] { grant });
    }

    [Fact]
    public async Task WorkforceStrategy_ResolverDenies_ShortCircuitsWithForbidden()
    {
        var resolver = new Mock<IWorkforcePrincipalResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkforcePrincipalResolution.Denied(
                WorkforceDenyReason.PrincipalNotResolved, "sdap.access.deny.principal_not_resolved"));

        var accessible = new Mock<IAccessibleRecordSetService>();
        var strategy = new WorkforcePrincipalStrategy(
            resolver.Object, accessible.Object, Mock.Of<ILogger<WorkforcePrincipalStrategy>>());

        var result = await strategy.ResolveAsync(new DefaultHttpContext { User = Principal() }, CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        // Never composes an accessible set for a denied caller.
        accessible.Verify(s => s.ComposeAsync(
            It.IsAny<WorkforcePrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── CIAM strategy: resolution by the oid binding (task 141); record scope from the evaluator (task 135) ──
    //
    // The vetoes themselves (FR-21/22/23), plane parity and the fault family are pinned end-to-end through
    // this strategy and the REAL evaluator in tests/integration/seam/ExternalAccess/UnifiedEvaluatorSeamTests.cs.
    // These unit tests pin only what the strategy itself owns: which contact it composes for, and that it
    // maps the evaluator's answer onto the principal without reading a grant level of its own.

    [Fact]
    public async Task CiamStrategy_ResolvedContact_TakesAllThreeRootScopesFromTheEvaluatorForThatContact()
    {
        var contactId = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var viewOnlyProject = Guid.NewGuid();
        var fullProject = Guid.NewGuid();
        var matter = Guid.NewGuid();
        var workAssignment = Guid.NewGuid();
        var store = new InMemoryContactIdentityStore();
        store.AddContact(contactId, email: "external@test.com", oid: oid.ToString("D"), plane: IdentityPlaneMarker.External);

        var accessible = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);
        SetupCiamComposition(accessible, contactId, "sprk_project", new Dictionary<Guid, AccessRights>
        {
            [viewOnlyProject] = AccessRights.Read,
            [fullProject] = AccessRights.Read | AccessRights.Write | AccessRights.Create | AccessRights.Delete,
        });
        SetupCiamComposition(accessible, contactId, "sprk_matter",
            new Dictionary<Guid, AccessRights> { [matter] = AccessRights.Read });
        SetupCiamComposition(accessible, contactId, "sprk_workassignment",
            new Dictionary<Guid, AccessRights> { [workAssignment] = AccessRights.Read | AccessRights.Write });

        var strategy = new CiamContactPrincipalStrategy(
            IdentityBindingTestKit.Binder(store), accessible.Object, Mock.Of<ILogger<CiamContactPrincipalStrategy>>());

        var ctx = new DefaultHttpContext
        {
            User = Principal(("oid", oid.ToString().ToUpperInvariant()), ("email", "external@test.com"))
        };
        var result = await strategy.ResolveAsync(ctx, CancellationToken.None);

        result.IsResolved.Should().BeTrue();
        result.Principal!.Plane.Should().Be(CallerPrincipalPlane.CiamContact);
        result.Principal.ContactId.Should().Be(contactId, "oids compare as parsed Guids, whatever their case");
        result.Principal.GetEffectiveRights(viewOnlyProject).Should().Be(AccessRights.Read,
            "a project's rights are the evaluator's, not re-derived from a grant row");
        result.Principal.GetEffectiveRights(fullProject).Should().Be(
            AccessRights.Read | AccessRights.Write | AccessRights.Create | AccessRights.Delete);
        result.Principal.GetAccessibleProjectIds().Should().BeEquivalentTo(new[] { viewOnlyProject, fullProject });
        result.Principal.MatterAccess.Should().Equal(new Dictionary<Guid, AccessRights> { [matter] = AccessRights.Read });
        result.Principal.WorkAssignmentAccess.Should().Equal(
            new Dictionary<Guid, AccessRights> { [workAssignment] = AccessRights.Read | AccessRights.Write });
    }

    /// <summary>
    /// Task 138 criterion 2, on task 135's pipeline: the CIAM strategy over the REAL evaluator. A Limited root
    /// gives the same answer whether the contact holds a direct grant, an org-inherited grant, or both — only the
    /// direct grant counts — and there is no CIAM-specific Limited code: the strategy maps what the shared
    /// contact-plane composition returns.
    /// </summary>
    [Fact]
    public async Task CiamStrategy_OnALimitedRoot_OnlyTheDirectGrantCounts_DirectOrgOrBoth()
    {
        var contactId = Guid.NewGuid();
        var directOnly = Guid.NewGuid();
        var orgOnly = Guid.NewGuid();
        var both = Guid.NewGuid();
        var limited = new RootRecordFlags(IsSecure: false, IsRestricted: false, IsLimited: true);

        // Task 141: the CIAM caller's contact is named by the oid binding (the identity store, through the binder),
        // no longer by the participation service — which now supplies only the grant data.
        var oid = Guid.NewGuid();
        var identities = new InMemoryContactIdentityStore();
        identities.AddContact(contactId, oid: oid.ToString("D"), plane: IdentityPlaneMarker.External);

        var participations = CreateParticipationServiceMock();
        participations.Setup(s => s.GetGrantSetAsync(contactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalGrantSet
            {
                Projects = new[]
                {
                    new ExternalParticipation { ProjectId = directOnly, AccessLevel = ExternalAccessLevel.Collaborate, DirectAccessLevel = ExternalAccessLevel.Collaborate },
                    new ExternalParticipation { ProjectId = orgOnly, AccessLevel = ExternalAccessLevel.FullAccess, DirectAccessLevel = null },
                    new ExternalParticipation { ProjectId = both, AccessLevel = ExternalAccessLevel.FullAccess, DirectAccessLevel = ExternalAccessLevel.ViewOnly },
                },
                MatterGrants = Array.Empty<ExternalRootGrant>(),
                WorkAssignmentGrants = Array.Empty<ExternalRootGrant>(),
            });
        participations.Setup(s => s.GetRootRecordFlagsAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                (IReadOnlyDictionary<Guid, RootRecordFlags>)ids.Distinct().ToDictionary(id => id, _ => limited));
        participations.Setup(s => s.ReadOrganizationMembershipsAsync(contactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveOrgMemberships.None);
        // Task 137: the evaluator reads the contact's live state first; an active contact composes as before.
        participations.Setup(s => s.QueryContactStateAsync(contactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContactRecordState.Active);
        participations.Setup(s => s.GetReferencedOrganizationIdsAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                (IReadOnlyDictionary<Guid, ReferencedOrganizations>)ids.Distinct().ToDictionary(id => id, _ => ReferencedOrganizations.None));

        // The REAL evaluator. CIAM composes no derived-member term, so membership and the standing reader must
        // never be reached (Strict, no setups).
        var evaluator = new AccessibleRecordSetService(
            new Mock<Sprk.Bff.Api.Services.Ai.Membership.IMembershipResolverService>(MockBehavior.Strict).Object,
            participations.Object,
            new Mock<ISubjectStandingGrantReader>(MockBehavior.Strict).Object,
            NeverDeniesReader(),
            Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.UnlinkedIdentityStore(),
            Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.InternalSystemUsers(),
            Mock.Of<ILogger<AccessibleRecordSetService>>());

        var strategy = new CiamContactPrincipalStrategy(
            IdentityBindingTestKit.Binder(identities), evaluator, Mock.Of<ILogger<CiamContactPrincipalStrategy>>());
        var result = await strategy.ResolveAsync(
            new DefaultHttpContext { User = Principal(("oid", oid.ToString())) }, CancellationToken.None);

        result.IsResolved.Should().BeTrue();
        result.Principal!.GetEffectiveRights(directOnly).Should().Be(ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate));
        result.Principal.HasProjectAccess(orgOnly).Should().BeFalse("an org-inherited grant confers nothing on a Limited root");
        result.Principal.GetEffectiveRights(both).Should().Be(AccessRights.Read,
            "direct ViewOnly under org FullAccess is EXACTLY Read — the same answer as a direct grant alone");
        result.Principal.GetAccessibleProjectIds().Should().BeEquivalentTo(new[] { directOnly, both });
    }

    [Fact]
    public async Task CiamStrategy_MissingOidAndEmail_Returns401()
    {
        var accessible = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);
        var strategy = new CiamContactPrincipalStrategy(
            IdentityBindingTestKit.Binder(new InMemoryContactIdentityStore()), accessible.Object,
            Mock.Of<ILogger<CiamContactPrincipalStrategy>>());

        var result = await strategy.ResolveAsync(
            new DefaultHttpContext { User = Principal() }, CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.GetType().Name.Should().Be("ProblemHttpResult");
    }

    /// <summary>
    /// Task 141 acceptance: <see cref="CiamContactPrincipalStrategy"/> returns the decision's DISTINCT deny code
    /// — not one <c>contact_not_found</c> for everything — so ambiguity, collision, an unreadable binding and an
    /// inactive contact are distinguishable in the audit trail.
    /// </summary>
    [Theory]
    [InlineData("not-found", "sdap.access.deny.contact_not_found")]
    [InlineData("email-ambiguous", "sdap.access.deny.contact_email_ambiguous")]
    [InlineData("collision", "sdap.access.deny.contact_bound_to_different_oid")]
    [InlineData("unreadable", "sdap.access.deny.contact_lookup_failed")]
    [InlineData("inactive", "sdap.access.deny.contact_inactive")]
    [InlineData("key-conflict", "sdap.access.deny.contact_key_conflict")]
    public async Task CiamStrategy_EachDeny_CarriesItsOwnReasonCode(string scenario, string expectedCode)
    {
        var oid = Guid.NewGuid();
        var store = new InMemoryContactIdentityStore();
        switch (scenario)
        {
            case "email-ambiguous":
                store.AddContact(Guid.NewGuid(), email: "x@firm.example");
                store.AddContact(Guid.NewGuid(), email: "x@firm.example");
                break;
            case "collision":
                store.AddContact(Guid.NewGuid(), email: "x@firm.example", oid: Guid.NewGuid().ToString("D"));
                break;
            case "unreadable":
                store.OidLookupStatus = LookupStatus.Failed;
                break;
            case "inactive":
                store.AddContact(Guid.NewGuid(), oid: oid.ToString("D"), plane: IdentityPlaneMarker.External, stateCode: 1);
                break;
            case "key-conflict":
                // B2: the repair bind's target is free, but another contact holds the oid in the uniqueness mirror.
                store.AddContact(Guid.NewGuid(), email: "x@firm.example");
                store.AddContact(Guid.NewGuid(), email: "y@firm.example", keyMirror: oid.ToString("D"));
                break;
        }

        // Strict + no setups: a caller with no contact must never reach the evaluator.
        var accessible = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);
        var strategy = new CiamContactPrincipalStrategy(
            IdentityBindingTestKit.Binder(store), accessible.Object, Mock.Of<ILogger<CiamContactPrincipalStrategy>>());
        var http = new DefaultHttpContext
        {
            User = Principal(("oid", oid.ToString()), ("preferred_username", "x@firm.example")),
            RequestServices = new ServiceCollection()
                .AddLogging().BuildServiceProvider(),
        };
        http.Response.Body = new MemoryStream();

        var result = await strategy.ResolveAsync(http, CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        await result.Failure!.ExecuteAsync(http);
        http.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        http.Response.Body.Position = 0;
        var body = await new StreamReader(http.Response.Body).ReadToEndAsync();
        body.Should().Contain(expectedCode);
    }

    // ── Construction-time pruning (task 136 · defect C2) — both strategies ─────────────────────────────
    //
    // The evaluator double returns a None-rights entry on every root type, as the evaluator itself did before
    // task 136 (a Secure root reached only through an organization grant; a level-less matter/WA grant row).
    // The assertions read the principal's RAW collections, not its Read-gated views, because those views
    // would hide the entry on their own — this pins the OTHER layer: the strategy never puts it there.

    private static readonly IReadOnlyDictionary<string, (Guid Readable, Guid Powerless)> RootsWithAPowerlessEntry =
        new Dictionary<string, (Guid, Guid)>
        {
            ["sprk_project"] = (Guid.NewGuid(), Guid.NewGuid()),
            ["sprk_matter"] = (Guid.NewGuid(), Guid.NewGuid()),
            ["sprk_workassignment"] = (Guid.NewGuid(), Guid.NewGuid()),
        };

    private static IReadOnlyDictionary<Guid, AccessRights> ReadableAndPowerless(string entityType) =>
        new Dictionary<Guid, AccessRights>
        {
            [RootsWithAPowerlessEntry[entityType].Readable] = AccessRights.Read,
            [RootsWithAPowerlessEntry[entityType].Powerless] = AccessRights.None,
        };

    private static void AssertOnlyTheReadableEntriesReachedThePrincipal(CallerPrincipal principal)
    {
        principal.ProjectAccess.Select(p => p.ProjectId).Should().Equal(
            new[] { RootsWithAPowerlessEntry["sprk_project"].Readable },
            "a project the caller holds nothing on is not one of its projects (C2)");
        principal.MatterAccess.Keys.Should().Equal(RootsWithAPowerlessEntry["sprk_matter"].Readable);
        principal.WorkAssignmentAccess.Keys.Should().Equal(RootsWithAPowerlessEntry["sprk_workassignment"].Readable);
    }

    [Fact]
    public async Task CiamStrategy_EvaluatorAnswerWithNoneRightsEntries_BuildsAPrincipalWithoutThem()
    {
        var contactId = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var store = new InMemoryContactIdentityStore();
        store.AddContact(contactId, oid: oid.ToString("D"), plane: IdentityPlaneMarker.External);

        var accessible = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);
        foreach (var entityType in RootsWithAPowerlessEntry.Keys)
        {
            SetupCiamComposition(accessible, contactId, entityType, ReadableAndPowerless(entityType));
        }

        var strategy = new CiamContactPrincipalStrategy(
            IdentityBindingTestKit.Binder(store), accessible.Object, Mock.Of<ILogger<CiamContactPrincipalStrategy>>());

        var result = await strategy.ResolveAsync(
            new DefaultHttpContext { User = Principal(("oid", oid.ToString())) }, CancellationToken.None);

        result.IsResolved.Should().BeTrue();
        AssertOnlyTheReadableEntriesReachedThePrincipal(result.Principal!);
    }

    [Fact]
    public async Task WorkforceStrategy_EvaluatorAnswerWithNoneRightsEntries_BuildsAPrincipalWithoutThem()
    {
        var resolver = new Mock<IWorkforcePrincipalResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkforcePrincipalResolution.ForContact(
                Guid.NewGuid(), Guid.NewGuid().ToString(), WorkforceTenantId));

        var accessible = new Mock<IAccessibleRecordSetService>();
        accessible.Setup(s => s.ComposeAsync(
                It.IsAny<WorkforcePrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkforcePrincipal _, string entity, CancellationToken _) => new AccessibleRecordSet
            {
                PrincipalKind = WorkforcePrincipalKind.ContactOnly,
                EntityType = entity,
                Rights = ReadableAndPowerless(entity),
                Sources = new AccessibleRecordSetSources(false, true, false),
            });

        var strategy = new WorkforcePrincipalStrategy(
            resolver.Object, accessible.Object, Mock.Of<ILogger<WorkforcePrincipalStrategy>>());

        var result = await strategy.ResolveAsync(new DefaultHttpContext { User = Principal() }, CancellationToken.None);

        result.IsResolved.Should().BeTrue();
        AssertOnlyTheReadableEntriesReachedThePrincipal(result.Principal!);
    }

    private static void SetupCiamComposition(
        Mock<IAccessibleRecordSetService> accessible,
        Guid contactId,
        string entityType,
        IReadOnlyDictionary<Guid, AccessRights> rights)
        => accessible.Setup(s => s.ComposeForCiamContactAsync(contactId, entityType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessibleRecordSet
            {
                PrincipalKind = WorkforcePrincipalKind.ContactOnly,
                EntityType = entityType,
                Rights = rights,
                Sources = new AccessibleRecordSetSources(false, true, false),
            });

    /// <summary>The participation service at its module boundary — grant data only since task 141.</summary>
    private static Mock<ExternalParticipationService> CreateParticipationServiceMock() =>
        new(
            new HttpClient(),
            Mock.Of<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>(),
            new ConfigurationBuilder().Build(),
            Mock.Of<Azure.Core.TokenCredential>(),
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<ILogger<ExternalParticipationService>>());

    /// <summary>A strategy stub that records whether it was invoked (for plane-routing tests).</summary>
    private sealed class StubStrategy : ICallerPrincipalStrategy
    {
        public StubStrategy(CallerPrincipalPlane plane) => Plane = plane;
        public CallerPrincipalPlane Plane { get; }
        public bool Called { get; set; }

        public Task<CallerPrincipalResolution> ResolveAsync(HttpContext httpContext, CancellationToken ct)
        {
            Called = true;
            return Task.FromResult(CallerPrincipalResolution.Resolved(new CallerPrincipal
            {
                Plane = Plane,
                ContactId = Guid.NewGuid(),
                ProjectAccess = Array.Empty<CallerProjectAccess>()
            }));
        }
    }
}
