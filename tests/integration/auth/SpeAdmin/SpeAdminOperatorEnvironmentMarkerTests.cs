using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Api.Filters;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.SpeAdmin;

/// <summary>
/// unified-access-control-r2 task 165, owner round 49 item 1 — the SPE admin routes whose answer spans the whole
/// SharePoint Embedded tenant (security alerts, secure score) or a whole container type (its app permissions, consuming
/// apps, registration) are for a ROOT-unit admin of a SPAARKE-OPERATED environment only. Under Model 1 every customer
/// environment's root admin is a "platform operator" in their own environment, so the root check alone cannot confine
/// them; the deployment setting <c>SpeAdmin:PlatformOperatorEnvironment</c> (default false — fail closed; validated at
/// startup) marks Spaarke's own environments. Every other caller gets the SAME 403 before anything is read.
/// Real host, fake Dataverse, WireMock Graph. ADR-038 §2 path #1.
/// </summary>
public static class OperatorEnvironmentRoutes
{
    internal const string ConfigSet = "sprk_specontainertypeconfigs";
    internal const string OperatorCode = "spe.admin.deny.platform_operator_required";
    internal const string TypeT = "77777777-0000-0000-0000-000000000077";

    internal static readonly Guid Root = Guid.Parse("10000000-0000-0000-0000-000000000000");
    internal static readonly Guid UnitA = Guid.Parse("1a000000-0000-0000-0000-000000000000");
    internal static readonly Guid ConfigA = Guid.Parse("ca000000-0000-0000-0000-00000000000a");

    /// <summary>Every tenant-wide and type-wide route (method, path).</summary>
    public static TheoryData<string, string> All() => new()
    {
        { "GET", "/api/spe/security/alerts" },
        { "GET", "/api/spe/security/score" },
        { "GET", $"/api/spe/containertypes/{TypeT}/permissions" },
        { "GET", $"/api/spe/containertypes/{TypeT}/consumers" },
        { "POST", $"/api/spe/containertypes/{TypeT}/consumers" },
        { "PUT", $"/api/spe/containertypes/{TypeT}/consumers/f0f0f0f0-0000-0000-0000-00000000000f" },
        { "DELETE", $"/api/spe/containertypes/{TypeT}/consumers/f0f0f0f0-0000-0000-0000-00000000000f" },
        { "POST", $"/api/spe/containertypes/{TypeT}/register" },
    };

    internal static HttpRequestMessage Request(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path + $"?configId={ConfigA}");
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new
            {
                appId = "f0f0f0f0-0000-0000-0000-00000000000f",
                displayName = "Consumer",
                sharePointAdminUrl = "https://contoso-admin.sharepoint.com",
                delegatedPermissions = new[] { "ReadContent" },
                applicationPermissions = Array.Empty<string>(),
            });
        }

        return request;
    }

    internal static void Seed(AdminSurfaceHostFixture fixture, Guid callerUnit)
    {
        var dv = fixture.Dataverse;
        dv.Add("sprk_speenvironments", AdminSurfaceHostFixture.BffEnvironmentRow()); // master's tenant guard (round 65 merge)
        dv.Add("businessunits", new() { ["businessunitid"] = Root, ["_parentbusinessunitid_value"] = null, ["name"] = "Root" });
        dv.Add("businessunits", new() { ["businessunitid"] = UnitA, ["_parentbusinessunitid_value"] = Root, ["name"] = "Unit A" });
        dv.Add("systemusers", new()
        {
            ["azureactivedirectoryobjectid"] = AdminSurfaceHostFixture.CallerOid,
            ["systemuserid"] = Guid.Parse("5e5e5e5e-0000-0000-0000-00000000004a"),
            ["_businessunitid_value"] = callerUnit,
        });
        dv.Add(ConfigSet, new()
        {
            ["sprk_specontainertypeconfigid"] = ConfigA,
            ["_sprk_environment_value"] = AdminSurfaceHostFixture.BffEnvironmentId,
            ["sprk_name"] = "Config A",
            ["_sprk_businessunit_value"] = UnitA,
            ["sprk_containertypeid"] = TypeT,
            ["sprk_owningappid"] = "a0a0a0a0-0000-0000-0000-00000000000a",
            ["sprk_keyvaultsecretname"] = "spe-owning-app-unit-a",
            ["statecode"] = 0,
        });
    }

    internal static async Task AssertRefusedBeforeAnythingIsRead(AdminSurfaceHostFixture fixture, HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, body);
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body)!["reasonCode"].GetString().Should().Be(OperatorCode);
        fixture.Dataverse.CallsOn(ConfigSet).Should().BeEmpty("refused before the config is looked at");
        fixture.Graph.AllRequests.Should().BeEmpty("nothing tenant-wide or type-wide was asked of Graph");
    }
}

/// <summary>A customer environment (the marker is <c>false</c>): even its root-unit admin is refused.</summary>
public sealed class SpeAdminOperatorRoutes_InACustomerEnvironment_Tests : IClassFixture<AdminSurfaceCustomerEnvironmentHostFixture>
{
    private readonly AdminSurfaceCustomerEnvironmentHostFixture _fixture;

