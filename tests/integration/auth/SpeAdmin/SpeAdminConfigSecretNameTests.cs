using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.SpeAdmin;

/// <summary>
/// unified-access-control-r2 task 165, owner round 35 item 3, as narrowed by main-session round 65 item 1: a SUPPLIED
/// <c>keyVaultSecretName</c> must be under ONE pinned prefix (<see cref="SpeConfigSecretNamePolicy.RequiredPrefix"/>), so a
/// config cannot name another secret in the BFF's Key Vault for the one remaining reader (the container-binding backfill).
/// </summary>
/// <remarks>
/// <para>
/// Config POST/PUT answer 400 for any other supplied name; the name is optional (master bb8ba7251). The BFF itself reads no
/// secret any more (SPE Admin runs as the BFF's own identity), so round 35's 409 on every credential route was REMOVED:
/// a config whose STORED name does not conform (dev's Model 1 config stores the literal "null") is served like any other.
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
    // A STORED non-conforming name refuses nothing (round 65 item 1: the 409 guarded a read that no longer exists)
    // ─────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, string, string> ConfigIdRoutes() => new()
    {
        { "GET", "/api/spe/containers?configId={cfg}", "" },
        { "GET", "/api/spe/recyclebin?configId={cfg}", "" },
        { "GET", "/api/spe/configs/{cfg}", "" },
        { "GET", "/api/spe/audit?configId={cfg}", "" },
    };

    [Theory]
    [MemberData(nameof(ConfigIdRoutes))]
    public async Task AConfigWhoseStoredSecretNameDoesNotConform_IsServed_TheRuleIsNotApplied(string method, string path, string body)
    {
        using var client = Admin();

        var response = await client.SendAsync(Request(method, path, ConfigBad, body));

        response.StatusCode.Should().NotBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(Code,
            "SPE Admin reads no secret by this name, so a stored one never blocks a route (the Model 1 config's \"null\")");
    }

    [Fact]
    public async Task AnotherCustomersConfig_IsStillTheUniform404()
    {
        using var client = Admin();

        var problem = await Problem(
            await client.GetAsync($"/api/spe/containers?configId={ConfigBadOther}"), HttpStatusCode.NotFound);

        problem["errorCode"].GetString().Should().Be("spe.admin.deny.config_out_of_scope");
    }

    [Fact]
    public async Task Post_WithNoSecretName_IsCreated_TheNameIsOptional()
    {
        using var client = Admin();

        var response = await client.PostAsJsonAsync("/api/spe/configs", new
        {
            name = "no-secret", owningAppId = Guid.NewGuid().ToString(), containerTypeId = Guid.NewGuid().ToString(),
            businessUnitId = UnitA,
        });

        (await response.Content.ReadAsStringAsync()).Should().NotContain(Code);
        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
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
        dv.Add("sprk_speenvironments", AdminSurfaceHostFixture.BffEnvironmentRow()); // master's tenant guard (round 65 merge)
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

    }

    private static Dictionary<string, object?> ConfigRow(Guid id, Guid? unit, string secretName) => new()
    {
        ["sprk_specontainertypeconfigid"] = id,
        ["_sprk_environment_value"] = AdminSurfaceHostFixture.BffEnvironmentId,
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
