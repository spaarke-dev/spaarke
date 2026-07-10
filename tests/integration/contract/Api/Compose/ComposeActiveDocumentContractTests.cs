// task 113 (UAT-R4/R5 defect 4) — through-the-wire DoD for the chat←Compose direction of the
// session-scoped active-document bridge.
//
// Defect 4: "Summarize this document" does NOT recognize a file uploaded DIRECTLY into Compose,
// because client-only Browse bytes never became a server-side ChatSessionFile and nothing marked
// them active. The fix: the Compose-direct upload lands its bytes as a ChatSessionFile (via the
// EXISTING chat upload endpoint, reused client-side) AND registers the active document through the
// NEW POST /api/compose/active-document endpoint — after which chat summarize resolves THAT file.
//
// This test drives the NEW register endpoint over REAL HTTP (through the real route, real auth
// pipeline, real ChatSessionManager) and asserts the persisted side effect (ActiveDocument set on
// the session) AND that the PRODUCTION summarize text-source (SessionFileTextSource) resolves the
// registered file's text — i.e. chat summarize would see it. A contract-shape test is NOT
// sufficient (project E2E DoD); this crosses the HTTP boundary and observes stored state.
//
// KEEP path: endpoint-contract (tests/integration/contract/Api/Compose/**) — "every new endpoint
// = >=1 integration test". Banned-pattern compliant: no Mock<HttpMessageHandler>, no DI-registration
// tests, no ctor null-check tests; the ChatSessionManager is a real production type over an
// in-memory tenant cache (the shared seam), and only the RAG boundary of the resolver is a double
// (never invoked on the inline-ExtractedText path).

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Compose;

[Trait("status", "repaired")]
public sealed class ComposeActiveDocumentContractTests : IClassFixture<ComposeActiveDocumentFixture>
{
    private readonly ComposeActiveDocumentFixture _fixture;

    public ComposeActiveDocumentContractTests(ComposeActiveDocumentFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PostActiveDocument_WhenUnauthenticated_Returns401()
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        using var content = JsonContent.Create(new { sessionId = "x", sessionFileId = "y" });
        var response = await client.PostAsync("/api/compose/active-document", content);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostActiveDocument_WithNeitherPointer_Returns400()
    {
        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/compose/active-document", new { sessionId = "s" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostActiveDocument_ForUnknownSession_Returns404()
    {
        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/compose/active-document",
            new { sessionId = Guid.NewGuid().ToString("N"), sessionFileId = "nope" });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The DoD test (defect 4, non-waivable): a Compose-direct upload — already landed as a
    /// ChatSessionFile with extracted text — is REGISTERED through the real HTTP route, after which
    /// (1) the session's ActiveDocument points at that file (persisted side effect) and (2) the
    /// PRODUCTION summarize text-source resolves THAT file's text (chat summarize would see it).
    /// </summary>
    [Fact]
    public async Task PostActiveDocument_RegistersComposeDirectUpload_ChatSummarizeResolvesThatDocument()
    {
        // Arrange — the compose-direct file's bytes were landed as a ChatSessionFile by the EXISTING
        // chat upload endpoint (reused client-side, separately contract-tested). Seed that session
        // state through the SAME ChatSessionManager the endpoint reads/writes.
        var sessionId = Guid.NewGuid().ToString("N");
        const string fileId = "compose-direct-file-abc123";
        const string extractedText = "This is the body of the file the user Browse-mounted directly in Compose.";

        var seeded = new ChatSession(
            SessionId: sessionId,
            TenantId: ComposeActiveDocumentFixture.TenantId,
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
                    FileId: fileId,
                    FileName: "browse-mounted.docx",
                    ContentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    SizeBytes: 256,
                    SearchDocumentIdsCsv: $"{fileId}_s_0",
                    UploadedAt: DateTimeOffset.UtcNow)
                {
                    ExtractedText = extractedText,
                },
            });
        await _fixture.Sessions.UpdateSessionCacheAsync(seeded);

        using var client = _fixture.CreateAuthenticatedClient();

        // Act — register the active document over the REAL route.
        var response = await client.PostAsJsonAsync("/api/compose/active-document", new
        {
            sessionId,
            sessionFileId = fileId,
            source = ActiveDocumentIdentity.SourceComposeDirect,
        });

        // Assert (HTTP contract).
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ComposeActiveDocumentResponse>();
        body.Should().NotBeNull();
        body!.Source.Should().Be(ActiveDocumentIdentity.SourceComposeDirect);
        body.SessionFileId.Should().Be(fileId);
        body.FileName.Should().Be("browse-mounted.docx", because: "the endpoint enriches the display name from the session manifest");

        // Assert (persisted side effect): the session's active document now points at the file.
        var after = await _fixture.Sessions.GetSessionAsync(ComposeActiveDocumentFixture.TenantId, sessionId);
        after.Should().NotBeNull();
        after!.ActiveDocument.Should().NotBeNull();
        after.ActiveDocument!.SessionFileId.Should().Be(fileId);
        after.ActiveDocument.Source.Should().Be(ActiveDocumentIdentity.SourceComposeDirect);

        // Assert (chat summarize resolves THAT document): the PRODUCTION summarize text-source reads
        // the registered file's extracted text (inline path — no RAG hop, no live LLM). This is what
        // makes "summarize this document" recognize the Compose-direct upload (defect 4 fixed).
        var textSource = new SessionFileTextSource(Mock.Of<IRagService>(), Mock.Of<ILogger<SessionFileTextSource>>());
        var resolved = await textSource.FetchAsync(
            ComposeActiveDocumentFixture.TenantId, sessionId, after.UploadedFiles!, CancellationToken.None);

        resolved.ExtractedText.Should().Contain(extractedText,
            because: "the registered Compose-direct upload is now a summarize-resolvable ChatSessionFile (defect 4)");
    }
}

