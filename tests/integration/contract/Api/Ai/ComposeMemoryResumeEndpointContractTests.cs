// Task 102 (E2E-R3) — Memory / annotations / cross-version resume vertical-slice contract test
// (KEEP path: endpoint-contract).
//
// WHY THIS FILE EXISTS (anti-recurrence, non-waivable per project CLAUDE.md §"E2E Definition-of-
// Done" + notes/e2e-gap-register.md Cluster 4):
//   Tasks 060/061/062 built the memory/resume SERVICE layer correctly (unit-green) but it was
//   DEAD CODE over HTTP — the Load endpoint DISCARDED the incoming sessionId+matterId, so
//   ComposeService.LoadAsync's resume branch never ran and EVERY reopen minted a brand-new EMPTY
//   session (gap 4.1, the linchpin); the Load response omitted anchoredAnnotations/definedTerms/
//   actionHistory (gaps 4.2/4.4); and there was NO route to persist anchored annotations
//   (gap 4.3). Every layer unit-passed while the reopen-restores-state slice was E2E-inert — that
//   is exactly how the false-green shipped.
//
//   This test drives the FULL slice through the REAL endpoints with the REAL ComposeService +
//   ChatSessionManager (only the external SPE / Dataverse / indexing boundaries are mocked), so a
//   broken wire between the endpoint and the service fails the build — a service-only unit test
//   would NOT catch it. It persists an annotation through the real save route, then reopens via
//   Load(sessionId, matterId) through the real route, and asserts (a) the SAME session resumed
//   (not a new empty one) and (b) all three collections were restored.
//
// KEEP-path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `endpoint-contract`  ·  Path: `tests/integration/contract/Api/Ai/**` (csproj
//     auto-includes `tests/integration/contract/**`).
//   - "Every new endpoint => >=1 integration test": closes the contract for the new
//     `GET/POST /api/compose/sessions/{sessionId}/annotations` routes AND the sessionId+matterId
//     query binding + 3-collection projection on `GET /api/compose/documents/{speId}` (task 102).
//
// Banned-pattern compliance (ADR-038 §4 + tests/CLAUDE.md): NO Mock<HttpMessageHandler>; mocks live
// ONLY at the SPE (ISpeFileOperations) / Dataverse (IGenericEntityService) / indexing module
// boundaries per ADR-038 §4 "mock at module boundaries". The classes-under-test (ComposeService,
// ChatSessionManager) and the HTTP boundary are REAL. Assertions are HTTP-observable + persisted
// side-effects (the resumed session id + restored collections).

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
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Through-the-wire (anti-recurrence) contract tests for FR-29/FR-33 memory/annotations/resume
/// (task 102, Cluster 4). Boots the BFF in-process with the REAL <see cref="ComposeService"/> +
/// <see cref="ChatSessionManager"/> and only the external SPE / Dataverse / indexing boundaries
/// mocked, and drives save-annotation → reopen(Load) through the real routes.
/// </summary>
public sealed class ComposeMemoryResumeEndpointContractTests
    : IClassFixture<ComposeMemoryResumeFixture>
{
    private readonly ComposeMemoryResumeFixture _fixture;

    public ComposeMemoryResumeEndpointContractTests(ComposeMemoryResumeFixture fixture)
    {
        _fixture = fixture;
    }

    // Minimal opaque DOCX-ish bytes — the SPE boundary is mocked, so no real OOXML parsing occurs.
    private static readonly byte[] DocBytes =
        { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00, 0x0A, 0x0B, 0x0C, 0x0D };

    private const string DriveId = "drive-memory-001";

    /// <summary>Arranges the SPE Load boundary (metadata + a fresh content stream per call).</summary>
    private void SetupSpeLoad(string speId)
    {
        var now = DateTimeOffset.UtcNow;
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: speId,
                Name: "contract.docx",
                ParentId: null,
                Size: DocBytes.Length,
                CreatedDateTime: now,
                LastModifiedDateTime: now,
                ETag: "\"etag-1\"",
                IsFolder: false,
                WebUrl: null,
                DriveId: DriveId));
        // Factory (not a single instance): Load disposes the stream, and a class may issue >1 Load.
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(DocBytes.ToArray()));
    }

    /// <summary>Creates a session bound to <paramref name="speId"/> + a Matter host context, seeds a
    /// compose-disposition ledger output (so action history is non-empty on resume), and returns its
    /// id — the same shape ComposeService.LoadAsync mints on a first open.</summary>
    private async Task<string> CreateResumableSessionAsync(string speId, string matterId)
    {
        using var scope = _fixture.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();

        var hostContext = new ChatHostContext(
            EntityType: ParentEntityContext.EntityTypes.Matter,
            EntityId: matterId);

        var session = await sessions.CreateSessionAsync(
            ComposeMemoryResumeFixture.TestTenantId,
            documentId: speId,
            playbookId: null,
            hostContext: hostContext);

        // Seed a compose-disposition ledger output via the SAME seam production uses
        // (session with { Outputs } + UpdateSessionCacheAsync — see OutputRouter.cs) so
        // GetActionHistory has an entry to restore (gap 4.4).
        var output = new SessionOutput
        {
            Key = "binding-x@t1",
            BindingId = "binding-x",
            UcId = "uc-compose-explain",
            Turn = 1,
            Disposition = "compose",
            Payload = JsonDocument.Parse("{}").RootElement.Clone(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await sessions.UpdateSessionCacheAsync(
            session with { Outputs = new[] { output } }, CancellationToken.None);

        return session.SessionId;
    }

    private static object BuildAnnotationsBody() => new
    {
        tenantId = ComposeMemoryResumeFixture.TestTenantId,
        anchoredAnnotations = new[]
        {
            new
            {
                id = "ann-1",
                type = "comment",
                anchor = new { textPattern = "the term", paragraphHint = 2, spanId = "span-1" },
                body = "Consider revising this clause.",
                author = "Alice",
                timestamp = "2026-07-10T00:00:00Z",
                source = "human",
            },
        },
        definedTermsTracking = new[]
        {
            new
            {
                term = "Confidential Information",
                definition = "as defined in section 1.1",
                source = "human",
            },
        },
    };

    [Fact]
    public async Task SaveAnnotationsThenReopen_WithSessionAndMatter_ResumesSameSessionAndRestoresAllThreeCollections()
    {
        // ── Arrange ────────────────────────────────────────────────────────────────────────────
        const string speId = "spe-item-memory-001";
        const string matterId = "11111111-1111-1111-1111-111111111111";

        _fixture.ResetBoundaries();
        SetupSpeLoad(speId);

        var sessionId = await CreateResumableSessionAsync(speId, matterId);

        using var client = _fixture.CreateAuthenticatedClient();

        // ── Act 1: persist annotations + defined terms through the REAL save route (gap 4.3) ─────
        var saveResponse = await client.PostAsJsonAsync(
            $"/api/compose/sessions/{sessionId}/annotations", BuildAnnotationsBody());

        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "the annotations save route persists onto the existing session over the real wire");
        var saved = await saveResponse.Content.ReadFromJsonAsync<ComposeAnnotationsResponse>();
        saved!.AnchoredAnnotations.Should().HaveCount(1);
        saved.DefinedTermsTracking.Should().HaveCount(1);

        // ── Act 2: REOPEN — Load with sessionId + matterId (the reopen the client actually sends) ─
        var loadUrl =
            $"/api/compose/documents/{speId}?driveId={DriveId}&tenantId={ComposeMemoryResumeFixture.TestTenantId}" +
            $"&sessionId={sessionId}&matterId={matterId}";
        var loadResponse = await client.GetAsync(loadUrl);

        // ── Assert: the reopen resumed the SAME session (gap 4.1 — the linchpin) ─────────────────
        loadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loaded = await loadResponse.Content.ReadFromJsonAsync<LoadComposeDocumentResponse>();
        loaded!.SessionId.Should().Be(sessionId,
            "the Load handler binds sessionId+matterId and routes to the resume branch — a reopen " +
            "resumes the SAME session, it does NOT mint a new empty one");

        // ── Assert: all three collections restored (gaps 4.2/4.4) — the payload a service-only test misses
        loaded.AnchoredAnnotations.Should().ContainSingle(a => a.Id == "ann-1",
            "the resumed session's anchored annotations are projected onto the Load response");
        loaded.DefinedTermsTracking.Should().ContainSingle(t => t.Term == "Confidential Information",
            "the resumed session's defined-terms tracking is projected onto the Load response");
        loaded.ActionHistory.Should().ContainSingle(h => h.OutputRef == "binding-x@t1",
            "the resumed session's action history (ledger projection) is projected onto the Load response");
    }

    [Fact]
    public async Task Reopen_WithoutSessionId_MintsNewEmptySession_ProvingResumeIsLoadBearing()
    {
        // The negative contrast to the linchpin: with an annotated session existing but its
        // sessionId NOT sent on Load, the handler mints a FRESH session with EMPTY collections.
        // This is what shipped as the false-green; asserting it proves the resume path is
        // genuinely load-bearing (the positive test's restoration is not incidental).
        const string speId = "spe-item-memory-002";
        const string matterId = "22222222-2222-2222-2222-222222222222";

        _fixture.ResetBoundaries();
        SetupSpeLoad(speId);

        var annotatedSessionId = await CreateResumableSessionAsync(speId, matterId);
        using var client = _fixture.CreateAuthenticatedClient();
        (await client.PostAsJsonAsync($"/api/compose/sessions/{annotatedSessionId}/annotations", BuildAnnotationsBody()))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Reopen WITHOUT sessionId → new session.
        var loadResponse = await client.GetAsync(
            $"/api/compose/documents/{speId}?driveId={DriveId}&tenantId={ComposeMemoryResumeFixture.TestTenantId}");

        loadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loaded = await loadResponse.Content.ReadFromJsonAsync<LoadComposeDocumentResponse>();
        loaded!.SessionId.Should().NotBe(annotatedSessionId, "no sessionId was sent → a NEW session is minted");
        loaded.AnchoredAnnotations.Should().BeEmpty("a freshly-minted session carries no prior annotations");
        loaded.DefinedTermsTracking.Should().BeEmpty();
        loaded.ActionHistory.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAnnotations_AfterSave_RoundTripsThroughTheReadRoute()
    {
        const string speId = "spe-item-memory-003";
        const string matterId = "33333333-3333-3333-3333-333333333333";

        _fixture.ResetBoundaries();
        SetupSpeLoad(speId);

        var sessionId = await CreateResumableSessionAsync(speId, matterId);
        using var client = _fixture.CreateAuthenticatedClient();
        (await client.PostAsJsonAsync($"/api/compose/sessions/{sessionId}/annotations", BuildAnnotationsBody()))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await client.GetAsync(
            $"/api/compose/sessions/{sessionId}/annotations?tenantId={ComposeMemoryResumeFixture.TestTenantId}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var read = await getResponse.Content.ReadFromJsonAsync<ComposeAnnotationsResponse>();
        read!.AnchoredAnnotations.Should().ContainSingle(a => a.Id == "ann-1");
        read.DefinedTermsTracking.Should().ContainSingle(t => t.Term == "Confidential Information");
    }

    [Fact]
    public async Task SaveAnnotations_WhenSessionUnknown_Returns404()
    {
        _fixture.ResetBoundaries();
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/compose/sessions/{Guid.NewGuid():N}/annotations", BuildAnnotationsBody());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "annotations can only be saved onto an existing session (SaveComposeAnnotationsAsync throws not-found)");
    }

    [Fact]
    public async Task SaveAnnotations_WhenUnauthenticated_Returns401()
    {
        _fixture.ResetBoundaries();
        using var client = _fixture.CreateUnauthenticatedClient();

        using var content = JsonContent.Create(BuildAnnotationsBody());
        var response = await client.PostAsync($"/api/compose/sessions/{Guid.NewGuid():N}/annotations", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the annotations routes inherit RequireAuthorization() from the /api/compose group (ADR-008 + ADR-028)");
    }
}

/// <summary>
/// In-process BFF fixture that keeps the REAL <see cref="ComposeService"/> + <see cref="ChatSessionManager"/>
/// (NOT mocked) and replaces only the external SPE / Dataverse / indexing boundaries with Moqs, so the
/// endpoint→service→session-store wire is genuinely exercised. Config-key set mirrors
/// <c>ComposeCreateOnSaveFixture</c> (task 100; bff-extensions.md §F.2 Fixture-Config-FIRST).
/// </summary>
public sealed class ComposeMemoryResumeFixture : WebApplicationFactory<Program>
{
    public const string TestTenantId = "tenant-memory-resume-001";

    public Mock<ISpeFileOperations> SpeMock { get; } = new(MockBehavior.Loose);
    public Mock<IGenericEntityService> DataverseMock { get; } = new(MockBehavior.Loose);
    public Mock<IPostUploadIndexingEnqueuer> IndexingMock { get; } = new(MockBehavior.Loose);

    /// <summary>Resets the boundary mocks between tests (xUnit runs a class's tests sequentially).</summary>
    public void ResetBoundaries()
    {
        SpeMock.Reset();
        DataverseMock.Reset();
        IndexingMock.Reset();
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
                options.DefaultAuthenticateScheme = MemoryResumeFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = MemoryResumeFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, MemoryResumeFakeAuthHandler>(
                MemoryResumeFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = MemoryResumeFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = MemoryResumeFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            services.RemoveAll<IHostedService>();

            // Mock IDataverseService (used by the session cold-storage repo / health probes).
            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);

            // Mock the ChatSession cold tier (Dataverse) so an UNKNOWN session resolves cleanly to
            // null on the cold path — the production behavior — instead of the concrete repository's
            // FetchXML mock-NRE. GetSessionAsync then returns null (Redis miss → Cosmos miss →
            // Dataverse null), letting SaveComposeAnnotationsAsync throw the "not found" that maps to
            // 404. CreateSessionAsync is a no-op (the real ChatSessionManager still writes the
            // in-memory Redis hot cache the resume/read tests rely on).
            var chatRepoMock = new Mock<IChatDataverseRepository>();
            chatRepoMock
                .Setup(r => r.GetSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ChatSession?)null);
            services.RemoveAll<IChatDataverseRepository>();
            services.AddSingleton(chatRepoMock.Object);

            // ── The point of this fixture: KEEP the real ComposeService + ChatSessionManager; mock
            //    ONLY the external SPE / Dataverse-entity / indexing boundaries they depend on. ──
            services.RemoveAll<ISpeFileOperations>();
            services.AddSingleton(SpeMock.Object);

            services.RemoveAll<IGenericEntityService>();
            services.AddSingleton(DataverseMock.Object);

            services.RemoveAll<IPostUploadIndexingEnqueuer>();
            services.AddSingleton(IndexingMock.Object);
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

/// <summary>
/// Fake auth handler that authenticates any request carrying an <c>Authorization</c> header and
/// emits <c>oid</c> + <c>tid</c> claims.
/// </summary>
internal sealed class MemoryResumeFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "MemoryResumeFakeAuth";

    public MemoryResumeFakeAuthHandler(
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
            new("tid", ComposeMemoryResumeFixture.TestTenantId),
            new(ClaimTypes.NameIdentifier, oid),
            new(ClaimTypes.Name, $"Memory-Resume Test User {oid}"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
