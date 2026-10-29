using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Services.Dataverse.Privileges;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 186, review round 1 F2: <c>/me/entitlements</c> never shows what the ceiling forbids.
/// Through the real route on the intake fixture (the module resolver and the intake types are the real ones).
/// </summary>
[Trait("category", "external-access")]
public sealed class WorkforceCeilingEntitlementsTests : IClassFixture<ExternalIntakeEndpointsTests.SwitchOffFixture>
{
    private const string Route = "/api/v1/external/me/entitlements";

    private readonly ExternalIntakeEndpointsTests.SwitchOffFixture _fixture;

    public WorkforceCeilingEntitlementsTests(ExternalIntakeEndpointsTests.SwitchOffFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.WorkforceCeiling.Reset();
    }

    private async Task<MeEntitlementsResponse> Entitlements()
    {
        using var response = await _fixture.CreateAuthenticatedClient().GetAsync(Route);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<MeEntitlementsResponse>())!;
    }

    [Fact]
    public async Task ARoleThatHoldsEveryRoutePrivilege_ShowsTheFrontDoor_AndItsIntakeTypes()
    {
        // Must-not-fire.
        _fixture.Principal = IntakeTestData.WorkforceContact();

        var me = await Entitlements();

        me.Entitlements.Should().Contain(new[] { "legal-front-door", "policy-library" });
        me.IntakeRequestTypes.Should().NotBeEmpty();
        _fixture.WorkforceCeiling.Reads.Should().Be(1);
    }

    [Fact]
    public async Task ARoleWithoutServiceRequestCreate_OffersNoIntakeType_ButKeepsTheTab()
    {
        _fixture.Principal = IntakeTestData.WorkforceContact();
        _fixture.WorkforceCeiling.Ceiling = CeilingRouteTestData.Without(WorkforceRoleCeilingGate.ServiceRequestTable, AccessRights.Create);

        var me = await Entitlements();

        me.Entitlements.Should().Contain("legal-front-door");
        me.IntakeRequestTypes.Should().BeEmpty("the submit route would refuse every type");
    }

    [Fact]
    public async Task ARoleWithoutServiceRequestRead_DropsTheFrontDoor_AndItsIntakeTypes_ButKeepsThePolicyLibrary()
    {
        _fixture.Principal = IntakeTestData.WorkforceContact();
        _fixture.WorkforceCeiling.Ceiling = CeilingRouteTestData.Without(WorkforceRoleCeilingGate.ServiceRequestTable, AccessRights.Read);

        var me = await Entitlements();

        me.Entitlements.Should().NotContain("legal-front-door").And.Contain("policy-library");
        me.IntakeRequestTypes.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnreadableRole_Is200_AndShowsNothingGated()
    {
        _fixture.Principal = IntakeTestData.WorkforceContact();
        _fixture.WorkforceCeiling.Ceiling = WorkforceRoleCeiling.Unavailable("read_failed");

        var me = await Entitlements();

        me.Entitlements.Should().NotContain("legal-front-door");
        me.IntakeRequestTypes.Should().BeEmpty();
    }

    [Fact]
    public async Task ASystemuser_IsNotIntersected_AndTheRoleIsNotRead()
    {
        _fixture.Principal = IntakeTestData.SystemUser();
        _fixture.WorkforceCeiling.Ceiling = WorkforceRoleCeiling.Unavailable("role_missing");

        var me = await Entitlements();

        me.Entitlements.Should().Contain("legal-front-door");
        me.IntakeRequestTypes.Should().NotBeEmpty();
        _fixture.WorkforceCeiling.Reads.Should().Be(0);
    }
}
