// spaarke-SPA-external-access-platform-r2 task 072 — ModuleEntitlementResolver tests (owner Option B).
//
// Locks the Tier-1 module-entitlement contract the external-spa widget registry gates tabs on
// (/api/v1/external/me/entitlements). Acceptance criteria (spec FR-07/FR-08/FR-09, NFR-06):
//   (1) Internal caller with the FrontDoorUser App-Role gets EXACTLY the module codes its
//       sprk_approlemodulemap rows grant.
//   (2) FR-08: adding a new active mapping row changes a role's entitlements with NO code change —
//       proven by running the SAME resolution logic against an augmented map.
//   (3) External (CIAM) callers are BLANKET-entitled to the outside-counsel set with no per-Contact
//       row, and never read the App-Role map.
//   (4) NEGATIVE (NFR-06): an internal caller whose App-Roles map to nothing gets NO internal modules —
//       the resolver never over-reports.
//
// The Dataverse read (GetActiveMapAsync) is overridden in a test subclass so the map is a fixture — the
// resolution BEHAVIOR is tested, not the HTTP plumbing (ADR-038: no Mock<HttpMessageHandler>).

using System.Security.Claims;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

public class ModuleEntitlementResolverTests
{
    // The seeded map (verified live in SPAARKE DEV 1): FrontDoorUser → {legal-front-door, policy-library}.
    private static readonly IReadOnlyList<AppRoleModuleMapping> SeededMap = new[]
    {
        new AppRoleModuleMapping { AppRoleName = "FrontDoorUser", ModuleCode = "legal-front-door" },
        new AppRoleModuleMapping { AppRoleName = "FrontDoorUser", ModuleCode = "policy-library" },
    };

    // ── (1) internal caller → exactly its mapped codes ─────────────────────────────────────────────
    [Fact]
    public void ResolveWorkforceEntitlements_FrontDoorUser_ReturnsExactlyMappedCodes()
    {
        var result = ModuleEntitlementResolver.ResolveWorkforceEntitlements(new[] { "FrontDoorUser" }, SeededMap);

        result.Should().BeEquivalentTo(new[] { "legal-front-door", "policy-library" });
    }

    [Fact]
    public void ResolveWorkforceEntitlements_MatchesRoleNameCaseInsensitively()
    {
        var result = ModuleEntitlementResolver.ResolveWorkforceEntitlements(new[] { "frontdooruser" }, SeededMap);

        result.Should().BeEquivalentTo(new[] { "legal-front-door", "policy-library" });
    }

    [Fact]
    public void ResolveWorkforceEntitlements_DeDuplicatesModuleCodesAcrossRoles()
    {
        // Two different roles both granting policy-library → the code appears once.
        var map = SeededMap.Append(new AppRoleModuleMapping { AppRoleName = "PolicyReader", ModuleCode = "policy-library" }).ToList();

        var result = ModuleEntitlementResolver.ResolveWorkforceEntitlements(new[] { "FrontDoorUser", "PolicyReader" }, map);

        result.Should().OnlyHaveUniqueItems();
        result.Should().BeEquivalentTo(new[] { "legal-front-door", "policy-library" });
    }

    // ── (2) FR-08: a new data row changes entitlements with NO code change ──────────────────────────
    [Fact]
    public void ResolveWorkforceEntitlements_AddingAnActiveMapRow_GrantsTheNewModule_NoCodeChange()
    {
        // BEFORE: the seeded map → 2 codes.
        var before = ModuleEntitlementResolver.ResolveWorkforceEntitlements(new[] { "FrontDoorUser" }, SeededMap);
        before.Should().NotContain("admin");

        // AFTER: the operator adds ONE data row (FrontDoorUser → admin). Same resolution logic, no code change.
        var augmented = SeededMap.Append(new AppRoleModuleMapping { AppRoleName = "FrontDoorUser", ModuleCode = "admin" }).ToList();
        var after = ModuleEntitlementResolver.ResolveWorkforceEntitlements(new[] { "FrontDoorUser" }, augmented);

        after.Should().Contain("admin");
        after.Should().BeEquivalentTo(new[] { "legal-front-door", "policy-library", "admin" });
    }

