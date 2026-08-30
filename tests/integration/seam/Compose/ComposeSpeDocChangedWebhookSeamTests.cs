// Task 030 (spaarkeai-compose-r5, FR-07 / gap G8) — the THROUGH-THE-WIRE proof that the SPE
// `spe-doc-changed` webhook DELIVERY leg reaches the already-registered receiver handler: an
// HMAC-signed Graph change notification, carrying the configured clientState, is accepted (202) and
// drives the SpeSyncOrchestrator; a wrong clientState is rejected (401); and the Graph subscription-
// validation handshake echoes the token (200).
//
// This is the receiver half of gap G8's delivery leg — the part testable IN-PROCESS with test config
// (a test signing key + clientState, NOT the real Key Vault secrets / DEF-03). The webhook receiver
// endpoint (ComposeEndpoints.cs:209, HandleSpeDocChangedWebhookAsync) + its HMAC filter
// (WebhookSignatureFilter) + clientState verifier (SpeWebhookNotificationVerifier) already exist;
// this slice proves an external-change notification travels the REAL route → filter → handler →
// real SpeSyncOrchestrator. The full Graph→BFF network delivery remains ✅◐ E2E-pending on task 056
// (owner secrets); the poll fallback + subscription-origin call are covered by
// ComposeWordShuttlePollEndpointContractTests.
//
// Reuses the proven ComposeWordShuttlePollFixture boot pattern (real SpeSyncOrchestrator; only the
// external SPE/Dataverse/indexing boundaries mocked) PLUS the two Compose:Webhook:* config keys the
// poll fixture deliberately omits — this fixture's whole purpose is exercising that configured path.
//
// ADR-038 seam DoD: through-the-wire WebApplicationFactory slice. NO Mock<HttpMessageHandler>, NO
// DI-registration test, NO ctor-null test. The HMAC is computed exactly as production validates it
// (HMACSHA256 over the raw UTF-8 body). Assertions are HTTP-observable.

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeSpeDocChangedWebhookSeamTests : IClassFixture<ComposeWebhookReceiverFixture>
{
    private readonly ComposeWebhookReceiverFixture _fixture;

    public ComposeSpeDocChangedWebhookSeamTests(ComposeWebhookReceiverFixture fixture) => _fixture = fixture;

    private const string SignatureHeader = "X-Hub-Signature-256";

    [Fact]
    public async Task Webhook_ValidationHandshake_EchoesToken_200_ThroughTheWire()
    {
        using var client = _fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Graph subscription-creation probe: POST ?validationToken=... with no signed body → echo 200 text.
        var token = "graph-validation-token-030";
        var response = await client.PostAsync(
            $"/api/compose/webhooks/spe-doc-changed?validationToken={Uri.EscapeDataString(token)}",
            new StringContent(string.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the Graph subscription-validation handshake bypasses HMAC and echoes the token — the delivery route is reachable");
        (await response.Content.ReadAsStringAsync()).Should().Be(token,
            "Graph requires the validationToken echoed back verbatim as text/plain");
    }

    [Fact]
    public async Task Webhook_SignedNotification_ValidClientState_Accepted202_ThroughTheWire()
    {
        using var client = _fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // A signed notification carrying the CONFIGURED clientState. Its resource driveId resolves to no
        // tracked container (empty in-memory Redis) → the handler skips enumeration but still returns 202
        // (the delivery reached the handler, passed HMAC + clientState — the wire is proven end-to-end).
        var body = BuildNotificationJson(ComposeWebhookReceiverFixture.TestClientState, "drives/b!untracked-030/root");
        using var content = SignedContent(body, ComposeWebhookReceiverFixture.TestSigningKey);

        var response = await client.PostAsync("/api/compose/webhooks/spe-doc-changed", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "a correctly-signed notification with the configured clientState reaches the handler and is accepted (202) — the webhook delivery leg is wired end-to-end at the receiver");
    }

    [Fact]
    public async Task Webhook_SignedNotification_WrongClientState_Rejected401_ThroughTheWire()
    {
        using var client = _fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Correct HMAC (so it passes the filter) but a FORGED clientState → the handler rejects the batch 401.
        var body = BuildNotificationJson("wrong-client-state", "drives/b!untracked-030/root");
        using var content = SignedContent(body, ComposeWebhookReceiverFixture.TestSigningKey);

        var response = await client.PostAsync("/api/compose/webhooks/spe-doc-changed", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a notification whose clientState does not match the configured value is rejected (401) — the receiver fails closed on forged authenticity");
    }

    [Fact]
    public async Task Webhook_BadSignature_Rejected401_ThroughTheWire()
    {
        using var client = _fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var body = BuildNotificationJson(ComposeWebhookReceiverFixture.TestClientState, "drives/b!untracked-030/root");
        // Sign with the WRONG key → the HMAC filter rejects before the handler runs.
        using var content = SignedContent(body, "the-wrong-signing-key");

        var response = await client.PostAsync("/api/compose/webhooks/spe-doc-changed", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a notification signed with the wrong key fails the HMAC filter (401) — an attacker with the URL but not the key cannot forge delivery");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string BuildNotificationJson(string clientState, string resource) =>
        $$"""
        {"value":[{"subscriptionId":"sub-030","clientState":"{{clientState}}","changeType":"updated","resource":"{{resource}}","tenantId":"tenant-030"}]}
        """;

    /// <summary>Wraps the body in StringContent + the exact HMAC-SHA256 header production validates
    /// (base64 digest over the raw UTF-8 body).</summary>
    private static StringContent SignedContent(string body, string signingKey)
    {
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        content.Headers.TryAddWithoutValidation(SignatureHeader, "sha256=" + Convert.ToBase64String(digest));
        return content;
    }
}

/// <summary>
/// Boots the BFF in-process with the REAL <see cref="SpeSyncOrchestrator"/> (Redis-backed delta
/// substrate, empty in-memory) and the two <c>Compose:Webhook:*</c> config keys set to TEST values —
/// the receiver path the poll fixture deliberately omits. Only the external SPE/Dataverse/indexing
/// boundaries are mocked. Mirrors <see cref="ComposeWordShuttlePollFixture"/>'s boot exactly.
/// </summary>
public sealed class ComposeWebhookReceiverFixture : WebApplicationFactory<Program>
{
    public const string TestSigningKey = "compose-webhook-test-signing-key-030-abcdef0123456789";
    public const string TestClientState = "compose-webhook-test-clientstate-030";

    public Mock<ISpeFileOperations> SpeMock { get; } = new(MockBehavior.Loose);
    public Mock<IGenericEntityService> DataverseMock { get; } = new(MockBehavior.Loose);
    public Mock<IPostUploadIndexingEnqueuer> IndexingMock { get; } = new(MockBehavior.Loose);

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["ConnectionStrings:ServiceBus"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["Cors:AllowedOrigins:0"] = "https://localhost:5173",
                ["UAMI_CLIENT_ID"] = "test-client-id",
                ["TENANT_ID"] = "test-tenant-id",
                ["API_APP_ID"] = "test-app-id",
                ["API_CLIENT_SECRET"] = "test-secret",
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "test-tenant-id",
                ["AzureAd:ClientId"] = "test-app-id",
                ["AzureAd:Audience"] = "api://test-app-id",
                ["Graph:TenantId"] = "test-tenant-id",
                ["Graph:ClientId"] = "test-client-id",
                ["Graph:ClientSecret"] = "test-client-secret",
                ["Graph:ManagedIdentity:Enabled"] = "false",
                ["Graph:Scopes:0"] = "https://graph.microsoft.com/.default",
                ["Dataverse:EnvironmentUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ClientId"] = "test-client-id",
                ["Dataverse:ClientSecret"] = "test-client-secret",
                ["Dataverse:TenantId"] = "test-tenant-id",
                ["ServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["ServiceBus:QueueName"] = "sdap-jobs",
                ["Redis:Enabled"] = "false",
                ["Redis:AllowInMemoryFallback"] = "true",
                ["OfficeRateLimit:Enabled"] = "false",
                // The two keys under test — present here (unlike the poll fixture) so the receiver's
                // HMAC filter + clientState verifier run against known TEST values.
                ["Compose:Webhook:SigningKey"] = TestSigningKey,
                ["Compose:Webhook:ClientState"] = TestClientState,
                ["ModelSelector:DefaultModel"] = "gpt-4o",
                ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAI:ChatModelName"] = "gpt-4o",
                ["DocumentIntelligence:Enabled"] = "true",
                ["DocumentIntelligence:OpenAiEndpoint"] = "https://test.openai.azure.com/",
                ["DocumentIntelligence:OpenAiKey"] = "test-key",
                ["DocumentIntelligence:OpenAiDeployment"] = "gpt-4o",
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
                ["Analysis:Enabled"] = "true",
                ["Analysis:UseStubResolver"] = "true",
                ["SpeAdmin:KeyVaultUri"] = "https://test.vault.azure.net/",
                ["ManagedIdentity:ClientId"] = "test-managed-identity-client-id",
                ["DocumentIntelligence:RecordMatchingEnabled"] = "true",
                ["AiSearchResilience:MaxRetryAttempts"] = "3",
                ["AiSearchResilience:CircuitBreakerFailureThreshold"] = "5",
                ["AiSearchResilience:CircuitBreakerDuration"] = "00:00:30",
                ["GraphResilience:MaxRetryAttempts"] = "3",
                ["GraphResilience:RetryDelay"] = "00:00:01",
                ["GraphResilience:CircuitBreakerFailureThreshold"] = "5",
                ["GraphResilience:CircuitBreakerDuration"] = "00:00:30",
                ["CosmosPersistence:Endpoint"] = "https://test.documents.azure.com:443/",
                ["CosmosPersistence:DatabaseName"] = "spaarke-ai-test",
                ["AgentService:Enabled"] = "false",
                ["AgentService:Endpoint"] = "https://test.services.ai.azure.com/api/projects/test-project",
                ["AgentService:AgentId"] = "test-agent-id",
                ["AgentService:MaxConcurrency"] = "4",
                ["AgentService:ThreadCacheExpiryMinutes"] = "60",
            };
            config.AddInMemoryCollection(dict);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        builder.ConfigureTestServices(services =>
        {
            // Test hosts must not authenticate for real — see TestTokenCredential.
            services.UseStubTokenCredential();

            services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options =>
            {
                options.ThrowOnBadRequest = false;
            });

            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            services.RemoveAll<IHostedService>();

            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);

            // KEEP the real SpeSyncOrchestrator; mock ONLY the external boundaries it depends on.
            services.RemoveAll<ISpeFileOperations>();
            services.AddSingleton(SpeMock.Object);
            services.RemoveAll<IGenericEntityService>();
            services.AddSingleton(DataverseMock.Object);
            services.RemoveAll<IPostUploadIndexingEnqueuer>();
            services.AddSingleton(IndexingMock.Object);
        });
    }
}
