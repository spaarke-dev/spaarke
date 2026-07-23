// Task 034 (FR-17 undo/replace via ledger supersession) — through-the-wire contract test
// (KEEP path: endpoint-contract). THE durability forcing function for the supersession seam.
//
// WHY THIS FILE EXISTS (non-waivable per project CLAUDE.md §"E2E Definition-of-Done"):
//   FR-17 undo/replace is a DURABLE ledger write, not a client DOM undo (ADR-040, HANDOFF §1 item 5).
//   The client re-materialize is only durable if the superseding `compose` SessionOutput is actually
//   PERSISTED server-side and a re-load reflects it. A client-only test cannot prove that. This test
//   drives the REAL POST /api/ai/chat/sessions/{id}/compose-outputs/supersede route over the REAL
//   ChatSessionManager (in-memory session store; only external boundaries mocked) and asserts the
//   persisted side-effect: a new highest-turn `compose` retraction entry that a subsequent GET
//   /compose-outputs (the re-load path) reflects. It also locks the negatives — idempotent
//   double-supersede (no second write), non-existent ref (honest 404), and unauthenticated (401).
//
// KEEP-path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `endpoint-contract` · Path: `tests/integration/contract/Api/Ai/**`.
//   - "Every new endpoint => >=1 integration test": anchors POST .../compose-outputs/supersede.
//
// Banned-pattern compliance (ADR-038 §4 + tests/CLAUDE.md): NO Mock<HttpMessageHandler>; the
// ChatSessionManager + session store under test are REAL; mocks live ONLY at external boundaries
// (Graph / Dataverse cold tier). Assertions are HTTP-observable + persisted ledger side-effects.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Through-the-wire contract tests for FR-17 undo/replace ledger supersession (task 034). Boots the
/// BFF in-process with the REAL <see cref="ChatSessionManager"/> (in-memory Redis fallback) and only
/// the external Graph / Dataverse cold-tier boundaries mocked, then drives supersede → re-load.
/// </summary>
public sealed class ComposeSupersedeEndpointContractTests : IClassFixture<ComposeSupersedeFixture>
{
    private readonly ComposeSupersedeFixture _fixture;

    public ComposeSupersedeEndpointContractTests(ComposeSupersedeFixture fixture)
    {
        _fixture = fixture;
    }

    private const string BindingId = "binding-supersede-x";

    /// <summary>Creates a session and seeds one applied `compose` output (binding-supersede-x@t1) via the
    /// SAME seam production uses (session with { Outputs } + UpdateSessionCacheAsync — see OutputRouter).</summary>
    private async Task<string> CreateSessionWithComposeDraftAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();

        var session = await sessions.CreateSessionAsync(
            ComposeSupersedeFixture.TestTenantId, documentId: null, playbookId: null, hostContext: null);

        var draft = new SessionOutput
        {
            Key = $"{BindingId}@t1",
            BindingId = BindingId,
            UcId = "uc-compose-draft-alternative",
            Turn = 1,
            Disposition = "compose",
            Payload = JsonDocument.Parse(
                """{"target_text":"quick","new_text":"nimble","match_mode":"strict"}""").RootElement.Clone(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await sessions.UpdateSessionCacheAsync(session with { Outputs = new[] { draft } }, CancellationToken.None);
        return session.SessionId;
    }

    private static async Task<IReadOnlyList<ComposeLedgerOutputDto>> GetComposeOutputsAsync(HttpClient client, string sessionId)
    {
        var response = await client.GetAsync($"/api/ai/chat/sessions/{sessionId}/compose-outputs");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<ComposeLedgerOutputDto>>())!;
    }

    [Fact]
    public async Task Supersede_UndoPriorDraft_WritesSupersedingRetractionEntry_ReloadReflectsItAsCurrentHead()
    {
        _fixture.ResetBoundaries();
        var sessionId = await CreateSessionWithComposeDraftAsync();
        using var client = _fixture.CreateAuthenticatedClient();

        // ── Act: retract the prior compose draft through the REAL supersede route ────────────────
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{sessionId}/compose-outputs/supersede",
            new { supersedesRef = $"{BindingId}@t1" });

        // ── Assert: the durable write happened (ADR-040 append-only supersession) ────────────────
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ComposeSupersedeResponse>();
        body!.Outcome.Should().Be(ComposeSupersedeResponse.OutcomeSuperseded);
        body.Key.Should().Be($"{BindingId}@t2", "the retraction is the next-turn compose entry for the binding");
        body.SupersedesRef.Should().Be($"{BindingId}@t1");

