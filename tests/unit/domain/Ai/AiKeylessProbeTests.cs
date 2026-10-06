using Azure.Core;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Spaarke.Contracts.Provisioning;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.Diagnostics;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Ai.Safety;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai;

/// <summary>
/// Task 230b — the AI-owned keyless probes decide, before any call, whether the BFF would use a key (then the stamp
/// is not keyless: <c>key-credential</c>, not called) or lacks a setting (<c>not-configured</c>, not called). These
/// rules mirror the production clients' own key selection; a probe that called through a key would prove nothing.
/// No case here reaches the network: every service is either keyed or unconfigured.
/// </summary>
public class AiKeylessProbeTests
{
    [Fact]
    public async Task ProbeAsync_EveryKeySettingProductionWouldUse_IsReportedAsKeyCredential_AndNotCalled()
    {
        var probe = Probe(
            new Dictionary<string, string?>
            {
                ["AzureOpenAI:ApiKey"] = "k",
                ["AzureOpenAI:Endpoint"] = "https://openai.example/",
                ["AzureOpenAI:ChatModelName"] = "gpt",
                ["DocumentIntelligence:AiSearchKey"] = "k",
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://search.example",
                [ContentSafetyAuthHandler.ApiKeyConfigKey] = "k", // the probe names the same setting the handler reads
            },
            new DocumentIntelligenceOptions
            {
                OpenAiKey = "k",
                OpenAiEndpoint = "https://openai.example/",
                DocIntelKey = "k",
                DocIntelEndpoint = "https://docintel.example/",
                AiSearchEndpoint = "https://search.example",
            });

        var results = await probe.ProbeAsync(CancellationToken.None);

        Outcome(results, KeylessProofContract.Services.OpenAiChat).Should().Be((KeylessProofContract.Outcomes.KeyCredential, "key-configured:AzureOpenAI:ApiKey"));
        Outcome(results, KeylessProofContract.Services.OpenAiEmbeddings).Should().Be((KeylessProofContract.Outcomes.KeyCredential, "key-configured:DocumentIntelligence:OpenAiKey"));
        Outcome(results, KeylessProofContract.Services.DocumentIntelligence).Should().Be((KeylessProofContract.Outcomes.KeyCredential, "key-configured:DocumentIntelligence:DocIntelKey"));
        Outcome(results, KeylessProofContract.Services.AiSearch).Should().Be((KeylessProofContract.Outcomes.KeyCredential, "key-configured:DocumentIntelligence:AiSearchKey"));
        Outcome(results, KeylessProofContract.Services.ContentSafetyPromptShield).Should().Be((KeylessProofContract.Outcomes.KeyCredential, "key-configured:AiSafety:ContentSafety:ApiKey"));
        Outcome(results, KeylessProofContract.Services.ContentSafetyGroundedness).Should().Be((KeylessProofContract.Outcomes.KeyCredential, "key-configured:AiSafety:ContentSafety:ApiKey"));
    }

    [Theory]
    [InlineData("AiSearch:ReferencesApiKey")]
    [InlineData("RecordSync:AiSearchApiKey")]
    public async Task ProbeAsync_AnyAiSearchAdminKey_IsAKeyCredential(string keySetting)
    {
        var results = await Probe(new Dictionary<string, string?> { [keySetting] = "k" }, new DocumentIntelligenceOptions())
            .ProbeAsync(CancellationToken.None);

        Outcome(results, KeylessProofContract.Services.AiSearch).Should().Be((KeylessProofContract.Outcomes.KeyCredential, "key-configured:" + keySetting));
    }

    [Fact]
    public async Task ProbeAsync_AKeyThatProductionIgnoresBecauseManagedIdentityIsForced_IsNotAKeyCredential()
    {
        // SearchClientFactory / ContentSafetyAuthHandler: the managed-identity flag wins over a configured key.
        var results = await Probe(
                new Dictionary<string, string?>
                {
                    ["DocumentIntelligence:AiSearchKey"] = "k",
                    ["AiSearch:ManagedIdentity:Enabled"] = "true",
                    ["AiSafety:ContentSafety:ApiKey"] = "k",
                    ["AiSafety:ContentSafety:ManagedIdentity:Enabled"] = "true",
                },
                new DocumentIntelligenceOptions())
            .ProbeAsync(CancellationToken.None);

        Outcome(results, KeylessProofContract.Services.AiSearch).Should().Be((KeylessProofContract.Outcomes.NotConfigured, "setting-missing:DocumentIntelligence:AiSearchEndpoint"));
        Outcome(results, KeylessProofContract.Services.ContentSafetyPromptShield).Should().Be((KeylessProofContract.Outcomes.NotConfigured, "setting-missing:AiSafety:ContentSafety:Endpoint"));
    }

