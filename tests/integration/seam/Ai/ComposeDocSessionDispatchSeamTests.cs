// spaarkeai-compose-r2 DEF-11 "Draft alternative" 404 fix — vertical-slice seam test (KEEP path:
// tests/integration/seam/** — end-to-end across the active-document → dispatch → ledger seam with
// production types; a router-unit ≠ working slice, ADR-038 §E-40 / tests/CLAUDE.md).
//
// THE GAP THIS CLOSES (why the mocked e2e missed it): every prior compose-dispatch test seeded the
// target ChatSession directly into a STUB session manager (TestableChatSessionManager.Session), so a
// dispatch to ANY session id "resolved". Production is different: the Compose "document session" is
// client-minted (crypto.randomUUID, hyphenated GUID) and is NEVER created via POST
// /api/ai/chat/sessions — so a `materializesInEditor` compose dispatch to
// POST /api/ai/chat/sessions/{documentSessionId}/dispatch loaded a session that did not exist →
// SessionDispatchOrchestrator.GetSessionAsync == null → 404 dispatch.session-not-found. App Insights
// confirmed: 200 for the server-created chat session, 404 for the client-minted document session.
//
// The fix (ComposeEndpoints.RegisterActiveDocument): registering the active document idempotently
// creates a resolvable ChatSession keyed by documentSessionId. This test proves it THROUGH THE WIRE
// over the REAL app (WebApplicationFactory<Program>) with a REAL ChatSessionManager (production type
// over an in-memory tenant cache — the SAME store both the register endpoint and the dispatch
// orchestrator read/write) + the REAL SessionDispatchOrchestrator + REAL ActionRunner + REAL
// ContextBinder + REAL OutputRouter. Only the routing/action/LLM module boundaries are doubled per
// ADR-038. Positive: register → dispatch → 200 + persisted `compose` SessionOutput on the DOCUMENT
// session. Negative contrast: dispatch to a NEVER-registered document session id → 404 — proving the
// doc session resolves ONLY because RegisterActiveDocument created it.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
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
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Api.Ai;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai;

/// <summary>
/// DEF-11 draft-alternative-404 seam tests over the REAL app. See file header for the gap this closes.
/// </summary>
public sealed class ComposeDocSessionDispatchSeamTests : IClassFixture<ComposeDocSessionDispatchSeamFixture>
{
    private readonly ComposeDocSessionDispatchSeamFixture _fx;

    private const string SelectionText =
        "The aggregate liability shall not exceed the fees paid in the prior twelve (12) months.";

    public ComposeDocSessionDispatchSeamTests(ComposeDocSessionDispatchSeamFixture fx) => _fx = fx;

    [Fact]
    public async Task RegisterActiveDocument_ThenDispatchComposeToDocumentSession_Returns200AndPersistsSessionOutput()
    {
        _fx.SeedComposeDraftAlternativeBinding();
        _fx.OpenAi.RawJsonToReturn =
            """{"explanation":"Consider: 'Provider's aggregate liability shall not exceed the fees paid in the prior twelve (12) months.'"}""";

        // The server-created chat session (this one WOULD dispatch 200 today). Seed it directly via the
        // SAME production ChatSessionManager the register endpoint reads.
        var chatSessionId = Guid.NewGuid().ToString("D");
        await _fx.Sessions.UpdateSessionCacheAsync(new ChatSession(
            SessionId: chatSessionId,
            TenantId: ComposeDocSessionDispatchSeamFixture.TenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: new[]
            {
                new ChatSessionFile(
                    FileId: "browse-mounted-file", FileName: "browse.docx",
                    ContentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    SizeBytes: 64, SearchDocumentIdsCsv: "browse-mounted-file_s_0",
                    UploadedAt: DateTimeOffset.UtcNow),
            }));

        // The client-minted Compose document session — a hyphenated crypto.randomUUID that is NEVER
        // created via POST /api/ai/chat/sessions.
        var documentSessionId = Guid.NewGuid().ToString("D");

        var client = _fx.CreateAuthenticatedClient();

        // ── STEP 1: register the active document, threading the Compose tab's document session id.
        var register = await client.PostAsJsonAsync("/api/compose/active-document", new
        {
            sessionId = chatSessionId,
            sessionFileId = "browse-mounted-file",
            source = ActiveDocumentIdentity.SourceComposeDirect,
            documentSessionId,
        });
        register.StatusCode.Should().Be(HttpStatusCode.OK, "the chat session exists, so registration succeeds");

        // The fix's core side effect: the document session is now a resolvable ChatSession (before the
        // fix, nothing keyed a session by documentSessionId — dispatch would 404).
        var docSessionAfterRegister = await _fx.Sessions.GetSessionAsync(
            ComposeDocSessionDispatchSeamFixture.TenantId, documentSessionId);
        docSessionAfterRegister.Should().NotBeNull(
            "RegisterActiveDocument idempotently creates a resolvable ChatSession keyed by documentSessionId");
        docSessionAfterRegister!.Outputs.Should().BeNullOrEmpty("a freshly-created document session has no outputs yet");

        // ── STEP 2: dispatch the `materializesInEditor` compose action to the DOCUMENT session — the
        //           exact route the client's dispatchConsumer posts to for "Draft alternative".
        var dispatch = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{documentSessionId}/dispatch",
            new
            {
                bindingId = ComposeDocSessionDispatchSeamFixture.ComposeBindingId.ToString(),
                args = new { selectionText = SelectionText },
            });

