using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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

namespace Spe.Integration.Tests;

/// <summary>
/// Constants used by integration tests for known test identity values.
/// </summary>
public static class IntegrationTestConstants
{
    /// <summary>The Entra ID object ID claim used by authorization filters.</summary>
    public const string TestUserId = "test-user-00000000-0000-0000-0000-integration001";

    /// <summary>Test bearer token value for fake authentication header.</summary>
    public const string TestBearerToken = "integration-test-token";
}

/// <summary>
/// Shared WebApplicationFactory for integration tests.
/// Bootstraps the full BFF API in-process with:
///   - All external dependencies replaced by in-memory or no-op fakes.
///   - MemoryDistributedCache replacing Redis (ADR-009: Redis-first in prod; in-memory for tests).
///   - A fake JWT authentication handler that injects a known user identity.
///   - Hosted services removed to avoid background workers depending on external services.
///
/// Pattern aligned with WorkspaceTestFixture for consistency across test projects.
/// </summary>
public class IntegrationTestFixture : WebApplicationFactory<Program>
{
    public IConfiguration Configuration { get; private set; } = null!;

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Provide all configuration required by Program.cs validators BEFORE the host builds.
        builder.ConfigureHostConfiguration(config =>
        {
            var settings = new Dictionary<string, string?>
            {
                // Service Bus (required by ServiceBusOptions and ServiceBusClient registration)
                ["ConnectionStrings:ServiceBus"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=dGVzdA==",

                // CORS
                ["Cors:AllowedOrigins:0"] = "https://localhost:5173",

                // Azure AD / UAMI identity
                ["UAMI_CLIENT_ID"] = "test-client-id",
                ["TENANT_ID"] = "test-tenant-id",
                ["API_APP_ID"] = "test-app-id",
                ["API_CLIENT_SECRET"] = "test-secret",

                // AzureAd section — required by AddMicrosoftIdentityWebApi
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "test-tenant-id",
                ["AzureAd:ClientId"] = "test-app-id",
                ["AzureAd:Audience"] = "api://test-app-id",

                // Graph options (GraphOptions validator)
                ["Graph:TenantId"] = "test-tenant-id",
                ["Graph:ClientId"] = "test-client-id",
                ["Graph:ClientSecret"] = "test-client-secret",
                ["Graph:UseManagedIdentity"] = "false",
                ["Graph:Scopes:0"] = "https://graph.microsoft.com/.default",

                // Dataverse options (DataverseOptions validator)
                ["Dataverse:EnvironmentUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ClientId"] = "test-client-id",
                ["Dataverse:ClientSecret"] = "test-client-secret",
                ["Dataverse:TenantId"] = "test-tenant-id",

                // ServiceBus options (ServiceBusOptions validator)
                ["ServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=dGVzdA==",
                ["ServiceBus:QueueName"] = "sdap-jobs",

                // Redis — disabled so Program.cs uses AddDistributedMemoryCache
                ["Redis:Enabled"] = "false",

                // Office rate limiting (disabled for tests)
                ["OfficeRateLimit:Enabled"] = "false",

                // Document Intelligence — enabled so all AI services register
                ["DocumentIntelligence:Enabled"] = "true",
                ["DocumentIntelligence:OpenAiEndpoint"] = "https://test.openai.azure.com/",
                ["DocumentIntelligence:OpenAiKey"] = "test-key",
                ["DocumentIntelligence:OpenAiDeployment"] = "gpt-4o",

                // Analysis — enabled so all AI-dependent endpoints can be mapped
                ["Analysis:Enabled"] = "true",

                // AI Search (required for IRagService and SemanticSearchService)
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",

                // AzureOpenAI options (required by AiModule for IChatClient)
                ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAI:ChatModelName"] = "gpt-4o",

                // Record Matching
                ["DocumentIntelligence:RecordMatchingEnabled"] = "true",

                // AiSearchResilienceOptions defaults (ValidateDataAnnotations)
                ["AiSearchResilience:MaxRetryAttempts"] = "3",
                ["AiSearchResilience:CircuitBreakerFailureThreshold"] = "5",
                ["AiSearchResilience:CircuitBreakerDuration"] = "00:00:30",

                // GraphResilienceOptions defaults
                ["GraphResilience:MaxRetryAttempts"] = "3",
                ["GraphResilience:RetryDelay"] = "00:00:01",
                ["GraphResilience:CircuitBreakerFailureThreshold"] = "5",
                ["GraphResilience:CircuitBreakerDuration"] = "00:00:30",

                // ModelSelectorOptions — all required fields with defaults
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

        var host = base.CreateHost(builder);

        // Capture the built configuration so tests can read config values
        Configuration = host.Services.GetRequiredService<IConfiguration>();

        return host;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            // ---------------------------------------------------------------
            // CACHE: Replace Redis with MemoryDistributedCache for deterministic
            // caching behavior (ADR-009: Redis-first in production).
            // ---------------------------------------------------------------
            var cacheDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IDistributedCache));
            if (cacheDescriptor != null)
                services.Remove(cacheDescriptor);

            services.AddSingleton<IDistributedCache, MemoryDistributedCache>();
            services.AddSingleton<IMemoryCache, MemoryCache>(sp =>
                new MemoryCache(Options.Create(new MemoryCacheOptions())));

            // ---------------------------------------------------------------
            // AUTHENTICATION: Replace JWT/OIDC with a fake handler that
            // injects a known test identity. This satisfies RequireAuthorization()
            // and authorization filters (which read the "oid" claim).
            // ---------------------------------------------------------------
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = IntegrationTestFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = IntegrationTestFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, IntegrationTestFakeAuthHandler>(
                IntegrationTestFakeAuthHandler.SchemeName, _ => { });

            // Override Microsoft Identity Web's PostConfigure which replaces our
            // DefaultAuthenticateScheme/DefaultChallengeScheme. This forces the
            // fake authentication handler to be used throughout the request pipeline.
            services.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = IntegrationTestFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = IntegrationTestFakeAuthHandler.SchemeName;
            });

            // ---------------------------------------------------------------
            // HOSTED SERVICES: Remove background workers that depend on
            // external services (ServiceBus, AI, etc.) not available in tests.
            // ---------------------------------------------------------------
            services.RemoveAll<IHostedService>();

            // ---------------------------------------------------------------
            // DATAVERSE: Mock to avoid real connection in tests.
            // ---------------------------------------------------------------
            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);
        });
    }

    /// <summary>
    /// Creates an HttpClient pre-configured with the test bearer token.
    /// The FakeAuthHandler recognises any non-empty Authorization header and
    /// injects the test user identity.
    /// </summary>
    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IntegrationTestConstants.TestBearerToken);
        client.DefaultRequestHeaders.Add("User-Agent", "SDAP-Integration-Tests/1.0");

        return client;
    }

    /// <summary>
    /// Creates an HttpClient with NO authorization header for testing 401 scenarios.
    /// </summary>
    public HttpClient CreateUnauthenticatedClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    /// <summary>
    /// Legacy method preserved for backward compatibility with existing tests.
    /// Returns an authenticated client.
    /// </summary>
    public HttpClient CreateHttpClient()
    {
        return CreateAuthenticatedClient();
    }
}

/// <summary>
/// Fake authentication handler used exclusively in integration tests.
/// When an Authorization header is present (any non-empty value), it creates
/// a ClaimsPrincipal with the "oid" claim set to <see cref="IntegrationTestConstants.TestUserId"/>.
/// When no Authorization header is present, authentication fails so the pipeline
/// returns 401 — this allows testing unauthorized scenarios.
/// </summary>
internal sealed class IntegrationTestFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "FakeAuth";

    public IntegrationTestFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // If no Authorization header, fail authentication so we get 401
        if (!Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));

        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader))
            return Task.FromResult(AuthenticateResult.Fail("Empty Authorization header"));

        // Build a test ClaimsPrincipal that authorization filters will accept.
        // The filters read the "oid" claim (Entra ID object ID).
        var claims = new[]
        {
            new Claim("oid", IntegrationTestConstants.TestUserId),
            new Claim(ClaimTypes.NameIdentifier, IntegrationTestConstants.TestUserId),
            new Claim(ClaimTypes.Name, "Integration Test User"),
            new Claim("name", "Integration Test User"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
