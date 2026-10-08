using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
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
using Spaarke.Contracts.Provisioning;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Platform;

/// <summary>
/// <c>POST /api/platform/keyless-proof</c> (customer-provisioning task 230b) — route, status, ProblemDetails and
/// payload shape, through the REAL Program (real route, real filter, real <c>KeylessProofService</c>); only the
/// AI-owned probe facade is substituted, because its probes call Azure.
/// </summary>
[Trait("category", "authorization")]
public sealed class KeylessProofEndpointContractTests : IClassFixture<KeylessProofHost>
{
    private readonly KeylessProofHost _host;

    public KeylessProofEndpointContractTests(KeylessProofHost host) => _host = host;

    [Fact]
    public async Task Prove_WithoutAToken_Is401()
    {
        var response = await _host.CreateClient().PostAsync(KeylessProofContract.Route, null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Prove_AnApplicationTokenWithoutTheRole_Is403()
    {
        var response = await _host.Caller(roles: "SystemAdmin").PostAsync(KeylessProofContract.Route, null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeAsync(response)).Should().Be("sdap.access.deny.keyless_proof_role");
    }

    [Fact]
    public async Task Prove_AUserTokenCarryingTheRole_Is403()
    {
        // A delegated token (scp) acts for a user — refused even if a future change let users hold the role.
        var response = await _host.Caller(roles: KeylessProofContract.AppRoleValue, scope: "user_impersonation")
            .PostAsync(KeylessProofContract.Route, null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Prove_ATokenWhoseIdtypIsUser_Is403()
    {
        var response = await _host.Caller(roles: KeylessProofContract.AppRoleValue, idtyp: "user")
            .PostAsync(KeylessProofContract.Route, null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Prove_ATokenForAnotherAudience_Is403()
    {
        var response = await _host.Caller(roles: KeylessProofContract.AppRoleValue, audience: "api://copilot-plugin")
            .PostAsync(KeylessProofContract.Route, null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Prove_AV1AppOnlyTokenWithoutIdtyp_IsAdmitted()
    {
        var response = await _host.Caller(roles: KeylessProofContract.AppRoleValue).PostAsync(KeylessProofContract.Route, null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Prove_TheL2ApplicationWithTheRole_GetsOneResultPerService_AndARefusalStaysARefusal()
    {
        var response = await _host.Caller(roles: KeylessProofContract.AppRoleValue, idtyp: "app")
            .PostAsync(KeylessProofContract.Route, null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var services = body.RootElement.GetProperty("services").EnumerateArray().ToList();

        services.Select(s => s.GetProperty("service").GetString()).Should().Equal(KeylessProofContract.Services.All);
        Outcome(services, KeylessProofContract.Services.OpenAiChat).Should().Be(KeylessProofContract.Outcomes.Refused,
            "a service that answered 403 is reported as refused, never as healthy");
        // The fixture configures Service Bus with a connection string and Redis disabled — the real platform probes.
        Outcome(services, KeylessProofContract.Services.ServiceBus).Should().Be(KeylessProofContract.Outcomes.KeyCredential);
        Outcome(services, KeylessProofContract.Services.Redis).Should().Be(KeylessProofContract.Outcomes.NotConfigured);

        services.Should().OnlyContain(s =>
            s.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal)
                .SequenceEqual(new[] { "code", "elapsedMs", "outcome", "service", "statusCode" }),
            "a result carries status and timing only — no data, secret or exception text");
    }

    private static string? Outcome(IEnumerable<JsonElement> services, string service)
        => services.Single(s => s.GetProperty("service").GetString() == service).GetProperty("outcome").GetString();

    private static async Task<string?> ReasonCodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("reasonCode", out var code) ? code.GetString() : null;
    }
}

/// <summary>The in-process host: the real Program, a header-driven fake token, and a stubbed AI probe facade.</summary>
public sealed class KeylessProofHost : WebApplicationFactory<Program>
{
    /// <summary>What the stubbed AI-owned probes report: everything proved except a refused chat call.</summary>
    private static readonly IReadOnlyList<KeylessProbeResult> AiResults = new[]
    {
        new KeylessProbeResult(KeylessProofContract.Services.OpenAiChat, KeylessProofContract.Outcomes.Refused, 403, 12, "http-403"),
        new KeylessProbeResult(KeylessProofContract.Services.OpenAiEmbeddings, KeylessProofContract.Outcomes.Proved, 200, 9, "ok"),
        new KeylessProbeResult(KeylessProofContract.Services.DocumentIntelligence, KeylessProofContract.Outcomes.Proved, 200, 7, "ok"),
        new KeylessProbeResult(KeylessProofContract.Services.AiSearch, KeylessProofContract.Outcomes.Proved, 200, 5, "ok"),
        new KeylessProbeResult(KeylessProofContract.Services.Cosmos, KeylessProofContract.Outcomes.Proved, 200, 6, "ok"),
        new KeylessProbeResult(KeylessProofContract.Services.BlobStorage, KeylessProofContract.Outcomes.Proved, 200, 4, "ok"),
        new KeylessProbeResult(KeylessProofContract.Services.ContentSafetyPromptShield, KeylessProofContract.Outcomes.Proved, 200, 8, "ok"),
        new KeylessProbeResult(KeylessProofContract.Services.ContentSafetyGroundedness, KeylessProofContract.Outcomes.Proved, 200, 8, "ok"),
    };

    public HttpClient Caller(string? roles = null, string? scope = null, string? idtyp = null, string? audience = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "task-230b");
        if (roles is not null) client.DefaultRequestHeaders.Add(KeylessProofFakeAuthHandler.RolesHeader, roles);
        if (scope is not null) client.DefaultRequestHeaders.Add(KeylessProofFakeAuthHandler.ScopeHeader, scope);
        if (idtyp is not null) client.DefaultRequestHeaders.Add(KeylessProofFakeAuthHandler.IdtypHeader, idtyp);
        if (audience is not null) client.DefaultRequestHeaders.Add(KeylessProofFakeAuthHandler.AudienceHeader, audience);
        return client;
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "https://localhost:5173",
            ["UAMI_CLIENT_ID"] = "test-client-id",
            ["TENANT_ID"] = "test-tenant-id",
            ["API_APP_ID"] = "test-app-id",
            ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
            ["AzureAd:TenantId"] = "test-tenant-id",
            ["AzureAd:ClientId"] = "test-app-id",
            ["AzureAd:Audience"] = "api://test-app-id",
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
            ["ServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
            ["ServiceBus:QueueName"] = "sdap-jobs",
            // The AI stack ON, as the route-sweep harness boots it. The host does not start without AzureOpenAI:Endpoint +
            // ChatModelName: the chat endpoints map unconditionally but IChatClient is registered only when both are set
            // (pre-existing; recorded in the task 230b notes).
            ["DocumentIntelligence:Enabled"] = "true",
            ["DocumentIntelligence:OpenAiEndpoint"] = "https://test.openai.azure.com/",
            ["DocumentIntelligence:OpenAiKey"] = "test-key",
            ["DocumentIntelligence:OpenAiDeployment"] = "gpt-4o",
            ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
            ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
            ["Analysis:Enabled"] = "true",
            ["Analysis:UseStubResolver"] = "true",
            ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
            ["AzureOpenAI:ChatModelName"] = "gpt-4o",
            ["ModelSelector:DefaultModel"] = "gpt-4o",
            ["ModelSelector:IntentClassification"] = "gpt-4o-mini",
            ["ModelSelector:PlanGeneration"] = "o1-mini",
            ["ModelSelector:NodeGeneration"] = "gpt-4o",
            ["ModelSelector:ClarificationGeneration"] = "gpt-4o-mini",
            ["ModelSelector:AnalysisGeneration"] = "gpt-4o",
            ["ModelSelector:ExtractionGeneration"] = "gpt-4o-mini",
            ["ModelSelector:EmbeddingGeneration"] = "text-embedding-3-large",
            ["ModelSelector:FallbackGeneration"] = "gpt-4o",
            ["AiSearchResilience:MaxRetryAttempts"] = "3",
            ["AiSearchResilience:CircuitBreakerFailureThreshold"] = "5",
            ["AiSearchResilience:CircuitBreakerDuration"] = "00:00:30",
            ["AgentService:Endpoint"] = "https://test.services.ai.azure.com/api/projects/test-project",
            ["AgentService:AgentId"] = "test-agent-id",
            ["AgentService:MaxConcurrency"] = "4",
            ["AgentService:ThreadCacheExpiryMinutes"] = "60",
            ["OfficeRateLimit:Enabled"] = "false",
            ["Redis:Enabled"] = "false",
            ["Redis:AllowInMemoryFallback"] = "true",
            ["GraphResilience:MaxRetryAttempts"] = "3",
            ["GraphResilience:RetryDelay"] = "00:00:01",
            ["GraphResilience:CircuitBreakerFailureThreshold"] = "5",
            ["GraphResilience:CircuitBreakerDuration"] = "00:00:30",
            ["SpeAdmin:KeyVaultUri"] = "https://test.vault.azure.net/",
            ["ManagedIdentity:ClientId"] = "test-managed-identity-client-id",
            ["CosmosPersistence:Endpoint"] = "https://test.documents.azure.com:443/",
            ["CosmosPersistence:DatabaseName"] = "spaarke-ai-test",
            ["AgentService:Enabled"] = "false",
            ["PowerBi:TenantId"] = "test-powerbi-tenant-id",
            ["PowerBi:ClientId"] = "test-powerbi-client-id",
            ["PowerBi:ClientSecret"] = "test-powerbi-client-secret",
            ["PowerBi:ApiUrl"] = "https://api.powerbi.com",
            ["PowerBi:Scope"] = "https://analysis.windows.net/.default",
            ["Reporting:ModuleEnabled"] = "false",
        }));

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        builder.ConfigureTestServices(services =>
        {
            services.UseStubTokenCredential();

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = KeylessProofFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = KeylessProofFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, KeylessProofFakeAuthHandler>(KeylessProofFakeAuthHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = KeylessProofFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = KeylessProofFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IHostedService>();

            var dataverse = new Mock<IDataverseService>();
            dataverse.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverse.Object);

            // The module boundary: the AI-owned probes call Azure.
            var aiProbe = new Mock<IAiKeylessProbe>();
            aiProbe.Setup(p => p.ProbeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AiResults);
            services.RemoveAll<IAiKeylessProbe>();
            services.AddSingleton(aiProbe.Object);
        });
    }
}

/// <summary>Authenticates a request with a bearer header; claims come from the test headers (roles, scp, idtyp).</summary>
internal sealed class KeylessProofFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "KeylessProof230bFakeAuth";
    public const string RolesHeader = "X-Test-Roles";
    public const string ScopeHeader = "X-Test-Scp";
    public const string IdtypHeader = "X-Test-Idtyp";
    public const string AudienceHeader = "X-Test-Aud";

    public KeylessProofFakeAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));

        var claims = new List<Claim>
        {
            new("tid", "test-tenant-id"),
            new("oid", "230b0000-0000-0000-0000-000000000001"),
            new("appid", "l2-worker-app-id"),
            // The fixture's AzureAd:ClientId — the audience the filter pins (or the override header).
            new("aud", Request.Headers.TryGetValue(AudienceHeader, out var aud) ? aud.ToString() : "api://test-app-id"),
        };
        if (Request.Headers.TryGetValue(RolesHeader, out var roles))
            claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(r => new Claim("roles", r)));
        if (Request.Headers.TryGetValue(ScopeHeader, out var scp))
            claims.Add(new Claim("scp", scp.ToString()));
        if (Request.Headers.TryGetValue(IdtypHeader, out var idtyp))
            claims.Add(new Claim("idtyp", idtyp.ToString()));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
