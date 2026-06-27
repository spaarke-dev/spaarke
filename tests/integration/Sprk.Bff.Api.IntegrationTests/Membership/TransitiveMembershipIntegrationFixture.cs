// R3 Part 1D — Task 054 (2026-06-21): Integration-test fixture for the
// transitive memberships (`includeRelated`) end-to-end surface.
//
// Mirrors AdminJobsIntegrationFixture (task 025) — full production BFF bootstrap with:
//   - Canonical config-key set so Program.cs validators all pass
//     (per test-fixture-contracts.md §F.2 — fixture-config FIRST inspection protocol)
//   - In-memory IDistributedCache substitution (ADR-009 — Redis disabled in tests)
//   - All IHostedService stripped to avoid background workers running during tests
//   - Mocked IMembershipResolverService + IDataverseService (the resolver mock is the
//     unit-of-test; Dataverse mock only services the AAD-oid → systemuserid lookup)
//   - Fake auth handler emitting the X-Test-Oid header value as the `oid` claim
//
// Why a separate fixture from DataverseIntegrationTestFixture / AdminJobsIntegrationFixture:
//   - Dataverse fixture: specialized for Dataverse-passthrough endpoints (different mocks)
//   - AdminJobs fixture: SystemAdmin policy + scheduling DI (different surface)
//   - Membership endpoint needs: auth + IMembershipResolverService mock + IDataverseService
//     mock for the AAD lookup. Cleanest as its own fixture.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-1D.1, FR-1D.2, FR-1D.3,
//            AC-1D.1, AC-1D.2; design.md Part 1 § "Endpoint contract".

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
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;

namespace Sprk.Bff.Api.IntegrationTests.Membership;

/// <summary>
/// WebApplicationFactory fixture for R3 task 054 (Phase 1D transitive memberships).
/// Bootstraps the full BFF with the IMembershipResolverService mocked so tests can
/// assert endpoint wiring (auth gate, query-param parsing, 400 mapping for
/// MembershipDepthExceededException, JSON shape pass-through) without standing up
/// a real Dataverse environment.
/// </summary>
public class TransitiveMembershipIntegrationFixture : WebApplicationFactory<Program>
{
    /// <summary>Per-fixture resolver mock — tests Setup() it before each request.</summary>
    public Mock<IMembershipResolverService> ResolverMock { get; } = new(MockBehavior.Strict);

    /// <summary>Per-fixture Dataverse mock — services the AAD-oid → systemuserid lookup.</summary>
    public Mock<IDataverseService> DataverseMock { get; } = new(MockBehavior.Loose);

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Canonical config-key set per test-fixture-contracts.md §F.2.
        // Mirrors AdminJobsIntegrationFixture; any new Program.cs validator needs a key here.
        builder.ConfigureHostConfiguration(config =>
        {
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
            // Replace Redis with in-memory cache (ADR-009).
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache, MemoryDistributedCache>();
            services.RemoveAll<IMemoryCache>();
            services.AddSingleton<IMemoryCache, MemoryCache>(sp =>
                new MemoryCache(Options.Create(new MemoryCacheOptions())));

            // Fake auth handler that emits `oid` from X-Test-Oid header.
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TransitiveMembershipFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TransitiveMembershipFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TransitiveMembershipFakeAuthHandler>(
                TransitiveMembershipFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = TransitiveMembershipFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TransitiveMembershipFakeAuthHandler.SchemeName;
            });

            // Strip hosted services so background workers don't run during tests
            // (same rationale as AdminJobsIntegrationFixture).
            services.RemoveAll<IHostedService>();

            // Mocked IMembershipResolverService — the unit-of-test.
            services.RemoveAll<IMembershipResolverService>();
            services.AddSingleton(ResolverMock.Object);

            // Mocked IDataverseService — services the AAD-oid → systemuserid lookup only.
            services.RemoveAll<IDataverseService>();
            DataverseMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.AddSingleton(DataverseMock.Object);
        });
    }

    /// <summary>HttpClient with authenticated identity carrying the given AAD oid.</summary>
    public HttpClient CreateAuthenticatedClient(Guid aadOid)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Test-Oid", aadOid.ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }

    /// <summary>HttpClient with NO Authorization header — expected to 401.</summary>
    public HttpClient CreateUnauthenticatedClient()
        => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>
    /// Configure the mocked Dataverse service to return the given systemuserid (or zero
    /// rows when null) for the AAD-oid → systemuserid cross-reference query the endpoint
    /// issues for the calling user.
    /// </summary>
    public void SeedSystemUserLookup(Guid aadOid, Guid? systemUserId)
    {
        DataverseMock.Reset();
        DataverseMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
        DataverseMock
            .Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q =>
                q.EntityName == "systemuser" &&
                q.Criteria.Conditions.Any(c =>
                    c.AttributeName == "azureactivedirectoryobjectid" &&
                    c.Values.Count == 1 &&
                    (Guid)c.Values[0]! == aadOid)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var collection = new EntityCollection();
                if (systemUserId.HasValue && systemUserId.Value != Guid.Empty)
                {
                    var entity = new Entity("systemuser") { Id = systemUserId.Value };
                    entity["systemuserid"] = systemUserId.Value;
                    collection.Entities.Add(entity);
                }
                return collection;
            });
    }
}

/// <summary>
/// Fake authentication handler scoped to the transitive-membership integration suite.
/// Emits an authenticated identity carrying the AAD <c>oid</c> claim from the
/// <c>X-Test-Oid</c> header. No <c>Authorization</c> header → 401.
/// </summary>
internal sealed class TransitiveMembershipFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TransitiveMembershipFakeAuth";

    public TransitiveMembershipFakeAuthHandler(
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

        var oid = Request.Headers["X-Test-Oid"].ToString();
        if (string.IsNullOrWhiteSpace(oid))
        {
            return Task.FromResult(AuthenticateResult.Fail("No X-Test-Oid header"));
        }

        var claims = new List<Claim>
        {
            new("oid", oid),
            new(System.Security.Claims.ClaimTypes.NameIdentifier, oid),
            new(System.Security.Claims.ClaimTypes.Name, $"Test User {oid}"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