    [Fact]
    public async Task ProbeAsync_MissingSettings_AreNotConfigured_NamingTheSetting()
    {
        var results = await Probe(new Dictionary<string, string?>(), new DocumentIntelligenceOptions()).ProbeAsync(CancellationToken.None);

        results.Select(r => r.Service).Should().BeEquivalentTo(new[]
        {
            KeylessProofContract.Services.OpenAiChat, KeylessProofContract.Services.OpenAiEmbeddings,
            KeylessProofContract.Services.DocumentIntelligence, KeylessProofContract.Services.AiSearch,
            KeylessProofContract.Services.Cosmos, KeylessProofContract.Services.BlobStorage,
            KeylessProofContract.Services.ContentSafetyPromptShield, KeylessProofContract.Services.ContentSafetyGroundedness,
        });
        results.Should().OnlyContain(r => r.Outcome == KeylessProofContract.Outcomes.NotConfigured && r.Code.StartsWith("setting-missing:"));
        Outcome(results, KeylessProofContract.Services.OpenAiChat).Code.Should().Be("setting-missing:AzureOpenAI:Endpoint");
        Outcome(results, KeylessProofContract.Services.BlobStorage).Code.Should().Be("setting-missing:SessionFileStore:BlobEndpoint");
        Outcome(results, KeylessProofContract.Services.Cosmos).Code.Should().Be("setting-missing:CosmosPersistence:Endpoint");
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.Forbidden, "refused")]
    [InlineData(System.Net.HttpStatusCode.Unauthorized, "refused")]
    [InlineData(System.Net.HttpStatusCode.OK, "proved")]
    public async Task ProbeAsync_ContentSafetyAnswer_IsReportedAsIs(System.Net.HttpStatusCode status, string expected)
    {
        var results = await Probe(new Dictionary<string, string?>(), new DocumentIntelligenceOptions(), new StatusClientFactory(status))
            .ProbeAsync(CancellationToken.None);

        Outcome(results, KeylessProofContract.Services.ContentSafetyPromptShield).Outcome.Should().Be(expected);
        Outcome(results, KeylessProofContract.Services.ContentSafetyGroundedness).Outcome.Should().Be(expected);
    }

    [Fact]
    public async Task ProbeAsync_AnInvalidBlobEndpoint_IsNotConfigured_NotAServiceFailure()
    {
        var results = await Probe(
                new Dictionary<string, string?> { ["SessionFileStore:BlobEndpoint"] = "http://not-https.example" },
                new DocumentIntelligenceOptions())
            .ProbeAsync(CancellationToken.None);

        Outcome(results, KeylessProofContract.Services.BlobStorage).Outcome.Should().Be(KeylessProofContract.Outcomes.NotConfigured);
    }

    private static (string Outcome, string Code) Outcome(IReadOnlyList<KeylessProbeResult> results, string service)
    {
        var result = results.Single(r => r.Service == service);
        return (result.Outcome, result.Code);
    }

    private static AiKeylessProbe Probe(Dictionary<string, string?> settings, DocumentIntelligenceOptions docIntel, IHttpClientFactory? clients = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var credential = new NeverCalledCredential();
        return new AiKeylessProbe(
            configuration,
            Options.Create(docIntel),
            credential,
            new CosmosClient("https://cosmos.example:443/", credential),
            clients ?? new NoBaseAddressHttpClientFactory(),
            NullLogger<AiKeylessProbe>.Instance);
    }

    private sealed class NeverCalledCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("no probe in these tests may reach a service");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("no probe in these tests may reach a service");
    }

    /// <summary>A Content Safety probe client whose transport answers <paramref name="status"/> (the auth handler is not under test here).</summary>
    private sealed class StatusClientFactory(System.Net.HttpStatusCode status) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StatusHandler(status)) { BaseAddress = new Uri("https://cs.example/") };

        private sealed class StatusHandler(System.Net.HttpStatusCode status) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") });
        }
    }

    /// <summary>AiSafetyModule leaves the probe client without a base address when no endpoint is configured.</summary>
    private sealed class NoBaseAddressHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
