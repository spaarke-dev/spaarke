using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Api.Filters;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.SpeAdmin;

/// <summary>
/// unified-access-control-r2 task 165 — two tenant-wide reads confined to the caller's reach:
/// <list type="bullet">
///   <item><b>Security alerts and secure score are platform-operator-only</b> (owner round 35 item 4). Both call the Graph
///   Security API app-only through a config's owning app, and Graph answers for the WHOLE Microsoft 365 tenant — in a
///   shared Model 1 tenant, every customer's. The configId cannot narrow that, so only an admin whose own business unit is
///   the root may call them, through the same check as the environment write rule; anyone else gets ONE 403 before
///   anything is read, whatever config they name. Owner round 49 item 1 adds the deployment marker: this host is a
///   Spaarke-operated environment (<see cref="AdminSurfaceHostFixture"/>); the customer-environment and unmarked hosts are
///   proven in <c>SpeAdminOperatorEnvironmentMarkerTests</c>.</item>
///   <item><b>The business-unit list is projected onto the caller's reach</b> (follow-up f2, verifier item 7). It returned
///   every unit in the environment, so a leaf admin read every other customer's unit name; it now holds the caller's own
///   unit and its descendants — exactly the units the caller may assign a config to.</item>
/// </list>
/// Root → {Unit A → {Unit A-sub}, Unit B}. Real host, fake Dataverse, WireMock Graph. ADR-038 §2 path #1.
/// </summary>
public sealed class SpeAdminOperatorRouteAndBusinessUnitScopeTests : IClassFixture<AdminSurfaceHostFixture>
{
    private const string ConfigSet = "sprk_specontainertypeconfigs";
    private const string OperatorCode = "spe.admin.deny.platform_operator_required";
    private const string UnverifiableCode = "spe.admin.deny.scope_unverifiable";