        // THE regression guard: 200 (NOT the 404 dispatch.session-not-found that shipped).
        dispatch.StatusCode.Should().Be(HttpStatusCode.OK,
            "the document session resolves because RegisterActiveDocument created it — NOT 404 dispatch.session-not-found");
        dispatch.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");
        var body = await dispatch.Content.ReadAsStringAsync();
        body.Should().NotContain("dispatch.session-not-found",
            "the DEF-11 defect was a 404 on the client-minted document session");
        body.Should().Contain("\"type\":\"complete\"", "the compose dispatch renders a terminal chunk");

        // ── The persisted side effect (ADR-040): the `compose` SessionOutput was stored ON THE DOCUMENT
        //    SESSION (where the editor materializes the redline) — proving the slice ran end to end, not
        //    merely that a stream opened.
        var docSessionAfterDispatch = await _fx.Sessions.GetSessionAsync(
            ComposeDocSessionDispatchSeamFixture.TenantId, documentSessionId);
        docSessionAfterDispatch!.Outputs.Should().NotBeNullOrEmpty(
            "the compose dispatch stored its SessionOutput on the document session (ADR-040 store-before-render)");
        docSessionAfterDispatch.Outputs!.Should().ContainSingle()
            .Which.Key.Should().Be($"{ComposeDocSessionDispatchSeamFixture.ComposeBindingId}@t1",
                "the compose output is addressable as {bindingId}@t{n} on the document session");
        docSessionAfterDispatch.Outputs!.Single().Disposition.Should().Be("compose");
    }

    [Fact]
    public async Task DispatchComposeToUnregisteredDocumentSession_Returns404_SessionNotFound()
    {
        _fx.SeedComposeDraftAlternativeBinding();

        // A document session id that was NEVER registered (no RegisterActiveDocument call) — exactly the
        // pre-fix state. This MUST 404 — the negative contrast proving the positive test's 200 comes
        // from the registration, not from an always-resolving stub.
        var unregisteredDocumentSessionId = Guid.NewGuid().ToString("D");

        var client = _fx.CreateAuthenticatedClient();

        var dispatch = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{unregisteredDocumentSessionId}/dispatch",
            new
            {
                bindingId = ComposeDocSessionDispatchSeamFixture.ComposeBindingId.ToString(),
                args = new { selectionText = SelectionText },
            });

        dispatch.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an unregistered document session does not resolve — this is the DEF-11 defect the fix removes " +
            "for registered sessions");
        (await dispatch.Content.ReadAsStringAsync()).Should().Contain("dispatch.session-not-found");
    }
}

/// <summary>
/// Boots the REAL app (WebApplicationFactory&lt;Program&gt;) with a fake auth scheme emitting BOTH the
/// <c>tid</c> and <c>oid</c> claims (the compose + dispatch endpoints both resolve identity from
/// claims), a REAL <see cref="ChatSessionManager"/> over an in-memory tenant cache (the seam under
/// test — shared by RegisterActiveDocument and the dispatch orchestrator/OutputRouter), and
/// module-boundary doubles for routing/action/LLM (ADR-038). The orchestrator, ActionRunner,
/// ContextBinder and OutputRouter stay REAL (Program registrations under the compound-ON gate:
/// Analysis:Enabled=true + DocumentIntelligence:Enabled=true).
/// </summary>
public sealed class ComposeDocSessionDispatchSeamFixture : WebApplicationFactory<Program>
{
    public const string TenantId = "00000000-0000-0000-0000-0000000000dd";

