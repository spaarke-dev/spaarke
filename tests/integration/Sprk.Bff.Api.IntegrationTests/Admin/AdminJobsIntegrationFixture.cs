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
using Spaarke.Scheduling;

namespace Sprk.Bff.Api.IntegrationTests.Admin;

/// <summary>
/// WebApplicationFactory fixture for R3 task 025 — end-to-end integration coverage of the
/// <c>/api/admin/jobs/*</c> endpoint surface. Complements the in-process WebApplicationFactory
/// unit-flavored tests at <c>tests/unit/Sprk.Bff.Api.Tests/Api/Admin/JobsEndpointsTests.cs</c>
/// by exercising the FULL production bootstrap (all DI modules + the real <c>SystemAdmin</c>
/// authorization policy) so wiring regressions surface at integration scope.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a separate fixture from <c>DataverseIntegrationTestFixture</c></b>: the Dataverse
/// fixture is specialised for the Dataverse-passthrough endpoint suite (mocks
/// <see cref="IDataversePrivilegeChecker"/>, swaps <see cref="IFetchXmlEntityExtractor"/>).
/// The admin-jobs surface needs neither of those substitutions — it needs (a) the real
/// <c>ScheduledJobRegistry</c> + <c>InMemoryBackgroundJobStore</c> (production DI shape from
/// <c>SchedulingModule</c>), (b) an auth handler that can emit admin vs non-admin vs
/// unauthenticated identities per request, and (c) the production
/// <c>RequireAuthorization("SystemAdmin")</c> policy left intact. This fixture mirrors the
/// canonical configuration set from <c>DataverseIntegrationTestFixture</c> so the Program.cs
/// option validators all pass (per <c>test-fixture-contracts.md</c> §F.2 — fixture-config FIRST).
/// </para>
///
/// <para>
/// <b>Auth model</b>: <see cref="AdminJobsIntegrationFakeAuthHandler"/> reads the
/// <c>X-Test-Role</c> header to pick the emitted identity shape:
/// </para>
/// <list type="bullet">
///   <item><c>X-Test-Role: admin</c> → identity with <c>roles=SystemAdmin</c> (passes the policy).</item>
///   <item><c>X-Test-Role: user</c> → identity with <c>roles=User</c> (authenticated; fails → 403).</item>
///   <item>No <c>Authorization</c> header → 401.</item>
/// </list>
/// </remarks>
public class AdminJobsIntegrationFixture : WebApplicationFactory<Program>
{
    private InMemoryBackgroundJobStore? _store;
    private ScheduledJobRegistry? _registry;

    /// <summary>Live in-process store (resolved after first client creation).</summary>
    public InMemoryBackgroundJobStore Store =>
        _store ?? throw new InvalidOperationException(
            "Store unavailable until the host has been built. Trigger via Create*Client() first.");

    /// <summary>Live in-process registry (resolved after first client creation).</summary>
    public ScheduledJobRegistry Registry =>
        _registry ?? throw new InvalidOperationException(
            "Registry unavailable until the host has been built. Trigger via Create*Client() first.");

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Mirror the canonical configuration set from DataverseIntegrationTestFixture so
        // every Program.cs validator passes. Any new options validator added later needs a
        // corresponding key here (per test-fixture-contracts.md §F.2).
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
            // Replace Redis with deterministic in-memory cache (ADR-009).
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache, MemoryDistributedCache>();
            services.RemoveAll<IMemoryCache>();
            services.AddSingleton<IMemoryCache, MemoryCache>(sp =>
                new MemoryCache(Options.Create(new MemoryCacheOptions())));

            // Fake authentication handler that emits admin / user / no-identity per X-Test-Role
            // header. Production SystemAdmin policy left in place — non-admin → 403.
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = AdminJobsIntegrationFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = AdminJobsIntegrationFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, AdminJobsIntegrationFakeAuthHandler>(
                AdminJobsIntegrationFakeAuthHandler.SchemeName, _ => { });

