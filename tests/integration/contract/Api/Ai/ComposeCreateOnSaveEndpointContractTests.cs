// Task 100 (E2E-R1) — Create-on-save vertical-slice contract test (KEEP path: endpoint-contract).
//
// WHY THIS FILE EXISTS (anti-recurrence, non-waivable per project CLAUDE.md §"E2E Definition-of-
// Done" + notes/e2e-gap-register.md Cluster 1):
//   The create-on-save SERVICE backbone (ComposeService.SaveAsync transient branch) was correct but
//   DEAD CODE over HTTP — no route reached it, the client never sent `containerId`, and the ONLY
//   test that touched Save mocked IComposeService entirely (ComposeEndpointsContractTests), so the
//   endpoint→DTO→service→SPE/Dataverse wire was never exercised. That is exactly how the false-green
//   shipped. This test drives the FULL slice through the REAL endpoints with the REAL ComposeService
//   (only the external SPE + Dataverse + indexing boundaries are mocked), so a broken wire between
//   the endpoint and the service fails the build — a service-only unit test would NOT catch it.
//
//   It POSTs an upload (proving the transient-mount bytes are served) then the create-on-save
//   (with `containerId`, NO `documentSpeId`) through the real POST routes, and asserts a new
//   sprk_document + SPE drive-item were created and the ChatSession rebound to the new record id.
//
// KEEP-path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `endpoint-contract`  ·  Path: `tests/integration/contract/Api/Ai/**` (csproj
//     auto-includes `tests/integration/contract/**`).
//   - "Every new endpoint => >=1 integration test": closes the contract for the new
//     `POST /api/compose/documents/create-on-save` route (task 100).
//
// Banned-pattern compliance (ADR-038 §4 + tests/CLAUDE.md): NO Mock<HttpMessageHandler>; mocks live
// ONLY at the SPE (ISpeFileOperations) / Dataverse (IGenericEntityService) / indexing module
// boundaries per ADR-038 §4 "mock at module boundaries". The class-under-test (ComposeService) and
// the HTTP boundary are REAL. Assertions are HTTP-observable + persisted side-effects.

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
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Through-the-wire (anti-recurrence) contract tests for FR-05 create-on-save (task 100). Boots the
/// BFF in-process with the REAL <see cref="ComposeService"/> and only the external SPE / Dataverse /
/// indexing boundaries mocked, and drives upload → create-on-save through the real POST routes.
/// </summary>
public sealed class ComposeCreateOnSaveEndpointContractTests
    : IClassFixture<ComposeCreateOnSaveFixture>
{
    private readonly ComposeCreateOnSaveFixture _fixture;

    public ComposeCreateOnSaveEndpointContractTests(ComposeCreateOnSaveFixture fixture)
    {
        _fixture = fixture;
    }

    // Minimal valid DOCX ZIP-signature bytes — treated as an opaque payload by the transient branch
    // (the SPE boundary is mocked; no real OOXML parsing occurs on this path).
    private static readonly byte[] DraftBytes =
        { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00, 0x01, 0x02, 0x03, 0x04 };

    [Fact]
    public async Task UploadThenCreateOnSave_TransientDraft_PersistsNewDocumentAndSpeItemAndRebindsSession()
    {
        // ── Arrange ────────────────────────────────────────────────────────────────────────────
        const string containerId = "b!container-bu-001";
        const string mintedSpeItemId = "spe-item-created-001";
        const string resolvedDriveId = "drive-created-001";
        var newDocumentId = Guid.NewGuid();
        var uploadFileId = Guid.NewGuid().ToString("N");

        _fixture.ResetBoundaries();

        // SPE boundary: the transient branch resolves the drive from the client-supplied container,
        // then mints a new drive-item under OBO. Capture the container it resolves + the bytes.
        string? resolvedContainerArg = null;
        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((c, _) => resolvedContainerArg = c)
            .ReturnsAsync(resolvedDriveId);
        _fixture.SpeMock
            .Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: mintedSpeItemId,
                Name: "draft.docx",
                ParentId: null,
                Size: DraftBytes.Length,
                CreatedDateTime: DateTimeOffset.UtcNow,
                LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"v1-etag\"",
                IsFolder: false,
                WebUrl: null,
                DriveId: resolvedDriveId));

        // Dataverse boundary: no existing row (alt-key lookup returns null) → create fires. Capture
        // the created entity so we can assert the SPE item id was recorded on the new sprk_document.
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);
        Entity? createdEntity = null;
        _fixture.DataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => createdEntity = e)
            .ReturnsAsync(newDocumentId);

        // Indexing boundary: sync-OBO indexing succeeds (non-terminal for this assertion).
        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        // Pre-create a REAL ChatSession (DocumentId unset) and seed the retained upload bytes so the
        // upload leg + the rebind are both exercised end-to-end.
        string sessionId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
            var session = await sessions.CreateSessionAsync(ComposeCreateOnSaveFixture.TestTenantId, documentId: null);
            sessionId = session.SessionId;

            var cache = scope.ServiceProvider.GetRequiredService<ITenantCache>();
            await cache.SetAsync(
                ComposeCreateOnSaveFixture.TestTenantId,
                "doc-upload-binary",
                $"{sessionId}:{uploadFileId}",
                1,
                DraftBytes);
        }

        using var client = _fixture.CreateAuthenticatedClient();

        // ── Act 1: transient-mount upload (real /api/compose/upload) ─────────────────────────────
        var uploadResponse = await client.PostAsJsonAsync(
            "/api/compose/upload", new { sessionId, documentId = uploadFileId });

        uploadResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "the upload endpoint serves the transient draft's retained bytes for the editor mount");
        var uploadBody = await uploadResponse.Content.ReadFromJsonAsync<ComposeUploadResponse>();
        uploadBody!.Content.Should().Equal(DraftBytes, "the exact retained bytes are served for the transient mount");

        // ── Act 2: create-on-save with containerId + NO documentSpeId (the body the client sends) ──
        var createOnSaveResponse = await client.PostAsJsonAsync(
            "/api/compose/documents/create-on-save",
            new
            {
                containerId,
                tenantId = ComposeCreateOnSaveFixture.TestTenantId,
                sessionId,
                content = uploadBody.Content,
                displayName = "draft.docx",
            });

        // ── Assert: HTTP contract ────────────────────────────────────────────────────────────────
        createOnSaveResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "the transient create-on-save reaches ComposeService.SaveAsync's create branch over the real wire");
        var result = await createOnSaveResponse.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        result.Should().NotBeNull();
        result!.DocumentRecordId.Should().Be(newDocumentId, "a NEW sprk_document was created");
        result.WasPromotedThisSave.Should().BeTrue("first Save of a transient draft creates the record (FR-06)");
        result.DocumentSpeId.Should().Be(mintedSpeItemId,
            "the server returns the minted SPE id so a second Save targets the real drive-item (gap 1.7)");

        // ── Assert: persisted side-effects (this is what a service-only unit test misses) ─────────
        resolvedContainerArg.Should().Be(containerId,
            "the client-resolved BU container flowed containerId (body) → request.ContainerId → SPE resolve (gaps 1.1/1.2/1.4)");
        _fixture.SpeMock.Verify(s => s.UploadSmallAsUserAsync(
            It.IsAny<HttpContext>(), resolvedDriveId, It.IsAny<string>(),
            It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once,
            "a new SPE drive-item was minted in the resolved BU drive");
        createdEntity.Should().NotBeNull("a new sprk_document row was created");
        createdEntity!.LogicalName.Should().Be("sprk_document");
        createdEntity.GetAttributeValue<string>("sprk_graphitemid").Should().Be(mintedSpeItemId,
            "the new document row points at the minted SPE drive-item");

        // ── Assert: session rebind (FR-07) ───────────────────────────────────────────────────────
        using (var scope = _fixture.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
            var reloaded = await sessions.GetSessionAsync(
                ComposeCreateOnSaveFixture.TestTenantId, sessionId, CancellationToken.None);
            reloaded.Should().NotBeNull();
            reloaded!.DocumentId.Should().Be(newDocumentId.ToString(),
                "the session rebinds from the transient draft to the new sprk_documentid (FR-07)");
        }
    }

    [Fact]
    public async Task CreateOnSave_WhenContainerIdMissing_Returns400()
    {
        _fixture.ResetBoundaries();
        using var client = _fixture.CreateAuthenticatedClient();

        // No containerId → the server cannot know which BU container to create the drive-item in
        // (the BFF does NOT resolve BU→container — Fork A / multi-container INV-7).
        var response = await client.PostAsJsonAsync(
            "/api/compose/documents/create-on-save",
            new
            {
                tenantId = ComposeCreateOnSaveFixture.TestTenantId,
                sessionId = Guid.NewGuid().ToString("N"),
                content = DraftBytes,
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "create-on-save requires the client-resolved containerId (Fork A)");
        _fixture.SpeMock.Verify(
            s => s.UploadSmallAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never, "no SPE work happens when the request is rejected at validation");
    }

    [Fact]
    public async Task CreateOnSave_WhenUnauthenticated_Returns401()
    {
        _fixture.ResetBoundaries();
        using var client = _fixture.CreateUnauthenticatedClient();

        using var content = JsonContent.Create(new { containerId = "c", tenantId = "t", sessionId = "s", content = DraftBytes });
        var response = await client.PostAsync("/api/compose/documents/create-on-save", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "create-on-save inherits RequireAuthorization() from the /api/compose group (ADR-008 + ADR-028)");
    }

    // ── Task 110 (UAT-R1) — non-waivable E2E DoD ────────────────────────────────────────────────
    // The Browse/open-local-file first Save legitimately has NO chat session — the client sends
    // sessionId:"" (ComposeWorkspace.tsx `sessionId: state.sessionId`, unset on the Browse path).
    // BEFORE task 110 this 400'd at ComposeEndpoints.cs:1312 (and would have thrown at
    // ComposeService.SaveAsync/PromoteIfEphemeralAsync). This drives the EMPTY-sessionId body
    // through the REAL create-on-save route and asserts 200 + the persisted sprk_document
    // side-effect, and that NO ChatSession is rebound (the FR-07 rebind is skipped, not attempted).
    // The WITH-session rebind-fires case is proven by
    // UploadThenCreateOnSave_TransientDraft_PersistsNewDocumentAndSpeItemAndRebindsSession above.
    [Fact]
    public async Task CreateOnSave_WithEmptySessionId_Returns200AndPersistsDocumentWithoutRebind()
    {
        // ── Arrange ────────────────────────────────────────────────────────────────────────────
        const string containerId = "b!container-bu-sessionless";
        const string mintedSpeItemId = "spe-item-sessionless-001";
        const string resolvedDriveId = "drive-sessionless-001";
        const string sentinelDocId = "sentinel-doc-untouched";
        var newDocumentId = Guid.NewGuid();

        _fixture.ResetBoundaries();

        string? resolvedContainerArg = null;
        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((c, _) => resolvedContainerArg = c)
            .ReturnsAsync(resolvedDriveId);
        _fixture.SpeMock
            .Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: mintedSpeItemId,
                Name: "draft.docx",
                ParentId: null,
                Size: DraftBytes.Length,
                CreatedDateTime: DateTimeOffset.UtcNow,
                LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"v1-etag\"",
                IsFolder: false,
                WebUrl: null,
                DriveId: resolvedDriveId));

        // Dataverse: no existing row → create fires. Capture the created entity for the persisted assertion.
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);
        Entity? createdEntity = null;
        _fixture.DataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => createdEntity = e)
            .ReturnsAsync(newDocumentId);

        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        // Pre-create an UNRELATED real session bound to a sentinel DocumentId. The sessionless Save
        // must NOT mutate it — proving no rebind was attempted on the empty-session path.
        string unrelatedSessionId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
            var session = await sessions.CreateSessionAsync(
                ComposeCreateOnSaveFixture.TestTenantId, documentId: sentinelDocId);
            unrelatedSessionId = session.SessionId;
        }

        using var client = _fixture.CreateAuthenticatedClient();

        // ── Act: create-on-save with EMPTY sessionId (the body the Browse client actually sends) ──
        var response = await client.PostAsJsonAsync(
            "/api/compose/documents/create-on-save",
            new
            {
                containerId,
                tenantId = ComposeCreateOnSaveFixture.TestTenantId,
                sessionId = string.Empty,
                content = DraftBytes,
                displayName = "draft.docx",
            });

        // ── Assert: HTTP contract — 200, NOT the pre-110 400 ─────────────────────────────────────
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a transient first Save with no chat session must succeed (task 110 — the sessionId guard is relaxed on the create-on-save path)");
        var result = await response.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        result.Should().NotBeNull();
        result!.DocumentRecordId.Should().Be(newDocumentId, "a NEW sprk_document was created even without a session");
        result.WasPromotedThisSave.Should().BeTrue("first Save of a transient draft creates the record (FR-06)");
        result.DocumentSpeId.Should().Be(mintedSpeItemId, "the server returns the minted SPE id");

        // ── Assert: persisted side-effect (the non-waivable E2E DoD — a service-only test misses this) ──
        resolvedContainerArg.Should().Be(containerId, "the client-resolved BU container still flows through without a session");
        _fixture.SpeMock.Verify(s => s.UploadSmallAsUserAsync(
            It.IsAny<HttpContext>(), resolvedDriveId, It.IsAny<string>(),
            It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once,
            "a new SPE drive-item was minted in the resolved BU drive");
        createdEntity.Should().NotBeNull("a new sprk_document row was persisted without a session (R5-E: file + record + index still required)");
        createdEntity!.LogicalName.Should().Be("sprk_document");
        createdEntity.GetAttributeValue<string>("sprk_graphitemid").Should().Be(mintedSpeItemId,
            "the persisted document row points at the minted SPE drive-item");

        // ── Assert: NO rebind attempted — the unrelated session's binding is untouched ────────────
        using (var scope = _fixture.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
            var reloaded = await sessions.GetSessionAsync(
                ComposeCreateOnSaveFixture.TestTenantId, unrelatedSessionId, CancellationToken.None);
            reloaded.Should().NotBeNull();
            reloaded!.DocumentId.Should().Be(sentinelDocId,
                "the sessionless Save skips the FR-07 rebind entirely — no ChatSession is mutated");
        }
    }
}

