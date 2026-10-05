using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.SpeAdmin;

/// <summary>
/// unified-access-control-r2 task 165, owner round 35 item 3 — at READ, the BFF refuses to resolve a Key Vault secret
/// name outside <see cref="SpeConfigSecretNamePolicy"/>: it fails closed with the rule's own reason code and NEVER reads
/// the secret — and never serves a client or token cached for the config under an earlier name.
/// </summary>
/// <remarks>
/// The vault is a <see cref="SecretClient"/> substituted at its own class boundary (its members are virtual — the Azure
/// SDK's documented mocking seam), so "the secret was never read" is a verified zero-call count. Not
/// <c>Mock&lt;HttpMessageHandler&gt;</c> (ADR-038). The HTTP-level 409 every credential route answers first is
/// <c>SpeAdminConfigSecretNameTests</c>.
/// </remarks>
public sealed class SpeConfigSecretNameReadGuardTests
{
    private static readonly Guid ConfigId = Guid.Parse("ca000000-0000-0000-0000-0000000000a9");
    private readonly Mock<SecretClient> _vault = new();

    [Theory]
    [InlineData("AzureOpenAI-ApiKey")]
    [InlineData("null")]
    [InlineData("")]
    public async Task GetClientForConfigAsync_RefusesANonConformingName_WithoutReadingTheVault_EvenWithACachedClient(string name)
    {
        var service = CreateGraphService(tokenProvider: null);
        service.UseClientForConfig(ConfigId, NewGraphClient());   // a client cached for this config under an earlier name

        var act = () => service.GetClientForConfigAsync(Config(name));

        var refusal = (await act.Should().ThrowAsync<SpeConfigSecretNameNotAllowedException>()).Which;
        refusal.ReasonCode.Should().Be(SpeConfigSecretNamePolicy.NotAllowedReasonCode);
        refusal.Message.Should().StartWith($"[{SpeConfigSecretNamePolicy.NotAllowedReasonCode}]",
            "a handler that reports it through ProblemDetailsHelper.Explain carries the code verbatim");
        refusal.ConfigId.Should().Be(ConfigId);
        VaultWasNeverRead();
    }

    [Fact]
    public async Task GetClientForConfigAsync_ServesAConformingConfig()
    {
        var service = CreateGraphService(tokenProvider: null);
        var cached = NewGraphClient();
        service.UseClientForConfig(ConfigId, cached);

        var client = await service.GetClientForConfigAsync(Config("spe-owning-app-acme"));

        client.Should().BeSameAs(cached, "the rule refuses only names outside the allow-list");
    }

    [Fact]
    public async Task RegisterContainerTypeAsync_RefusesANonConformingName_BeforeTheVault()
    {
        var service = CreateGraphService(tokenProvider: null);

        var act = () => service.RegisterContainerTypeAsync(
            Config("redis-connection-string"), "77777777-0000-0000-0000-000000000077", "https://contoso-admin.sharepoint.com",
            "f0f0f0f0-0000-0000-0000-00000000000f", new[] { "ReadContent" }, Array.Empty<string>());

        await act.Should().ThrowAsync<SpeConfigSecretNameNotAllowedException>();
        VaultWasNeverRead();
    }

    [Fact]
    public async Task GetClientForOwningAppAsync_RefusesANonConformingOwningAppSecret_BeforeTheOboCacheAndTheVault()
    {
        var provider = new SpeAdminTokenProvider(_vault.Object, NullLogger<SpeAdminTokenProvider>.Instance);
        var service = CreateGraphService(provider);

        var act = () => service.GetClientForOwningAppAsync(Config("AzureOpenAI-ApiKey"), userAccessToken: "user-token");

        await act.Should().ThrowAsync<SpeConfigSecretNameNotAllowedException>();
        VaultWasNeverRead();
    }

    [Fact]
    public async Task TheTokenProvider_RefusesANonConformingOwningAppSecret_WithoutReadingTheVault()
    {
        var provider = new SpeAdminTokenProvider(_vault.Object, NullLogger<SpeAdminTokenProvider>.Instance);

        await ((Func<Task>)(() => provider.AcquireOwningAppTokenAsync(Config("AzureOpenAI-ApiKey"), "user-token")))
            .Should().ThrowAsync<SpeConfigSecretNameNotAllowedException>();
        await ((Func<Task>)(() => provider.FetchOwningAppSecretAsync(Config("Communication-WebhookSigningKey"))))
            .Should().ThrowAsync<SpeConfigSecretNameNotAllowedException>();
        VaultWasNeverRead();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private void VaultWasNeverRead() =>
        _vault.Verify(v => v.GetSecretAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never,
            "a non-conforming secret name is never sent to the vault");

    private static SpeAdminGraphService.ContainerTypeConfig Config(string secretName) => new(
        ConfigId: ConfigId,
        ContainerTypeId: "77777777-0000-0000-0000-000000000077",
        ClientId: "a0a0a0a0-0000-0000-0000-00000000000a",
        TenantId: "11111111-2222-3333-4444-555555555555",
        SecretKeyVaultName: secretName,
        OwningAppId: "a0a0a0a0-0000-0000-0000-00000000000a",
        OwningAppTenantId: "11111111-2222-3333-4444-555555555555",
        OwningAppSecretName: secretName);

    private static GraphServiceClient NewGraphClient() =>
        new(new HttpClient(), new AnonymousAuthenticationProvider(), "https://graph.unused.invalid/beta");

    private SpeAdminGraphService CreateGraphService(SpeAdminTokenProvider? tokenProvider)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://unused.invalid" })
            .Build();

        return new SpeAdminGraphService(
            httpClientFactory: new UnusedHttpClientFactory(),
            secretClient: _vault.Object,
            dataverseClient: new DataverseWebApiClient(configuration, NullLogger<DataverseWebApiClient>.Instance, new UnusableCredential()),
            configuration: configuration,
            logger: NullLogger<SpeAdminGraphService>.Instance,
            tokenProvider: tokenProvider);
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new InvalidOperationException($"No HTTP client may be built before the secret-name rule ('{name}').");
    }

    private sealed class UnusableCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext r, CancellationToken c)
            => throw new InvalidOperationException("No credential may be used by these tests.");

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext r, CancellationToken c)
            => throw new InvalidOperationException("No credential may be used by these tests.");
    }
}
