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
using Sprk.Bff.Api.Services.Dataverse.FetchXml;
using Sprk.Bff.Api.Services.Dataverse.Privileges;

namespace Sprk.Bff.Api.IntegrationTests;

/// <summary>
/// Shared constants for integration tests.
/// </summary>
internal static class DataverseTestConstants
{
    /// <summary>Test caller oid — must be a valid GUID for the DataverseAuthorizationFilter.</summary>
    public const string TestUserOid = "11111111-1111-1111-1111-111111111111";

    /// <summary>Test bearer token. The fake auth handler accepts any non-empty value.</summary>
    public const string TestBearerToken = "test-bearer-token";

    /// <summary>SavedQuery id used for happy-path tests (RetrieveAsync mock returns a populated payload).</summary>
    public static readonly Guid TestSavedQueryId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");

    /// <summary>SavedQuery id used for not-found tests (RetrieveAsync mock returns null / throws).</summary>
    public static readonly Guid MissingSavedQueryId = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");
}

/// <summary>
/// WebApplicationFactory for Dataverse passthrough endpoint integration tests (task 016).
/// </summary>
/// <remarks>
/// <para>
/// Bootstraps the full BFF API in-process with:
/// </para>
/// <list type="bullet">
///   <item><description>All required configuration injected via ConfigureHostConfiguration so module validators pass.</description></item>
///   <item><description><see cref="IDataverseService"/> replaced by a Moq'd mock so the filter and RecordService paths do not require a real Dataverse connection.</description></item>
///   <item><description><see cref="IDataversePrivilegeChecker"/> replaced by a Moq'd mock so privilege grant/deny + call counts are observable per-test.</description></item>
///   <item><description><see cref="MemoryDistributedCache"/> replaces Redis (deterministic; ADR-009).</description></item>
///   <item><description>A fake JWT authentication handler injects the test caller's <c>oid</c> claim so the filter sees a valid identity.</description></item>
///   <item><description>All <see cref="IHostedService"/>s removed so background workers do not contact external services.</description></item>
/// </list>
/// <para>
/// <b>Why no real <c>DataverseServiceClientImpl</c>?</b> The three "internal sealed" services
/// (<c>SavedQueryService</c>, <c>MetadataService</c>, <c>FetchService</c>) hard-cast
/// <see cref="IDataverseService"/> to the concrete <see cref="DataverseServiceClientImpl"/> to reach
/// the underlying <c>ServiceClient</c> (which is itself a sealed type without a public mockable
/// contract). For integration tests we substitute a <c>Mock&lt;IDataverseService&gt;</c>; happy-path
/// service code paths surface as 500 (recorded in deviations). The tests in this project cover the
/// behaviors that DO NOT need a live ServiceClient: the authorization filter (the security gate),
/// endpoint-level 400 validation, RecordService happy-path with <c>$select</c> (uses
/// <see cref="IDataverseService.RetrieveAsync"/> directly), and the cross-entity privilege bypass
/// check (entirely filter-level).
/// </para>
/// </remarks>
public class DataverseIntegrationTestFixture : WebApplicationFactory<Program>
{
    /// <summary>
    /// Mock privilege checker — internal because <see cref="IDataversePrivilegeChecker"/> is internal
    /// to <c>Sprk.Bff.Api</c>. Tests in this assembly have visibility via <c>InternalsVisibleTo</c>.
    /// </summary>
    internal Mock<IDataversePrivilegeChecker> PrivilegeCheckerMock { get; } = new(MockBehavior.Loose);

    /// <summary>
    /// Mock Dataverse service. <see cref="MockBehavior.Loose"/> so tests that don't configure the
    /// mock get null returns rather than throws (avoids cascading failures on tests focused on the
    /// filter, which never reach the service).
    /// </summary>
    public Mock<IDataverseService> DataverseServiceMock { get; } = new(MockBehavior.Loose);

