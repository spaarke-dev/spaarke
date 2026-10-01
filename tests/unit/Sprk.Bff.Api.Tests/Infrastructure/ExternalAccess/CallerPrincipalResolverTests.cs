using System.Security.Claims;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
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
///   • <see cref="CiamContactPrincipalStrategy"/> — reproduces the legacy CIAM identity deny semantics
///     (R2 guardrail #3 / FR-15) and, since task 135, takes its record scope from the unified evaluator.
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

    // ── CIAM strategy: identity resolution unchanged (FR-15); record scope from the evaluator (task 135) ──
    //
    // The vetoes themselves (FR-21/22/23), plane parity and the fault family are pinned end-to-end through
    // this strategy and the REAL evaluator in tests/integration/seam/ExternalAccess/UnifiedEvaluatorSeamTests.cs.
    // These unit tests pin only what the strategy itself owns: which contact it composes for, and that it
    // maps the evaluator's answer onto the principal without reading a grant level of its own.

    [Fact]
    public async Task CiamStrategy_ResolvedContact_TakesAllThreeRootScopesFromTheEvaluatorForThatContact()
    {
        var contactId = Guid.NewGuid();
        var viewOnlyProject = Guid.NewGuid();
        var fullProject = Guid.NewGuid();
        var matter = Guid.NewGuid();
        var workAssignment = Guid.NewGuid();

        var participations = CreateParticipationServiceMock();
        participations.Setup(s => s.ResolveExternalContactAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(contactId);

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
            participations.Object, accessible.Object, Mock.Of<ILogger<CiamContactPrincipalStrategy>>());

        var ctx = new DefaultHttpContext
        {
            User = Principal(("oid", Guid.NewGuid().ToString()), ("email", "external@test.com"))
        };
        var result = await strategy.ResolveAsync(ctx, CancellationToken.None);

        result.IsResolved.Should().BeTrue();
        result.Principal!.Plane.Should().Be(CallerPrincipalPlane.CiamContact);
        result.Principal.ContactId.Should().Be(contactId);
        result.Principal.GetEffectiveRights(viewOnlyProject).Should().Be(AccessRights.Read,
            "a project's rights are the evaluator's, not re-derived from a grant row");
        result.Principal.GetEffectiveRights(fullProject).Should().Be(
            AccessRights.Read | AccessRights.Write | AccessRights.Create | AccessRights.Delete);
        result.Principal.GetAccessibleProjectIds().Should().BeEquivalentTo(new[] { viewOnlyProject, fullProject });
        result.Principal.MatterAccess.Should().Equal(new Dictionary<Guid, AccessRights> { [matter] = AccessRights.Read });
        result.Principal.WorkAssignmentAccess.Should().Equal(
            new Dictionary<Guid, AccessRights> { [workAssignment] = AccessRights.Read | AccessRights.Write });

        participations.Verify(s => s.GetGrantSetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "the strategy must not read grant rows itself — doing so is how the CIAM plane skipped every veto (C1)");
    }

    [Fact]
    public async Task CiamStrategy_MissingOidAndEmail_Returns401()
    {
        var accessible = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);
        var strategy = new CiamContactPrincipalStrategy(
            CreateParticipationServiceMock().Object, accessible.Object, Mock.Of<ILogger<CiamContactPrincipalStrategy>>());

        var result = await strategy.ResolveAsync(
            new DefaultHttpContext { User = Principal() }, CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.GetType().Name.Should().Be("ProblemHttpResult");
    }

    [Fact]
    public async Task CiamStrategy_ContactNotFound_Returns403()
    {
        var participations = CreateParticipationServiceMock();
        participations.Setup(s => s.ResolveExternalContactAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        // Strict + no setups: a caller with no contact must never reach the evaluator.
        var accessible = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);
        var strategy = new CiamContactPrincipalStrategy(
            participations.Object, accessible.Object, Mock.Of<ILogger<CiamContactPrincipalStrategy>>());

        var ctx = new DefaultHttpContext { User = Principal(("oid", Guid.NewGuid().ToString())) };
        var result = await strategy.ResolveAsync(ctx, CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.Failure.Should().NotBeNull();
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

    private static Mock<ExternalParticipationService> CreateParticipationServiceMock() =>
        new(
            new HttpClient(),
            Mock.Of<ITenantCache>(),
            new ConfigurationBuilder().Build(),
            Mock.Of<TokenCredential>(),
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