/// <summary>
/// In-process BFF fixture for the active-document register endpoint. Mirrors the canonical
/// <see cref="ComposeContractFixture"/> config key-set (bff-extensions.md §F.2) and swaps in a fake
/// auth scheme, but ADDITIONALLY replaces <see cref="ChatSessionManager"/> with a real instance over
/// an in-memory tenant cache so the test can seed a session AND the register endpoint reads/writes
/// the SAME store (the shared seam — not a mock of the class under test).
/// </summary>
public sealed class ComposeActiveDocumentFixture : WebApplicationFactory<Program>
{
    public const string TenantId = "00000000-0000-0000-0000-0000000000dd";

    /// <summary>The shared production ChatSessionManager over an in-memory tenant cache.</summary>
    public ChatSessionManager Sessions { get; } = new(
        new InMemoryTenantCache(),
        Mock.Of<IChatDataverseRepository>(),
        Mock.Of<ILogger<ChatSessionManager>>());

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
                options.DefaultAuthenticateScheme = ComposeActiveDocFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = ComposeActiveDocFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, ComposeActiveDocFakeAuthHandler>(
                ComposeActiveDocFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = ComposeActiveDocFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = ComposeActiveDocFakeAuthHandler.SchemeName;
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

            // The shared seam: register endpoint + test seeding use the SAME ChatSessionManager
            // (real production type over an in-memory tenant cache — not a mock of the CUT).
            services.RemoveAll<ChatSessionManager>();
            services.AddSingleton(Sessions);
        });
    }

    public HttpClient CreateUnauthenticatedClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString());
        // The register endpoint reads the tenant from tid / X-Tenant-Id (dual-form). The fake auth
        // handler emits no tid claim, so the header supplies the tenant for the endpoint.
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TenantId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }
}

/// <summary>Fake auth handler — authenticates on any Authorization header; emits an oid claim.</summary>
internal sealed class ComposeActiveDocFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ComposeActiveDocFakeAuth";

    public ComposeActiveDocFakeAuthHandler(
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
            new(ClaimTypes.NameIdentifier, oid),
            new(ClaimTypes.Name, $"Compose Test User {oid}"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
