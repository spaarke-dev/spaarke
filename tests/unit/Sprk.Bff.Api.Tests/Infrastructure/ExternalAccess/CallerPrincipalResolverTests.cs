using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
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

    // ── R3 task 046: a WorkforceHome principal is chosen by SCHEME, never by iss — never the CIAM plane ─────────

    /// <summary>A principal authenticated by the WorkforceHome scheme (its AuthenticationType is set by the handler).</summary>
    private static ClaimsPrincipal HomePrincipal(params (string type, string value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.type, c.value)),
            Sprk.Bff.Api.Infrastructure.Authentication.AuthSchemes.WorkforceHome));

    [Theory]
    [InlineData("ciam-iss")]
    [InlineData("ciam-tid")]
    [InlineData("both")]
    [InlineData("customer")]
    public void DeterminePlane_WorkforceHomePrincipal_IsAlwaysWorkforce_WhateverItsIssOrTid(string shape)
    {
        var claims = shape switch
        {
            "ciam-iss" => new[] { ("iss", $"https://spaarke.ciamlogin.com/{Guid.NewGuid()}/v2.0"), ("tid", Guid.NewGuid().ToString()) },
            "ciam-tid" => new[] { ("iss", $"https://login.microsoftonline.com/{CiamTenantId}/v2.0"), ("tid", CiamTenantId) },
            "both" => new[] { ("iss", $"https://spaarke.ciamlogin.com/{CiamTenantId}/v2.0"), ("tid", CiamTenantId) },
            _ => new[] { ("iss", "https://login.microsoftonline.com/99999999-eeee-4eee-8eee-999999999999/v2.0"),
                ("tid", "99999999-eeee-4eee-8eee-999999999999") },
        };

        CreateResolver().DeterminePlane(HomePrincipal(claims)).Should().Be(CallerPrincipalPlane.Workforce);
    }

    [Fact]
    public async Task ResolveAsync_AWorkforceHomePrincipal_NeverReachesTheCiamStrategy()
    {
        var ciam = new StubStrategy(CallerPrincipalPlane.CiamContact);
        var workforce = new StubStrategy(CallerPrincipalPlane.Workforce);
        var http = new DefaultHttpContext
        {
            User = HomePrincipal(("iss", $"https://x.ciamlogin.com/{CiamTenantId}/v2.0"), ("tid", CiamTenantId)),
        };

        await CreateResolver(ciam, workforce).ResolveAsync(http, CancellationToken.None);

        ciam.Called.Should().BeFalse("a home principal never resolves on the CIAM (contact-by-oid, invite-bind) plane");
        workforce.Called.Should().BeTrue();
    }

    [Fact]
    public async Task WorkforceStrategy_RegistrationRequired_Is403_WithTheReasonCodeAndTheTokenPrefill()
    {
        var resolver = new Mock<IWorkforcePrincipalResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkforcePrincipalResolution.Denied(
                WorkforceDenyReason.PrincipalNotResolved, WorkforcePrincipalResolver.DenyRegistrationRequired));
        var accessible = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);
        var strategy = new WorkforcePrincipalStrategy(
            resolver.Object, accessible.Object, Mock.Of<ILogger<WorkforcePrincipalStrategy>>());
        var http = new DefaultHttpContext
        {
            User = HomePrincipal(
                ("oid", Guid.NewGuid().ToString()), ("tid", "99999999-eeee-4eee-8eee-999999999999"),
                ("given_name", "Mike"), ("family_name", "Okafor"), ("email", "mike@customer.example")),
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        http.Response.Body = new MemoryStream();

        var result = await strategy.ResolveAsync(http, CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        var body = await DenyBodyAsync(result, http);
        http.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        using var json = System.Text.Json.JsonDocument.Parse(body);
        json.RootElement.GetProperty("reasonCode").GetString().Should().Be("sdap.access.registration_required");
        json.RootElement.GetProperty("givenName").GetString().Should().Be("Mike");
        json.RootElement.GetProperty("familyName").GetString().Should().Be("Okafor");
        json.RootElement.GetProperty("email").GetString().Should().Be("mike@customer.example");
    }

    [Fact]
    public async Task WorkforceStrategy_AnyOtherDeny_CarriesNoPrefill()
    {
        // The prefill is for the registration form only; every other deny stays the plain reasonCode problem.
        var resolver = new Mock<IWorkforcePrincipalResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkforcePrincipalResolution.Denied(
                WorkforceDenyReason.PrincipalNotResolved, WorkforcePrincipalResolver.DenySystemUserDisabled));
        var strategy = new WorkforcePrincipalStrategy(
            resolver.Object, new Mock<IAccessibleRecordSetService>(MockBehavior.Strict).Object,
            Mock.Of<ILogger<WorkforcePrincipalStrategy>>());
        var http = new DefaultHttpContext
        {
            User = HomePrincipal(("given_name", "Mike"), ("family_name", "Okafor"), ("email", "mike@customer.example")),
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        http.Response.Body = new MemoryStream();

        var body = await DenyBodyAsync(await strategy.ResolveAsync(http, CancellationToken.None), http);

        http.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        body.Should().Contain(WorkforcePrincipalResolver.DenySystemUserDisabled);
        body.Should().NotContain("Mike").And.NotContain("mike@customer.example");
    }

    [Fact]
    public async Task WorkforceStrategy_CustomerTenantGuest_Is403_WithTheExactReasonCodeAndHomeSignInDetail()
    {
        // #2128: the shape of the CIAM plane's contact_not_invited (ProblemDetailsHelper.Forbidden + a detail). The SPA
        // (R3) keys on this reasonCode; the detail names no tenant, id or address and makes sense inside Teams.
        const string ExpectedDetail =
            "You're signed in as a guest of the Spaarke directory. Sign in with your own organization's work account "
            + "instead: open the portal in a browser and choose your work or school account.";
        var resolver = new Mock<IWorkforcePrincipalResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkforcePrincipalResolution.Denied(
                WorkforceDenyReason.PrincipalNotResolved, WorkforceMembershipTest.DenyGuestUseHomeSignin));
        var strategy = new WorkforcePrincipalStrategy(
            resolver.Object, new Mock<IAccessibleRecordSetService>(MockBehavior.Strict).Object,
            Mock.Of<ILogger<WorkforcePrincipalStrategy>>());
        var http = new DefaultHttpContext
        {
            User = Principal(("oid", Guid.NewGuid().ToString()), ("acct", "1"), ("email", "new.hire@customer.example")),
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        http.Response.Body = new MemoryStream();

        var body = await DenyBodyAsync(await strategy.ResolveAsync(http, CancellationToken.None), http);

        http.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        using var json = System.Text.Json.JsonDocument.Parse(body);
        json.RootElement.GetProperty("reasonCode").GetString()
            .Should().Be("sdap.access.deny.workforce_guest_use_home_signin");
        json.RootElement.GetProperty("title").GetString().Should().Be("Forbidden");
        var detail = json.RootElement.GetProperty("detail").GetString();
        detail.Should().Be(ExpectedDetail);
        detail.Should().NotContain("@");
        System.Text.RegularExpressions.Regex.IsMatch(detail!, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-")
            .Should().BeFalse("the detail carries no tenant or object id");
        body.Should().NotContain("new.hire@customer.example");
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

    [Theory(DisplayName = "Task 036: the strategy marks a Dataverse-decided systemuser for write confirmation and carries a truncated set (NFR-03)")]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task WorkforceStrategy_CarriesTheDataverseDecisionAndTheTruncation(bool impersonated, bool matterCapped)
    {
        var project = Guid.NewGuid();
        var resolver = new Mock<IWorkforcePrincipalResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkforcePrincipalResolution.ForSystemUser(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString(), WorkforceTenantId));

        var accessible = new Mock<IAccessibleRecordSetService>();
        accessible.Setup(s => s.ComposeAsync(It.IsAny<WorkforcePrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkforcePrincipal _, string entity, CancellationToken _) => new AccessibleRecordSet
            {
                PrincipalKind = WorkforcePrincipalKind.SystemUser,
                EntityType = entity,
                Rights = entity == "sprk_project" ? RightsOf(project) : RightsOf(),
                Capped = matterCapped && entity == "sprk_matter",
                Sources = new AccessibleRecordSetSources(
                    SystemUserMembership: !impersonated, ContactGrants: false, StandingGrantMembership: false,
                    ImpersonatedRootSet: impersonated),
            });

        var strategy = new WorkforcePrincipalStrategy(
            resolver.Object, accessible.Object, Mock.Of<ILogger<WorkforcePrincipalStrategy>>());

        var result = await strategy.ResolveAsync(
            new DefaultHttpContext { User = Principal(("email", "staff@contoso.com")) }, CancellationToken.None);

        result.IsResolved.Should().BeTrue();
        result.Principal!.DataverseConfirmsWrites.Should().Be(impersonated,
            "only a principal whose own access Dataverse decided has its writes confirmed against Dataverse");
        result.Principal.TruncatedRootEntityTypes.Should().BeEquivalentTo(
            matterCapped ? new[] { "sprk_matter" } : Array.Empty<string>(),
            "a capped set is carried to the responses that list through it, never dropped (NFR-03)");
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

    // Task 035 review F1: the workforce resolver's "derived contact could not be READ" flag must reach the principal. Without
    // it ContactId = Guid.Empty is all a handler sees, and a linked lawyer hit by a Dataverse 429 reads as "not linked".
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WorkforceStrategy_SystemUser_CarriesTheContactUnreadableFlagOntoThePrincipal(bool contactUnreadable)
    {
        var resolver = new Mock<IWorkforcePrincipalResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkforcePrincipalResolution.ForSystemUser(
                Guid.NewGuid(), derivedContactId: null, Guid.NewGuid().ToString(), WorkforceTenantId,
                contactUnreadable: contactUnreadable));

        var accessible = new Mock<IAccessibleRecordSetService>();
        accessible.Setup(s => s.ComposeAsync(
                It.IsAny<WorkforcePrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkforcePrincipal _, string entity, CancellationToken _) => new AccessibleRecordSet
            {
                PrincipalKind = WorkforcePrincipalKind.SystemUser,
                EntityType = entity,
                Rights = RightsOf(),
                Sources = new AccessibleRecordSetSources(true, false, false)
            });
        var strategy = new WorkforcePrincipalStrategy(
            resolver.Object, accessible.Object, Mock.Of<ILogger<WorkforcePrincipalStrategy>>());

        var result = await strategy.ResolveAsync(
            new DefaultHttpContext { User = Principal(("email", "lawyer@contoso.com")) }, CancellationToken.None);

        result.IsResolved.Should().BeTrue();
        result.Principal!.ContactId.Should().Be(Guid.Empty);
        result.Principal.ContactUnreadable.Should().Be(contactUnreadable,
            "an unreadable contact and an absent one are both Guid.Empty; only the flag tells them apart");
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
            Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.NoFilingEntities(),
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
    [InlineData("not-invited", "sdap.access.deny.contact_not_invited")] // task 184
    public async Task CiamStrategy_EachDeny_CarriesItsOwnReasonCode(string scenario, string expectedCode)
    {
        var oid = Guid.NewGuid();
        var store = new InMemoryContactIdentityStore();
        string? presentedCode = null;
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
                // Task 184: invited here, so the repair bind is attempted (E11's intent gate).
                // Task 185: the intent gate needs the invite's code too, so the request presents it in the header.
                var invited = Guid.NewGuid();
                var invite = InviteCode.Create(invited);
                store.AddContact(invited, email: "x@firm.example", invitedOn: IdentityBindingTestKit.Now,
                    inviteCodeHash: invite.StoredValue);
                store.AddContact(Guid.NewGuid(), email: "y@firm.example", keyMirror: oid.ToString("D"));
                presentedCode = invite.Code;
                break;
            case "not-invited":
                // Task 184: an unbound contact with the email, but no grant and no pending invite in this environment.
                store.AddContact(Guid.NewGuid(), email: "x@firm.example");
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
        if (presentedCode is not null)
        {
            http.Request.Headers[CiamContactPrincipalStrategy.InviteCodeHeader] = presentedCode;
        }

        var result = await strategy.ResolveAsync(http, CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        await result.Failure!.ExecuteAsync(http);
        http.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        http.Response.Body.Position = 0;
        var body = await new StreamReader(http.Response.Body).ReadToEndAsync();
        body.Should().Contain(expectedCode);
    }

    // ── Task 185 slice C: the X-Spaarke-Invite-Code header (design §4.1 step 5) ───────────────────────────

    private const string InvitedEmail = "invitee@firm.example";

    private static DefaultHttpContext CiamRequest(Guid oid, string? inviteCode, params string[] extraCodes)
    {
        var http = new DefaultHttpContext
        {
            User = Principal(
                ("iss", $"https://x.ciamlogin.com/{CiamTenantId}/v2.0"),
                ("oid", oid.ToString()),
                ("preferred_username", InvitedEmail)),
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        http.Response.Body = new MemoryStream();
        if (inviteCode is not null)
        {
            http.Request.Headers[CiamContactPrincipalStrategy.InviteCodeHeader] =
                new StringValues(new[] { inviteCode }.Concat(extraCodes).ToArray());
        }

        return http;
    }

    private static async Task<string> DenyBodyAsync(CallerPrincipalResolution result, HttpContext http)
    {
        await result.Failure!.ExecuteAsync(http);
        http.Response.Body.Position = 0;
        return await new StreamReader(http.Response.Body).ReadToEndAsync();
    }

    private static Mock<IAccessibleRecordSetService> EmptyCiamScope(Guid contactId)
    {
        var accessible = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);
        foreach (var entityType in RootsWithAPowerlessEntry.Keys)
        {
            SetupCiamComposition(accessible, contactId, entityType, new Dictionary<Guid, AccessRights>());
        }

        return accessible;
    }

    [Fact]
    public async Task CiamStrategy_TheInviteCodeHeader_ReachesTheBindDecision_AndTheBindConsumesIt()
    {
        var contactId = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var invite = InviteCode.Create(contactId);
        var store = new InMemoryContactIdentityStore();
        store.AddContact(contactId, email: InvitedEmail, invitedOn: IdentityBindingTestKit.Now,
            inviteCodeHash: invite.StoredValue);
        var strategy = new CiamContactPrincipalStrategy(
            IdentityBindingTestKit.Binder(store), EmptyCiamScope(contactId).Object,
            Mock.Of<ILogger<CiamContactPrincipalStrategy>>());

        var result = await strategy.ResolveAsync(CiamRequest(oid, invite.Code), CancellationToken.None);

        result.IsResolved.Should().BeTrue("the header carried the invite's code to E11's marker leg");
        result.Principal!.ContactId.Should().Be(contactId);
        var row = store.Contacts[contactId];
        Guid.Parse(row.Oid!).Should().Be(oid);
        row.InviteCodeHash.Should().BeNull("the bind consumed the code in the same write");
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("malformed")]
    [InlineData("repeated")]
    [InlineData("wrong")]
    public async Task CiamStrategy_WithoutTheInvitesCode_DeniesNotInvited_WithTheOpenTheLinkGuidance(string header)
    {
        var contactId = Guid.NewGuid();
        var invite = InviteCode.Create(contactId);
        var wrong = InviteCode.Create(contactId).Code;
        var store = new InMemoryContactIdentityStore();
        store.AddContact(contactId, email: InvitedEmail, invitedOn: IdentityBindingTestKit.Now,
            inviteCodeHash: invite.StoredValue);
        var http = header switch
        {
            "absent" => CiamRequest(Guid.NewGuid(), null),
            "malformed" => CiamRequest(Guid.NewGuid(), invite.Code + "x"),
            "repeated" => CiamRequest(Guid.NewGuid(), invite.Code, invite.Code),
            _ => CiamRequest(Guid.NewGuid(), wrong),
        };
        var strategy = new CiamContactPrincipalStrategy(
            IdentityBindingTestKit.Binder(store), new Mock<IAccessibleRecordSetService>(MockBehavior.Strict).Object,
            Mock.Of<ILogger<CiamContactPrincipalStrategy>>());

        var result = await strategy.ResolveAsync(http, CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        var body = await DenyBodyAsync(result, http);
        http.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        body.Should().Contain(ContactBindingDecision.DenyContactNotInvited, "the uniform deny reason code is unchanged");
        body.Should().Contain("Open the invitation link from your email in this browser");
        body.Should().NotContain(invite.Code, "the header is never echoed back");
        body.Should().NotContain(wrong);
        body.Should().NotContain(invite.StoredValue);
        store.Contacts[contactId].Oid.Should().BeNull();
        store.Contacts[contactId].InviteCodeHash.Should().Be(invite.StoredValue, "a deny consumes nothing");
    }

    [Fact]
    public async Task CiamStrategy_ABoundCaller_ResolvesByOid_AndTheHeaderIsIgnored()
    {
        var contactId = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var store = new InMemoryContactIdentityStore();
        store.AddContact(contactId, email: InvitedEmail, oid: oid.ToString("D"), plane: IdentityPlaneMarker.External);
        var strategy = new CiamContactPrincipalStrategy(
            IdentityBindingTestKit.Binder(store), EmptyCiamScope(contactId).Object,
            Mock.Of<ILogger<CiamContactPrincipalStrategy>>());

        var result = await strategy.ResolveAsync(
            CiamRequest(oid, InviteCode.Create(Guid.NewGuid()).Code), CancellationToken.None);

        result.IsResolved.Should().BeTrue();
        result.Principal!.ContactId.Should().Be(contactId);
        store.Writes.Should().BeEmpty("a bound caller makes no write, whatever code it carries");
    }

    [Fact]
    public async Task TheInviteCodeHeader_OnAWorkforceToken_IsNeverRead_TheCiamStrategyIsNotReached()
    {
        // The CIAM strategy behind the resolver would bind this contact if it saw the header; on a workforce token it is
        // never selected, so nothing binds and nothing is consumed.
        var contactId = Guid.NewGuid();
        var invite = InviteCode.Create(contactId);
        var store = new InMemoryContactIdentityStore();
        store.AddContact(contactId, email: InvitedEmail, invitedOn: IdentityBindingTestKit.Now,
            inviteCodeHash: invite.StoredValue);
        var ciam = new CiamContactPrincipalStrategy(
            IdentityBindingTestKit.Binder(store), new Mock<IAccessibleRecordSetService>(MockBehavior.Strict).Object,
            Mock.Of<ILogger<CiamContactPrincipalStrategy>>());
        var workforce = new StubStrategy(CallerPrincipalPlane.Workforce);
        var resolver = CreateResolver(ciam, workforce);
        var http = new DefaultHttpContext
        {
            User = Principal(
                ("iss", $"https://login.microsoftonline.com/{WorkforceTenantId}/v2.0"),
                ("tid", WorkforceTenantId), ("oid", Guid.NewGuid().ToString()), ("preferred_username", InvitedEmail)),
        };
        http.Request.Headers[CiamContactPrincipalStrategy.InviteCodeHeader] = invite.Code;

        var result = await resolver.ResolveAsync(http, CancellationToken.None);

        workforce.Called.Should().BeTrue();
        result.Principal!.Plane.Should().Be(CallerPrincipalPlane.Workforce);
        store.Writes.Should().BeEmpty();
        store.Contacts[contactId].InviteCodeHash.Should().Be(invite.StoredValue);
    }

    [Fact]
    public async Task TheInviteCode_AndItsHash_AreAbsentFromEveryLogLine_OnTheDenyAndOnTheBind()
    {
        var contactId = Guid.NewGuid();
        var invite = InviteCode.Create(contactId);
        var wrong = InviteCode.Create(contactId).Code;
        var store = new InMemoryContactIdentityStore();
        store.AddContact(contactId, email: InvitedEmail, invitedOn: IdentityBindingTestKit.Now,
            inviteCodeHash: invite.StoredValue);
        var binderLog = new BindingLogCapture<ContactIdentityBinder>();
        var strategyLog = new BindingLogCapture<CiamContactPrincipalStrategy>();
        var strategy = new CiamContactPrincipalStrategy(
            IdentityBindingTestKit.Binder(store, binderLog), EmptyCiamScope(contactId).Object, strategyLog);

        var denied = await strategy.ResolveAsync(CiamRequest(Guid.NewGuid(), wrong), CancellationToken.None);
        var bound = await strategy.ResolveAsync(CiamRequest(Guid.NewGuid(), invite.Code), CancellationToken.None);

        denied.IsResolved.Should().BeFalse();
        bound.IsResolved.Should().BeTrue();
        var lines = binderLog.Messages.Concat(strategyLog.Messages).ToList();
        lines.Should().NotBeEmpty("the deny and the bind are both logged");
        lines.Should().NotContain(l => l.Contains(invite.Code) || l.Contains(wrong) || l.Contains(invite.StoredValue));
        lines.Should().NotContain(l => l.Contains(CiamContactPrincipalStrategy.InviteCodeHeader));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")] // 43 chars, not base64url
    public void ReadInviteCode_AnythingButOneWellFormedValue_IsNoCode(string? value)
    {
        var http = new DefaultHttpContext();
        if (value is not null)
        {
            http.Request.Headers[CiamContactPrincipalStrategy.InviteCodeHeader] = value;
        }

        CiamContactPrincipalStrategy.ReadInviteCode(http.Request).Should().BeNull();
    }

    [Fact]
    public void ReadInviteCode_OneWellFormedValue_IsTheCode()
    {
        var code = InviteCode.Create(Guid.NewGuid()).Code;
        var http = new DefaultHttpContext();
        http.Request.Headers[CiamContactPrincipalStrategy.InviteCodeHeader] = code;

        CiamContactPrincipalStrategy.ReadInviteCode(http.Request).Should().Be(code);
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
            Mock.Of<ILogger<ExternalParticipationService>>(),
            AccessibleRecordSetTestFactory.NoFilingEntities());

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