    internal static readonly Guid ComposeBindingId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    internal static readonly Guid ComposeActionId = Guid.Parse("ffffffff-1111-2222-3333-444444444444");

    private const string ComposeSelectionInputSchema =
        """{"type":"object","properties":{"selectionText":{"type":"string"}}}""";

    /// <summary>The shared production ChatSessionManager over an in-memory tenant cache.</summary>
    public ChatSessionManager Sessions { get; } = new(
        new InMemoryTenantCache(),
        Mock.Of<IChatDataverseRepository>(),
        Mock.Of<ILogger<ChatSessionManager>>());

    public Mock<IConsumerRoutingService> ConsumerRoutingMock { get; } = new();
    public Mock<IScopeResolverService> ScopeResolverMock { get; } = new();
    public StubOpenAiClient OpenAi { get; } = new();

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
                options.DefaultAuthenticateScheme = ComposeDocSessionFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = ComposeDocSessionFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, ComposeDocSessionFakeAuthHandler>(
                ComposeDocSessionFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = ComposeDocSessionFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = ComposeDocSessionFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            services.RemoveAll<IHostedService>();

            var dataverseMock = new Mock<IDataverseService>();
            dataverseMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseMock.Object);

            services.RemoveAll<IComposeService>();
            services.AddSingleton(Mock.Of<IComposeService>());

            // The shared seam: register endpoint + dispatch orchestrator + OutputRouter all read/write
            // the SAME real ChatSessionManager (over an in-memory tenant cache).
            services.RemoveAll<ChatSessionManager>();
            services.AddSingleton(Sessions);

            // Module-boundary doubles for the dispatch pipeline (ADR-038). The orchestrator + ActionRunner
            // + ContextBinder + OutputRouter stay REAL (Program registrations under the compound-ON gate).
            services.RemoveAll<IConsumerRoutingService>();
            services.AddSingleton(ConsumerRoutingMock.Object);
            services.RemoveAll<IScopeResolverService>();
            services.AddSingleton(ScopeResolverMock.Object);
            services.RemoveAll<IOpenAiClient>();
            services.AddSingleton<IOpenAiClient>(OpenAi);
        });
    }

    /// <summary>Seeds the compose-draft-alternative Binding (Compose disposition, the
    /// <c>materializesInEditor</c> in-editor action) + its Action at the routing module boundary. The
    /// action resolves its operand from the <c>selectionText</c> arg (structured-operand path — no
    /// session files required), so a minimal document session is sufficient for it to run.</summary>
    public void SeedComposeDraftAlternativeBinding()
    {
        ConsumerRoutingMock
            .Setup(c => c.GetBindingByIdAsync(ComposeBindingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Binding
            {
                BindingId = ComposeBindingId,
                ConsumerType = ConsumerTypes.ComposeDraftAlternative,
                ConsumerCode = "default",
                Environment = "*",
                ActionId = ComposeActionId,
                ActionKind = ActionKind.Prompted,
                Disposition = BindingDisposition.Compose,
                InputSchemaJson = ComposeSelectionInputSchema,
            });

        ScopeResolverMock
            .Setup(s => s.GetActionAsync(ComposeActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction
            {
                Id = ComposeActionId,
                Name = "Compose — Draft Alternative",
                SystemPrompt =
                    """{"$schema":"https://spaarke.com/schemas/prompt/v1","instruction":{"role":"You are the Spaarke Compose clause assistant.","task":"Draft an alternative for the clause in the ## Input section."},"output":{"fields":[{"name":"explanation","type":"string","description":"the drafted alternative"}],"structuredOutput":true}}""",
                OutputSchemaJson =
                    """{"type":"object","additionalProperties":false,"required":["explanation"],"properties":{"explanation":{"type":"string"}}}""",
                Temperature = 0.2m,
            });
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }
}

/// <summary>Fake auth handler emitting BOTH tid (tenant) + oid (user) claims — the dispatch endpoint
/// requires the tid claim (header not accepted), and both endpoints read oid.</summary>
internal sealed class ComposeDocSessionFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ComposeDocSessionFakeAuth";

    public ComposeDocSessionFakeAuthHandler(
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

        var oid = Request.Headers["X-Test-User"].ToString();
        if (string.IsNullOrWhiteSpace(oid)) oid = Guid.NewGuid().ToString();

        var claims = new List<Claim>
        {
            new("oid", oid),
            new("tid", ComposeDocSessionDispatchSeamFixture.TenantId),
            new(ClaimTypes.NameIdentifier, oid),
            new(ClaimTypes.Name, $"Compose DocSession Test User {oid}"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
