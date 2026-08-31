// -----------------------------------------------------------------------------
// ConsentCallbackE2ETestFixture.cs
//
// WebApplicationFactory<Program> fixture for the POST /api/onboarding/consent-
// callback endpoint (task 042 impl). Purpose: boot the full BFF pipeline
// (middleware, auth, rate-limiter, HMAC verifier, endpoint routing) and drive
// HTTP requests through it via a real HttpClient — the only substitution is
// IProvisioningEnqueuer (a Service-Bus test double captures the enqueue call
// instead of sending to a live namespace).
//
// Task 078 (customer-provisioning-orchestration-r1) — Wave 4 Batch 4E.
//
// Design approach (per POML "SIGNED SYNTHETIC payload" allowance):
//   - Real WebApplicationFactory<Program> boots the whole BFF.
//   - Config supplies a deterministic Onboarding:HmacSigningKey so tests can
//     compute a matching HMAC-SHA256 over their request body.
//   - CapturingProvisioningEnqueuer records every EnqueueAsync call so tests
//     can assert wire-shape (HandlerId, RunId, CustomerId, ParametersJson).
//   - All hosted services removed to keep the fixture deterministic and to
//     avoid the real ServiceBusClient trying to reach a live namespace.
//
// Reason we do NOT drive a real /adminconsent flow: interactive browser +
// live dev tenant + redirect-URI whitelist would take a human, not gate CI.
// POML explicitly permits the signed-synthetic-payload alternative for this
// reason. The synthetic path exercises byte-for-byte the same HMAC verifier +
// endpoint code that a real admin-consent redirect would hit.
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Endpoints.Onboarding;

namespace Sprk.Bff.Api.IntegrationTests.Onboarding;

/// <summary>Shared constants for the consent-callback E2E tests (task 078).</summary>
internal static class ConsentCallbackE2EConstants
{
    /// <summary>
    /// Test HMAC signing key. Sufficient entropy for real HMAC-SHA256 verification
    /// (the BFF endpoint's HmacSignatureVerifier does not enforce a minimum length —
    /// the operational KV secret is 256 bits / 32 bytes; this test key mirrors that).
    /// </summary>
    public const string HmacSigningKey = "e2e-test-signing-key-31337-not-a-real-secret-value-please";

    /// <summary>Signature header name (mirrors OnboardingOptions default).</summary>
    public const string SignatureHeaderName = "X-Signature-256";

    /// <summary>Endpoint route (mirrors ConsentCallbackEndpoint.Route).</summary>
    public const string Route = "/api/onboarding/consent-callback";

    public const string CustomerId = "acme-corp-e2e";
    public const string CustomerTenantId = "22222222-3333-4444-5555-666666666666";
    public const string CorrelationId = "e2e-correlation-42";
}