    public SpeAdminOperatorRoutes_InACustomerEnvironment_Tests(AdminSurfaceCustomerEnvironmentHostFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Theory]
    [MemberData(nameof(OperatorEnvironmentRoutes.All), MemberType = typeof(OperatorEnvironmentRoutes))]
    public async Task ARootAdminOfACustomerEnvironment_GetsTheUniform403_BeforeAnythingIsRead(string method, string path)
    {
        OperatorEnvironmentRoutes.Seed(_fixture, callerUnit: OperatorEnvironmentRoutes.Root);
        using var client = _fixture.CreateCaller(new[] { "Admin" });

        var response = await client.SendAsync(OperatorEnvironmentRoutes.Request(method, path));

        await OperatorEnvironmentRoutes.AssertRefusedBeforeAnythingIsRead(_fixture, response);
        _fixture.Dataverse.CallsOn("systemusers").Should().BeEmpty(
            "the deployment marker is judged first — not even the caller's business unit is read");
    }
}

/// <summary>A deployment with NO marker at all: the default is false — refused (fail closed).</summary>
public sealed class SpeAdminOperatorRoutes_WithTheMarkerMissing_Tests : IClassFixture<AdminSurfaceUnmarkedHostFixture>
{
    private readonly AdminSurfaceUnmarkedHostFixture _fixture;

    public SpeAdminOperatorRoutes_WithTheMarkerMissing_Tests(AdminSurfaceUnmarkedHostFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Theory]
    [MemberData(nameof(OperatorEnvironmentRoutes.All), MemberType = typeof(OperatorEnvironmentRoutes))]
    public async Task ARootAdmin_WhereTheMarkerIsMissing_GetsTheUniform403(string method, string path)
    {
        OperatorEnvironmentRoutes.Seed(_fixture, callerUnit: OperatorEnvironmentRoutes.Root);
        using var client = _fixture.CreateCaller(new[] { "Admin" });

        var response = await client.SendAsync(OperatorEnvironmentRoutes.Request(method, path));

        await OperatorEnvironmentRoutes.AssertRefusedBeforeAnythingIsRead(_fixture, response);
    }
}

/// <summary>A Spaarke-operated environment (the marker is <c>true</c>): a root admin reaches the handlers; a leaf admin does not.</summary>
public sealed class SpeAdminOperatorRoutes_InASpaarkeOperatedEnvironment_Tests : IClassFixture<AdminSurfaceHostFixture>
{
    private readonly AdminSurfaceHostFixture _fixture;

    public SpeAdminOperatorRoutes_InASpaarkeOperatedEnvironment_Tests(AdminSurfaceHostFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Theory]
    [MemberData(nameof(OperatorEnvironmentRoutes.All), MemberType = typeof(OperatorEnvironmentRoutes))]
    public async Task ALeafAdminOfAnOperatorEnvironment_GetsTheUniform403(string method, string path)
    {
        OperatorEnvironmentRoutes.Seed(_fixture, callerUnit: OperatorEnvironmentRoutes.UnitA);
        using var client = _fixture.CreateCaller(new[] { "Admin" });

        var response = await client.SendAsync(OperatorEnvironmentRoutes.Request(method, path));

        await OperatorEnvironmentRoutes.AssertRefusedBeforeAnythingIsRead(_fixture, response);
    }

    [Theory]
    [MemberData(nameof(OperatorEnvironmentRoutes.All), MemberType = typeof(OperatorEnvironmentRoutes))]
    public async Task ARootAdminOfAnOperatorEnvironment_ReachesTheHandler(string method, string path)
    {
        OperatorEnvironmentRoutes.Seed(_fixture, callerUnit: OperatorEnvironmentRoutes.Root);
        using var client = _fixture.CreateCaller(new[] { "Admin" });

        var response = await client.SendAsync(OperatorEnvironmentRoutes.Request(method, path));

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(OperatorEnvironmentRoutes.OperatorCode);
        body.Should().NotContain("spe.admin.deny.", "no SPE admin rule refused it — " + body);
        _fixture.Dataverse.CallsOn(OperatorEnvironmentRoutes.ConfigSet, "Retrieve")
            .Should().Contain(c => c.Id == OperatorEnvironmentRoutes.ConfigA, "the handler resolved its config — it ran");
    }

    [Fact]
    public void ExactlyTheTenantWideAndTypeWideRoutes_CarryTheOperatorRule()
    {
        var marked = _fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<SpeAdminPlatformOperatorOnly>() is not null
                        || e.Metadata.GetMetadata<SpeAdminContainerTypeScope>() is not null)
            .Select(e => $"{e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.FirstOrDefault()} {e.RoutePattern.RawText}")
            .ToHashSet();

        marked.Should().BeEquivalentTo(new[]
        {
            "GET /api/spe/security/alerts",
            "GET /api/spe/security/score",
            "GET /api/spe/containertypes/{typeId}/permissions",
            "GET /api/spe/containertypes/{typeId}/consumers",
            "POST /api/spe/containertypes/{typeId}/consumers",
            "PUT /api/spe/containertypes/{typeId}/consumers/{appId}",
            "DELETE /api/spe/containertypes/{typeId}/consumers/{appId}",
            "POST /api/spe/containertypes/{typeId}/register",
        });
    }
}

/// <summary>The marker is validated at startup: a value that is not a boolean stops the host.</summary>
public sealed class SpeAdminOperatorEnvironmentMarkerStartupTests
{
    [Fact]
    public void AMarkerThatIsNotABoolean_StopsTheHostAtStartup()
    {
        using var fixture = new AdminSurfaceMalformedMarkerHostFixture();

        var start = () => fixture.CreateCaller(new[] { "Admin" }).Dispose();

        start.Should().Throw<Exception>()
            .Where(e => e.ToString().Contains("PlatformOperatorEnvironment", StringComparison.Ordinal),
                "a misspelt marker must fail the deployment, not silently read as false or true");
    }
}
