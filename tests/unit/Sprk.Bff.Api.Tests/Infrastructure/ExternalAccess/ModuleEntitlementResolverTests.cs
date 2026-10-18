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
//
// spaarke-SPA-external-access-platform-r3 task 011 (spec FR-19 / FR-06, ADR-028 Amendment A8) — default modules by
// server-resolved principal kind, and the per-customer partner switch ExternalAccess:PartnerServiceRequestsEnabled
// (default off). Every case runs with the switch OFF and ON:
//   (5) a workforce systemuser and a workforce contact each get legal-front-door + policy-library with NO map row;
//   (6) map rows still add extras for workforce callers;
//   (7) a CIAM caller gets assigned-work only (switch off) / assigned-work + legal-front-door (switch on);
//   (8) the "service-requests" descriptor: switch off → a CIAM caller's accessible set is empty (today's behaviour);
//       switch on → it is exactly the caller's own contact, so only rows whose sprk_requestedby is that contact survive
//       — a CIAM caller never gets another contact's request rows. Workforce is unchanged by the switch.

using System.Security.Claims;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using EntityReference = Microsoft.Xrm.Sdk.EntityReference;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.DI;
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

    // ── ResolveAsync: default modules by principal kind (r3 task 011, A8) ──────────────────────────
    // (5) Workforce systemuser — legal-front-door + policy-library with NO sprk_approlemodulemap row, either setting.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveAsync_WorkforceSystemUser_NoRolesNoMapRow_GetsDefaultModules(bool partnerSwitch)
    {
        var resolver = new TestResolver(Array.Empty<AppRoleModuleMapping>(), partnerSwitch);

        var result = await resolver.ResolveAsync(WorkforceSystemUser(), UserWithRoles());

        result.Should().BeEquivalentTo(new[] { "legal-front-door", "policy-library" });
        resolver.MapReads.Should().Be(0, "no App-Roles → no extras can apply → the map is not read");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveAsync_WorkforceSystemUser_RoleWithNoMapRow_GetsDefaultModules(bool partnerSwitch)
    {
        // The map is EMPTY: the defaults never depend on a map row.
        var resolver = new TestResolver(Array.Empty<AppRoleModuleMapping>(), partnerSwitch);

        var result = await resolver.ResolveAsync(WorkforceSystemUser(), UserWithRoles("SomeUnmappedRole"));

        result.Should().BeEquivalentTo(new[] { "legal-front-door", "policy-library" });
    }

    // (5) Workforce contact (no systemuser) — the same defaults, and never the partner set.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveAsync_WorkforceContact_GetsDefaultModules_NotAssignedWork(bool partnerSwitch)
    {
        var resolver = new TestResolver(Array.Empty<AppRoleModuleMapping>(), partnerSwitch);

        var result = await resolver.ResolveAsync(WorkforceContact(), UserWithRoles());

        result.Should().BeEquivalentTo(new[] { "legal-front-door", "policy-library" });
        result.Should().NotContain("assigned-work");
    }

    // (6) Map rows still add extras for workforce callers, on top of the defaults.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveAsync_WorkforceCaller_MapRowsAddExtrasOnTopOfDefaults(bool partnerSwitch)
    {
        var map = new[] { new AppRoleModuleMapping { AppRoleName = "AdminUser", ModuleCode = "admin" } };
        var resolver = new TestResolver(map, partnerSwitch);

        var result = await resolver.ResolveAsync(WorkforceSystemUser(), UserWithRoles("AdminUser"));

        result.Should().BeEquivalentTo(new[] { "admin", "legal-front-door", "policy-library" });
        resolver.MapReads.Should().Be(1);
    }

    [Fact]
    public async Task ResolveAsync_WorkforceCaller_MapRowsOverlappingDefaults_AreNotDuplicated()
    {
        // The seeded dev map grants FrontDoorUser exactly the defaults — the union carries each code once.
        var resolver = new TestResolver(SeededMap);

        var result = await resolver.ResolveAsync(WorkforceContact(), UserWithRoles("FrontDoorUser"));

        result.Should().OnlyHaveUniqueItems();
        result.Should().BeEquivalentTo(new[] { "legal-front-door", "policy-library" });
    }

    // R3 task 046, finding (e): a home principal's roles claim was issued for the CUSTOMER directory's app registration — it
    // is not Spaarke's App-Role assignment. It is ignored: exactly the defaults, and the map is never read.
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ResolveAsync_WorkforceHomePrincipal_IgnoresTheRolesClaim_GetsExactlyTheDefaults(bool partnerSwitch, bool systemUser)
    {
        var map = new[] { new AppRoleModuleMapping { AppRoleName = "AdminUser", ModuleCode = "admin" } };
        var resolver = new TestResolver(map, partnerSwitch);
        var home = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("roles", "AdminUser") }, AuthSchemes.WorkforceHome));

        var result = await resolver.ResolveAsync(systemUser ? WorkforceSystemUser() : WorkforceContact(), home);

        result.Should().BeEquivalentTo(new[] { "legal-front-door", "policy-library" });
        result.Should().NotContain("admin", "a customer directory's role never maps to a Spaarke module");
        resolver.MapReads.Should().Be(0, "the roles claim of a home principal is never read, so neither is the map");
    }

    // (7) CIAM — assigned-work only with the switch off (today's behaviour), never reading the map.
    [Fact]
    public async Task ResolveAsync_Ciam_SwitchOff_AssignedWorkOnly_AndNeverReadsTheMap()
    {
        var resolver = new TestResolver(SeededMap, partnerSwitch: false);

        // A role claim on a CIAM token must not leak workforce modules in.
        var result = await resolver.ResolveAsync(Ciam(), UserWithRoles("FrontDoorUser"));

        result.Should().BeEquivalentTo(new[] { "assigned-work" });
        resolver.MapReads.Should().Be(0, "the CIAM term never reads the App-Role map");
    }

    // (7) CIAM — assigned-work + legal-front-door with the switch on; never policy-library.
    [Fact]
    public async Task ResolveAsync_Ciam_SwitchOn_AssignedWorkPlusLegalFrontDoor_NotPolicyLibrary()
    {
        var resolver = new TestResolver(SeededMap, partnerSwitch: true);

        var result = await resolver.ResolveAsync(Ciam(), UserWithRoles("FrontDoorUser"));

        result.Should().BeEquivalentTo(new[] { "assigned-work", "legal-front-door" });
        result.Should().NotContain("policy-library");
        resolver.MapReads.Should().Be(0, "the CIAM term never reads the App-Role map");
    }

    // ── (9) IsEntitledAsync — the one Tier-1 check a module-gated route asks (r3 task 035; review 011 K2) ──────
    // It answers from the SAME set ResolveAsync returns (/me/entitlements), so the route cannot drift from /me.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task IsEntitledAsync_LegalFrontDoor_Ciam_FollowsThePartnerSwitch(bool partnerSwitch, bool expected)
    {
        var resolver = new TestResolver(SeededMap, partnerSwitch);
        var caller = Ciam();

        var entitled = await resolver.IsEntitledAsync(caller, UserWithRoles(), ModuleEntitlementResolver.LegalFrontDoorModule);

        entitled.Should().Be(expected);
        entitled.Should().Be((await resolver.ResolveAsync(caller, UserWithRoles())).Contains("legal-front-door"),
            "the check is the /me set, not a second rule");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IsEntitledAsync_LegalFrontDoor_EveryWorkforceCaller_IsEntitled_WithNoMapRow(bool partnerSwitch)
    {
        var resolver = new TestResolver(Array.Empty<AppRoleModuleMapping>(), partnerSwitch);

        (await resolver.IsEntitledAsync(WorkforceSystemUser(), UserWithRoles(), "legal-front-door")).Should().BeTrue();
        (await resolver.IsEntitledAsync(WorkforceContact(), UserWithRoles(), "LEGAL-FRONT-DOOR")).Should().BeTrue(
            "module codes compare case-insensitively, as the resolver's own union does");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("assigned-work")]
    public async Task IsEntitledAsync_BlankOrUngrantedModule_IsFalse(string moduleCode)
    {
        var resolver = new TestResolver(SeededMap);

        (await resolver.IsEntitledAsync(WorkforceContact(), UserWithRoles("FrontDoorUser"), moduleCode)).Should().BeFalse();
    }

    // ── (10) intake TYPE availability (owner decision 2026-10-12): Invention Submission is workforce-only, and a CIAM
    //        partner gets it only behind ExternalAccess:PartnerInventionIntakeEnabled (default off). ───────────────────
    private const int Nda = 100000000;
    private const int Invention = 100000001;
    private const int Policy = 100000002;
    private const int Trademark = 100000003;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void IsIntakeTypeAvailable_Workforce_EveryType_WhateverTheSwitches(bool partnerSwitch, bool inventionSwitch)
    {
        var resolver = new TestResolver(SeededMap, partnerSwitch, inventionSwitch);

        foreach (var type in new[] { Nda, Invention, Policy, Trademark })
        {
            resolver.IsIntakeTypeAvailable(WorkforceSystemUser(), type).Should().BeTrue();
            resolver.IsIntakeTypeAvailable(WorkforceContact(), type).Should().BeTrue();
        }
    }

    [Fact]
    public void IsIntakeTypeAvailable_Ciam_InventionSwitchOff_RefusesOnlyInvention()
    {
        var resolver = new TestResolver(SeededMap, partnerSwitch: true, inventionSwitch: false);

        resolver.IsIntakeTypeAvailable(Ciam(), Invention).Should().BeFalse();
        resolver.IsIntakeTypeAvailable(Ciam(), Nda).Should().BeTrue();
        resolver.IsIntakeTypeAvailable(Ciam(), Policy).Should().BeTrue();
        resolver.IsIntakeTypeAvailable(Ciam(), Trademark).Should().BeTrue();
    }

    [Fact]
    public void IsIntakeTypeAvailable_Ciam_InventionSwitchOn_AdmitsInvention()
    {
        var resolver = new TestResolver(SeededMap, partnerSwitch: true, inventionSwitch: true);

        resolver.IsIntakeTypeAvailable(Ciam(), Invention).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100000004)]
    [InlineData(-1)]
    public void IsIntakeTypeAvailable_UnknownType_IsNeverAvailable(int type)
    {
        var resolver = new TestResolver(SeededMap, partnerSwitch: true, inventionSwitch: true);

        resolver.IsIntakeTypeAvailable(WorkforceContact(), type).Should().BeFalse();
        resolver.IsIntakeTypeAvailable(Ciam(), type).Should().BeFalse();
    }

    [Fact]
    public void IntakeRequestTypesFor_ReportsExactlyWhatIsIntakeTypeAvailableAllows()
    {
        new TestResolver(SeededMap, true, false).IntakeRequestTypesFor(Ciam())
            .Should().Equal(Nda, Policy, Trademark);
        new TestResolver(SeededMap, true, true).IntakeRequestTypesFor(Ciam())
            .Should().Equal(Nda, Invention, Policy, Trademark);
        new TestResolver(SeededMap, false, false).IntakeRequestTypesFor(WorkforceContact())
            .Should().Equal(Nda, Invention, Policy, Trademark);
    }

    [Fact]
    public void PartnerInventionIntakeEnabled_DefaultsOff()
    {
        new ExternalAccessOptions().PartnerInventionIntakeEnabled.Should().BeFalse();
    }

    // ── (8) the "service-requests" descriptor predicate (FR-06 partner switch) ──────────────────────
    [Fact]
    public void ServiceRequests_CiamCaller_SwitchOff_AccessibleSetEmpty_AndNoRowSurvives()
    {
        var module = ExternalAccessModule.ServiceRequestsModule(partnerServiceRequestsEnabled: false);
        var caller = Ciam();

        module.AccessibleRecordIds!(caller).Should().BeEmpty();
        module.ScopeRows(caller, new[] { RequestRow(caller.ContactId), RequestRow(Guid.NewGuid()) })
            .Should().BeEmpty("with the switch off a CIAM caller sees 0 service-request rows, even its own");
        module.IsRecordAccessible(caller, caller.ContactId).Should().BeFalse();
    }

    [Fact]
    public void ServiceRequests_CiamCaller_SwitchOn_AccessibleSetIsExactlyTheCallersContact()
    {
        var module = ExternalAccessModule.ServiceRequestsModule(partnerServiceRequestsEnabled: true);
        var caller = Ciam();

        module.AccessibleRecordIds!(caller).Should().BeEquivalentTo(new[] { caller.ContactId });
    }

    [Fact]
    public void ServiceRequests_CiamCaller_SwitchOn_KeepsOnlyRowsRequestedByTheCaller()
    {
        var module = ExternalAccessModule.ServiceRequestsModule(partnerServiceRequestsEnabled: true);
        var caller = Ciam();
        var otherContact = Guid.NewGuid();
        var own = RequestRow(caller.ContactId);

        var scoped = module.ScopeRows(caller, new[] { own, RequestRow(otherContact), RequestRow(null) });

        scoped.Should().ContainSingle().Which.Should().BeSameAs(own);
        module.IsRecordAccessible(caller, otherContact).Should().BeFalse("a CIAM caller never gets another contact's requests");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void ServiceRequests_WorkforceCaller_IsUnchangedByTheSwitch_OwnRequestsOnly(bool partnerSwitch, bool systemUser)
    {
        var module = ExternalAccessModule.ServiceRequestsModule(partnerSwitch);
        var caller = systemUser ? WorkforceSystemUser() : WorkforceContact();
        var own = RequestRow(caller.ContactId);

        module.AccessibleRecordIds!(caller).Should().BeEquivalentTo(new[] { caller.ContactId });
        module.ScopeRows(caller, new[] { own, RequestRow(Guid.NewGuid()) }).Should().ContainSingle().Which.Should().BeSameAs(own);
    }

    [Theory]
    [InlineData(false, CallerPrincipalPlane.CiamContact)]
    [InlineData(true, CallerPrincipalPlane.CiamContact)]
    [InlineData(false, CallerPrincipalPlane.Workforce)]
    [InlineData(true, CallerPrincipalPlane.Workforce)]
    public void ServiceRequests_CallerWithNoContact_AccessibleSetEmpty(bool partnerSwitch, CallerPrincipalPlane plane)
    {
        // A systemuser with no linked contact (ContactId = Guid.Empty) — never matches a null/empty sprk_requestedby.
        var module = ExternalAccessModule.ServiceRequestsModule(partnerSwitch);
        var caller = new CallerPrincipal
        {
            Plane = plane,
            ContactId = Guid.Empty,
            SystemUserId = plane == CallerPrincipalPlane.Workforce ? Guid.NewGuid() : null,
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
        };

        module.AccessibleRecordIds!(caller).Should().BeEmpty();
    }

    // ── the switch is BOUND from ExternalAccess:PartnerServiceRequestsEnabled (default off) ─────────
    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void PartnerSwitch_IsBoundFromTheExternalAccessSection_AndDrivesTheRegisteredDescriptor(
        string? configured, bool expectedOn)
    {
        var settings = new Dictionary<string, string?>
        {
            // A sibling key already in the section must not disturb the bind.
            ["ExternalAccess:PortalUrl"] = "https://portal.example.test",
        };
        if (configured is not null)
        {
            settings["ExternalAccess:PartnerServiceRequestsEnabled"] = configured;
        }
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddExternalAccess();

        using var provider = services.BuildServiceProvider();
        var caller = Ciam();

        provider.GetRequiredService<IOptions<ExternalAccessOptions>>().Value.PartnerServiceRequestsEnabled
            .Should().Be(expectedOn);
        var module = provider.GetRequiredService<ExternalModuleRegistry>().FindByName("service-requests");
        module.Should().NotBeNull("the factory-registered descriptor must still reach the registry");
        module!.AccessibleRecordIds!(caller).Contains(caller.ContactId).Should().Be(expectedOn);
    }

    // A blank or non-boolean switch value fails closed at startup (ValidateOnStart); an operator turns the switch
    // off by REMOVING the setting, not by blanking it.
    [Theory]
    [InlineData("")]
    [InlineData("yes")]
    public void PartnerSwitch_BlankOrNonBooleanValue_FailsStartup(string configured)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ExternalAccess:PartnerServiceRequestsEnabled"] = configured,
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddExternalAccess();

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<ExternalAccessOptions>>().Value;

        act.Should().Throw<Exception>();
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

    /// <summary>A licensed workforce caller: a systemuser with its linked contact (task 141 link).</summary>
    private static CallerPrincipal WorkforceSystemUser() => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = Guid.NewGuid(),
        SystemUserId = Guid.NewGuid(),
        Email = "staff@contoso.com",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
    };

    /// <summary>A licence-free workforce contact: contact only, no systemuser.</summary>
    private static CallerPrincipal WorkforceContact() => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = Guid.NewGuid(),
        SystemUserId = null,
        Email = "staff@contoso.com",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
    };

    /// <summary>A projected sprk_servicerequest row; sprk_requestedby carries the SDK's EntityReference shape.</summary>
    private static IReadOnlyDictionary<string, object?> RequestRow(Guid? requestedBy) => new Dictionary<string, object?>
    {
        ["@logicalName"] = "sprk_servicerequest",
        ["sprk_servicerequestid"] = Guid.NewGuid(),
        ["sprk_requestedby"] = requestedBy is { } id ? new EntityReference("contact", id) : null,
    };

    /// <summary>Resolver whose Dataverse read is a fixture — exercises the plane branch + resolution
    /// without HTTP. GetActiveMapAsync is overridden; the base ctor deps are inert on the tested paths.</summary>
    private sealed class TestResolver : ModuleEntitlementResolver
    {
        private readonly IReadOnlyList<AppRoleModuleMapping> _map;
        public int MapReads { get; private set; }

        public TestResolver(IReadOnlyList<AppRoleModuleMapping> map, bool partnerSwitch = false, bool inventionSwitch = false)
            : base(new HttpClient(),
                   new InMemoryTenantCache(),
                   new ConfigurationBuilder().Build(),
                   new FakeCredential(),
                   new HttpContextAccessor(),
                   Options.Create(new ExternalAccessOptions
                   {
                       PartnerServiceRequestsEnabled = partnerSwitch,
                       PartnerInventionIntakeEnabled = inventionSwitch,
                   }),
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
