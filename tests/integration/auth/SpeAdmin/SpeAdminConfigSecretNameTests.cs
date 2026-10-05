using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.SpeAdmin;

/// <summary>
/// unified-access-control-r2 task 165, owner round 35 item 3 — <c>sprk_keyvaultsecretname</c> is allow-listed: the BFF
/// resolves only names under ONE pinned prefix (<see cref="SpeConfigSecretNamePolicy.RequiredPrefix"/>), so a config can no
/// longer point the BFF at another secret in its Key Vault (the OpenAI key, the Redis connection string …).
/// </summary>
/// <remarks>
/// <para>
/// Config POST/PUT answer 400 for any other name. Every configId route that USES the config's credential answers 409
/// with the rule's own reason code before any handler or Graph call — and therefore before any secret is read. The
/// config RECORD routes and the audit log never use the credential and stay usable, so an administrator can see,
/// correct and delete a misconfigured config. Another customer's config still gets the uniform 404 (its state is never
/// disclosed). The read-time guard itself (vault never called) is <c>SpeConfigSecretNameReadGuardTests</c>.
/// </para>
/// <para>
/// The tenant: Root → {Unit A, Unit B}; the caller is a leaf SPE admin of Unit A. Real host, fake Dataverse, WireMock
/// Graph. ADR-038 §2 path #1.
/// </para>
/// </remarks>
public sealed class SpeAdminConfigSecretNameTests : IClassFixture<AdminSurfaceHostFixture>
{
    private const string ConfigSet = "sprk_specontainertypeconfigs";
    private const string Code = SpeConfigSecretNamePolicy.NotAllowedReasonCode;
    private const string TypeT = "77777777-0000-0000-0000-000000000077";