            // Override Microsoft Identity Web's PostConfigure (matches Dataverse fixture pattern).
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = AdminJobsIntegrationFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = AdminJobsIntegrationFakeAuthHandler.SchemeName;
            });

            // Strip all hosted services so background workers (ScheduledJobHost cron loop,
            // ServiceBusJobProcessor, SchedulingBootstrapHostedService) don't run during tests —
            // the admin endpoints don't need the loop, and the bootstrap is replaced with
            // direct Registry/Store seeding inside each test for determinism.
            //
            // Note: removing IHostedService is *the* asymmetric piece of this fixture vs production
            // (per bff-extensions.md §F.1) — see also DataverseIntegrationTestFixture L#229 for
            // the same pattern. The ScheduledJobRegistry + InMemoryBackgroundJobStore are still
            // resolvable as DI singletons (SchedulingModule registers them as such), so the
            // admin endpoints work end-to-end against the production wiring.
            services.RemoveAll<IHostedService>();

            // Replace IDataverseService with a loose mock so any incidental code paths that try
            // to reach Dataverse (e.g., Tag/Healthz checks, AnalysisOrchestrationService internals)
            // don't try the real network. Admin endpoints themselves do NOT touch IDataverseService —
            // they only consume ScheduledJobRegistry + IBackgroundJobStore + ScheduledJobHost.
            services.RemoveAll<IDataverseService>();
            var dataverseMock = new Mock<IDataverseService>(MockBehavior.Loose);
            dataverseMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.AddSingleton(dataverseMock.Object);
        });
    }

    /// <summary>HttpClient authenticated as SystemAdmin — passes the SystemAdmin policy.</summary>
    public HttpClient CreateAdminClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Test-Role", "admin");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "admin-token");
        InitializeReferences();
        return client;
    }

    /// <summary>HttpClient authenticated as a non-admin user — should 403 on admin endpoints.</summary>
    public HttpClient CreateNonAdminClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Test-Role", "user");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");
        InitializeReferences();
        return client;
    }

    /// <summary>HttpClient with NO Authorization header — should 401 on admin endpoints.</summary>
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
    /// Reset registry + store between tests. The fixture is shared (IClassFixture) so state
    /// accumulates without reset. Production types intentionally don't expose Clear() (single
    /// host invariant in production); we reflect over the private dictionaries as a test-only
    /// seam (mirrors the canonical pattern in tests/unit/.../AdminJobsTestFixture-consumers).
    /// </summary>
    public void ResetSchedulingState()
    {
        var runsField = typeof(InMemoryBackgroundJobStore)
            .GetField("_runs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var jobsField = typeof(InMemoryBackgroundJobStore)
            .GetField("_jobs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var runs = runsField!.GetValue(Store);
        var jobs = jobsField!.GetValue(Store);
        runs!.GetType().GetMethod("Clear")!.Invoke(runs, null);
        jobs!.GetType().GetMethod("Clear")!.Invoke(jobs, null);

        var registryField = typeof(ScheduledJobRegistry)
            .GetField("_jobs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var registryJobs = registryField!.GetValue(Registry);
        registryJobs!.GetType().GetMethod("Clear")!.Invoke(registryJobs, null);
    }
}

/// <summary>
/// Fake authentication handler scoped to the admin-jobs integration suite. Honors the
/// <c>X-Test-Role</c> header to choose between admin and non-admin identities, allowing a
/// single fixture instance to service 401 / 403 / 200 test paths without rewiring auth.
/// </summary>
internal sealed class AdminJobsIntegrationFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "AdminJobsIntegrationFakeAuth";

    public AdminJobsIntegrationFakeAuthHandler(
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

        var role = Request.Headers["X-Test-Role"].ToString();
        if (string.IsNullOrWhiteSpace(role))
        {
            return Task.FromResult(AuthenticateResult.Fail("No X-Test-Role header"));
        }

        // SystemAdmin policy (AuthorizationModule.cs:241) accepts role=Admin or role=SystemAdmin
        // (claim type "roles" — emit accordingly).
        var claims = new List<Claim>
        {
            new("oid", "test-admin-jobs-integration-user"),
            new(ClaimTypes.NameIdentifier, "test-admin-jobs-integration-user"),
            new(ClaimTypes.Name, "Admin Jobs Integration Test User"),
            new("name", "Admin Jobs Integration Test User"),
        };

        claims.Add(string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase)
            ? new Claim("roles", "SystemAdmin")
            : new Claim("roles", "User"));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
