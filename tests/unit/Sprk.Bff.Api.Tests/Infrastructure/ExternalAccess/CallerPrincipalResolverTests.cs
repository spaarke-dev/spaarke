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

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// Tests for the principal-agnostic caller resolution spine (teams-app-r1 task 025 · R2 FR-22):
///   • <see cref="CallerPrincipalResolver.DeterminePlane"/> — plane selection by token issuer/tenant
///     (a CIAM token routes to the CIAM strategy; a workforce token routes to the workforce strategy;
///     never cross-selected).
///   • <see cref="WorkforcePrincipalStrategy"/> — the workforce caller's record scope is EXACTLY the
///     accessible-record-set (R2 NFR-08: not all projects), sourced from IAccessibleRecordSetService
///     for entity <c>sprk_project</c>; a resolver deny short-circuits with ProblemDetails.
///   • <see cref="CiamContactPrincipalStrategy"/> — reproduces the legacy CIAM deny + participation
///     resolution (R2 guardrail #3 / FR-15).
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
                RecordIds = new HashSet<Guid> { p1, p2 },
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
                RecordIds = new HashSet<Guid> { grant },
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

    // ── CIAM strategy: legacy resolution + deny semantics preserved (FR-15) ──────────────────────

    [Fact]
    public async Task CiamStrategy_ResolvesContactAndParticipations_PreservesAccessLevels()
    {
        var contactId = Guid.NewGuid();
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();

        var participations = CreateParticipationServiceMock();
        participations.Setup(s => s.ResolveExternalContactAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(contactId);
        participations.Setup(s => s.GetParticipationsAsync(contactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExternalParticipation>
            {
                new() { ProjectId = p1, AccessLevel = ExternalAccessLevel.ViewOnly },
                new() { ProjectId = p2, AccessLevel = ExternalAccessLevel.FullAccess }
            });

        var strategy = new CiamContactPrincipalStrategy(
            participations.Object, Mock.Of<ILogger<CiamContactPrincipalStrategy>>());

        var ctx = new DefaultHttpContext
        {
            User = Principal(("oid", Guid.NewGuid().ToString()), ("email", "external@test.com"))
        };
        var result = await strategy.ResolveAsync(ctx, CancellationToken.None);

        result.IsResolved.Should().BeTrue();
        result.Principal!.Plane.Should().Be(CallerPrincipalPlane.CiamContact);
        result.Principal.ContactId.Should().Be(contactId);
        result.Principal.GetAccessLevel(p1).Should().Be(ExternalAccessLevel.ViewOnly);
        result.Principal.GetAccessLevel(p2).Should().Be(ExternalAccessLevel.FullAccess);
    }

    [Fact]
    public async Task CiamStrategy_MissingOidAndEmail_Returns401()
    {
        var strategy = new CiamContactPrincipalStrategy(
            CreateParticipationServiceMock().Object, Mock.Of<ILogger<CiamContactPrincipalStrategy>>());

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

        var strategy = new CiamContactPrincipalStrategy(
            participations.Object, Mock.Of<ILogger<CiamContactPrincipalStrategy>>());

        var ctx = new DefaultHttpContext { User = Principal(("oid", Guid.NewGuid().ToString())) };
        var result = await strategy.ResolveAsync(ctx, CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.Failure.Should().NotBeNull();
    }

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
