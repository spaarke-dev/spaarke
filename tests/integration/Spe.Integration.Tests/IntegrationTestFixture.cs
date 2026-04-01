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

                // SpeAdmin — required by SpeAdminModule (KeyVault SecretClient)
                // A fake URI is sufficient; SecretClient is replaced by test doubles before any calls.
                ["SpeAdmin:KeyVaultUri"] = "https://test-keyvault.vault.azure.net/",

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

                // DataverseWebApiClient requires ManagedIdentity:ClientId
                // A test value avoids ArgumentNullException during singleton construction.
                // No actual Managed Identity calls are made during unit/integration tests.
                ["ManagedIdentity:ClientId"] = "00000000-0000-0000-0000-000000000001",

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

                // PowerBi options (required by ReportingModule — PowerBiOptions ValidateDataAnnotations)
                // These are test values; no real Power BI API calls are made in integration tests.
                ["PowerBi:TenantId"]     = "test-powerbi-tenant-id",
                ["PowerBi:ClientId"]     = "test-powerbi-client-id",
                ["PowerBi:ClientSecret"] = "test-powerbi-client-secret",
                ["PowerBi:ApiUrl"]       = "https://api.powerbi.com",
                ["PowerBi:Scope"]        = "https://analysis.windows.net/.default",

                // Reporting module gate — disabled by default so module-disabled tests pass.
                // Individual tests that need a 200 from /api/reporting/* override this via
                // WithWebHostBuilder / ConfigureTestServices on a per-test WebApplicationFactory.
                ["Reporting:ModuleEnabled"] = "false",

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

    /// <summary>
    /// Creates an authenticated HttpClient with the Reporting module explicitly enabled and
    /// the user pre-configured with the specified Dataverse security role claims.
    ///
    /// Use this in Reporting endpoint tests that need to verify 200/403 privilege behavior.
    /// </summary>
    /// <param name="roles">
    ///   Dataverse security role names to inject into the test user's claims.
    ///   Always include "sprk_ReportingAccess" for basic access; add "sprk_ReportingAdmin"
    ///   for admin-level operations.
    /// </param>
    public HttpClient CreateReportingClient(params string[] roles)
    {
        var factory = this.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                // Register the role set as a singleton so the auth handler can resolve it.
                services.AddSingleton(new ReportingTestRoleSet(roles));

                // Override the authentication scheme to include the requested roles.
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = ReportingRoleFakeAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = ReportingRoleFakeAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, ReportingRoleFakeAuthHandler>(
                    ReportingRoleFakeAuthHandler.SchemeName, _ => { });

                services.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = ReportingRoleFakeAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = ReportingRoleFakeAuthHandler.SchemeName;
                });
            });

            builder.ConfigureAppConfiguration(config =>
            {
                // Enable the Reporting module for these tests.
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Reporting:ModuleEnabled"] = "true"
                });
            });
        });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", IntegrationTestConstants.TestBearerToken);

        return client;
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

/// <summary>
/// Carries the Dataverse security role names that <see cref="ReportingRoleFakeAuthHandler"/>
/// should inject into the test user's claims. Registered as a singleton by
/// <see cref="IntegrationTestFixture.CreateReportingClient"/> so the handler can resolve it from DI.
/// </summary>
internal sealed class ReportingTestRoleSet
{
    public string[] Roles { get; }

    public ReportingTestRoleSet(string[] roles)
    {
        Roles = roles;
    }
}

/// <summary>
/// Fake authentication handler for Reporting endpoint tests.
/// Injects the Dataverse security role claims provided via <see cref="ReportingTestRoleSet"/>
/// into the test user identity so that <see cref="ReportingAuthorizationFilter"/> correctly
/// resolves privilege levels without making real Entra ID token calls.
///
/// Used exclusively by <see cref="IntegrationTestFixture.CreateReportingClient"/>.
/// </summary>
internal sealed class ReportingRoleFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ReportingFakeAuth";

    private readonly ReportingTestRoleSet _roleSet;

    public ReportingRoleFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ReportingTestRoleSet roleSet)
        : base(options, logger, encoder)
    {
        _roleSet = roleSet;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));

        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader))
            return Task.FromResult(AuthenticateResult.Fail("Empty Authorization header"));

        var claims = new List<Claim>
        {
            new("oid", IntegrationTestConstants.TestUserId),
            new(ClaimTypes.NameIdentifier, IntegrationTestConstants.TestUserId),
            new(ClaimTypes.Name, "Reporting Test User"),
            new("preferred_username", "reporting-test@contoso.com"),
            new("businessunit", "bu-test"),
        };

        // Inject each requested role as both "roles" and ClaimTypes.Role so that
        // ReportingAuthorizationFilter's IsInRole + HasClaim checks both succeed.
        foreach (var role in _roleSet.Roles)
        {
            claims.Add(new Claim("roles", role));
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