/// <summary>
/// WebApplicationFactory that boots the full BFF but substitutes IProvisioningEnqueuer
/// with an in-memory <see cref="CapturingProvisioningEnqueuer"/>. Configuration mirrors
/// the pattern used by <see cref="PlaybookByIdIntegrationTestFixture"/> for stability
/// across module validators.
/// </summary>
public sealed class ConsentCallbackE2ETestFixture : WebApplicationFactory<Program>
{
    /// <summary>Captures every enqueue call. Cleared per test class instance.</summary>
    public CapturingProvisioningEnqueuer Enqueuer { get; } = new();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            // Minimal config so module validators pass in "Testing" env. The
            // Onboarding section supplies a deterministic HMAC signing key so the
            // real HmacSignatureVerifier accepts our synthetic-signed payloads.
            var settings = new Dictionary<string, string?>
            {
                // -------- Onboarding (task 042) --------
                ["Onboarding:HmacSigningKey"] = ConsentCallbackE2EConstants.HmacSigningKey,
                ["Onboarding:QueueName"] = "sprk-provisioning-jobs",
                ["Onboarding:SignatureHeaderName"] = ConsentCallbackE2EConstants.SignatureHeaderName,
                ["Onboarding:EnableDevBypass"] = "false",

                // -------- Mirror PlaybookByIdIntegrationTestFixture config --------
                ["ConnectionStrings:ServiceBus"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=dGVzdA==",
                ["Cors:AllowedOrigins:0"] = "https://localhost:5173",
                ["UAMI_CLIENT_ID"] = "test-client-id",
                ["TENANT_ID"] = "test-tenant-id",
                ["API_APP_ID"] = "test-app-id",
                ["API_CLIENT_SECRET"] = "test-secret",
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "test-tenant-id",
                ["AzureAd:ClientId"] = "test-app-id",
                ["AzureAd:Audience"] = "api://test-app-id",
                ["SpeAdmin:KeyVaultUri"] = "https://test-keyvault.vault.azure.net/",
                ["CosmosPersistence:Endpoint"] = "https://test.documents.azure.com:443/",
                ["CosmosPersistence:DatabaseName"] = "spaarke-ai-test",
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
                ["ManagedIdentity:ClientId"] = "00000000-0000-0000-0000-000000000001",
                ["ServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=dGVzdA==",
                ["ServiceBus:QueueName"] = "sdap-jobs",
                ["Redis:Enabled"] = "false",
                ["OfficeRateLimit:Enabled"] = "false",
                ["DocumentIntelligence:Enabled"] = "true",
                ["DocumentIntelligence:OpenAiEndpoint"] = "https://test.openai.azure.com/",
                ["DocumentIntelligence:OpenAiKey"] = "test-key",
                ["DocumentIntelligence:OpenAiDeployment"] = "gpt-4o",
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
                ["DocumentIntelligence:RecordMatchingEnabled"] = "true",
                ["Analysis:Enabled"] = "true",
                ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAI:ChatModelName"] = "gpt-4o",
                ["PowerBi:TenantId"] = "test-powerbi-tenant-id",
                ["PowerBi:ClientId"] = "test-powerbi-client-id",
                ["PowerBi:ClientSecret"] = "test-powerbi-client-secret",
                ["PowerBi:ApiUrl"] = "https://api.powerbi.com",
                ["PowerBi:Scope"] = "https://analysis.windows.net/.default",
                ["Reporting:ModuleEnabled"] = "false",
                ["AiSearchResilience:MaxRetryAttempts"] = "3",
                ["AiSearchResilience:CircuitBreakerFailureThreshold"] = "5",
                ["AiSearchResilience:CircuitBreakerDuration"] = "00:00:30",
                ["GraphResilience:MaxRetryAttempts"] = "3",
                ["GraphResilience:RetryDelay"] = "00:00:01",
                ["GraphResilience:CircuitBreakerFailureThreshold"] = "5",
                ["GraphResilience:CircuitBreakerDuration"] = "00:00:30",
                ["ModelSelector:IntentClassification"] = "gpt-4o-mini",
                ["ModelSelector:PlanGeneration"] = "o1-mini",
                ["ModelSelector:NodeGeneration"] = "gpt-4o",
                ["ModelSelector:ClarificationGeneration"] = "gpt-4o-mini",
                ["ModelSelector:AnalysisGeneration"] = "gpt-4o",
                ["ModelSelector:ExtractionGeneration"] = "gpt-4o-mini",
                ["ModelSelector:EmbeddingGeneration"] = "text-embedding-3-large",
                ["ModelSelector:FallbackGeneration"] = "gpt-4o",
            };
            config.AddInMemoryCollection(settings);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" env: OnboardingModule allows the HmacSigningKey validator to
        // skip fail-fast (but we still supply a real key so verification succeeds).
        // Same env matches PlaybookByIdIntegrationTestFixture.
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            // Test hosts must not authenticate for real — see TestTokenCredential.
            services.UseStubTokenCredential();

            // In-memory cache (ADR-009 — deterministic without Redis).
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache, MemoryDistributedCache>();
            services.RemoveAll<IMemoryCache>();
            services.AddSingleton<IMemoryCache, MemoryCache>(sp =>
                new MemoryCache(Options.Create(new MemoryCacheOptions())));

            // Remove ALL hosted services (background workers touching Service Bus,
            // reconcilers, etc.) — the endpoint under test does not need any of them,
            // and eliminating them prevents the real ServiceBusClient from booting a
            // connection attempt to the fake connection string.
            services.RemoveAll<IHostedService>();

            // Replace IDataverseService with a no-op mock — production impl tries to
            // MSAL-acquire a token at request time and 500s. Mirrors
            // PlaybookByIdIntegrationTestFixture pattern.
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(new Mock<IDataverseService>(MockBehavior.Loose).Object);

            // -------- THE KEY SUBSTITUTION for task 078 --------
            // Replace the real ServiceBusProvisioningEnqueuer with our capturing
            // test double. This is the ONLY seam the endpoint hits that would
            // otherwise reach outside the process. HmacSignatureVerifier stays
            // as the real production implementation — HMAC verification IS what
            // this E2E test is measuring.
            services.RemoveAll<IProvisioningEnqueuer>();
            services.AddSingleton<IProvisioningEnqueuer>(Enqueuer);
        });
    }

    /// <summary>Creates an HttpClient that does NOT follow redirects (endpoint returns 202, not a redirect anyway).</summary>
    public HttpClient CreateHttpClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        // Endpoint is Anonymous — no Authorization header needed. HMAC signature is the compensating control.
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}

/// <summary>
/// In-memory <see cref="IProvisioningEnqueuer"/> that captures every EnqueueAsync
/// call for later assertion. Thread-safe (uses ConcurrentQueue) because the BFF
/// pipeline may resolve it as a singleton across multiple test invocations.
/// </summary>
public sealed class CapturingProvisioningEnqueuer : IProvisioningEnqueuer
{
    private readonly ConcurrentQueue<EnqueueRecord> _calls = new();

    /// <summary>All enqueue calls captured, in the order received.</summary>
    public IReadOnlyList<EnqueueRecord> Calls => _calls.ToArray();

    /// <summary>Clear the capture buffer between tests.</summary>
    public void Reset()
    {
        while (_calls.TryDequeue(out _)) { }
    }

    /// <inheritdoc/>
    public Task EnqueueAsync(
        string handlerId,
        string runId,
        string customerId,
        string parametersJson,
        CancellationToken cancellationToken)
    {
        _calls.Enqueue(new EnqueueRecord(handlerId, runId, customerId, parametersJson, DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    /// <summary>Record of a captured enqueue call.</summary>
    public sealed record EnqueueRecord(
        string HandlerId,
        string RunId,
        string CustomerId,
        string ParametersJson,
        DateTimeOffset CapturedAt);
}