/// <summary>
/// In-process BFF fixture that keeps the REAL <see cref="ComposeService"/> (NOT mocked) and replaces
/// only the external SPE / Dataverse / indexing boundaries with Moqs, so the endpoint→service wire
/// is genuinely exercised. Config-key set mirrors <c>ComposeContractFixture</c> (bff-extensions.md
/// §F.2 Fixture-Config-FIRST).
/// </summary>
public sealed class ComposeCreateOnSaveFixture : WebApplicationFactory<Program>
{
    public const string TestTenantId = "tenant-create-on-save-001";

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
                options.DefaultAuthenticateScheme = CreateOnSaveFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = CreateOnSaveFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, CreateOnSaveFakeAuthHandler>(
                CreateOnSaveFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = CreateOnSaveFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = CreateOnSaveFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            services.RemoveAll<IHostedService>();

            // Mock IDataverseService (used by the session cold-storage repo / health probes).
            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);

            // ── The point of this fixture: KEEP the real ComposeService; mock ONLY the external
            //    SPE / Dataverse-entity / indexing boundaries it depends on. ──
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
/// emits <c>oid</c> + <c>tid</c> claims (the upload endpoint reads tenant from the <c>tid</c> claim).
/// </summary>
internal sealed class CreateOnSaveFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "CreateOnSaveFakeAuth";

    public CreateOnSaveFakeAuthHandler(
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
            new("tid", ComposeCreateOnSaveFixture.TestTenantId),
            new(System.Security.Claims.ClaimTypes.NameIdentifier, oid),
            new(System.Security.Claims.ClaimTypes.Name, $"Create-On-Save Test User {oid}"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