    // ── (4) negative: unmapped role → no internal modules (never over-report) ──────────────────────
    [Fact]
    public void ResolveWorkforceEntitlements_UnmappedRole_ReturnsEmpty()
    {
        var result = ModuleEntitlementResolver.ResolveWorkforceEntitlements(new[] { "SomeUnmappedRole" }, SeededMap);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ResolveWorkforceEntitlements_NoRoles_ReturnsEmpty()
    {
        var result = ModuleEntitlementResolver.ResolveWorkforceEntitlements(Array.Empty<string>(), SeededMap);

        result.Should().BeEmpty();
    }

    // ── App-Role claim extraction (both `roles` and mapped ClaimTypes.Role) ────────────────────────
    [Fact]
    public void ExtractAppRoles_ReadsRolesClaim_AndDeDuplicates()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("roles", "FrontDoorUser"),
            new Claim("roles", "AdminUser"),
            new Claim(ClaimTypes.Role, "FrontDoorUser"), // duplicate via the mapped type — de-duplicated
        }, "test"));

        var roles = ModuleEntitlementResolver.ExtractAppRoles(user);

        roles.Should().BeEquivalentTo(new[] { "FrontDoorUser", "AdminUser" });
    }

    [Fact]
    public void ExtractAppRoles_NullOrNoRoles_ReturnsEmpty()
    {
        ModuleEntitlementResolver.ExtractAppRoles(null).Should().BeEmpty();
        ModuleEntitlementResolver.ExtractAppRoles(new ClaimsPrincipal(new ClaimsIdentity())).Should().BeEmpty();
    }

    // ── (3) CIAM blanket set ───────────────────────────────────────────────────────────────────────
    [Fact]
    public void OutsideCounselEntitlements_IsTheBlanketOutsideCounselSet()
    {
        ModuleEntitlementResolver.OutsideCounselEntitlements.Should().BeEquivalentTo(new[] { "assigned-work" });
    }

    // ── ResolveAsync plane branch (end-to-end over a fixture map) ──────────────────────────────────
    [Fact]
    public async Task ResolveAsync_CiamPlane_ReturnsBlanketSet_AndNeverReadsTheMap()
    {
        var resolver = new TestResolver(SeededMap);

        var result = await resolver.ResolveAsync(Ciam(), UserWithRoles("FrontDoorUser"));

        result.Should().BeEquivalentTo(new[] { "assigned-work" });
        resolver.MapReads.Should().Be(0, "the CIAM term is blanket — it must not read the App-Role map");
    }

    [Fact]
    public async Task ResolveAsync_WorkforcePlane_FrontDoorUser_ReturnsMappedCodes()
    {
        var resolver = new TestResolver(SeededMap);

        var result = await resolver.ResolveAsync(Workforce(), UserWithRoles("FrontDoorUser"));

        result.Should().BeEquivalentTo(new[] { "legal-front-door", "policy-library" });
        resolver.MapReads.Should().Be(1);
    }

    [Fact]
    public async Task ResolveAsync_WorkforcePlane_NoRoles_ReturnsEmpty_AndNeverReadsTheMap()
    {
        var resolver = new TestResolver(SeededMap);

        var result = await resolver.ResolveAsync(Workforce(), UserWithRoles());

        result.Should().BeEmpty();
        resolver.MapReads.Should().Be(0, "no App-Roles → no internal modules → no need to read the map");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────
    private static ClaimsPrincipal UserWithRoles(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim("roles", r)), "test"));

    private static CallerPrincipal Ciam() => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = Guid.NewGuid(),
        Email = "external@test.com",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
    };

    private static CallerPrincipal Workforce() => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = Guid.NewGuid(),
        Email = "staff@contoso.com",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
    };

    /// <summary>Resolver whose Dataverse read is a fixture — exercises the plane branch + resolution
    /// without HTTP. GetActiveMapAsync is overridden; the base ctor deps are inert on the tested paths.</summary>
    private sealed class TestResolver : ModuleEntitlementResolver
    {
        private readonly IReadOnlyList<AppRoleModuleMapping> _map;
        public int MapReads { get; private set; }

        public TestResolver(IReadOnlyList<AppRoleModuleMapping> map)
            : base(new HttpClient(),
                   new InMemoryTenantCache(),
                   new ConfigurationBuilder().Build(),
                   new FakeCredential(),
                   new HttpContextAccessor(),
                   NullLogger<ModuleEntitlementResolver>.Instance)
        {
            _map = map;
        }

        public override Task<IReadOnlyList<AppRoleModuleMapping>> GetActiveMapAsync(CancellationToken ct = default)
        {
            MapReads++;
            return Task.FromResult(_map);
        }
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("test-token", DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(new AccessToken("test-token", DateTimeOffset.MaxValue));
    }
}
