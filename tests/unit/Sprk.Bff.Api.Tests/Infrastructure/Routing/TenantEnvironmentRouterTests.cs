// teams-app-r1 Task 060 (2026-08-03) — BFF `tid`→environment router unit tests.
//
// Binds spec FR-09 acceptance + POML step 4. Proves the deny-by-design contract behaviorally
// (ADR-038: observable behavior, not container-wiring assertions):
//   1. Each of the three deployment models resolves its own tid to its own environment.
//   2. An unmapped tid is DENIED with NO environment attached (never a default).
//   3. An ambiguous mapping (duplicate tid) is DENIED, not best-guessed.
//   4. A missing tid claim, a malformed mapping, and an empty config all DENY.
//   5. Matching is case-insensitive on the tid.
//
// Reference impl: src/server/api/Sprk.Bff.Api/Infrastructure/Routing/TenantEnvironmentRouter.cs

using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Infrastructure.Routing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.Routing;

public class TenantEnvironmentRouterTests
{
    // Distinct tids for the three deployment models.
    private const string DedicatedTid = "11111111-1111-1111-1111-111111111111";
    private const string CustomerTid = "22222222-2222-2222-2222-222222222222";
    private const string SaaSTid = "33333333-3333-3333-3333-333333333333";
    private const string UnmappedTid = "99999999-9999-9999-9999-999999999999";

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static TenantEnvironmentRouter BuildRouter(params TenantEnvironmentMapping[] tenants)
    {
        var options = Options.Create(new TenantEnvironmentRoutingOptions
        {
            Tenants = tenants.ToList()
        });
        return new TenantEnvironmentRouter(options, NullLogger<TenantEnvironmentRouter>.Instance);
    }

