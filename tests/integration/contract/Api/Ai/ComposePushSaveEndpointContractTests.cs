// Task 055 (FR-28) — Push/save vertical-slice contract test (KEEP path: endpoint-contract).
// THE anti-recurrence forcing function for the two NEW push/save surfaces.
//
// WHY THIS FILE EXISTS (non-waivable per project CLAUDE.md §"E2E Definition-of-Done"):
//   Task 055 added two client-facing surfaces — POST /document/{id}/push-preview (new route) and
//   an EXTENDED POST /document/{id}/push-annotations (now returning preview + completionState AND
//   persisting a per-step JobAwareCompletionState to Redis, ADR-009). The 15 service-level unit
//   tests mock ISpeFileOperations and call ComposeService directly — none crosses the HTTP
//   boundary for the new route or the new persisted side-effect. That is exactly the false-green
//   class the DoD prevents: a broken endpoint→DTO→service wire (or an unregistered route) would
//   pass every unit test. This test drives BOTH surfaces through the REAL routes with the REAL
//   ComposeService + the REAL IDistributedCache the host registers (in-memory fallback), mocking
//   ONLY the external SPE / Dataverse / indexing boundaries, and asserts the persisted per-step
//   state by reading it back through the same cache — not via a mock.
//
// KEEP-path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `endpoint-contract` · Path: `tests/integration/contract/Api/Ai/**`.
//   - "Every new/changed endpoint => >=1 integration test": anchors the new push-preview contract
//     AND the extended push-annotations contract (preview + completionState + Redis side-effect).
//
// Banned-pattern compliance (ADR-038 §4 + tests/CLAUDE.md): NO Mock<HttpMessageHandler>; the
// ComposeService + ComposePushSaveStatusStore + routes under test are REAL; mocks live ONLY at the
// SPE (ISpeFileOperations) / Dataverse / indexing module boundaries. Assertions are HTTP-observable
// + persisted side-effects read back through the real cache.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
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
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Through-the-wire (anti-recurrence) contract tests for FR-28 push/save (task 055). Boots the BFF
/// in-process with the REAL <see cref="ComposeService"/> + the host's real in-memory
/// <see cref="IDistributedCache"/> and only the external SPE / Dataverse / indexing boundaries
/// mocked, and drives push-preview + push-annotations through the real POST routes.
/// </summary>
public sealed class ComposePushSaveEndpointContractTests
    : IClassFixture<ComposePushSaveFixture>
{
    private readonly ComposePushSaveFixture _fixture;

    public ComposePushSaveEndpointContractTests(ComposePushSaveFixture fixture)
    {
        _fixture = fixture;
    }

    // TrackChangeKind has no string-enum converter and the BFF configures none globally, so the
    // push wire contract carries `kind` as its NUMERIC enum value (Insertion=0, Deletion=1,
    // Comment=2). These constants make the wire values explicit + refactor-visible.
    private const int KindInsertion = (int)TrackChangeKind.Insertion;
    private const int KindDeletion = (int)TrackChangeKind.Deletion;
    private const int KindComment = (int)TrackChangeKind.Comment;

    private static readonly DateTimeOffset When = new(2026, 7, 10, 9, 0, 0, TimeSpan.Zero);

    // A DOCX whose single paragraph contains every annotation target ("quick", "fox", "lazy ").
    private static byte[] BuildDocx()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());
            body.AppendChild(new Paragraph(new Run(
                new Text("The quick brown fox jumps over the lazy dog.") { Space = SpaceProcessingModeValues.Preserve })));
            body.AppendChild(new SectionProperties());
            mainPart.Document.Save();
        }
        return stream.ToArray();
    }

    // The three accepted annotations the client assembles (1 comment + 1 insertion + 1 deletion).
    private static object[] ThreeAnnotations() => new object[]
    {
        new { kind = KindComment, targetText = "quick", commentText = "Consider a stronger adjective.", author = "Spaarke AI", date = When },
        new { kind = KindInsertion, targetText = "fox", newText = " (Vulpes vulpes)", author = "Spaarke AI", date = When },
        new { kind = KindDeletion, targetText = "lazy ", author = "Spaarke AI", date = When },
    };

    private void SetupDownload(string driveId, string documentSpeId, byte[] docxBytes) =>
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(
                It.IsAny<HttpContext>(), driveId, documentSpeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(docxBytes.ToArray()));

    /// <summary>Creates a REAL ChatSession and seeds it with N defined terms (the Compose-only
    /// collection with no Word-native representation) via the REAL ComposeService write path.</summary>
    private async Task<string> SeedSessionWithDefinedTermsAsync(int termCount)
    {
        using var scope = _fixture.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
        var session = await sessions.CreateSessionAsync(ComposePushSaveFixture.TestTenantId, documentId: "doc-pushsave");

        var terms = Enumerable.Range(0, termCount)
            .Select(i => new DefinedTerm { Term = $"Term {i}", Definition = $"def {i}", Source = "ai" })
            .ToList();

        var compose = scope.ServiceProvider.GetRequiredService<IComposeService>();
        await compose.SaveComposeAnnotationsAsync(new SaveComposeAnnotationsRequest
        {
            TenantId = ComposePushSaveFixture.TestTenantId,
            SessionId = session.SessionId,
            DefinedTermsTracking = terms,
        });

        return session.SessionId;
    }

    private async Task<JobAwareCompletionState?> ReadPersistedStateAsync(string documentSpeId)
    {
        using var scope = _fixture.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        return await new ComposePushSaveStatusStore(cache).GetAsync(documentSpeId, CancellationToken.None);
    }

    private static JobAwareState StateOf(JobAwareCompletionState state, string stepName) =>
        state.Steps.Single(s => s.StepName == stepName).State;

    // ─────────────────────────────────────────────────────────────────────────
    // 1. NEW push-preview route: returns counts + Word-vs-Compose split; touches NO SPE.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PushPreview_WithSessionDefinedTerms_ReturnsCountsAndSplit_WithoutTouchingSpe()
    {
        const string documentSpeId = "spe-item-preview-055";
        _fixture.ResetBoundaries();
        var sessionId = await SeedSessionWithDefinedTermsAsync(termCount: 2);

        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/compose/document/{documentSpeId}/push-preview",
            new
            {
                tenantId = ComposePushSaveFixture.TestTenantId,
                sessionId,
                annotations = ThreeAnnotations(),
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the new push-preview route reaches the REAL ComposeService.PreviewPushAnnotationsAsync over the wire");
        var body = await response.Content.ReadFromJsonAsync<PushPreviewResponse>();
        body!.Preview.CommentCount.Should().Be(1);
        body.Preview.InsertionCount.Should().Be(1);
        body.Preview.DeletionCount.Should().Be(1);
        body.Preview.TrackChangeCount.Should().Be(2, "insertions + deletions = the track-change half of the split");
        body.Preview.WordBoundCount.Should().Be(3, "all three annotations materialize as native Word markup");
        body.Preview.ComposeOnlyCount.Should().Be(2,
            "the session's 2 DefinedTermsTracking entries have no Word-native representation — they stay in Compose (sourced via SessionId over the wire)");

        // The preview is NON-MUTATING: no SPE download and no SPE write happen on this path.
        _fixture.SpeMock.Verify(s => s.DownloadFileAsUserAsync(
            It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "push-preview computes purely from the supplied batch + session — it never touches SPE");
        _fixture.SpeMock.Verify(s => s.ReplaceFileContentAsUserAsync(
            It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "a preview never writes");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. EXTENDED push-annotations: response carries preview + completionState, AND the ordered
    //    per-step JobAwareCompletionState (push→save→version) is PERSISTED to Redis (ADR-009).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PushAnnotations_HappyPath_ReturnsPreviewAndCompletionState_AndPersistsOrderedPerStepState()
    {
        const string driveId = "b!pushsave-001";
        const string documentSpeId = "spe-item-pushsave-001";
        const string loadEtag = "\"etag-load-1\"";
        const string newVersionId = "spe-item-pushsave-001-v2";
        _fixture.ResetBoundaries();
        SetupDownload(driveId, documentSpeId, BuildDocx());
        var sessionId = await SeedSessionWithDefinedTermsAsync(termCount: 2);

        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, documentSpeId, It.IsAny<Stream>(), loadEtag, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: newVersionId, Name: "contract.docx", ParentId: null, Size: 4321,
                CreatedDateTime: When, LastModifiedDateTime: When, ETag: "\"etag-v2\"",
                IsFolder: false, WebUrl: null, DriveId: driveId));

        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/compose/document/{documentSpeId}/push-annotations",
            new
            {
                driveId,
                tenantId = ComposePushSaveFixture.TestTenantId,
                ifMatch = loadEtag,
                annotations = ThreeAnnotations(),
                sessionId,
            });

        // ── HTTP contract: the response now carries preview + completionState ──────────────────────
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the extended push-annotations reaches the REAL ComposeService.PushAnnotationsAsync pipeline over the wire");
        var body = await response.Content.ReadFromJsonAsync<PushAnnotationsResponse>();
        body!.VersionId.Should().Be(newVersionId, "the save committed a new SPE version");
        body.Preview.Should().NotBeNull("the extended contract returns the Tier-2c preview as post-write evidence");
        body.Preview!.WordBoundCount.Should().Be(3);
        body.Preview.ComposeOnlyCount.Should().Be(2, "the Word-vs-Compose split reflects the session's defined terms");
        body.CompletionState.Should().NotBeNull("the extended contract returns the per-step JobAwareCompletionState");
        StateOf(body.CompletionState!, ComposeService.StepPush).Should().Be(JobAwareState.Completed);
        StateOf(body.CompletionState!, ComposeService.StepSave).Should().Be(JobAwareState.Completed);
        StateOf(body.CompletionState!, ComposeService.StepVersion).Should().Be(JobAwareState.Completed);
        body.CompletionState!.Aggregate.Should().Be(JobAwareState.Completed);

        // ── Persisted side-effect (this is what a service-only unit test can't prove through the
        //    wire): the ordered push→save→version state is in the host's real distributed cache. ────
        var persisted = await ReadPersistedStateAsync(documentSpeId);
        persisted.Should().NotBeNull(
            "the pipeline persists the per-step completion state to the real IDistributedCache (ADR-009) at sdap:compose:pushsave:{id}");
        persisted!.Steps.Select(s => s.StepName).Should().Equal(
            ComposeService.StepPush, ComposeService.StepSave, ComposeService.StepVersion);
        persisted.Aggregate.Should().Be(JobAwareState.Completed);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. NEGATIVE through the wire: an etag conflict on save returns the 412-equivalent AND the
    //    persisted state shows push=Completed, save=Failed, version=Queued, aggregate=Failed
    //    (no partial write).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PushAnnotations_WhenEtagConflict_Returns412_AndPersistsPushCompletedSaveFailedVersionQueued()
    {
        const string driveId = "b!pushsave-002";
        const string documentSpeId = "spe-item-pushsave-002";
        const string loadEtag = "\"etag-stale\"";
        _fixture.ResetBoundaries();
        SetupDownload(driveId, documentSpeId, BuildDocx());

        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, documentSpeId, It.IsAny<Stream>(), loadEtag, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EtagPreconditionFailedException(documentSpeId, loadEtag));

        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/compose/document/{documentSpeId}/push-annotations",
            new
            {
                driveId,
                tenantId = ComposePushSaveFixture.TestTenantId,
                ifMatch = loadEtag,
                annotations = ThreeAnnotations(),
            });

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed,
            "an ETag precondition failure surfaces as 412 through the real route (the document changed under the caller)");

        // The persisted state proves the abort point AND that nothing partially wrote: push landed,
        // save failed, version never started.
        var persisted = await ReadPersistedStateAsync(documentSpeId);
        persisted.Should().NotBeNull("even on failure the pipeline persists the per-step state for a future OutcomeCard");
        StateOf(persisted!, ComposeService.StepPush).Should().Be(JobAwareState.Completed,
            "the pure OOXML render succeeded before the write was attempted");
        StateOf(persisted!, ComposeService.StepSave).Should().Be(JobAwareState.Failed,
            "the If-Match write was rejected (412) — the save step failed");
        StateOf(persisted!, ComposeService.StepVersion).Should().Be(JobAwareState.Queued,
            "version never started once save failed — no partial write");
        persisted!.Aggregate.Should().Be(JobAwareState.Failed);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Auth — the NEW push-preview route inherits RequireAuthorization() from /api/compose.
    //    A 401 (not 404) also PROVES the route is registered: an unregistered route would 404.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PushPreview_WhenUnauthenticated_Returns401()
    {
        _fixture.ResetBoundaries();
        using var client = _fixture.CreateUnauthenticatedClient();

        using var content = JsonContent.Create(new
        {
            tenantId = ComposePushSaveFixture.TestTenantId,
            annotations = ThreeAnnotations(),
        });
        var response = await client.PostAsync("/api/compose/document/spe-x/push-preview", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the push-preview route is REGISTERED (a 404 here would mean the route is missing) and inherits " +
            "RequireAuthorization() from the /api/compose group (ADR-008 + ADR-028)");
    }
}