    /// <summary>
    /// Real FetchXmlEntityExtractor (it's pure XML parsing — using the real one yields fidelity to
    /// the production cross-entity check and avoids re-implementing the parser in a mock).
    /// Internal because <see cref="IFetchXmlEntityExtractor"/> is internal.
    /// </summary>
    internal IFetchXmlEntityExtractor FetchXmlExtractor { get; } = new FetchXmlEntityExtractor();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            // Minimal configuration required for Program.cs module validators to pass.
            // Mirrors Spe.Integration.Tests/IntegrationTestFixture.cs (the canonical Spaarke pattern).
            var settings = new Dictionary<string, string?>
            {
                // Service Bus
                ["ConnectionStrings:ServiceBus"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=dGVzdA==",

                // CORS
                ["Cors:AllowedOrigins:0"] = "https://localhost:5173",

                // Azure AD / identity
                ["UAMI_CLIENT_ID"] = "test-client-id",
                ["TENANT_ID"] = "test-tenant-id",
                ["API_APP_ID"] = "test-app-id",
                ["API_CLIENT_SECRET"] = "test-secret",

                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "test-tenant-id",
                ["AzureAd:ClientId"] = "test-app-id",
                ["AzureAd:Audience"] = "api://test-app-id",

                // SpeAdmin
                ["SpeAdmin:KeyVaultUri"] = "https://test-keyvault.vault.azure.net/",

                // Cosmos
                ["CosmosPersistence:Endpoint"] = "https://test.documents.azure.com:443/",
                ["CosmosPersistence:DatabaseName"] = "spaarke-ai-test",

                // Graph
                ["Graph:TenantId"] = "test-tenant-id",
                ["Graph:ClientId"] = "test-client-id",
                ["Graph:ClientSecret"] = "test-client-secret",
                ["Graph:UseManagedIdentity"] = "false",
                ["Graph:Scopes:0"] = "https://graph.microsoft.com/.default",

                // Dataverse
                ["Dataverse:EnvironmentUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ClientId"] = "test-client-id",
                ["Dataverse:ClientSecret"] = "test-client-secret",
                ["Dataverse:TenantId"] = "test-tenant-id",

                ["ManagedIdentity:ClientId"] = "00000000-0000-0000-0000-000000000001",

                // Service Bus
                ["ServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=dGVzdA==",
                ["ServiceBus:QueueName"] = "sdap-jobs",

                // Redis (off — use in-memory cache)
                ["Redis:Enabled"] = "false",

                // Office rate limiting
                ["OfficeRateLimit:Enabled"] = "false",

                // Document Intelligence + Analysis (so all modules register)
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

                // PowerBI
                ["PowerBi:TenantId"] = "test-powerbi-tenant-id",
                ["PowerBi:ClientId"] = "test-powerbi-client-id",
                ["PowerBi:ClientSecret"] = "test-powerbi-client-secret",
                ["PowerBi:ApiUrl"] = "https://api.powerbi.com",
                ["PowerBi:Scope"] = "https://analysis.windows.net/.default",
                ["Reporting:ModuleEnabled"] = "false",

                // Resilience defaults
                ["AiSearchResilience:MaxRetryAttempts"] = "3",
                ["AiSearchResilience:CircuitBreakerFailureThreshold"] = "5",
                ["AiSearchResilience:CircuitBreakerDuration"] = "00:00:30",

                ["GraphResilience:MaxRetryAttempts"] = "3",
                ["GraphResilience:RetryDelay"] = "00:00:01",
                ["GraphResilience:CircuitBreakerFailureThreshold"] = "5",
                ["GraphResilience:CircuitBreakerDuration"] = "00:00:30",

                // ModelSelector
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
            // Replace Redis with deterministic in-memory cache (ADR-009).
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache, MemoryDistributedCache>();
            services.RemoveAll<IMemoryCache>();
            services.AddSingleton<IMemoryCache, MemoryCache>(sp =>
                new MemoryCache(Options.Create(new MemoryCacheOptions())));

            // Fake auth handler that recognises any non-empty Authorization header and injects
            // the test caller's oid claim. The DataverseAuthorizationFilter reads "oid".
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = DataverseFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = DataverseFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, DataverseFakeAuthHandler>(
                DataverseFakeAuthHandler.SchemeName, _ => { });

            // Override Microsoft Identity Web's PostConfigure (matches Spe pattern).
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = DataverseFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = DataverseFakeAuthHandler.SchemeName;
            });

            // Remove all hosted services so background workers don't contact external systems.
            services.RemoveAll<IHostedService>();

            // Replace IDataverseService with the test mock. SavedQueryService, MetadataService,
            // FetchService all hard-cast IDataverseService → DataverseServiceClientImpl to reach
            // ServiceClient; the mock fails this cast and surfaces as 500. This is documented in
            // 016-deviations.md. RecordService happy-path uses IDataverseService.RetrieveAsync
            // directly and IS testable via this mock.
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(DataverseServiceMock.Object);

            // Replace IDataversePrivilegeChecker with the test mock. This is THE security gate —
            // every authorization test calls into this mock.
            services.RemoveAll<IDataversePrivilegeChecker>();
            services.AddSingleton(PrivilegeCheckerMock.Object);

            // Replace IFetchXmlEntityExtractor with the real implementation (pure XML parsing).
            // The cross-entity privilege bypass test depends on the real parser's behavior.
            services.RemoveAll<IFetchXmlEntityExtractor>();
            services.AddSingleton(FetchXmlExtractor);

            // Register a no-op "standard" rate-limit policy.
            //
            // FINDING (recorded in 016-deviations.md §D-016-03): SavedQueryEndpoints.cs line 45 + 59
            // attach `.RequireRateLimiting("standard")` to both savedquery endpoints, but the
            // production RateLimitingModule.cs registers no policy named "standard" (it registers
            // "graph-read", "graph-write", "dataverse-query", etc.). In production this throws
            // InvalidOperationException on first request to either savedquery endpoint, surfacing
            // as 500. The tests register a no-op limiter here so the savedquery endpoint tests
            // can verify the auth filter + handler behavior. The production bug remains; this is
            // a follow-up task (not in scope for task 016).
            // Configure<RateLimiterOptions> mutates the existing options registered by
            // AddRateLimitingModule(), so the "standard" policy is added on top of the production
            // policy set without replacing the limiter or re-registering middleware.
            services.Configure<RateLimiterOptions>(options =>
            {
                options.AddPolicy("standard", _ =>
                    RateLimitPartition.GetNoLimiter("standard"));
            });
        });
    }

    /// <summary>
    /// Creates an HttpClient with the test bearer token pre-configured. The fake auth handler
    /// accepts any non-empty Authorization header and injects the test oid claim.
    /// </summary>
    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", DataverseTestConstants.TestBearerToken);
        client.DefaultRequestHeaders.Add("User-Agent", "SDAP-Integration-Tests/1.0");

        return client;
    }

    /// <summary>
    /// Creates an HttpClient WITHOUT an Authorization header for 401 tests.
    /// </summary>
    public HttpClient CreateUnauthenticatedClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }
}

/// <summary>
/// Fake authentication handler — accepts any non-empty Authorization header and injects the test
/// oid claim. Mirrors Spe.Integration.Tests.IntegrationTestFakeAuthHandler.
/// </summary>
internal sealed class DataverseFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DataverseFakeAuth";

    public DataverseFakeAuthHandler(
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

        var claims = new[]
        {
            new Claim("oid", DataverseTestConstants.TestUserOid),
            new Claim(ClaimTypes.NameIdentifier, DataverseTestConstants.TestUserOid),
            new Claim(ClaimTypes.Name, "Dataverse Integration Test User"),
            new Claim("name", "Dataverse Integration Test User"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