    private static readonly Guid Root = Guid.Parse("10000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitA = Guid.Parse("1a000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitB = Guid.Parse("1b000000-0000-0000-0000-000000000000");

    private static readonly Guid ConfigGood = Guid.Parse("ca000000-0000-0000-0000-0000000000a1");
    private static readonly Guid ConfigBad = Guid.Parse("ca000000-0000-0000-0000-0000000000a2");
    private static readonly Guid ConfigBadUnitLess = Guid.Parse("c0000000-0000-0000-0000-0000000000e2");
    private static readonly Guid ConfigBadOther = Guid.Parse("cb000000-0000-0000-0000-0000000000b2");

    private readonly AdminSurfaceHostFixture _fixture;

    public SpeAdminConfigSecretNameTests(AdminSurfaceHostFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        Seed();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Routes that use the config's credential — refused, 409, nothing read
    // ─────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, string, string> CredentialRoutes() => new()
    {
        { "GET", "/api/spe/containers?configId={cfg}", "" },
        { "GET", "/api/spe/containers/c-x?configId={cfg}", "" },
        { "POST", "/api/spe/search/items?configId={cfg}", "search" },
        { "POST", "/api/spe/bulk/delete", "bulk" },
        { "GET", "/api/spe/recyclebin?configId={cfg}", "" },
    };

    [Fact]
    public async Task ATypeWideCredentialRoute_ForARootAdminOfAnOperatorEnvironment_OnAConfigWhoseSecretNameIsNotAllowed_Is409()
    {
        // The type-wide routes are for a root admin of a Spaarke-operated environment only (owner round 49 item 1) — the
        // leaf caller of this class is refused before the rule — so the secret-name rule is proven here for that caller.
        _fixture.Reset();
        Seed(callerUnit: Root);
        using var client = Admin();

        var problem = await Problem(
            await client.SendAsync(Request("GET", "/api/spe/containertypes/" + TypeT + "/consumers?configId={cfg}", ConfigBad, "")),
            HttpStatusCode.Conflict);

        problem["errorCode"].GetString().Should().Be(Code);
        _fixture.Dataverse.CallsOn(ConfigSet, "Retrieve").Should().BeEmpty("no handler resolved the config's credential");
        _fixture.Graph.AllRequests.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(CredentialRoutes))]
    public async Task ACredentialRoute_OnAConfigWhoseSecretNameIsNotAllowed_Is409_BeforeAnyHandlerOrGraphCall(
        string method, string path, string body)
    {
        using var client = Admin();

        var problem = await Problem(await client.SendAsync(Request(method, path, ConfigBad, body)), HttpStatusCode.Conflict);

        problem["errorCode"].GetString().Should().Be(Code);
        problem["detail"].GetString().Should().Contain(SpeConfigSecretNamePolicy.RequiredPrefix);
        _fixture.Dataverse.CallsOn(ConfigSet, "Retrieve").Should().BeEmpty("no handler resolved the config's credential");
        _fixture.Graph.AllRequests.Should().BeEmpty("nothing was read with a credential the BFF may not resolve");
    }

    [Fact]
    public async Task AUnitLessConfig_WhoseSecretNameIsNotAllowed_Is409Too()
    {
        using var client = Admin();

        var problem = await Problem(
            await client.GetAsync($"/api/spe/containers?configId={ConfigBadUnitLess}"), HttpStatusCode.Conflict);

        problem["errorCode"].GetString().Should().Be(Code);
    }

    [Fact]
    public async Task AnotherCustomersConfig_WhoseSecretNameIsNotAllowed_IsStillTheUniform404()
    {
        // The rule is judged only for a config the caller may act on — never an oracle for another customer's state.
        using var client = Admin();

        var problem = await Problem(
            await client.GetAsync($"/api/spe/containers?configId={ConfigBadOther}"), HttpStatusCode.NotFound);

        problem["errorCode"].GetString().Should().Be("spe.admin.deny.config_out_of_scope");
    }

    [Fact]
    public async Task ACredentialRoute_OnAConformingConfig_IsNotRefusedByTheRule()
    {
        using var client = Admin();

        var response = await client.GetAsync($"/api/spe/containers?configId={ConfigGood}");

        (await response.Content.ReadAsStringAsync()).Should().NotContain(Code);
        _fixture.Dataverse.CallsOn(ConfigSet, "Retrieve").Should().Contain(c => c.Id == ConfigGood, "the handler ran");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The config record routes and the audit log never use the credential — they stay usable
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheConfigRecordRoutesAndTheAuditLog_StayUsable_SoTheConfigCanBeSeenCorrectedAndDeleted()
    {
        using var client = Admin();

        (await client.GetAsync($"/api/spe/configs/{ConfigBad}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/spe/audit?configId={ConfigBad}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var put = await client.PutAsJsonAsync($"/api/spe/configs/{ConfigBad}", new { keyVaultSecretName = "spe-owning-app-unit-a" });
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        _fixture.Dataverse.CallsOn(ConfigSet, "Update").Should().ContainSingle("the rename is the fix");

        (await client.DeleteAsync($"/api/spe/configs/{ConfigBad}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public void OnlyTheConfigRecordRoutesAndTheAuditLog_AreExemptFromTheRule()
    {
        var exempt = _fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<SpeAdminConfigCredentialUnused>() is not null)
            .Select(e => $"{e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.FirstOrDefault()} {e.RoutePattern.RawText}")
            .ToHashSet();

        exempt.Should().BeEquivalentTo(new[]
        {
            "GET /api/spe/configs/{configId:guid}",
            "PUT /api/spe/configs/{configId:guid}",
            "DELETE /api/spe/configs/{configId:guid}",
            "GET /api/spe/audit",
        }, "every other configId route uses the config's credential and must get the rule");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST / PUT — 400 for a name outside the allow-list
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("AzureOpenAI-ApiKey")]            // another secret in the BFF vault
    [InlineData("redis-connection-string")]
    [InlineData("SPE-ContainerTypeId")]           // starts with "spe-" but is not an owning-app secret
    [InlineData("spe-owning-apps-acme")]          // a near miss of the prefix
    [InlineData("spe-owning-app-")]               // the prefix alone names nothing
    [InlineData("spe-owning-app-acme\n")]         // round 41 item 5: a trailing newline (the format check's '$' admits it)
    public async Task Post_WithASecretNameOutsideThePrefix_Is400_WithTheRulesCode_AndNothingIsCreated(string name)
    {
        using var client = Admin();

        var problem = await Problem(
            await client.PostAsJsonAsync("/api/spe/configs", NewConfig(name)), HttpStatusCode.BadRequest);

        problem["errorCode"].GetString().Should().Be(Code);
        _fixture.Dataverse.CallsOn(ConfigSet, "Create").Should().BeEmpty();
    }

    [Fact]
    public async Task Put_WithASecretNameOutsideThePrefix_Is400_AndNothingIsWritten()
    {
        using var client = Admin();

        var problem = await Problem(
            await client.PutAsJsonAsync($"/api/spe/configs/{ConfigGood}", new { keyVaultSecretName = "AzureOpenAI-ApiKey" }),
            HttpStatusCode.BadRequest);

        problem["errorCode"].GetString().Should().Be(Code);
        _fixture.Dataverse.CallsOn(ConfigSet, "Update").Should().BeEmpty();
    }

    [Theory]
    [InlineData("spe-owning-app-acme")]
    [InlineData("SPE-OWNING-APP-ACME")]   // Key Vault names are case-insensitive; so is the rule
    public async Task Post_WithAConformingName_IsCreated(string name)
    {
        using var client = Admin();

        var response = await client.PostAsJsonAsync("/api/spe/configs", NewConfig(name));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private HttpClient Admin() => _fixture.CreateCaller(new[] { "Admin" });

    private static HttpRequestMessage Request(string method, string path, Guid configId, string body)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path.Replace("{cfg}", configId.ToString()));
        request.Content = body switch
        {
            "search" => JsonContent.Create(new { query = "x" }),
            "bulk" => JsonContent.Create(new { containerIds = new[] { "c-x" }, configId = configId.ToString() }),
            _ => null,
        };
        return request;
    }

    private static Dictionary<string, object?> NewConfig(string secretName) => new()
    {
        ["name"] = "New config",
        ["containerTypeId"] = TypeT,
        ["owningAppId"] = "a0a0a0a0-0000-0000-0000-00000000000a",
        ["keyVaultSecretName"] = secretName,
        ["businessUnitId"] = UnitA.ToString(),
    };

    private void Seed(Guid? callerUnit = null)
    {
        var dv = _fixture.Dataverse;
        dv.Add("businessunits", new() { ["businessunitid"] = Root, ["_parentbusinessunitid_value"] = null });
        dv.Add("businessunits", new() { ["businessunitid"] = UnitA, ["_parentbusinessunitid_value"] = Root });
        dv.Add("businessunits", new() { ["businessunitid"] = UnitB, ["_parentbusinessunitid_value"] = Root });
        dv.Add("systemusers", new()
        {
            ["azureactivedirectoryobjectid"] = AdminSurfaceHostFixture.CallerOid,
            ["systemuserid"] = Guid.Parse("5e5e5e5e-0000-0000-0000-000000000007"),
            ["_businessunitid_value"] = callerUnit ?? UnitA,
        });

        dv.Add(ConfigSet, ConfigRow(ConfigGood, UnitA, "spe-owning-app-unit-a"));
        dv.Add(ConfigSet, ConfigRow(ConfigBad, UnitA, "AzureOpenAI-ApiKey"));
        dv.Add(ConfigSet, ConfigRow(ConfigBadUnitLess, null, "null"));
        dv.Add(ConfigSet, ConfigRow(ConfigBadOther, UnitB, "redis-connection-string"));

        _fixture.UseGraphForConfig(ConfigGood, ConfigBad, ConfigBadUnitLess, ConfigBadOther);
    }

    private static Dictionary<string, object?> ConfigRow(Guid id, Guid? unit, string secretName) => new()
    {
        ["sprk_specontainertypeconfigid"] = id,
        ["sprk_name"] = $"Config {id.ToString()[..2]}",
        ["_sprk_businessunit_value"] = unit,
        ["sprk_containertypeid"] = TypeT,
        ["sprk_owningappid"] = "a0a0a0a0-0000-0000-0000-00000000000a",
        ["sprk_keyvaultsecretname"] = secretName,
        ["statecode"] = 0,
    };

    private static async Task<Dictionary<string, JsonElement>> Problem(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, body);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body)!;
    }
}