/// <summary>
/// In-process BFF fixture that keeps the REAL <see cref="ComposeService"/> + the host's real
/// in-memory <see cref="IDistributedCache"/> (Redis-off + AllowInMemoryFallback ⇒
/// <c>AddDistributedMemoryCache()</c>) and replaces only the external SPE / Dataverse / indexing
/// boundaries with Moqs, so the endpoint→service→cache wire is genuinely exercised. Config-key set
/// mirrors <c>ComposeCreateOnSaveFixture</c> (task 100; bff-extensions.md §F.2 Fixture-Config-FIRST).
/// </summary>
public sealed class ComposePushSaveFixture : WebApplicationFactory<Program>
{
    public const string TestTenantId = "tenant-push-save-001";

    public Mock<ISpeFileOperations> SpeMock { get; } = new(MockBehavior.Loose);
    public Mock<IGenericEntityService> DataverseMock { get; } = new(MockBehavior.Loose);
    public Mock<IPostUploadIndexingEnqueuer> IndexingMock { get; } = new(MockBehavior.Loose);

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
                options.DefaultAuthenticateScheme = PushSaveFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = PushSaveFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, PushSaveFakeAuthHandler>(
                PushSaveFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = PushSaveFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = PushSaveFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            services.RemoveAll<IHostedService>();

            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);

            // Cold ChatSession tier → null so a fresh session mints cleanly in the hot in-memory tier.
            var chatRepoMock = new Mock<IChatDataverseRepository>();
            chatRepoMock
                .Setup(r => r.GetSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ChatSession?)null);
            services.RemoveAll<IChatDataverseRepository>();
            services.AddSingleton(chatRepoMock.Object);

            // KEEP the real ComposeService + the host's real IDistributedCache; mock ONLY the
            // external SPE / Dataverse-entity / indexing boundaries.
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

/// <summary>Fake auth handler authenticating any request carrying an <c>Authorization</c> header.</summary>
internal sealed class PushSaveFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "PushSaveFakeAuth";

    public PushSaveFakeAuthHandler(
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
            new("tid", ComposePushSaveFixture.TestTenantId),
            new(ClaimTypes.NameIdentifier, oid),
            new(ClaimTypes.Name, $"Push-Save Test User {oid}"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
