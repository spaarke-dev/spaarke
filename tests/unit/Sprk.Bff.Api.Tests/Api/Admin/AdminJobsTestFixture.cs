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
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Tests.Mocks;

namespace Sprk.Bff.Api.Tests.Api.Admin;

/// <summary>
/// Test fixture for <c>/api/admin/jobs/*</c> endpoint tests (R3 task 020). Bootstraps the BFF
/// in-process with the in-memory Spaarke.Scheduling backing store + a fake authentication
/// handler that can emit either an admin or non-admin identity (or no auth) per test.
/// </summary>
/// <remarks>
/// <para><b>Why a dedicated fixture (not reusing CustomWebAppFactory or WorkspaceTestFixture)</b>:
/// The admin tests need (a) deterministic registry seeding per-test (a single shared registry
/// across tests would cause inter-test pollution), (b) the ability to choose admin vs non-admin
/// claims per HttpClient, and (c) full BFF bootstrap (so the real <c>SystemAdmin</c> policy is
/// applied — not a stripped-down test policy). This fixture mirrors the canonical configuration
/// set from <see cref="CustomWebAppFactory"/> (per `test-fixture-contracts.md` §F.2 — all required
/// config keys must be present so the host builds without options-validation failures).</para>
///
/// <para><b>Auth model</b>: <see cref="AdminJobsFakeAuthHandler"/> emits one of three identity
/// shapes based on a header:
/// <list type="bullet">
///   <item><c>X-Test-Role: admin</c> → identity with <c>roles=SystemAdmin</c> claim (passes SystemAdmin policy).</item>
///   <item><c>X-Test-Role: user</c> → identity with <c>roles=User</c> (authenticated; fails SystemAdmin → 403).</item>
///   <item>No <c>X-Test-Role</c> header → authentication fails → 401.</item>
/// </list>
/// </para>
/// </remarks>
public class AdminJobsTestFixture : WebApplicationFactory<Program>
{
    private InMemoryBackgroundJobStore? _store;
    private ScheduledJobRegistry? _registry;

    /// <summary>Resolves the shared in-process <see cref="InMemoryBackgroundJobStore"/> so tests
    /// can seed jobs / run records before issuing HTTP requests.</summary>
    public InMemoryBackgroundJobStore Store =>
        _store ?? throw new InvalidOperationException(
            "Store is only available after the host has been built. Trigger via CreateClient() first.");

    /// <summary>Resolves the shared <see cref="ScheduledJobRegistry"/> so tests can register
    /// <see cref="IScheduledJob"/> handlers without going through DI.</summary>
    public ScheduledJobRegistry Registry =>
        _registry ?? throw new InvalidOperationException(
            "Registry is only available after the host has been built. Trigger via CreateClient() first.");

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Canonical config-key set — mirrors CustomWebAppFactory so the host builds even when
        // this fixture is used in isolation. Any new validator added to Program.cs needs a
        // corresponding key added here (per test-fixture-contracts.md §F.2).
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
                // spaarke-redis-cache-remediation-r1 task 003 (FR-02 fail-fast): CacheModule now
                // throws unless either Redis is enabled OR AllowInMemoryFallback is set AND env
                // is Development. Opt the test host into the in-memory fallback branch so the
                // host can build. See bff-extensions.md §F.2 (Fixture-Config-FIRST).
                ["Redis:AllowInMemoryFallback"] = "true",
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
        // spaarke-redis-cache-remediation-r1 task 003 (FR-02): CacheModule's in-memory fallback
        // branch requires IHostEnvironment.IsDevelopment(). "Testing" environment trips Branch (c)
        // and throws at startup. Switch to "Development" so the fallback path runs.
        builder.UseEnvironment("Development");

        // Development environment defaults to ValidateScopes=true, which catches pre-existing
        // singleton→scoped DI lifetime issues in the production codebase (not introduced by this
        // PR). Original "Testing" environment defaulted ValidateScopes=false. Restore that
        // behavior explicitly so the existing tests pass unchanged.
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        builder.ConfigureTestServices(services =>
        {
            // Override auth — fake handler that honors X-Test-Role header to choose admin / user / none.
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = AdminJobsFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = AdminJobsFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, AdminJobsFakeAuthHandler>(
                AdminJobsFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = AdminJobsFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = AdminJobsFakeAuthHandler.SchemeName;
            });

            // Replace Graph factory so any incidental OBO-touching code doesn't try real MSAL.
            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            // Strip hosted services (no background workers in tests).
            services.RemoveAll<IHostedService>();

            // Mock Dataverse to avoid real network.
            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);
        });

        // After the host is built, capture references to the registered store + registry so tests
        // can seed them. Resolution happens on first CreateClient() per WebApplicationFactory contract.
        builder.ConfigureTestServices(services =>
        {
            services.AddHostedService<FixtureCaptureService>(); // ensures host gets built
        });
    }

    /// <summary>
    /// Build a client authenticated as a SystemAdmin.
    /// </summary>
    public HttpClient CreateAdminClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Test-Role", "admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin-token");
        InitializeReferences();
        return client;
    }

    /// <summary>
    /// Build a client authenticated as a non-admin user (should 403 on admin endpoints).
    /// </summary>
    public HttpClient CreateNonAdminClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Test-Role", "user");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "user-token");
        InitializeReferences();
        return client;
    }

    /// <summary>
    /// Build a client with NO authorization header (should 401).
    /// </summary>
    public HttpClient CreateUnauthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        InitializeReferences();
        return client;
    }

    private void InitializeReferences()
    {
        if (_store is not null && _registry is not null) return;
        _store = Services.GetRequiredService<InMemoryBackgroundJobStore>();
        _registry = Services.GetRequiredService<ScheduledJobRegistry>();
    }

    /// <summary>
    /// No-op hosted service whose only purpose is to keep the host alive after build so the
    /// fixture can resolve store + registry via <see cref="WebApplicationFactory{TEntryPoint}.Services"/>.
    /// </summary>
    private sealed class FixtureCaptureService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

/// <summary>
/// Fake authentication handler scoped to admin-jobs tests. Honors the <c>X-Test-Role</c> header
/// to choose between admin and non-admin identities, allowing a single fixture instance to
/// service 401 / 403 / 200 test paths without rewiring auth.
/// </summary>
internal sealed class AdminJobsFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "AdminJobsFakeAuth";

    public AdminJobsFakeAuthHandler(
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

        // Build a ClaimsPrincipal with either admin or user claims. SystemAdmin policy accepts
        // role=Admin OR role=SystemAdmin OR claim type "roles" with those values OR scope claim
        // containing "admin" — emit a roles claim so the assertion matches per AuthorizationModule.cs:241.
        var claims = new List<Claim>
        {
            new("oid", "test-admin-jobs-user-id"),
            new(ClaimTypes.NameIdentifier, "test-admin-jobs-user-id"),
            new(ClaimTypes.Name, "Test User"),
        };

        if (string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
        {
            claims.Add(new Claim("roles", "SystemAdmin"));
        }
        else
        {
            // Authenticated but lacking admin role → 403 on /api/admin/jobs/*
            claims.Add(new Claim("roles", "User"));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