    private static readonly Guid Root = Guid.Parse("10000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitA = Guid.Parse("1a000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitASub = Guid.Parse("1a100000-0000-0000-0000-000000000000");
    private static readonly Guid UnitB = Guid.Parse("1b000000-0000-0000-0000-000000000000");
    private static readonly Guid ConfigA = Guid.Parse("ca000000-0000-0000-0000-00000000000a");

    private readonly AdminSurfaceHostFixture _fixture;

    public SpeAdminOperatorRouteAndBusinessUnitScopeTests(AdminSurfaceHostFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Security alerts / secure score — platform operators only (round 35 item 4)
    // ─────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, string> SecurityRoutesAndConfigs() => new()
    {
        { "/api/spe/security/alerts", "ca000000-0000-0000-0000-00000000000a" },   // the leaf admin's OWN config
        { "/api/spe/security/score", "ca000000-0000-0000-0000-00000000000a" },
        { "/api/spe/security/alerts", "cf000000-0000-0000-0000-0000000000ff" },   // a config that does not exist
        { "/api/spe/security/score", "cf000000-0000-0000-0000-0000000000ff" },
    };

    [Theory]
    [MemberData(nameof(SecurityRoutesAndConfigs))]
    public async Task ALeafAdmin_OnTheSecurityRoutes_GetsOne403_WhateverConfigItNames_AndNothingIsRead(string route, string configId)
    {
        Seed(callerUnit: UnitA);
        using var client = Admin();

        var problem = await Problem(await client.GetAsync($"{route}?configId={configId}"), HttpStatusCode.Forbidden);

        problem["reasonCode"].GetString().Should().Be(OperatorCode,
            "the same 403 for its own config and a nonexistent one — no config oracle");
        _fixture.Dataverse.CallsOn(ConfigSet).Should().BeEmpty("refused before the config is even looked at");
        _fixture.Graph.AllRequests.Should().BeEmpty("the tenant-wide Security API was never called");
    }

    [Theory]
    [InlineData("/api/spe/security/alerts")]
    [InlineData("/api/spe/security/score")]
    public async Task ACallerWithNoResolvableBusinessUnit_GetsTheSame403(string route)
    {
        Seed(callerUnit: null);
        using var client = Admin();

        var problem = await Problem(await client.GetAsync($"{route}?configId={ConfigA}"), HttpStatusCode.Forbidden);

        problem["reasonCode"].GetString().Should().Be(OperatorCode);
    }

    [Theory]
    [InlineData("/api/spe/security/alerts")]
    [InlineData("/api/spe/security/score")]
    public async Task ARootAdmin_ReachesTheSecurityHandlers(string route)
    {
        Seed(callerUnit: Root);
        using var client = Admin();

        var response = await client.GetAsync($"{route}?configId={ConfigA}");

        (await response.Content.ReadAsStringAsync()).Should().NotContain(OperatorCode);
        _fixture.Dataverse.CallsOn(ConfigSet, "Retrieve").Should().Contain(c => c.Id == ConfigA,
            "the handler ran and resolved the config");
    }

    [Theory]
    [InlineData("/api/spe/security/alerts")]
    [InlineData("/api/spe/security/score")]
    public async Task WhenTheCallersScopeCannotBeRead_TheSecurityRoutesAre503_AndNothingIsRead(string route)
    {
        Seed(callerUnit: Root);
        _fixture.Dataverse.FaultQueriesOn("businessunits");
        using var client = Admin();

        var problem = await Problem(await client.GetAsync($"{route}?configId={ConfigA}"), HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be(UnverifiableCode);
        _fixture.Graph.AllRequests.Should().BeEmpty();
    }

    [Fact]
    public void ExactlyTheTwoSecurityRoutes_ArePlatformOperatorOnly()
    {
        var marked = _fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<SpeAdminPlatformOperatorOnly>() is not null)
            .Select(e => $"{e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.FirstOrDefault()} {e.RoutePattern.RawText}")
            .ToHashSet();

        marked.Should().BeEquivalentTo(new[] { "GET /api/spe/security/alerts", "GET /api/spe/security/score" });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/spe/businessunits — the caller's own unit and its descendants (verifier item 7)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheBusinessUnitList_ForALeafAdmin_HoldsOnlyItsOwnUnitAndItsDescendants()
    {
        Seed(callerUnit: UnitA);
        using var client = Admin();

        var units = await UnitIds(await client.GetAsync("/api/spe/businessunits"));

        units.Should().BeEquivalentTo(new[] { UnitA, UnitASub },
            "another customer's unit (Unit B) and the root are not the leaf admin's to see or assign");
    }

    [Fact]
    public async Task TheBusinessUnitList_ForARootAdmin_HoldsEveryUnit()
    {
        Seed(callerUnit: Root);
        using var client = Admin();

        var units = await UnitIds(await client.GetAsync("/api/spe/businessunits"));

        units.Should().BeEquivalentTo(new[] { Root, UnitA, UnitASub, UnitB });
    }

    [Fact]
    public async Task TheBusinessUnitList_ForACallerWithNoResolvableBusinessUnit_IsEmpty()
    {
        Seed(callerUnit: null);
        using var client = Admin();

        var units = await UnitIds(await client.GetAsync("/api/spe/businessunits"));

        units.Should().BeEmpty();
    }

    [Fact]
    public async Task TheBusinessUnitList_WhenTheHierarchyCannotBeRead_Is503_NeverTheWholeTable()
    {
        Seed(callerUnit: UnitA);
        _fixture.Dataverse.FaultQueriesOn("businessunits");
        using var client = Admin();

        var problem = await Problem(await client.GetAsync("/api/spe/businessunits"), HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be(UnverifiableCode);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private HttpClient Admin() => _fixture.CreateCaller(new[] { "Admin" });

    private void Seed(Guid? callerUnit)
    {
        var dv = _fixture.Dataverse;
        dv.Add("sprk_speenvironments", AdminSurfaceHostFixture.BffEnvironmentRow()); // master's tenant guard (round 65 merge)
        dv.Add("businessunits", new() { ["businessunitid"] = Root, ["_parentbusinessunitid_value"] = null, ["name"] = "Root" });
        dv.Add("businessunits", new() { ["businessunitid"] = UnitA, ["_parentbusinessunitid_value"] = Root, ["name"] = "Unit A" });
        dv.Add("businessunits", new() { ["businessunitid"] = UnitASub, ["_parentbusinessunitid_value"] = UnitA, ["name"] = "Unit A-sub" });
        dv.Add("businessunits", new() { ["businessunitid"] = UnitB, ["_parentbusinessunitid_value"] = Root, ["name"] = "Unit B" });

        if (callerUnit is { } unit)
        {
            dv.Add("systemusers", new()
            {
                ["azureactivedirectoryobjectid"] = AdminSurfaceHostFixture.CallerOid,
                ["systemuserid"] = Guid.Parse("5e5e5e5e-0000-0000-0000-000000000009"),
                ["_businessunitid_value"] = unit,
            });
        }

        dv.Add(ConfigSet, new()
        {
            ["sprk_specontainertypeconfigid"] = ConfigA,
            ["_sprk_environment_value"] = AdminSurfaceHostFixture.BffEnvironmentId,
            ["sprk_name"] = "Config A",
            ["_sprk_businessunit_value"] = UnitA,
            ["sprk_containertypeid"] = "77777777-0000-0000-0000-000000000077",
            ["sprk_owningappid"] = "a0a0a0a0-0000-0000-0000-00000000000a",
            ["sprk_keyvaultsecretname"] = "spe-owning-app-unit-a",
            ["statecode"] = 0,
        });

    }

    private static async Task<IReadOnlyList<Guid>> UnitIds(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.EnumerateArray()
            .Select(u => u.GetProperty("businessUnitId").GetGuid())
            .ToList();
    }

    private static async Task<Dictionary<string, JsonElement>> Problem(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, body);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body)!;
    }
}
