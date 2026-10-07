using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.SpeAdmin;

/// <summary>
/// unified-access-control-r2 task 165, round 16 item 4 — the business-unit boundary for SPE environments
/// (<c>/api/spe/environments</c>) and for the environment a config links (<c>POST/PUT /api/spe/configs</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule.</b> <c>sprk_speenvironment</c> is shared tenant infrastructure with no business-unit column.
/// Only a platform operator — an SPE admin whose OWN business unit is the root — may create, change or delete
/// an environment, and reads every one. Any other admin reads only the environments linked by a config they
/// can reach (their units' configs and business-unit-less configs), gets ONE 404 for any other id, and may link
/// a config only to an environment they can read.
/// </para>
/// <para>
/// <b>The tenant.</b> Root → {Unit A, Unit B}. Config A (Unit A) links Env A, Config B (Unit B) links Env B,
/// Config N (no business unit) links Env N; Env O is linked by nothing. Every request goes through the real
/// BFF pipeline (<see cref="AdminSurfaceHostFixture"/>); Dataverse is the in-memory fake at the
/// <c>DataverseWebApiClient</c> boundary, so "nothing was read / written" is the absence of a recorded call.
/// </para>
/// <para>ADR-038 §2 path #1 (security-auth KEEP). ADR-008: by-id reads and writes are decided by the
/// <c>/api/spe</c> group's <c>SpeAdminTenantScopeFilter</c> from the route's <c>SpeAdminEnvironmentOperation</c>
/// mark (round 20 item 4: one SPE-admin scope filter); the list trims itself (list precedent).</para>
/// </remarks>
public sealed class SpeAdminEnvironmentScopeTests : IClassFixture<AdminSurfaceHostFixture>
{
    private const string EnvironmentSet = "sprk_speenvironments";
    private const string ConfigSet = "sprk_specontainertypeconfigs";

    private const string NotFoundCode = "spe.admin.deny.environment_out_of_scope";
    private const string WriteDeniedCode = "spe.admin.deny.environment_write_requires_platform_operator";
    private const string ConfigLinkCode = "spe.admin.deny.environment_out_of_scope";
    private const string UnverifiableCode = "spe.admin.deny.scope_unverifiable";