        // ── Assert: a fresh GET (the re-load path) reflects the retraction as the CURRENT head ───
        // This is the load-bearing durability proof — a client-only DOM undo would NOT survive here.
        var outputs = await GetComposeOutputsAsync(client, sessionId);
        outputs.Should().HaveCount(2, "the prior draft is retained (append-only) and the retraction is appended");
        var head = outputs.OrderByDescending(o => o.Turn).First();
        head.Key.Should().Be($"{BindingId}@t2");
        head.Turn.Should().Be(2, "the retraction supersedes by turn ordering — a re-load materializes from it");
        head.Payload.TryGetProperty("retracted", out var retracted).Should().BeTrue();
        retracted.GetBoolean().Should().BeTrue("the retraction's empty payload re-materializes to NOTHING (the prior redline disappears)");
        head.Payload.TryGetProperty("supersedes_ref", out var supRef).Should().BeTrue();
        supRef.GetString().Should().Be($"{BindingId}@t1", "the retraction records the superseded key (provenance, ADR-040)");
    }

    [Fact]
    public async Task Supersede_AlreadySupersededRef_IsIdempotentNoOp_WritesNothingFurther()
    {
        _fixture.ResetBoundaries();
        var sessionId = await CreateSessionWithComposeDraftAsync();
        using var client = _fixture.CreateAuthenticatedClient();

        // First supersede writes the retraction.
        (await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{sessionId}/compose-outputs/supersede",
            new { supersedesRef = $"{BindingId}@t1" })).StatusCode.Should().Be(HttpStatusCode.OK);

        // Second supersede of the SAME (now-superseded) ref is an idempotent no-op — no third entry.
        var second = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{sessionId}/compose-outputs/supersede",
            new { supersedesRef = $"{BindingId}@t1" });

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await second.Content.ReadFromJsonAsync<ComposeSupersedeResponse>();
        body!.Outcome.Should().Be(ComposeSupersedeResponse.OutcomeNoop,
            "superseding an already-superseded entry is an idempotent no-op (not a new write)");

        var outputs = await GetComposeOutputsAsync(client, sessionId);
        outputs.Should().HaveCount(2, "the idempotent no-op does NOT append a redundant retraction");
    }

    [Fact]
    public async Task Supersede_NonExistentRef_Returns404_HonestFailure()
    {
        _fixture.ResetBoundaries();
        var sessionId = await CreateSessionWithComposeDraftAsync();
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{sessionId}/compose-outputs/supersede",
            new { supersedesRef = "no-such-binding@t9" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "superseding a non-existent compose ref fails honestly (does not silently write)");
    }

    [Fact]
    public async Task Supersede_WhenSessionUnknown_Returns404()
    {
        _fixture.ResetBoundaries();
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{Guid.NewGuid():N}/compose-outputs/supersede",
            new { supersedesRef = $"{BindingId}@t1" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Supersede_WhenUnauthenticated_Returns401()
    {
        _fixture.ResetBoundaries();
        using var client = _fixture.CreateUnauthenticatedClient();

        using var content = JsonContent.Create(new { supersedesRef = $"{BindingId}@t1" });
        var response = await client.PostAsync(
            $"/api/ai/chat/sessions/{Guid.NewGuid():N}/compose-outputs/supersede", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the supersede route inherits RequireAuthorization() from the /api/ai/chat group (ADR-008 + ADR-028)");
    }
}

/// <summary>
/// In-process BFF fixture keeping the REAL <see cref="ChatSessionManager"/> (in-memory session store)
/// and mocking only the external Graph / Dataverse cold-tier boundaries. Config mirrors
/// <c>ComposeMemoryResumeFixture</c> (bff-extensions.md §F.2 Fixture-Config-FIRST).
/// </summary>
public sealed class ComposeSupersedeFixture : WebApplicationFactory<Program>
{
    public const string TestTenantId = "tenant-supersede-001";

    public void ResetBoundaries()
    {
        // No mutable boundary state beyond the in-memory session store (reset per session id).
    }

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
        builder.UseEnvironment("Development");
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        builder.ConfigureTestServices(services =>
        {
            services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options =>
            {
                options.ThrowOnBadRequest = false;
            });

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = SupersedeFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = SupersedeFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, SupersedeFakeAuthHandler>(
                SupersedeFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = SupersedeFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = SupersedeFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            services.RemoveAll<IHostedService>();

            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);

            // Cold tier (Dataverse) resolves an unknown session to null so GetSessionAsync returns the
            // in-memory hot copy (or null for a truly unknown id → 404). CreateSessionAsync still writes
            // the in-memory hot cache the tests read.
            var chatRepoMock = new Mock<IChatDataverseRepository>();
            chatRepoMock
                .Setup(r => r.GetSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ChatSession?)null);
            services.RemoveAll<IChatDataverseRepository>();
            services.AddSingleton(chatRepoMock.Object);
        });
    }

    public HttpClient CreateUnauthenticatedClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }
}

/// <summary>Fake auth handler authenticating any request with an Authorization header; emits oid + tid claims.</summary>
internal sealed class SupersedeFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "SupersedeFakeAuth";

    public SupersedeFakeAuthHandler(
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

        var oid = Guid.NewGuid().ToString();
        var claims = new List<Claim>
        {
            new("oid", oid),
            new("tid", ComposeSupersedeFixture.TestTenantId),
            new(ClaimTypes.NameIdentifier, oid),
            new(ClaimTypes.Name, $"Supersede Test User {oid}"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