    private static ClaimsPrincipal PrincipalWithTid(string? tid)
    {
        var claims = new List<Claim>();
        if (tid is not null)
        {
            claims.Add(new Claim("tid", tid));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static ClaimsPrincipal PrincipalWithLongFormTid(string tid) =>
        new(new ClaimsIdentity(
            new[] { new Claim("http://schemas.microsoft.com/identity/claims/tenantid", tid) },
            "TestAuth"));

    private static TenantEnvironmentMapping Dedicated => new()
    {
        Tid = DedicatedTid,
        DeploymentModel = TenantDeploymentModel.SpaarkeHostedDedicated,
        EnvironmentId = "env-dedicated-acme",
        TenantScoped = false
    };

    private static TenantEnvironmentMapping Customer => new()
    {
        Tid = CustomerTid,
        DeploymentModel = TenantDeploymentModel.CustomerHosted,
        EnvironmentId = "https://customer.crm.dynamics.com",
        TenantScoped = false
    };

    private static TenantEnvironmentMapping SaaS => new()
    {
        Tid = SaaSTid,
        DeploymentModel = TenantDeploymentModel.SaaSShared,
        EnvironmentId = "env-saas-shared",
        TenantScoped = true
    };

    // ---------------------------------------------------------------------------
    // 1. Each deployment model resolves correctly (FR-09 acceptance a/b/c)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Resolve_SpaarkeHostedDedicated_ResolvesToDedicatedEnvironment()
    {
        var router = BuildRouter(Dedicated, Customer, SaaS);

        var result = router.Resolve(PrincipalWithTid(DedicatedTid));

        result.IsResolved.Should().BeTrue();
        result.Environment.Should().NotBeNull();
        result.Environment!.EnvironmentId.Should().Be("env-dedicated-acme");
        result.Environment.DeploymentModel.Should().Be(TenantDeploymentModel.SpaarkeHostedDedicated);
        result.Environment.TenantScoped.Should().BeFalse();
        result.Environment.Tid.Should().Be(DedicatedTid);
        result.DenyReason.Should().BeNull();
    }

    [Fact]
    public void Resolve_CustomerHosted_ResolvesToCustomerOwnEnvironment()
    {
        var router = BuildRouter(Dedicated, Customer, SaaS);

        var result = router.Resolve(PrincipalWithTid(CustomerTid));

        result.IsResolved.Should().BeTrue();
        result.Environment!.EnvironmentId.Should().Be("https://customer.crm.dynamics.com");
        result.Environment.DeploymentModel.Should().Be(TenantDeploymentModel.CustomerHosted);
        result.Environment.TenantScoped.Should().BeFalse();
    }

    [Fact]
    public void Resolve_SaaSShared_ResolvesToSharedEnvironmentWithTenantScoping()
    {
        var router = BuildRouter(Dedicated, Customer, SaaS);

        var result = router.Resolve(PrincipalWithTid(SaaSTid));

        result.IsResolved.Should().BeTrue();
        result.Environment!.EnvironmentId.Should().Be("env-saas-shared");
        result.Environment.DeploymentModel.Should().Be(TenantDeploymentModel.SaaSShared);
        // The shared-SaaS environment MUST carry tenant-scoping context.
        result.Environment.TenantScoped.Should().BeTrue();
        result.Environment.Tid.Should().Be(SaaSTid);
    }

    [Fact]
    public void Resolve_MatchesTidCaseInsensitively()
    {
        var router = BuildRouter(Dedicated);

        var result = router.Resolve(PrincipalWithTid(DedicatedTid.ToUpperInvariant()));

        result.IsResolved.Should().BeTrue();
        result.Environment!.EnvironmentId.Should().Be("env-dedicated-acme");
    }

    [Fact]
    public void Resolve_ReadsLongFormTenantIdClaim()
    {
        var router = BuildRouter(Dedicated);

        var result = router.Resolve(PrincipalWithLongFormTid(DedicatedTid));

        result.IsResolved.Should().BeTrue();
        result.Environment!.EnvironmentId.Should().Be("env-dedicated-acme");
    }

    // ---------------------------------------------------------------------------
    // 2. Unmapped tid → deny, NO environment attached (never a default) — FR-09 acceptance d
    // ---------------------------------------------------------------------------

    [Fact]
    public void Resolve_UnmappedTid_IsDeniedWithNoEnvironment()
    {
        var router = BuildRouter(Dedicated, Customer, SaaS);

        var result = router.Resolve(PrincipalWithTid(UnmappedTid));

        result.IsResolved.Should().BeFalse();
        result.Environment.Should().BeNull("an unmapped tid must NEVER receive an environment");
        result.DenyReason.Should().Be(TenantEnvironmentDenyReason.UnmappedTenant);
        result.DenyCode.Should().Be(TenantEnvironmentRouter.DenyUnmappedTenant);
    }

    [Fact]
    public void Resolve_EmptyConfig_DeniesEveryRequest_NoDefaultEnvironment()
    {
        var router = BuildRouter(/* no tenants configured */);

        var result = router.Resolve(PrincipalWithTid(DedicatedTid));

        result.IsResolved.Should().BeFalse();
        result.Environment.Should().BeNull();
        result.DenyReason.Should().Be(TenantEnvironmentDenyReason.UnmappedTenant);
    }

    // ---------------------------------------------------------------------------
    // 3. Ambiguous mapping (duplicate tid) → deny, not best-guess — POML step 4
    // ---------------------------------------------------------------------------

    [Fact]
    public void Resolve_AmbiguousMapping_IsDeniedNotBestGuessed()
    {
        // Two entries claim the SAME tid but point at DIFFERENT environments.
        var first = new TenantEnvironmentMapping
        {
            Tid = DedicatedTid,
            DeploymentModel = TenantDeploymentModel.SpaarkeHostedDedicated,
            EnvironmentId = "env-A",
            TenantScoped = false
        };
        var second = new TenantEnvironmentMapping
        {
            Tid = DedicatedTid,
            DeploymentModel = TenantDeploymentModel.CustomerHosted,
            EnvironmentId = "env-B",
            TenantScoped = false
        };
        var router = BuildRouter(first, second);

        var result = router.Resolve(PrincipalWithTid(DedicatedTid));

        result.IsResolved.Should().BeFalse();
        result.Environment.Should().BeNull("an ambiguous tid must be denied, never resolved to env-A or env-B");
        result.DenyReason.Should().Be(TenantEnvironmentDenyReason.AmbiguousMapping);
        result.DenyCode.Should().Be(TenantEnvironmentRouter.DenyAmbiguousMapping);
    }

    [Fact]
    public void Resolve_DuplicateTidCaseInsensitive_IsAmbiguousDeny()
    {
        var first = new TenantEnvironmentMapping
        {
            Tid = DedicatedTid.ToLowerInvariant(),
            DeploymentModel = TenantDeploymentModel.SpaarkeHostedDedicated,
            EnvironmentId = "env-A",
            TenantScoped = false
        };
        var second = new TenantEnvironmentMapping
        {
            Tid = DedicatedTid.ToUpperInvariant(),
            DeploymentModel = TenantDeploymentModel.SpaarkeHostedDedicated,
            EnvironmentId = "env-B",
            TenantScoped = false
        };
        var router = BuildRouter(first, second);

        var result = router.Resolve(PrincipalWithTid(DedicatedTid));

        result.IsResolved.Should().BeFalse();
        result.DenyReason.Should().Be(TenantEnvironmentDenyReason.AmbiguousMapping);
    }

    // ---------------------------------------------------------------------------
    // 4. Missing tid claim → deny
    // ---------------------------------------------------------------------------

    [Fact]
    public void Resolve_MissingTidClaim_IsDenied()
    {
        var router = BuildRouter(Dedicated);

        var result = router.Resolve(PrincipalWithTid(null));

        result.IsResolved.Should().BeFalse();
        result.Environment.Should().BeNull();
        result.DenyReason.Should().Be(TenantEnvironmentDenyReason.MissingTenantClaim);
        result.DenyCode.Should().Be(TenantEnvironmentRouter.DenyMissingTenantClaim);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_BlankTidClaim_IsDenied(string tid)
    {
        var router = BuildRouter(Dedicated);

        var result = router.Resolve(PrincipalWithTid(tid));

        result.IsResolved.Should().BeFalse();
        result.DenyReason.Should().Be(TenantEnvironmentDenyReason.MissingTenantClaim);
    }

    // ---------------------------------------------------------------------------
    // 5. Malformed single mapping → deny (never resolve a half-specified env)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Resolve_MappingMissingEnvironmentId_IsMalformedDeny()
    {
        var router = BuildRouter(new TenantEnvironmentMapping
        {
            Tid = DedicatedTid,
            DeploymentModel = TenantDeploymentModel.SpaarkeHostedDedicated,
            EnvironmentId = "   ",
            TenantScoped = false
        });

        var result = router.Resolve(PrincipalWithTid(DedicatedTid));

        result.IsResolved.Should().BeFalse();
        result.Environment.Should().BeNull();
        result.DenyReason.Should().Be(TenantEnvironmentDenyReason.MalformedMapping);
        result.DenyCode.Should().Be(TenantEnvironmentRouter.DenyMalformedMapping);
    }

    [Fact]
    public void Resolve_MappingWithUnspecifiedModel_IsMalformedDeny()
    {
        var router = BuildRouter(new TenantEnvironmentMapping
        {
            Tid = DedicatedTid,
            DeploymentModel = TenantDeploymentModel.Unspecified, // config left the model unset
            EnvironmentId = "env-something",
            TenantScoped = false
        });

        var result = router.Resolve(PrincipalWithTid(DedicatedTid));

        result.IsResolved.Should().BeFalse();
        result.DenyReason.Should().Be(TenantEnvironmentDenyReason.MalformedMapping);
    }

    [Fact]
    public void Resolve_SaaSMappingWithoutTenantScoping_IsMalformedDeny()
    {
        // A shared-SaaS env that forgot to set TenantScoped would read the shared env WITHOUT a
        // tenant partition — a cross-tenant exposure. It must deny, not resolve.
        var router = BuildRouter(new TenantEnvironmentMapping
        {
            Tid = SaaSTid,
            DeploymentModel = TenantDeploymentModel.SaaSShared,
            EnvironmentId = "env-saas-shared",
            TenantScoped = false
        });

        var result = router.Resolve(PrincipalWithTid(SaaSTid));

        result.IsResolved.Should().BeFalse();
        result.DenyReason.Should().Be(TenantEnvironmentDenyReason.MalformedMapping);
    }

    [Fact]
    public void Resolve_DedicatedMappingWithTenantScoping_IsMalformedDeny()
    {
        var router = BuildRouter(new TenantEnvironmentMapping
        {
            Tid = DedicatedTid,
            DeploymentModel = TenantDeploymentModel.SpaarkeHostedDedicated,
            EnvironmentId = "env-dedicated",
            TenantScoped = true // contradictory: a dedicated env is not a shared partitioned env
        });

        var result = router.Resolve(PrincipalWithTid(DedicatedTid));

        result.IsResolved.Should().BeFalse();
        result.DenyReason.Should().Be(TenantEnvironmentDenyReason.MalformedMapping);
    }

    // ---------------------------------------------------------------------------
    // Config hygiene: a blank-tid config entry can never match a real caller
    // ---------------------------------------------------------------------------

    [Fact]
    public void Resolve_BlankTidConfigEntry_DoesNotMatch_RealCallerStillDenied()
    {
        var blank = new TenantEnvironmentMapping
        {
            Tid = "   ",
            DeploymentModel = TenantDeploymentModel.SpaarkeHostedDedicated,
            EnvironmentId = "env-ghost",
            TenantScoped = false
        };
        var router = BuildRouter(blank, Dedicated);

        // The real, mapped tid still resolves...
        router.Resolve(PrincipalWithTid(DedicatedTid)).IsResolved.Should().BeTrue();
        // ...and an unmapped caller is denied (the blank entry is not a catch-all).
        router.Resolve(PrincipalWithTid(UnmappedTid)).IsResolved.Should().BeFalse();
    }

    [Fact]
    public void Resolve_NullPrincipal_Throws()
    {
        var router = BuildRouter(Dedicated);

        var act = () => router.Resolve(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
