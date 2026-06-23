using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Tests.Mocks;

namespace Sprk.Bff.Api.Tests.Api.Admin;

/// <summary>
/// Test fixture for <c>/api/admin/membership/*</c> endpoint tests (R3 task 036). Bootstraps
/// the BFF in-process with a swappable mock <see cref="IMembershipFieldDiscoveryService"/>
/// + a fake authentication handler that emits admin / non-admin / unauthenticated identities.
/// </summary>
/// <remarks>
/// <para><b>Why a dedicated fixture (mirrors <see cref="AdminJobsTestFixture"/>)</b>: the
/// admin tests need full BFF bootstrap so the real <c>SystemAdmin</c> policy is applied
/// (per <c>test-fixture-contracts.md</c> §F.2). The discovery service is mocked so tests
/// can assert HTTP shape + behavior without standing up Dataverse metadata. The mock
/// reference is shared across the fixture so tests can call
/// <see cref="MembershipDiscoveryMock"/> to set up per-test expectations.</para>
///
/// <para><b>Config-key set</b>: mirrors <see cref="AdminJobsTestFixture"/> verbatim — any
/// new validator added to <c>Program.cs</c> needs a corresponding key added in BOTH
/// fixtures.</para>
/// </remarks>
public class AdminMembershipTestFixture : WebApplicationFactory<Program>
{
    /// <summary>
    /// Shared mock the tests configure per scenario. The fixture wires this instance into
    /// the DI container in place of the real <see cref="MembershipFieldDiscoveryService"/>.
    /// Reset by tests as needed via <see cref="Mock.Reset"/>.
    /// </summary>
    public Mock<IMembershipFieldDiscoveryService> MembershipDiscoveryMock { get; } = new(MockBehavior.Strict);

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
                ["Graph:UseManagedIdentity"] = "false",
                ["Graph:Scopes:0"] = "https://graph.microsoft.com/.default",
                ["Dataverse:EnvironmentUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ClientId"] = "test-client-id",
                ["Dataverse:ClientSecret"] = "test-client-secret",
                ["Dataverse:TenantId"] = "test-tenant-id",
                ["ServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["ServiceBus:QueueName"] = "sdap-jobs",
                ["DocumentIntelligence:Enabled"] = "true",
                ["DocumentIntelligence:OpenAiEndpoint"] = "https://test.openai.azure.com/",
                ["DocumentIntelligence:OpenAiKey"] = "test-key",
                ["DocumentIntelligence:OpenAiDeployment"] = "gpt-4o",
                ["Analysis:Enabled"] = "true",
                ["Analysis:UseStubResolver"] = "true",
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
                ["OfficeRateLimit:Enabled"] = "false",
                ["Redis:Enabled"] = "false",
                ["ModelSelector:DefaultModel"] = "gpt-4o",
                ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAI:ChatModelName"] = "gpt-4o",
                ["DocumentIntelligence:RecordMatchingEnabled"] = "true",
                ["AiSearchResilience:MaxRetryAttempts"] = "3",
                ["AiSearchResilience:CircuitBreakerFailureThreshold"] = "5",
                ["AiSearchResilience:CircuitBreakerDuration"] = "00:00:30",
                ["GraphResilience:MaxRetryAttempts"] = "3",
                ["GraphResilience:RetryDelay"] = "00:00:01",
                ["GraphResilience:CircuitBreakerFailureThreshold"] = "5",
                ["GraphResilience:CircuitBreakerDuration"] = "00:00:30",
                ["SpeAdmin:KeyVaultUri"] = "https://test.vault.azure.net/",
                ["ManagedIdentity:ClientId"] = "test-managed-identity-client-id",
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
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            // Fake auth — same X-Test-Role contract as AdminJobsTestFixture.
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = AdminMembershipFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = AdminMembershipFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, AdminMembershipFakeAuthHandler>(
                AdminMembershipFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = AdminMembershipFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = AdminMembershipFakeAuthHandler.SchemeName;
            });

            // Strip Graph + Dataverse so we don't touch real services during admin tests.
            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            // No background workers in test host.
            services.RemoveAll<IHostedService>();

            // Mock Dataverse — admin endpoints don't directly touch it (the mocked
            // IMembershipFieldDiscoveryService takes that responsibility), but other
            // services in the BFF DI graph may need a non-null IDataverseService.
            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);

            // Replace the real discovery service with the shared test mock so each
            // test can configure expectations directly via the fixture instance.
            services.RemoveAll<IMembershipFieldDiscoveryService>();
            services.RemoveAll<MembershipFieldDiscoveryService>();
            services.AddSingleton(MembershipDiscoveryMock.Object);
        });
    }

    /// <summary>Build a client authenticated as a SystemAdmin (passes the policy).</summary>
    public HttpClient CreateAdminClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Test-Role", "admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin-token");
        return client;
    }

    /// <summary>Build a client authenticated as a non-admin (fails the policy → 403).</summary>
    public HttpClient CreateNonAdminClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Test-Role", "user");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "user-token");
        return client;
    }

    /// <summary>Build a client with no Authorization header (→ 401).</summary>
    public HttpClient CreateUnauthenticatedClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }
}

/// <summary>
/// Fake authentication handler scoped to admin-membership tests. Mirrors the contract
/// of <see cref="AdminJobsFakeAuthHandler"/>: <c>X-Test-Role: admin | user</c> → success
/// with the corresponding claims; missing Authorization header → 401.
/// </summary>
internal sealed class AdminMembershipFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "AdminMembershipFakeAuth";

    public AdminMembershipFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));

        var role = Request.Headers["X-Test-Role"].ToString();
        if (string.IsNullOrWhiteSpace(role))
            return Task.FromResult(AuthenticateResult.Fail("No X-Test-Role header"));

        var claims = new List<Claim>
        {
            new("oid", "test-admin-membership-user-id"),
            new(ClaimTypes.NameIdentifier, "test-admin-membership-user-id"),
            new(ClaimTypes.Name, "Test User"),
        };

        if (string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
        {
            claims.Add(new Claim("roles", "SystemAdmin"));
        }
        else
        {
            claims.Add(new Claim("roles", "User"));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
