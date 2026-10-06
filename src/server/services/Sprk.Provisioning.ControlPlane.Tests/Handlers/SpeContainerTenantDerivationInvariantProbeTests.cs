// -----------------------------------------------------------------------------
// SpeContainerTenantDerivationInvariantProbeTests.cs
//
// L2 CONTROL-PLANE unit tests for SpeContainerTenantDerivationInvariantProbe
// (task 204c B07 — I4 independent-re-verification probe). Proves the REAL
// HttpClient probe path against ARM (sites/list + config/appsettings/list) via
// a hand-rolled FakeHttpMessageHandler — never Mock<HttpMessageHandler> per
// ADR-038 path #1 + testing.md's ban. Complements
// the task-176 BFF-diagnostic variant's tests (retired with it by task 227f);
// this file exercises the INDEPENDENT ARM-config-read variant.
//
// PATH: tests/CLAUDE.md 7 KEEP paths — component-scoped unit test of the probe
// class through its PUBLIC ProbeAsync surface. Sits alongside sibling H13
// real-probe test files (the retired task-176 probe tests,
// AiSearchTenantFilterInvariantProbeTests, CosmosPartitionKeyInvariantProbeTests).
//
// COVERAGE — every branch enumerated in the SpeContainerTenantDerivationInvariantProbe.cs
// file header § "EDGE CASES + INFRA-FAULT DISCIPLINE":
//   AC-1 Kind property = InvariantKind.I4SpeContainerResolver.
//   Task 227c rewrite (owner D28): the probe compares the deployed settings with the run's own values.
//   AC-2 (Fail) — a container setting names another container (CATASTROPHIC — another customer's
//        documents); container setting missing / blank; container type missing / blank / not the run's.
//   AC-3 (Pass) — container type = the run's; both container settings = H8's container.
//   InfraFault — the run's containerTypeId or SpeContainerId is empty (no ARM call).
//   AC-InfraFault — every non-verdictable branch: token failure, ARM sites/list
//        401/403/404/5xx, empty sites/list response, no matching site, config/
//        appsettings/list 401/403/404/5xx, malformed JSON, timeout, transport
//        error, non-http BFF URL, malformed BFF URL, custom-domain BFF URL,
//        blank subscription id, blank BFF URL, blank tenant id.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class SpeContainerTenantDerivationInvariantProbeTests
{
    private const string CustomerId = "acme";
    private const string RunId = "run-i4-tenant-derivation-probe-1";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";
    private const string ResourceGroupName = "rg-spaarke-acme-prod";
    private const string SiteName = "bff-acme";
    private const string BffApiUrl = "https://bff-acme.azurewebsites.net";
    private static readonly string SiteResourceId =
        $"/subscriptions/{SubscriptionId}/resourceGroups/{ResourceGroupName}" +
        $"/providers/Microsoft.Web/sites/{SiteName}";

    private const string ContainerTypeId = "fb3817a8-5a55-42ba-8cc9-12cf055168b8";
    private const string ContainerId = "b!AAAAAAAAAAAAAAAAAAAA-_BBBBBBBBBBBBBBBBBBBB_customerA";
    private const string OtherCustomersContainerId = "b!AAAAAAAAAAAAAAAAAAAA-_BBBBBBBBBBBBBBBBBBBB_customerB";

    /// <summary>What H4b writes for this run: the run's container type and H8's container (task 227c).</summary>
    private static Dictionary<string, string> CorrectSettings() => new()
    {
        ["SharePointEmbedded__ContainerTypeId"] = ContainerTypeId,
        ["EmailProcessing__DefaultContainerId"] = ContainerId,
        ["Communication__ArchiveContainerId"] = ContainerId,
    };

    // -----------------------------------------------------------------------
    // AC-1: probe metadata + IInvariantProbe contract
    // -----------------------------------------------------------------------

    [Fact]
    public void Kind_IsI4SpeContainerResolver()
    {
        var probe = BuildProbe();
        probe.Kind.Should().Be(InvariantKind.I4SpeContainerResolver);
    }

    [Fact]
    public void ExtractAppServiceName_RecognizesStandardAzureWebsitesNetHost()
    {
        SpeContainerTenantDerivationInvariantProbe
            .ExtractAppServiceName(new Uri("https://bff-acme.azurewebsites.net")).Should().Be("bff-acme");
    }

    [Fact]
    public void ExtractAppServiceName_ReturnsNullForCustomDomain()
    {
        SpeContainerTenantDerivationInvariantProbe
            .ExtractAppServiceName(new Uri("https://api.customer.example.com")).Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // AC-3: Pass — the deployed settings name this run's container type and this customer's container (task 227c)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_SettingsNameTheRunsContainerTypeAndContainer_ReturnsPassedViaGenuineArmCalls()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (IsSitesListCall(req)) return SitesListResponse(SiteResourceId, SiteName);
            if (IsAppSettingsListCall(req, SiteResourceId)) return AppSettingsResponse(CorrectSettings());
            return Unexpected(req);
        });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Passed>()
            .Which.Kind.Should().Be(InvariantKind.I4SpeContainerResolver);
        handler.RequestedUrls.Should().Contain(u => u.Contains("/providers/Microsoft.Web/sites?", StringComparison.Ordinal),
            "asserts the sites-list ARM call was actually made (not a hard-coded Pass)");
        handler.RequestedUrls.Should().Contain(u => u.Contains("/config/appsettings/list?", StringComparison.Ordinal),
            "asserts the app-settings/list ARM call was actually made");
        handler.CapturedAuthorizationSchemes.Should().OnlyContain(s => s == "Bearer");
    }

    [Fact]
    public void ClassifyAppSettings_ColonFormContainerTypeName_AlsoPasses()
    {
        var settings = CorrectSettings();
        settings.Remove("SharePointEmbedded__ContainerTypeId");
        settings["SharePointEmbedded:ContainerTypeId"] = ContainerTypeId.ToUpperInvariant();

        BuildProbe().ClassifyAppSettings(settings, SiteResourceId, ContainerTypeId, ContainerId)
            .Should().BeOfType<InvariantVerificationOutcome.Passed>();
    }

    // -----------------------------------------------------------------------
    // AC-2: Fail — another customer's container (CATASTROPHIC under owner D28)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("EmailProcessing__DefaultContainerId")]
    [InlineData("Communication__ArchiveContainerId")]
    public void ClassifyAppSettings_ContainerSettingNamesAnotherContainer_FailsCatastrophic(string setting)
    {
        var settings = CorrectSettings();
        settings[setting] = OtherCustomersContainerId;

        var outcome = BuildProbe().ClassifyAppSettings(settings, SiteResourceId, ContainerTypeId, ContainerId);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("CATASTROPHIC").And.Contain(setting).And.Contain("another customer");
    }

    [Fact]
    public void ClassifyAppSettings_ContainerSettingStillAKeyVaultReference_FailsWithoutCatastrophe()
    {
        var settings = CorrectSettings();
        settings["EmailProcessing__DefaultContainerId"] = "@Microsoft.KeyVault(SecretUri=https://kv.vault.azure.net/secrets/SPE-DefaultContainerId/)";

        BuildProbe().ClassifyAppSettings(settings, SiteResourceId, ContainerTypeId, ContainerId)
            .Should().BeOfType<InvariantVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("still a Key Vault reference").And.NotContain("CATASTROPHIC");
    }

    [Theory]
    [InlineData("EmailProcessing__DefaultContainerId", null)]
    [InlineData("Communication__ArchiveContainerId", "  ")]
    public void ClassifyAppSettings_ContainerSettingMissingOrBlank_Fails(string setting, string? value)
    {
        var settings = CorrectSettings();
        if (value is null) settings.Remove(setting); else settings[setting] = value;

        var outcome = BuildProbe().ClassifyAppSettings(settings, SiteResourceId, ContainerTypeId, ContainerId);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain(setting).And.NotContain("CATASTROPHIC");
    }

    // -----------------------------------------------------------------------
    // AC-2: Fail — container type missing / blank / not the run's
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_ContainerTypeAppSettingMissing_ReturnsFailed()
    {
        var settings = CorrectSettings();
        settings.Remove("SharePointEmbedded__ContainerTypeId");
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (IsSitesListCall(req)) return SitesListResponse(SiteResourceId, SiteName);
            if (IsAppSettingsListCall(req, SiteResourceId)) return AppSettingsResponse(settings);
            return Unexpected(req);
        });

        var outcome = await BuildProbe(handler).ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("NO 'SharePointEmbedded__ContainerTypeId'");
    }

    [Theory]
    [InlineData("   ", "BLANK")]
    [InlineData("99999999-0000-0000-0000-000000000099", "not configured with this run's container type")]
    [InlineData("@Microsoft.KeyVault(SecretUri=https://kv.vault.azure.net/secrets/SPE-ContainerTypeId/)", "not configured with this run's container type")]
    public void ClassifyAppSettings_ContainerTypeBlankOrNotTheRuns_Fails(string value, string expectedText)
    {
        var settings = CorrectSettings();
        settings["SharePointEmbedded__ContainerTypeId"] = value;

        BuildProbe().ClassifyAppSettings(settings, SiteResourceId, ContainerTypeId, ContainerId)
            .Should().BeOfType<InvariantVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain(expectedText);
    }

    [Theory]
    [InlineData("", ContainerId)]
    [InlineData(ContainerTypeId, "")]
    public async Task ProbeAsync_RunValuesMissing_ReturnsInfraFault_WithoutCallingArm(string containerTypeId, string containerId)
    {
        var handler = new FakeHttpMessageHandler(req => Unexpected(req));

        var outcome = await BuildProbe(handler).ProbeAsync(
            BuildRequest() with { ContainerTypeId = containerTypeId, SpeContainerId = containerId }, CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>();
        handler.RequestedUrls.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Defense-in-depth: blank tenantId
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_BlankTenantId_ReturnsFailed_DefenseInDepth()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            throw new InvalidOperationException("HTTP must not be exercised when tenantId is blank"));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(tenantId: ""), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("blank tenantId");
    }

    // -----------------------------------------------------------------------
    // InfraFault paths — every non-verdictable branch
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_BlankSubscriptionId_ReturnsInfraFault()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            throw new InvalidOperationException("HTTP must not be exercised when subscription is blank"));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(subscriptionId: ""), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("SubscriptionId is empty");
    }

    [Fact]
    public async Task ProbeAsync_BlankBffApiUrl_ReturnsInfraFault()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            throw new InvalidOperationException("HTTP must not be exercised when BffApiUrl is blank"));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(bffApiUrl: ""), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("BffApiUrl is empty");
    }

    [Fact]
    public async Task ProbeAsync_MalformedBffApiUrl_ReturnsInfraFault()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            throw new InvalidOperationException("HTTP must not be exercised when BffApiUrl is malformed"));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(bffApiUrl: "not a url"), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("not a valid http(s) absolute URL");
    }

    [Fact]
    public async Task ProbeAsync_CustomDomainBffApiUrl_ReturnsInfraFault()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            throw new InvalidOperationException("HTTP must not be exercised for custom-domain BFF URLs"));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(
            BuildRequest(bffApiUrl: "https://api.customer.example.com"), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("does not follow the App Service");
    }

    [Fact]
    public async Task ProbeAsync_TokenAcquisitionThrows_ReturnsInfraFault()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            throw new InvalidOperationException("HTTP must not be exercised when token acquisition fails"));
        var throwingCred = new ThrowingTokenCredential(
            new InvalidOperationException("simulated auth failure"));
        var probe = BuildProbe(handler, throwingCred);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("ARM token acquisition")
            .And.Contain("simulated auth failure");
    }

    [Fact]
    public async Task ProbeAsync_SitesListHttp403_ReturnsInfraFault()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (IsSitesListCall(req))
                return new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("forbidden", Encoding.UTF8, "text/plain"),
                };
            return Unexpected(req);
        });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("HTTP 403");
    }

    [Fact]
    public async Task ProbeAsync_SitesListNoMatchingSite_ReturnsInfraFault()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (IsSitesListCall(req))
                return SitesListResponse(
                    "/subscriptions/" + SubscriptionId + "/resourceGroups/other/providers/Microsoft.Web/sites/some-other-site",
                    "some-other-site");
            return Unexpected(req);
        });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("No App Service named 'bff-acme'");
    }

    [Fact]
    public async Task ProbeAsync_SitesListMalformedJson_ReturnsInfraFault()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (IsSitesListCall(req))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{invalid", Encoding.UTF8, "application/json"),
                };
            return Unexpected(req);
        });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("not parseable JSON");
    }

    [Fact]
    public async Task ProbeAsync_AppSettingsListHttp404_ReturnsInfraFault()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (IsSitesListCall(req)) return SitesListResponse(SiteResourceId, SiteName);
            if (IsAppSettingsListCall(req, SiteResourceId))
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found", Encoding.UTF8, "text/plain"),
                };
            return Unexpected(req);
        });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("HTTP 404");
    }

    [Fact]
    public async Task ProbeAsync_AppSettingsListHttp500_ReturnsInfraFault()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (IsSitesListCall(req)) return SitesListResponse(SiteResourceId, SiteName);
            if (IsAppSettingsListCall(req, SiteResourceId))
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("arm boom", Encoding.UTF8, "text/plain"),
                };
            return Unexpected(req);
        });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("HTTP 500");
    }

    [Fact]
    public async Task ProbeAsync_TransportThrows_ReturnsInfraFault()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            throw new HttpRequestException("connection refused"));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("connection refused");
    }

    // -----------------------------------------------------------------------
    // Cancellation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_CancellationBeforeCall_Throws()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (IsSitesListCall(req)) return SitesListResponse(SiteResourceId, SiteName);
            if (IsAppSettingsListCall(req, SiteResourceId))
                return AppSettingsResponse(new Dictionary<string, string>
                {
                    ["SharePointEmbedded__ContainerTypeId"] =
                        "@Microsoft.KeyVault(SecretUri=https://v.vault.azure.net/secrets/x/)",
                });
            return Unexpected(req);
        });
        var probe = BuildProbe(handler);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await probe.ProbeAsync(BuildRequest(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static SpeContainerTenantDerivationInvariantProbe BuildProbe(
        FakeHttpMessageHandler? handler = null,
        TokenCredential? credential = null)
    {
        handler ??= new FakeHttpMessageHandler(_ =>
            throw new InvalidOperationException("default handler must not be exercised"));
        credential ??= new FakeTokenCredential();
        var options = Options.Create(new H13AcceptanceOptions
        {
            InvariantVerifierTimeout = TimeSpan.FromSeconds(30),
        });
        return new SpeContainerTenantDerivationInvariantProbe(
            new FakeHttpClientFactory(handler),
            credential,
            options,
            NullLogger<SpeContainerTenantDerivationInvariantProbe>.Instance);
    }

    private static InvariantVerificationRequest BuildRequest(
        string tenantId = TenantId,
        string subscriptionId = SubscriptionId,
        string bffApiUrl = BffApiUrl)
        => new(
            CustomerId: CustomerId,
            RunId: RunId,
            TenantId: tenantId,
            SubscriptionId: subscriptionId,
            AiSearchEndpoint: string.Empty,
            CosmosEndpoint: string.Empty,
            BffApiUrl: bffApiUrl,
            ContainerTypeId: ContainerTypeId,
            SpeContainerId: ContainerId);

    private static bool IsSitesListCall(HttpRequestMessage req)
        => req.Method == HttpMethod.Get
        && req.RequestUri is not null
        && req.RequestUri.AbsolutePath.EndsWith("/providers/Microsoft.Web/sites", StringComparison.OrdinalIgnoreCase);

    private static bool IsAppSettingsListCall(HttpRequestMessage req, string siteResourceId)
        => req.Method == HttpMethod.Post
        && req.RequestUri is not null
        && req.RequestUri.AbsolutePath.EndsWith(
            $"{siteResourceId}/config/appsettings/list", StringComparison.OrdinalIgnoreCase);

    private static HttpResponseMessage SitesListResponse(string siteResourceId, string siteName)
    {
        var body = $$"""
            {
              "value": [
                {
                  "id": "{{siteResourceId}}",
                  "name": "{{siteName}}",
                  "type": "Microsoft.Web/sites"
                }
              ]
            }
            """;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private static HttpResponseMessage AppSettingsResponse(IDictionary<string, string> settings)
    {
        var props = string.Join(",", settings.Select(kv =>
            $"\"{kv.Key}\":{System.Text.Json.JsonSerializer.Serialize(kv.Value)}"));
        var body = $$"""{ "properties": { {{props}} } }""";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private static HttpResponseMessage Unexpected(HttpRequestMessage req)
        => throw new InvalidOperationException(
            $"Test fake received an unexpected HTTP call: {req.Method} {req.RequestUri}");

    // ---- Fake collaborators (hand-rolled per ADR-038 §5 no Mock<T>) --------

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpClientFactory(HttpMessageHandler handler) { _handler = handler; }
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-i4-arm-token", DateTimeOffset.UtcNow.AddHours(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    private sealed class ThrowingTokenCredential : TokenCredential
    {
        private readonly Exception _toThrow;
        public ThrowingTokenCredential(Exception toThrow) { _toThrow = toThrow; }
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw _toThrow;
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw _toThrow;
    }

    /// <summary>
    /// Hand-rolled <see cref="HttpMessageHandler"/> — records every requested
    /// URL + captured Authorization scheme so tests can assert the probe issued
    /// real HTTP calls with the correct URL shape + bearer auth. Never Mock&lt;T&gt;.
    /// </summary>
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<string> RequestedUrls { get; } = new();
        public List<string> CapturedAuthorizationSchemes { get; } = new();

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedUrls.Add(request.RequestUri?.ToString() ?? string.Empty);
            if (request.Headers.Authorization is not null)
            {
                CapturedAuthorizationSchemes.Add(request.Headers.Authorization.Scheme);
            }
            return Task.FromResult(_responder(request));
        }
    }
}
