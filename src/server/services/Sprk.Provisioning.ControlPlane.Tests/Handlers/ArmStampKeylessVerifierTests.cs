// -----------------------------------------------------------------------------
// ArmStampKeylessVerifierTests.cs — task 230b (owner D13)
//
// The ARM half of H13's keyless gate, driven through its public VerifyAsync over a hand-rolled fake ARM
// (never Mock<HttpMessageHandler>). Each test starts from a correctly keyless stamp and changes one thing.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ArmStampKeylessVerifierTests
{
    private const string Sub = "11111111-2222-3333-4444-555555555555";
    private const string Rg = "rg-spaarke-acme-prod";
    private const string Site = "spaarke-acme-prod-bff";
    private static readonly string Group = $"/subscriptions/{Sub}/resourceGroups/{Rg}";

    [Fact]
    public async Task VerifyAsync_AKeylessStamp_Passes_AndCoversEverySlot()
    {
        var arm = new FakeArm();

        var outcome = await Verify(arm);

        var passed = outcome.Should().BeOfType<StampKeylessOutcome.Passed>().Subject;
        passed.Checked.Should().Contain($"{Site}[production]").And.Contain($"{Site}[staging]").And.Contain("acme-search");
    }

    [Fact]
    public async Task VerifyAsync_StorageAcceptingSharedKeys_Fails_NamingTheAccount()
    {
        var arm = new FakeArm();
        arm.Properties["sprkacmesa"] = new JsonObject { ["allowSharedKeyAccess"] = true };

        var violations = await FailedWith(arm);

        violations.Should().ContainSingle().Which.Should().Contain("sprkacmesa").And.Contain("allowSharedKeyAccess");
    }

    [Theory]
    [InlineData("acme-openai")]
    [InlineData("acme-servicebus")]
    [InlineData("acme-cosmos")]
    public async Task VerifyAsync_LocalAuthNotDisabled_Fails(string resource)
    {
        var arm = new FakeArm();
        arm.Properties[resource] = new JsonObject { ["disableLocalAuth"] = false };

        (await FailedWith(arm)).Should().ContainSingle().Which.Should().Contain(resource).And.Contain("disableLocalAuth");
    }

    [Fact]
    public async Task VerifyAsync_SearchWithAuthOptions_Fails_EvenWithLocalAuthDisabled()
    {
        var arm = new FakeArm();
        arm.Properties["acme-search"] = new JsonObject { ["disableLocalAuth"] = true, ["authOptions"] = new JsonObject { ["apiKeyOnly"] = new JsonObject() } };

        (await FailedWith(arm)).Should().ContainSingle().Which.Should().Contain("authOptions");
    }

    [Fact]
    public async Task VerifyAsync_RedisDatabaseWithAccessKeys_Fails()
    {
        var arm = new FakeArm { RedisAccessKeys = "Enabled" };

        (await FailedWith(arm)).Should().ContainSingle().Which.Should().Contain("accessKeysAuthentication").And.Contain("Enabled");
    }

    [Fact]
    public async Task VerifyAsync_AMissingKeyedResource_Fails()
    {
        // The stamp deploys three Cognitive Services accounts (OpenAI, Document Intelligence, Content Safety).
        var arm = new FakeArm();
        arm.Resources.RemoveAll(r => r.Name == "acme-contentsafety");

        (await FailedWith(arm)).Should().ContainSingle().Which.Should().Contain("Microsoft.CognitiveServices/accounts").And.Contain("at least 3");
    }

    [Fact]
    public async Task VerifyAsync_AKeySettingOnTheStagingSlot_Fails_NamingTheSettingButNotItsValue()
    {
        var arm = new FakeArm();
        arm.SlotSettings["staging"]["AzureOpenAI__ApiKey"] = "@Microsoft.KeyVault(SecretUri=https://v.vault.azure.net/secrets/openai-key/)";

        var violations = await FailedWith(arm);

        violations.Should().ContainSingle().Which.Should().Contain("staging").And.Contain("AzureOpenAI__ApiKey").And.NotContain("openai-key");
    }

    [Fact]
    public async Task VerifyAsync_AKeyShapedValueUnderAnyName_Fails()
    {
        var arm = new FakeArm();
        arm.SlotSettings["production"]["Custom__Storage"] = "DefaultEndpointsProtocol=https;AccountName=x;AccountKey=abc==";

        (await FailedWith(arm)).Should().ContainSingle().Which.Should().Contain("Custom__Storage").And.NotContain("abc==");
    }

    [Fact]
    public async Task VerifyAsync_AnEmptyKeySetting_IsNotAViolation()
    {
        var arm = new FakeArm();
        arm.SlotSettings["production"]["AzureOpenAI__ApiKey"] = "";

        (await Verify(arm)).Should().BeOfType<StampKeylessOutcome.Passed>();
    }

    [Fact]
    public async Task VerifyAsync_ARedisConnectionString_Fails()
    {
        var arm = new FakeArm();
        arm.ConnectionStrings["production"]["Redis"] = "acme.redis.cache.windows.net:6380,password=abc,ssl=True";

        (await FailedWith(arm)).Should().ContainSingle().Which.Should().Contain("connection string 'Redis'").And.NotContain("abc");
    }

    [Fact]
    public async Task VerifyAsync_FollowsTheResourceListNextLink()
    {
        var arm = new FakeArm { PageSize = 3 };
        arm.Properties["sprkacmesa"] = new JsonObject { ["allowSharedKeyAccess"] = true }; // on a later page

        (await FailedWith(arm)).Should().ContainSingle().Which.Should().Contain("sprkacmesa");
    }

    [Fact]
    public async Task VerifyAsync_ArmRefusesTheL2Identity_IsAnInfraFault_NotAPass()
    {
        var arm = new FakeArm { Status = HttpStatusCode.Forbidden };

        (await Verify(arm)).Should().BeOfType<StampKeylessOutcome.InfraFault>().Which.Diagnostic.Should().Contain("403");
    }

    // ---- helpers ------------------------------------------------------------

    private static Task<StampKeylessOutcome> Verify(FakeArm arm)
        => new ArmStampKeylessVerifier(new SingleClientFactory(arm), new FixedCredential(), NullLogger<ArmStampKeylessVerifier>.Instance)
            .VerifyAsync(new StampKeylessRequest("acme", "run-230b", Sub, Rg, Site), CancellationToken.None);

    private static async Task<IReadOnlyList<string>> FailedWith(FakeArm arm)
        => (await Verify(arm)).Should().BeOfType<StampKeylessOutcome.Failed>().Subject.Violations;

    private sealed record ArmResource(string Type, string Name);

    /// <summary>A keyless stamp's resource group, served as ARM REST. Tests change one thing.</summary>
    private sealed class FakeArm : HttpMessageHandler
    {
        public List<ArmResource> Resources { get; } = new()
        {
            new("Microsoft.Search/searchServices", "acme-search"),
            new("Microsoft.CognitiveServices/accounts", "acme-openai"),
            new("Microsoft.CognitiveServices/accounts", "acme-docintel"),
            new("Microsoft.CognitiveServices/accounts", "acme-contentsafety"),
            new("Microsoft.ServiceBus/namespaces", "acme-servicebus"),
            new("Microsoft.DocumentDB/databaseAccounts", "acme-cosmos"),
            new("Microsoft.Storage/storageAccounts", "sprkacmesa"),
            new("Microsoft.Cache/redisEnterprise", "acme-redis"),
            new("Microsoft.Web/sites", Site),
            new("Microsoft.Insights/components", "acme-insights"), // excluded type: never read
        };

        public Dictionary<string, JsonObject> Properties { get; } = new();
        public string RedisAccessKeys { get; init; } = "Disabled";
        public int PageSize { get; init; } = 100;
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public Dictionary<string, Dictionary<string, string>> SlotSettings { get; } = new()
        {
            ["production"] = new() { ["Redis__Endpoint"] = "acme-redis.westus2.redis.azure.net:10000", ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = "InstrumentationKey=x;IngestionEndpoint=y" },
            ["staging"] = new() { ["Redis__Endpoint"] = "acme-redis.westus2.redis.azure.net:10000" },
        };

        public Dictionary<string, Dictionary<string, string>> ConnectionStrings { get; } = new() { ["production"] = new(), ["staging"] = new() };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Status != HttpStatusCode.OK)
                return Task.FromResult(new HttpResponseMessage(Status));

            var path = request.RequestUri!.AbsolutePath;
            var query = request.RequestUri.Query;
            var site = $"{Group}/providers/Microsoft.Web/sites/{Site}";

            if (path == $"{Group}/resources")
            {
                var skip = query.Contains("skip=") ? int.Parse(query[(query.IndexOf("skip=", StringComparison.Ordinal) + 5)..]) : 0;
                var page = Resources.Skip(skip).Take(PageSize).Select(r => new JsonObject
                {
                    ["id"] = $"{Group}/providers/{r.Type}/{r.Name}",
                    ["name"] = r.Name,
                    ["type"] = r.Type,
                });
                var body = new JsonObject { ["value"] = new JsonArray(page.ToArray<JsonNode?>()) };
                if (skip + PageSize < Resources.Count)
                    body["nextLink"] = $"https://management.azure.com{Group}/resources?api-version=2021-04-01&skip={skip + PageSize}";
                return Json(body);
            }
            if (path == $"{site}/slots")
                return Json(new JsonObject { ["value"] = new JsonArray(new JsonObject { ["name"] = $"{Site}/staging" }) });
            if (path.EndsWith("/config/appsettings/list", StringComparison.Ordinal))
                return Json(new JsonObject { ["properties"] = ToObject(SlotSettings[SlotOf(path, site)]) });
            if (path.EndsWith("/config/connectionstrings/list", StringComparison.Ordinal))
            {
                var props = new JsonObject();
                foreach (var (name, value) in ConnectionStrings[SlotOf(path, site)])
                    props[name] = new JsonObject { ["value"] = value, ["type"] = "Custom" };
                return Json(new JsonObject { ["properties"] = props });
            }
            if (path.EndsWith("/databases", StringComparison.Ordinal))
                return Json(new JsonObject { ["value"] = new JsonArray(new JsonObject { ["name"] = "default", ["properties"] = new JsonObject { ["accessKeysAuthentication"] = RedisAccessKeys } }) });

            var resource = Resources.Single(r => path == $"{Group}/providers/{r.Type}/{r.Name}");
            var properties = Properties.TryGetValue(resource.Name, out var p) ? p
                : resource.Type == "Microsoft.Storage/storageAccounts" ? new JsonObject { ["allowSharedKeyAccess"] = false }
                : new JsonObject { ["disableLocalAuth"] = true };
            return Json(new JsonObject { ["properties"] = properties.DeepClone() });
        }

        private static string SlotOf(string path, string site)
            => path.StartsWith(site + "/slots/", StringComparison.Ordinal) ? path[(site.Length + 7)..].Split('/')[0] : "production";

        private static JsonObject ToObject(Dictionary<string, string> settings)
        {
            var o = new JsonObject();
            foreach (var (k, v) in settings) o[k] = v;
            return o;
        }

        private static Task<HttpResponseMessage> Json(JsonNode body)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") });
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FixedCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("arm-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }
}
