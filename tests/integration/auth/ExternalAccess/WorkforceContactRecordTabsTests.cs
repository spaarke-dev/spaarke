using System.Net.Http.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.Privileges;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;
using Xunit;
using AccessRights = Spaarke.Dataverse.AccessRights;

namespace Sprk.Bff.Api.Tests.Integration.Auth.ExternalAccess;

/// <summary>
/// spaarke-SPA-external-access-platform-r4 task 040 (FR-24, #2508): <c>GET /api/v1/external/me/entitlements</c> reports the
/// record-tab code <c>assigned-work</c> to a workforce CONTACT that can read at least one record, and to nobody else on the
/// workforce plane. The external SPA offers the Projects, Matters, Work Assignments, Documents and Invoices tabs only with
/// that code (owner 2026-11-09: a contact with no grants sees none of them). A CIAM partner's answer is unchanged.
/// </summary>
/// <remarks>
/// Through the REAL <c>/api/v1/external</c> group, route, handler, <see cref="ModuleEntitlementResolver"/> rule and
/// Workforce-role ceiling intersection. Doubles at module boundaries only, as in <see cref="ExternalIntakeEndpointsTests"/>:
/// the caller principal (the plane strategies compose it; here it is given), the resolver's App-Role map read (empty), and
/// the Workforce-role read (<see cref="WorkforceCeilingDouble"/>). This decides only whether the tabs are offered: every
/// list behind them stays Tier-2-scoped by the module fetch.
/// </remarks>
[Trait("category", "external-access")]
public sealed class WorkforceContactRecordTabsTests : IClassFixture<WorkforceContactRecordTabsTests.RecordTabsFixture>
{
    private const string Route = "/api/v1/external/me/entitlements";
    private const string RecordTabs = ModuleEntitlementResolver.AssignedWorkModule;
    private readonly RecordTabsFixture _fixture;

    public WorkforceContactRecordTabsTests(RecordTabsFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.WorkforceCeiling.Reset();
    }

    public static TheoryData<string> GrantedRootTypes => new() { "project", "matter", "work-assignment" };

    // ── must fire ────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(GrantedRootTypes))]
    public async Task MeEntitlements_WorkforceContactWithOneReadableRecord_ReportsTheRecordTabs(string rootType)
    {
        _fixture.Principal = WorkforceContact(rootType, AccessRights.Read);

        var me = await GetEntitlementsAsync();

        me.Plane.Should().Be("workforce");
        me.Entitlements.Should().Contain(RecordTabs);
        me.Entitlements.Should().Contain(new[] { "legal-front-door", "policy-library" }, "the workforce defaults stay");
    }

    // ── must not fire ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MeEntitlements_WorkforceContactWithNoGrants_DoesNotReportTheRecordTabs()
    {
        _fixture.Principal = WorkforceContact(rootType: null, AccessRights.None);

        var me = await GetEntitlementsAsync();

        me.Entitlements.Should().BeEquivalentTo(new[] { "legal-front-door", "policy-library" });
    }

    [Theory]
    [MemberData(nameof(GrantedRootTypes))]
    public async Task MeEntitlements_LicensedSystemUserWithReadableRecords_DoesNotReportTheRecordTabs(string rootType)
    {
        // A licensed user keeps its own model (#2508): its tabs are exactly what they were, whatever it can read.
        var principal = WorkforceContact(rootType, AccessRights.Read | AccessRights.Write);
        _fixture.Principal = new CallerPrincipal
        {
            Plane = principal.Plane,
            ContactId = principal.ContactId,
            SystemUserId = Guid.NewGuid(),
            Email = principal.Email,
            ProjectAccess = principal.ProjectAccess,
            MatterAccess = principal.MatterAccess,
            WorkAssignmentAccess = principal.WorkAssignmentAccess,
        };

        var me = await GetEntitlementsAsync();

        me.Entitlements.Should().BeEquivalentTo(new[] { "legal-front-door", "policy-library" });
    }

    [Fact]
    public async Task MeEntitlements_WorkforceRoleCannotReadWorkAssignments_DropsTheRecordTabs()
    {
        // The Workforce-role ceiling (unified-access-control-r2 task 186) still decides: a role without Read on the table the
        // code maps to shows no record tab, even to a granted contact.
        _fixture.Principal = WorkforceContact("project", AccessRights.Read);
        _fixture.WorkforceCeiling.Ceiling = WorkforceRoleCeiling.Available(
            WorkforceCeilingDouble.OwnerPrerequisite
                .Where(kvp => kvp.Key != "sprk_workassignment")
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value));

        var me = await GetEntitlementsAsync();

        me.Entitlements.Should().NotContain(RecordTabs);
    }

    // ── CIAM unchanged ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MeEntitlements_CiamPartner_WithOrWithoutGrants_IsUnchanged(bool granted)
    {
        var contact = WorkforceContact(granted ? "matter" : null, AccessRights.Read);
        _fixture.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = contact.ContactId,
            Email = "partner@outside.test",
            ProjectAccess = contact.ProjectAccess,
            MatterAccess = contact.MatterAccess,
        };

        var me = await GetEntitlementsAsync();

        me.Plane.Should().Be("ciam");
        me.Entitlements.Should().Equal(RecordTabs);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private async Task<MeEntitlementsResponse> GetEntitlementsAsync()
    {
        using var client = _fixture.CreateAuthenticatedClient();
        var me = await client.GetFromJsonAsync<MeEntitlementsResponse>(Route);
        me.Should().NotBeNull();
        return me!;
    }

    /// <summary>
    /// A workforce contact (no systemuser) holding <paramref name="rights"/> on one root of <paramref name="rootType"/>
    /// (<c>project</c>, <c>matter</c>, <c>work-assignment</c>), or on nothing when it is null.
    /// </summary>
    private static CallerPrincipal WorkforceContact(string? rootType, AccessRights rights)
    {
        var id = Guid.NewGuid();
        var none = new Dictionary<Guid, AccessRights>();
        return new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce,
            ContactId = Guid.NewGuid(),
            SystemUserId = null,
            Email = "staff@customer.test",
            ProjectAccess = rootType == "project"
                ? new[] { new CallerProjectAccess { ProjectId = id, Rights = rights } }
                : Array.Empty<CallerProjectAccess>(),
            MatterAccess = rootType == "matter" ? new Dictionary<Guid, AccessRights> { [id] = rights } : none,
            WorkAssignmentAccess = rootType == "work-assignment" ? new Dictionary<Guid, AccessRights> { [id] = rights } : none,
        };
    }

    /// <summary>The intake host (partner switch off): the stub principal and the real resolver with an empty map.</summary>
    public sealed class RecordTabsFixture : IntakeFixture
    {
        public RecordTabsFixture() : base(partnerServiceRequestsEnabled: false) { }
    }
}
