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
using Sprk.Bff.Api.Services.Workspace;

namespace Sprk.Bff.Api.Tests.Integration.Workspace;

/// <summary>
/// Constants used by workspace integration tests for known test identity values.
/// </summary>
public static class WorkspaceTestConstants
{
    /// <summary>The Entra ID object ID claim used by the WorkspaceAuthorizationFilter.</summary>
    public const string TestUserId = "test-user-00000000-0000-0000-0000-000000000001";

    /// <summary>Test bearer token value for fake authentication header.</summary>
    public const string TestBearerToken = "workspace-test-token";
}

/// <summary>
/// Shared WebApplicationFactory for workspace integration tests.
/// Bootstraps the full BFF API in-process with:
///   - All external dependencies replaced by in-memory or no-op fakes.
///   - MemoryDistributedCache replacing Redis (ADR-009: Redis-first in prod; in-memory for tests).
///   - A fake JWT authentication handler that injects a known user identity.
///   - Workspace services registered as normal (PriorityScoringService, EffortScoringService, etc.).
///
/// Implements IClassFixture so the factory is reused across tests in the same class,
/// reducing startup overhead for the full Minimal API pipeline.
/// </summary>
public class WorkspaceTestFixture : WebApplicationFactory<Program>
{
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

                // Document Intelligence — enabled so all AI services register
                ["DocumentIntelligence:Enabled"] = "true",
                ["DocumentIntelligence:OpenAiEndpoint"] = "https://test.openai.azure.com/",
                ["DocumentIntelligence:OpenAiKey"] = "test-key",
                ["DocumentIntelligence:OpenAiDeployment"] = "gpt-4o",

                // Analysis — enabled so all AI-dependent endpoints can be mapped
                ["Analysis:Enabled"] = "true",

                // AI Search (required for IRagService)
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

                // SpeAdmin options (required by SpeAdminModule — added to Program.cs)
                ["SpeAdmin:KeyVaultUri"] = "https://test.vault.azure.net/",

                // ManagedIdentity options (required by DataverseWebApiClient in SpeAdminModule)
                ["ManagedIdentity:ClientId"] = "test-managed-identity-client-id",
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
            // and WorkspaceAuthorizationFilter (which reads the "oid" claim).
            // ---------------------------------------------------------------
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = FakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = FakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, FakeAuthHandler>(
                FakeAuthHandler.SchemeName, _ => { });

            // ---------------------------------------------------------------
            // HOSTED SERVICES: Remove background workers that depend on
            // AI services not registered when DocumentIntelligence is disabled.
            // ---------------------------------------------------------------
            services.RemoveAll<IHostedService>();

            // ---------------------------------------------------------------
            // DATAVERSE: Mock to avoid real connection in tests.
            // ---------------------------------------------------------------
            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);

            // PortfolioService calls RetrieveMultipleAsync for matter queries.
            // Return 3 test matters matching assertions in WorkspaceEndpointsTests.
            var matterA = new Microsoft.Xrm.Sdk.Entity("sprk_matter", Guid.NewGuid());
            matterA["sprk_name"] = "Matter A";
            matterA["sprk_totalspend"] = new Microsoft.Xrm.Sdk.Money(125_000m);
            matterA["sprk_totalbudget"] = new Microsoft.Xrm.Sdk.Money(150_000m);
            matterA["sprk_overdueeventcount"] = 0;
            matterA["statecode"] = new Microsoft.Xrm.Sdk.OptionSetValue(0);

            var matterB = new Microsoft.Xrm.Sdk.Entity("sprk_matter", Guid.NewGuid());
            matterB["sprk_name"] = "Matter B (at risk)";
            matterB["sprk_totalspend"] = new Microsoft.Xrm.Sdk.Money(92_000m);
            matterB["sprk_totalbudget"] = new Microsoft.Xrm.Sdk.Money(80_000m);
            matterB["sprk_overdueeventcount"] = 2;
            matterB["statecode"] = new Microsoft.Xrm.Sdk.OptionSetValue(0);

            var matterC = new Microsoft.Xrm.Sdk.Entity("sprk_matter", Guid.NewGuid());
            matterC["sprk_name"] = "Matter C";
            matterC["sprk_totalspend"] = new Microsoft.Xrm.Sdk.Money(40_000m);
            matterC["sprk_totalbudget"] = new Microsoft.Xrm.Sdk.Money(60_000m);
            matterC["sprk_overdueeventcount"] = 0;
            matterC["statecode"] = new Microsoft.Xrm.Sdk.OptionSetValue(0);

            var entityCollection = new Microsoft.Xrm.Sdk.EntityCollection(
                new List<Microsoft.Xrm.Sdk.Entity> { matterA, matterB, matterC });
            dataverseServiceMock
                .Setup(d => d.RetrieveMultipleAsync(
                    It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(entityCollection);

            // RetrieveAsync for AI summary entity description (event/matter/project).
            dataverseServiceMock
                .Setup(d => d.RetrieveAsync(
                    It.IsAny<string>(), It.IsAny<Guid>(),
                    It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Microsoft.Xrm.Sdk.Entity("sprk_entity", Guid.NewGuid()));

            // GetDocumentAsync for AI summary on sprk_document entity type.
            dataverseServiceMock
                .Setup(d => d.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Spaarke.Dataverse.DocumentEntity
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Test Document",
                    FileName = "test.pdf",
                    ContainerId = Guid.NewGuid().ToString()
                });

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
            new AuthenticationHeaderValue("Bearer", WorkspaceTestConstants.TestBearerToken);

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
}

/// <summary>
/// Fake authentication handler used exclusively in integration tests.
/// When an Authorization header is present (any non-empty value), it creates
/// a ClaimsPrincipal with the "oid" claim set to <see cref="WorkspaceTestConstants.TestUserId"/>.
/// When no Authorization header is present, authentication fails so the pipeline
/// returns 401 — this allows testing unauthorized scenarios.
/// </summary>
internal sealed class FakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "FakeAuth";

    public FakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // If no Authorization header → fail authentication so we get 401
        if (!Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));

        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader))
            return Task.FromResult(AuthenticateResult.Fail("Empty Authorization header"));

        // Build a test ClaimsPrincipal that:
        //   - WorkspaceAuthorizationFilter will accept (reads "oid" claim)
        //   - SpeAdminAuthorizationFilter will accept (checks "roles" claim for Admin/SystemAdmin)
        var claims = new[]
        {
            new Claim("oid", WorkspaceTestConstants.TestUserId),
            new Claim(ClaimTypes.NameIdentifier, WorkspaceTestConstants.TestUserId),
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim("name", "Test User"),
            // Admin role required by SpeAdminAuthorizationFilter
            new Claim("roles", "SystemAdmin"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