    private static readonly Guid Root = Guid.Parse("20000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitA = Guid.Parse("2a000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitB = Guid.Parse("2b000000-0000-0000-0000-000000000000");

    private static readonly Guid ConfigA = Guid.Parse("da000000-0000-0000-0000-00000000000a");
    private static readonly Guid ConfigB = Guid.Parse("db000000-0000-0000-0000-00000000000b");
    private static readonly Guid ConfigN = Guid.Parse("d0000000-0000-0000-0000-00000000000e");

    private static readonly Guid EnvA = Guid.Parse("ea000000-0000-0000-0000-00000000000a");
    private static readonly Guid EnvB = Guid.Parse("eb000000-0000-0000-0000-00000000000b");
    private static readonly Guid EnvN = Guid.Parse("e0000000-0000-0000-0000-00000000000e");
    private static readonly Guid EnvO = Guid.Parse("e0000000-0000-0000-0000-0000000000f0");
    private static readonly Guid NoSuchEnv = Guid.Parse("ef000000-0000-0000-0000-0000000000ff");

    private readonly AdminSurfaceHostFixture _fixture;

    public SpeAdminEnvironmentScopeTests(AdminSurfaceHostFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        SeedTenant(callerUnit: UnitA);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reads — a leaf admin
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ForALeafAdmin_HoldsOnlyTheEnvironmentsTheirReachableConfigsLink()
    {
        using var client = Admin();

        var ids = await ListedIds(client);

        ids.Should().BeEquivalentTo(new[] { EnvA, EnvN },
            "Env A is linked by their unit's config and Env N by a business-unit-less one; Env B (Unit B) and the unlinked Env O are not theirs");
    }

    [Fact]
    public async Task GetById_ForALeafAdmin_AnswersTheSame404ForAnUnreadableAndAnUnknownEnvironment_AndReadsNeither()
    {
        using var client = Admin();

        var unreadable = await Problem(await client.GetAsync($"/api/spe/environments/{EnvB}"), HttpStatusCode.NotFound);
        var unlinked = await Problem(await client.GetAsync($"/api/spe/environments/{EnvO}"), HttpStatusCode.NotFound);
        var unknown = await Problem(await client.GetAsync($"/api/spe/environments/{NoSuchEnv}"), HttpStatusCode.NotFound);

        AssertUniformNotFound(unreadable, EnvB);
        AssertUniformNotFound(unlinked, EnvO);
        AssertUniformNotFound(unknown, NoSuchEnv);
        _fixture.Dataverse.CallsOn(EnvironmentSet, "Retrieve").Should().BeEmpty(
            "the handler's app-only read must not run for an environment the caller cannot read");
    }

    [Theory]
    [InlineData("ea000000-0000-0000-0000-00000000000a")]   // Env A — linked by their unit's config
    [InlineData("e0000000-0000-0000-0000-00000000000e")]   // Env N — linked by a business-unit-less config
    public async Task GetById_ForALeafAdmin_ServesAnEnvironmentTheirReachableConfigLinks(string environmentId)
    {
        using var client = Admin();

        var response = await client.GetAsync($"/api/spe/environments/{environmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Writes — a leaf admin
    // ─────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, string> EnvironmentWrites() => new()
    {
        { "POST", "/api/spe/environments" },
        { "PUT", "/api/spe/environments/ea000000-0000-0000-0000-00000000000a" },   // even an environment they CAN read
        { "PUT", "/api/spe/environments/eb000000-0000-0000-0000-00000000000b" },
        { "PUT", "/api/spe/environments/ef000000-0000-0000-0000-0000000000ff" },   // and one that does not exist
        { "DELETE", "/api/spe/environments/ea000000-0000-0000-0000-00000000000a" },
        { "DELETE", "/api/spe/environments/e0000000-0000-0000-0000-0000000000f0" },
        { "DELETE", "/api/spe/environments/ef000000-0000-0000-0000-0000000000ff" },
    };

    [Theory]
    [MemberData(nameof(EnvironmentWrites))]
    public async Task Write_ByALeafAdmin_IsOne403_AndNothingIsReadOrWritten(string method, string url)
    {
        using var client = Admin();

        var problem = await Problem(await client.SendAsync(WriteRequest(method, url)), HttpStatusCode.Forbidden);

        problem["reasonCode"].GetString().Should().Be(WriteDeniedCode);
        problem["detail"].GetString().Should().Be(
            "Only a platform operator (an administrator in the root business unit) may create, change or delete SPE environments.",
            "the answer is the same whatever id is named, so it says nothing about which environments exist");
        _fixture.Dataverse.CallsOn(EnvironmentSet).Should().BeEmpty("the refusal comes before any environment read or write");
    }

    [Theory]
    [MemberData(nameof(EnvironmentWrites))]
    public async Task Write_ByACallerWithNoResolvableBusinessUnit_IsTheSame403(string method, string url)
    {
        _fixture.Reset();
        SeedTenant(callerUnit: null);
        using var client = Admin();

        var problem = await Problem(await client.SendAsync(WriteRequest(method, url)), HttpStatusCode.Forbidden);

        problem["reasonCode"].GetString().Should().Be(WriteDeniedCode);
        _fixture.Dataverse.CallsOn(EnvironmentSet).Should().BeEmpty();
    }

    [Fact]
    public async Task ACallerWithNoResolvableBusinessUnit_ListsNothing_AndGetsThe404()
    {
        _fixture.Reset();
        SeedTenant(callerUnit: null);
        using var client = Admin();

        (await ListedIds(client)).Should().BeEmpty();
        AssertUniformNotFound(await Problem(await client.GetAsync($"/api/spe/environments/{EnvA}"), HttpStatusCode.NotFound), EnvA);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // A platform operator (root business unit) — unchanged behaviour
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ARootAdmin_ListsAndReadsEveryEnvironment()
    {
        _fixture.Reset();
        SeedTenant(callerUnit: Root);
        using var client = Admin();

        (await ListedIds(client)).Should().BeEquivalentTo(new[] { EnvA, EnvB, EnvN, EnvO });
        (await client.GetAsync($"/api/spe/environments/{EnvB}")).StatusCode.Should().Be(HttpStatusCode.OK);
        AssertUniformNotFound(
            await Problem(await client.GetAsync($"/api/spe/environments/{NoSuchEnv}"), HttpStatusCode.NotFound), NoSuchEnv);
    }

    [Fact]
    public async Task ARootAdmin_MayCreateUpdateAndDeleteEnvironments()
    {
        _fixture.Reset();
        SeedTenant(callerUnit: Root);
        using var client = Admin();

        var created = await client.PostAsJsonAsync("/api/spe/environments", NewEnvironment());
        var updated = await client.PutAsJsonAsync($"/api/spe/environments/{EnvB}", new { name = "Renamed" });
        var deleted = await client.DeleteAsync($"/api/spe/environments/{EnvO}");

        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());
        _fixture.Dataverse.CallsOn(EnvironmentSet, "Create").Should().ContainSingle();
        _fixture.Dataverse.CallsOn(EnvironmentSet, "Update").Should().ContainSingle(c => c.Id == EnvB);
        _fixture.Dataverse.CallsOn(EnvironmentSet, "Delete").Should().ContainSingle(c => c.Id == EnvO);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fail closed
    // ─────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, string, string> EnvironmentRoutesAndFaults()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var fault in new[] { "businessunits", ConfigSet })
        {
            data.Add("GET", "/api/spe/environments", fault);
            data.Add("GET", $"/api/spe/environments/{EnvA}", fault);
        }

        // Writes depend only on the caller's own unit and the hierarchy.
        data.Add("POST", "/api/spe/environments", "businessunits");
        data.Add("PUT", $"/api/spe/environments/{EnvA}", "businessunits");
        data.Add("DELETE", $"/api/spe/environments/{EnvA}", "businessunits");
        return data;
    }

    [Theory]
    [MemberData(nameof(EnvironmentRoutesAndFaults))]
    public async Task EnvironmentRoute_WhenTheReachCannotBeRead_Is503_AndNoEnvironmentIsReadOrWritten(
        string method, string url, string faultingSet)
    {
        _fixture.Dataverse.FaultQueriesOn(faultingSet);
        using var client = Admin();

        var problem = await Problem(await client.SendAsync(WriteRequest(method, url)), HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be(UnverifiableCode);
        _fixture.Dataverse.CallsOn(EnvironmentSet).Should().BeEmpty(
            "an unverifiable reach never falls back to the whole table");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The environment a config links — POST / PUT /api/spe/configs
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("eb000000-0000-0000-0000-00000000000b")]   // Env B — another unit's
    [InlineData("e0000000-0000-0000-0000-0000000000f0")]   // Env O — linked by no config
    [InlineData("ef000000-0000-0000-0000-0000000000ff")]   // no such environment
    [InlineData("00000000-0000-0000-0000-000000000000")]   // Guid.Empty
    public async Task PostConfig_LinkingAnEnvironmentTheLeafAdminCannotRead_IsOne403_AndNothingIsCreated(string environmentId)
    {
        using var client = Admin();

        var problem = await Problem(
            await client.PostAsJsonAsync("/api/spe/configs", NewConfig(UnitA, environmentId)),
            HttpStatusCode.Forbidden);

        problem["reasonCode"].GetString().Should().Be(ConfigLinkCode);
        problem["detail"].GetString().Should().Be("The SPE environment is not one you can use.");
        _fixture.Dataverse.CallsOn(ConfigSet, "Create").Should().BeEmpty();
    }

    [Theory]
    [InlineData("ea000000-0000-0000-0000-00000000000a")]
    [InlineData("e0000000-0000-0000-0000-00000000000e")]
    public async Task PostConfig_LinkingAnEnvironmentTheLeafAdminCanRead_IsCreated(string environmentId)
    {
        using var client = Admin();

        var response = await client.PostAsJsonAsync("/api/spe/configs", NewConfig(UnitA, environmentId));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PutConfig_RelinkingToAnUnreadableEnvironment_Is403_AndNothingIsWritten()
    {
        using var client = Admin();

        var problem = await Problem(
            await client.PutAsJsonAsync($"/api/spe/configs/{ConfigA}", new { environmentId = EnvB }),
            HttpStatusCode.Forbidden);

        problem["reasonCode"].GetString().Should().Be(ConfigLinkCode);
        _fixture.Dataverse.CallsOn(ConfigSet, "Update").Should().BeEmpty();
    }

    [Fact]
    public async Task PutConfig_ReSendingItsOwnEnvironment_IsServed()
    {
        // The shipped client re-sends every field on save, the linked environment included.
        using var client = Admin();

        var response = await client.PutAsJsonAsync($"/api/spe/configs/{ConfigA}", new { name = "Config A", environmentId = EnvA });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Dataverse.CallsOn(ConfigSet, "Update").Should().ContainSingle();
    }

    [Fact]
    public async Task PostConfig_ByARootAdmin_MayLinkAnyEnvironment()
    {
        _fixture.Reset();
        SeedTenant(callerUnit: Root);
        using var client = Admin();

        var response = await client.PostAsJsonAsync("/api/spe/configs", NewConfig(UnitB, EnvO.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PostConfig_WhenTheConfigTableCannotBeReadForTheLink_Is503_AndNothingIsCreated()
    {
        _fixture.Dataverse.FaultQueriesOn(ConfigSet);
        using var client = Admin();

        var problem = await Problem(
            await client.PostAsJsonAsync("/api/spe/configs", NewConfig(UnitA, EnvA.ToString())),
            HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be(UnverifiableCode);
        _fixture.Dataverse.CallsOn(ConfigSet, "Create").Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private HttpClient Admin() => _fixture.CreateCaller(new[] { "Admin" });

    private void SeedTenant(Guid? callerUnit)
    {
        var dv = _fixture.Dataverse;
        dv.Add("businessunits", new() { ["businessunitid"] = Root, ["_parentbusinessunitid_value"] = null });
        dv.Add("businessunits", new() { ["businessunitid"] = UnitA, ["_parentbusinessunitid_value"] = Root });
        dv.Add("businessunits", new() { ["businessunitid"] = UnitB, ["_parentbusinessunitid_value"] = Root });

        if (callerUnit is { } unit)
        {
            dv.Add("systemusers", new()
            {
                ["azureactivedirectoryobjectid"] = AdminSurfaceHostFixture.CallerOid,
                ["systemuserid"] = Guid.Parse("5e5e5e5e-0000-0000-0000-000000000002"),
                ["_businessunitid_value"] = unit,
            });
        }

        dv.Add(ConfigSet, ConfigRow(ConfigA, UnitA, EnvA, "a"));
        dv.Add(ConfigSet, ConfigRow(ConfigB, UnitB, EnvB, "b"));
        dv.Add(ConfigSet, ConfigRow(ConfigN, null, EnvN, "n"));

        foreach (var (id, name) in new[] { (EnvA, "Env A"), (EnvB, "Env B"), (EnvN, "Env N"), (EnvO, "Env O") })
        {
            dv.Add(EnvironmentSet, new()
            {
                ["sprk_speenvironmentid"] = id,
                ["sprk_name"] = name,
                ["sprk_tenantid"] = "11111111-2222-3333-4444-555555555555",
                ["sprk_rootsiteurl"] = "https://contoso.sharepoint.com",
                ["sprk_isdefault"] = false,
                ["statecode"] = 0,
            });
        }
    }

    private static Dictionary<string, object?> ConfigRow(Guid id, Guid? unit, Guid environment, string tag) => new()
    {
        ["sprk_specontainertypeconfigid"] = id,
        ["sprk_name"] = $"Config {tag}",
        ["_sprk_businessunit_value"] = unit,
        ["_sprk_environment_value"] = environment,
        ["sprk_containertypeid"] = $"{tag}{tag}{tag}{tag}0000-0000-0000-0000-00000000000{tag}",
        ["sprk_owningappid"] = $"{tag}0{tag}0{tag}0{tag}0-0000-0000-0000-00000000000{tag}",
        ["sprk_keyvaultsecretname"] = $"spe-owning-app-unit-{tag}",
        ["sprk_billingclassification"] = 100000001,
        ["statecode"] = 0,
    };

    private static Dictionary<string, object?> NewConfig(Guid businessUnitId, string environmentId) => new()
    {
        ["name"] = "New config",
        ["containerTypeId"] = "dddddddd-0000-0000-0000-00000000000d",
        ["owningAppId"] = "d0d0d0d0-0000-0000-0000-00000000000d",
        ["keyVaultSecretName"] = "spe-owning-app-new-config",
        ["businessUnitId"] = businessUnitId,
        ["environmentId"] = environmentId,
    };

    private static object NewEnvironment() => new
    {
        name = "New environment",
        tenantId = "11111111-2222-3333-4444-555555555555",
        rootSiteUrl = "https://fabrikam.sharepoint.com",
        isDefault = false,
    };

    private static HttpRequestMessage WriteRequest(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST") request.Content = JsonContent.Create(NewEnvironment());
        if (method == "PUT") request.Content = JsonContent.Create(new { name = "Renamed" });
        return request;
    }

    private static async Task<List<Guid>> ListedIds(HttpClient client)
    {
        var response = await client.GetAsync("/api/spe/environments");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid())
            .ToList();
    }

    private static void AssertUniformNotFound(Dictionary<string, JsonElement> problem, Guid environmentId)
    {
        problem["status"].GetInt32().Should().Be(404);
        problem["title"].GetString().Should().Be("Not Found");
        problem["detail"].GetString().Should().Be($"SPE environment '{environmentId}' was not found.");
        problem["errorCode"].GetString().Should().Be(NotFoundCode);
        problem.Keys.Except(new[] { "type", "title", "status", "detail" })
            .Should().BeEquivalentTo(new[] { "errorCode", "traceId" });
    }

    private static async Task<Dictionary<string, JsonElement>> Problem(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, body);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body)!;
    }
}
