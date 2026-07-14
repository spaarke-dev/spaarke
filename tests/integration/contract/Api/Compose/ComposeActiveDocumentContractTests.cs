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
using System.Text.Json;
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

    /// <summary>
    /// Multi-Compose-tab (UAT 2026-07-14): switching tabs fires the newly-active tab's register
    /// (visible:true) AND the hidden tab's WITHDRAW (visible:false). A withdraw MUST NOT re-pin the
    /// withdrawing document as active — the server previously had no `visible` property (it was dropped
    /// on deserialization), so a hidden tab's withdraw re-asserted itself as active, leaving the
    /// Assistant stuck on the first document after every tab switch. A withdraw clears ActiveDocument
    /// ONLY if it still points at THAT document; a stale withdraw of a non-active tab is a no-op.
    /// </summary>
    [Fact]
    public async Task PostActiveDocument_WithdrawOfNonActiveDocument_DoesNotClobberActiveDocument()
    {
        // Arrange — a session with TWO compose-direct files (two open Compose tabs).
        var sessionId = Guid.NewGuid().ToString("N");
        const string fileA = "compose-file-a";
        const string fileB = "compose-file-b";
        const string docType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
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
                new ChatSessionFile(fileA, "a.docx", docType, 256, $"{fileA}_s_0", DateTimeOffset.UtcNow),
                new ChatSessionFile(fileB, "b.docx", docType, 256, $"{fileB}_s_0", DateTimeOffset.UtcNow),
            });
        await _fixture.Sessions.UpdateSessionCacheAsync(seeded);

        using var client = _fixture.CreateAuthenticatedClient();

        // Act — tab A active, then switch to tab B (B registers as active).
        (await client.PostAsJsonAsync("/api/compose/active-document",
            new { sessionId, sessionFileId = fileA, source = ActiveDocumentIdentity.SourceComposeDirect }))
            .EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/compose/active-document",
            new { sessionId, sessionFileId = fileB, source = ActiveDocumentIdentity.SourceComposeDirect }))
            .EnsureSuccessStatusCode();

        // The now-hidden tab A withdraws (visible:false) — the bug re-pinned A as active here.
        var withdrawA = await client.PostAsJsonAsync("/api/compose/active-document",
            new { sessionId, sessionFileId = fileA, source = ActiveDocumentIdentity.SourceComposeDirect, visible = false });
        withdrawA.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert — the active document is STILL B (A's stale withdraw did not clobber it).
        var afterWithdrawA = await _fixture.Sessions.GetSessionAsync(ComposeActiveDocumentFixture.TenantId, sessionId);
        afterWithdrawA!.ActiveDocument.Should().NotBeNull(
            because: "a stale withdraw of the non-active tab must neither clear nor change the active document");
        afterWithdrawA.ActiveDocument!.SessionFileId.Should().Be(fileB,
            because: "tab B is the active tab; the hidden tab A's withdraw must NOT re-pin A as active (UAT stuck-on-first-doc)");

        // Withdrawing the ACTIVE tab (B) clears the active document (nothing is active).
        (await client.PostAsJsonAsync("/api/compose/active-document",
            new { sessionId, sessionFileId = fileB, source = ActiveDocumentIdentity.SourceComposeDirect, visible = false }))
            .EnsureSuccessStatusCode();
        var afterWithdrawB = await _fixture.Sessions.GetSessionAsync(ComposeActiveDocumentFixture.TenantId, sessionId);
        afterWithdrawB!.ActiveDocument.Should().BeNull(
            because: "withdrawing the currently-active document clears it — there is no active tab");
    }

    /// <summary>
    /// Wave 3 (DEF-11 TEXT-path close): the client now threads the Compose tab's document session id
    /// as <c>documentSessionId</c>. This drives the REAL route with it and asserts (1) the response
    /// echoes it and (2) the persisted <see cref="ActiveDocumentIdentity.DocumentSessionId"/> is set —
    /// which is precisely the field <c>BindingCapabilityTool</c> reads to route a TEXT/typed
    /// revise-or-draft into the document session (proven end-to-end by
    /// <c>ComposeDocumentSessionRoutingTests</c>). Without this persistence the TEXT path fail-softs to
    /// the chat session and no redline appears in the open document.
    /// </summary>
    [Fact]
    public async Task PostActiveDocument_WithDocumentSessionId_PersistsItOnActiveDocument()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var documentSessionId = Guid.NewGuid().ToString("N");
        const string fileId = "browse-mounted-file-def11";

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
                    FileName: "browse.docx",
                    ContentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    SizeBytes: 64,
                    SearchDocumentIdsCsv: $"{fileId}_s_0",
                    UploadedAt: DateTimeOffset.UtcNow),
            });
        await _fixture.Sessions.UpdateSessionCacheAsync(seeded);

        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/compose/active-document", new
        {
            sessionId,
            sessionFileId = fileId,
            source = ActiveDocumentIdentity.SourceComposeDirect,
            documentSessionId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ComposeActiveDocumentResponse>();
        body!.DocumentSessionId.Should().Be(documentSessionId,
            because: "the response echoes the registered document session id (Wave 3 DEF-11)");

        var after = await _fixture.Sessions.GetSessionAsync(ComposeActiveDocumentFixture.TenantId, sessionId);
        after!.ActiveDocument.Should().NotBeNull();
        after.ActiveDocument!.DocumentSessionId.Should().Be(documentSessionId,
            because: "BindingCapabilityTool reads ActiveDocument.DocumentSessionId to route a TEXT-path " +
            "compose edit into the DOCUMENT session (redline in the open doc), not the chat session");
    }

    /// <summary>
    /// DEF-11 draft-alternative-404 fix (spaarkeai-compose-r2): registering the active document with a
    /// <c>documentSessionId</c> idempotently CREATES a resolvable <see cref="ChatSession"/> keyed by
    /// that id — so a <c>materializesInEditor</c> compose dispatch to
    /// <c>POST /api/ai/chat/sessions/{documentSessionId}/dispatch</c> resolves (200) instead of the
    /// 404 <c>dispatch.session-not-found</c> that shipped (the client-minted document session was never
    /// created via <c>POST /api/ai/chat/sessions</c>). Asserts the persisted side effect directly.
    /// </summary>
    [Fact]
    public async Task PostActiveDocument_WithDocumentSessionId_CreatesResolvableDocumentSession()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var documentSessionId = Guid.NewGuid().ToString("D");
        const string fileId = "browse-mounted-file-create";

        await _fixture.Sessions.UpdateSessionCacheAsync(new ChatSession(
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
                new ChatSessionFile(fileId, "browse.docx",
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    64, $"{fileId}_s_0", DateTimeOffset.UtcNow),
            }));

        using var client = _fixture.CreateAuthenticatedClient();

        // Before registration the document session does not exist (it is client-minted, never created
        // via the chat session-create endpoint).
        (await _fixture.Sessions.GetSessionAsync(ComposeActiveDocumentFixture.TenantId, documentSessionId))
            .Should().BeNull("the client-minted document session is not created until registration");

        var response = await client.PostAsJsonAsync("/api/compose/active-document", new
        {
            sessionId,
            sessionFileId = fileId,
            source = ActiveDocumentIdentity.SourceComposeDirect,
            documentSessionId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var docSession = await _fixture.Sessions.GetSessionAsync(
            ComposeActiveDocumentFixture.TenantId, documentSessionId);
        docSession.Should().NotBeNull(
            "RegisterActiveDocument idempotently creates a resolvable ChatSession keyed by documentSessionId — " +
            "the fix that lets the compose /dispatch resolve it (200) instead of 404");
        docSession!.SessionId.Should().Be(documentSessionId);
        docSession.TenantId.Should().Be(ComposeActiveDocumentFixture.TenantId);
        docSession.Outputs.Should().BeNullOrEmpty("a freshly-created document session carries no outputs");
    }

    /// <summary>
    /// Idempotency guard (the critical property): re-registering the SAME documentSessionId across the
    /// multiple mount doors (Browse, upload, stored-doc, DEF-08 draft) MUST NOT clobber an existing
    /// document session's <see cref="ChatSession.Outputs"/> — that ledger holds the pending compose
    /// redlines the editor materializes; wiping it would drop them.
    /// </summary>
    [Fact]
    public async Task PostActiveDocument_ReRegisteringDocumentSession_PreservesExistingOutputs()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var documentSessionId = Guid.NewGuid().ToString("D");
        const string fileId = "browse-mounted-file-idempotent";

        await _fixture.Sessions.UpdateSessionCacheAsync(new ChatSession(
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
                new ChatSessionFile(fileId, "browse.docx",
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    64, $"{fileId}_s_0", DateTimeOffset.UtcNow),
            }));

        using var client = _fixture.CreateAuthenticatedClient();

        object RegisterBody() => new
        {
            sessionId,
            sessionFileId = fileId,
            source = ActiveDocumentIdentity.SourceComposeDirect,
            documentSessionId,
        };

        // First registration creates the document session.
        (await client.PostAsJsonAsync("/api/compose/active-document", RegisterBody()))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Simulate a compose dispatch having landed a redline output on the document session (the state
        // a real materializesInEditor dispatch produces via OutputRouter).
        var docSession = await _fixture.Sessions.GetSessionAsync(
            ComposeActiveDocumentFixture.TenantId, documentSessionId);
        docSession.Should().NotBeNull();
        var pendingRedline = new SessionOutput
        {
            Key = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@t1",
            BindingId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            UcId = "UC-COMPOSE-1",
            Turn = 1,
            Disposition = "compose",
            Payload = JsonDocument.Parse("""{"new_text":"redline the editor will materialize"}""").RootElement.Clone(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _fixture.Sessions.UpdateSessionCacheAsync(
            docSession! with { Outputs = new[] { pendingRedline } });

        // Second registration of the SAME document session — MUST preserve the pending redline.
        (await client.PostAsJsonAsync("/api/compose/active-document", RegisterBody()))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await _fixture.Sessions.GetSessionAsync(
            ComposeActiveDocumentFixture.TenantId, documentSessionId);
        after!.Outputs.Should().NotBeNullOrEmpty(
            "re-registration MUST NOT clobber an existing document session's Outputs (pending redlines)");
        after.Outputs!.Should().ContainSingle()
            .Which.Key.Should().Be("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@t1",
                "the previously-stored compose output survives a repeat active-document registration");
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
