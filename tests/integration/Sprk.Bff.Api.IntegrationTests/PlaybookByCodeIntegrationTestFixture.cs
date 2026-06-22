using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.IntegrationTests;

/// <summary>
/// Shared constants for the GET /api/ai/playbooks/by-code/{code} integration tests (task 010).
/// </summary>
internal static class PlaybookByCodeTestConstants
{
    public const string TestUserOid = "11111111-1111-1111-1111-111111111111";
    public const string TestBearerToken = "playbook-by-code-test-bearer";

    /// <summary>Tenant A — the "current" caller's tenant.</summary>
    public const string TenantA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    /// <summary>Tenant B — used to verify cross-tenant lookup returns 404.</summary>
    public const string TenantB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    public const string KnownGoodCode = "summarize-document-chat";
    public const string KnownMissingCode = "nonexistent-playbook-code";
    public const string TenantBOnlyCode = "tenant-b-only-code";
}

/// <summary>
/// WebApplicationFactory for the <c>GET /api/ai/playbooks/by-code/{code}</c> endpoint (task 010).
/// </summary>
/// <remarks>
/// <para>
/// Substitutes <see cref="IPlaybookLookupService"/> with a <see cref="StubPlaybookLookupService"/> so
/// tests can deterministically control cold-path latency, miss behavior, and tenant routing without
/// requiring a live Dataverse connection. The endpoint's tenant scoping is implemented in
/// <c>PlaybookEndpoints.GetPlaybookByCode</c> via the cache key — the stub records calls so the
/// warm-hit test can verify cache hit.
/// </para>
/// </remarks>
public class PlaybookByCodeIntegrationTestFixture : WebApplicationFactory<Program>
{
    /// <summary>
    /// Test stub for <see cref="IPlaybookLookupService"/>. Tracks invocation count + simulated latency.
    /// </summary>
    public StubPlaybookLookupService PlaybookLookup { get; } = new StubPlaybookLookupService();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            // Minimal config so module validators pass. Mirrors DataverseIntegrationTestFixture.
            var settings = new Dictionary<string, string?>
            {
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
                ["Graph:UseManagedIdentity"] = "false",
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
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            // In-memory cache (ADR-009 — tests are deterministic without Redis).
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache, MemoryDistributedCache>();
            services.RemoveAll<IMemoryCache>();
            services.AddSingleton<IMemoryCache, MemoryCache>(sp =>
                new MemoryCache(Options.Create(new MemoryCacheOptions())));

            // Fake JWT auth — tenant claim is configurable per-request via X-Test-Tenant header.
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = PlaybookByCodeFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = PlaybookByCodeFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, PlaybookByCodeFakeAuthHandler>(
                PlaybookByCodeFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = PlaybookByCodeFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = PlaybookByCodeFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IHostedService>();

            // Replace IDataverseService with a no-op mock. Otherwise the production
            // DataverseServiceClientImpl tries to MSAL-acquire a token at request time and 500s
            // (mirrors the substitution in DataverseIntegrationTestFixture).
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(new Mock<IDataverseService>(MockBehavior.Loose).Object);

            // Substitute IPlaybookLookupService with the deterministic stub.
            services.RemoveAll<IPlaybookLookupService>();
            services.AddSingleton<IPlaybookLookupService>(PlaybookLookup);
        });
    }

    /// <summary>
    /// Creates an authenticated HttpClient. The tenant id can be overridden via X-Test-Tenant header
    /// so individual tests can simulate cross-tenant lookups.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(string tenantId = PlaybookByCodeTestConstants.TenantA)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", PlaybookByCodeTestConstants.TestBearerToken);
        client.DefaultRequestHeaders.Add("X-Test-Tenant", tenantId);

        return client;
    }
}

/// <summary>
/// Fake auth handler. Reads the X-Test-Tenant header (if present) and projects it into the
/// <c>tid</c> claim so the endpoint's tenant scoping uses it. Defaults to Tenant A.
/// </summary>
internal sealed class PlaybookByCodeFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "PlaybookByCodeFakeAuth";

    public PlaybookByCodeFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));
        }

        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader))
        {
            return Task.FromResult(AuthenticateResult.Fail("Empty Authorization header"));
        }

        // Tenant override via header (defaults to Tenant A).
        var tenantId = Request.Headers.TryGetValue("X-Test-Tenant", out var tidHeader)
            ? tidHeader.ToString()
            : PlaybookByCodeTestConstants.TenantA;

        var claims = new[]
        {
            new Claim("oid", PlaybookByCodeTestConstants.TestUserOid),
            new Claim("tid", tenantId),
            new Claim(ClaimTypes.NameIdentifier, PlaybookByCodeTestConstants.TestUserOid),
            new Claim(ClaimTypes.Name, "Playbook By-Code Integration Test User"),
            new Claim("name", "Playbook By-Code Integration Test User"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
