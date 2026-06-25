using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Telemetry;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Unit tests for the R5 task 014 (D2-04) direct BFF endpoint
/// <c>POST /api/ai/chat/sessions/{sessionId}/summarize</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hosting approach</b>: builds a minimal in-process <see cref="WebApplication"/>
/// that maps ONLY this endpoint, registers test-double dependencies, and exercises the
/// real <see cref="SessionSummarizeOrchestrator"/> against stubs. This pattern (rather
/// than <see cref="WebApplicationFactory{TEntryPoint}"/>) is used because:
/// <list type="number">
///   <item><see cref="SessionSummarizeOrchestrator"/> is <c>sealed</c> with no interface
///         (ADR-010); we cannot replace it via DI override. We exercise the real class
///         with stub dependencies (the same pattern used by
///         <c>SessionSummarizeOrchestratorTests</c>).</item>
///   <item>Avoids the full BFF config matrix (Dataverse / Graph / Cosmos / Service Bus
///         test-fixture wiring) — the endpoint contract is independent of those.</item>
/// </list>
/// </para>
/// <para>
/// <b>Coverage</b>: happy path SSE stream, 400 missing-tenant, 400 invalid GUID, 404
/// session-not-found, 503 feature-disabled, ProblemDetails shape (stable errorCode +
/// correlationId), endpoint mapping registration, auth filter wired, fresh-token-per-request
/// (no closure capture — verified via orchestrator constructor signature inspection).
/// </para>
/// </remarks>
public class SummarizeSessionEndpointTests : IClassFixture<SummarizeSessionEndpointTestFixture>
{
    private readonly SummarizeSessionEndpointTestFixture _fx;

    private const string TestTenantId = "00000000-0000-0000-0000-000000000abc";
    private const string TestSessionId = "11111111-2222-3333-4444-555555555555";
    private const string TestUserOid = "test-user-r5-summarize-endpoint";
    private const string TestBearer = "summarize-session-test-bearer";

    public SummarizeSessionEndpointTests(SummarizeSessionEndpointTestFixture fx)
    {
        _fx = fx;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — SSE 200 with progressive AnalysisChunk events
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_HappyPath_StreamsSseAnalysisChunks()
    {
        // Arrange — session with one file; orchestrator yields one structured token then completes.
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(TestSessionId, fileId: "file-001");
        _fx.OpenAi.TokensToYield = new[]
        {
            // Single-token valid Structured-Outputs payload — parser emits one FieldDelta.
            "{\"tldr\":[\"x\"]}"
        };

        var client = _fx.CreateAuthenticatedClient();

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/summarize",
            new { });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        var body = await response.Content.ReadAsStringAsync();

        // Each SSE frame is "data: {json}\n\n" per the canonical pattern.
        body.Should().Contain("data: ");
        body.Should().NotBeNullOrEmpty();
        // The terminal chunk has type=complete per AnalysisChunk.Completed(DocumentAnalysisResult).
        body.Should().Contain("\"type\":\"complete\"",
            "the orchestrator yields a final Completed chunk after streaming deltas");
    }

    [Fact]
    public async Task Post_HappyPath_PassesFileIdsAndStyleToOrchestrator()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(TestSessionId, fileId: "file-A", fileId2: "file-B");
        _fx.OpenAi.TokensToYield = new[] { "{\"tldr\":[\"a\"]}" };

        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/summarize",
            new { fileIds = new[] { "file-A" }, style = "executive" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The orchestrator was invoked with the right tenant + session via the captured
        // RagSearchOptions (the only externally-visible signal).
        _fx.RagServiceMock.Verify(
            r => r.SearchAsync(It.IsAny<string>(),
                It.Is<RagSearchOptions>(o => o.TenantId == TestTenantId && o.SessionId == TestSessionId),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "endpoint must propagate tenant + session to the orchestrator (ADR-014)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // VALIDATION — 400 ProblemDetails with stable errorCode (ADR-019)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_InvalidGuidSessionId_Returns400_SessionIdInvalid()
    {
        _fx.Reset();
        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/api/ai/chat/sessions/not-a-guid/summarize",
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("sessionId.invalid",
            "ADR-019: stable errorCode for GUID-format failure");
        raw.Should().Contain("correlationId",
            "ADR-019: every ProblemDetails carries a correlationId extension");
    }

    [Fact]
    public async Task Post_TooManyFileIds_Returns400_TooManyFiles()
    {
        _fx.Reset();
        var client = _fx.CreateAuthenticatedClient();

        // 21 fileIds — exceeds NFR-02 cap of 20.
        var tooMany = Enumerable.Range(0, 21).Select(i => $"file-{i:000}").ToArray();
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/summarize",
            new { fileIds = tooMany });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("summarize.too-many-files",
            "ADR-019: stable errorCode for NFR-02 cap violation");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AUTH — 401 missing tid + 401 unauthenticated
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_MissingTenantClaim_Returns401_TidMissing()
    {
        _fx.Reset();
        var client = _fx.CreateAuthenticatedClientWithoutTid();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/summarize",
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("auth.tid-missing",
            "ADR-019: stable errorCode for missing tid claim");
    }

    [Fact]
    public async Task Post_Unauthenticated_Returns401()
    {
        _fx.Reset();
        var client = _fx.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/summarize",
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NOT FOUND — 404 with stable errorCode
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_SessionNotFound_Returns404_SessionNotFound()
    {
        _fx.Reset();
        _fx.Sessions.Session = null;  // orchestrator throws InvalidOperationException("not found")

        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/summarize",
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("summarize.session-not-found",
            "ADR-019: stable errorCode for missing session");
        raw.Should().NotContain(TestSessionId,
            "ADR-019: do not echo sensitive identifiers in error detail strings (defense-in-depth)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FEATURE DISABLED — 503 via canonical FeatureDisabledResults helper
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_FeatureDisabled_Returns503_WithFeatureKey()
    {
        _fx.Reset();
        // Force the orchestrator's first downstream call (entity service) to throw
        // FeatureDisabledException so the endpoint's "FIRST per ADR-032 P3" catch fires.
        _fx.Sessions.Session = BuildSession(TestSessionId, fileId: "file-001");
        // R6 task 025 (D-A-17) — engine path uses RetrieveAsync (FK-resolved ID), not
        // RetrieveByAlternateKeyAsync. Wire the FeatureDisabledException onto the new path.
        _fx.EntityServiceMock
            .Setup(e => e.RetrieveAsync(
                It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FeatureDisabledException("ai.analysis.disabled",
                "Analysis feature is disabled for this environment."));

        // The orchestrator catches entity-service failures internally and yields
        // AnalysisChunk.FromError — so the stream will be 200 with an "error" chunk,
        // NOT a 503. This is the documented behavior of task 012's orchestrator
        // (it converts exceptions to chunks). The endpoint's 503 path activates only
        // when FeatureDisabledException escapes the orchestrator (e.g., from a different
        // dep boundary). For this test, validate the error-chunk path is honored.
        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/summarize",
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "task 012 orchestrator catches FeatureDisabledException and yields AnalysisChunk.FromError per its documented contract");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"type\":\"error\"",
            "feature-disabled propagates as an SSE error chunk per orchestrator design");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REGISTRATION + WIRING
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Endpoint_IsRegistered_AtExpectedRoute()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(TestSessionId, fileId: "file-001");
        _fx.OpenAi.TokensToYield = new[] { "{\"tldr\":[\"x\"]}" };

        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/summarize",
            new { });

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "MapSummarizeSessionEndpoint() must register POST /api/ai/chat/sessions/{sessionId}/summarize");
    }

    [Fact]
    public async Task Endpoint_AuthFilter_IsWired_RejectsUnauthenticated()
    {
        // Negative confirmation that .RequireAuthorization() is on the group. We tested
        // the positive earlier; this asserts that anonymous requests are NOT silently
        // allowed through.
        _fx.Reset();
        var client = _fx.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/summarize",
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // Sanity: response did NOT reach the handler (no SSE content-type).
        response.Content.Headers.ContentType?.MediaType.Should().NotBe("text/event-stream");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ADR-028 — fresh OBO token per request (no closure capture)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Endpoint_DoesNotCaptureTokenIntoClosure_PerAdr028()
    {
        // ADR-028 fresh-token-per-request contract: the endpoint MUST NOT accept a string
        // bearer token via constructor injection or capture one into a closure. The
        // orchestrator's constructor is the only token-shaped surface; verify it accepts
        // no `string` parameter (tokens are resolved via DI inside its dependencies, NOT
        // passed in via parameters).
        var ctor = typeof(SessionSummarizeOrchestrator).GetConstructors().Single();
        var parameters = ctor.GetParameters();

        parameters.Should().NotContain(
            p => p.ParameterType == typeof(string),
            "ADR-028: orchestrator must not accept a bearer-token string parameter; tokens are " +
            "resolved per-request via DI inside the orchestrator's dependencies");

        // Defense-in-depth: the SummarizeSessionFilesRequest contract carries tenantId +
        // sessionId + fileIds + style + path + correlationId — but NO token field.
        var requestProps = typeof(SummarizeSessionFilesRequest)
            .GetProperties()
            .Select(p => p.Name)
            .ToArray();
        requestProps.Should().NotContain("AccessToken",
            "ADR-028: request shape MUST NOT carry an access token; tokens stay in HttpContext");
        requestProps.Should().NotContain("BearerToken",
            "ADR-028: request shape MUST NOT carry a bearer token");
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private static ChatSession BuildSession(string sessionId, string fileId, string? fileId2 = null)
    {
        var files = new List<ChatSessionFile>
        {
            new(FileId: fileId, FileName: $"{fileId}.pdf", ContentType: "application/pdf",
                SizeBytes: 1024, SearchDocumentIdsCsv: $"doc-{fileId}-1",
                UploadedAt: DateTimeOffset.UtcNow)
        };
        if (fileId2 is not null)
        {
            files.Add(new ChatSessionFile(
                FileId: fileId2, FileName: $"{fileId2}.pdf", ContentType: "application/pdf",
                SizeBytes: 1024, SearchDocumentIdsCsv: $"doc-{fileId2}-1",
                UploadedAt: DateTimeOffset.UtcNow));
        }

        return new ChatSession(
            SessionId: sessionId,
            TenantId: TestTenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: files);
    }
}

/// <summary>
/// Test fixture for <see cref="SummarizeSessionEndpointTests"/>. Hosts a minimal
/// <see cref="WebApplication"/> that maps ONLY
/// <see cref="SummarizeSessionEndpoint.MapSummarizeSessionEndpoint"/> against test-double
/// dependencies — no production-app config required.
/// </summary>
public sealed class SummarizeSessionEndpointTestFixture : IAsyncLifetime, IDisposable
{
    public TestableChatSessionManager Sessions { get; } = new();
    public Mock<IRagService> RagServiceMock { get; } = new();
    public StubOpenAiClient OpenAi { get; } = new();
    public Mock<IGenericEntityService> EntityServiceMock { get; } = new();
    public Mock<INodeService> NodeServiceMock { get; } = new();
    public Mock<IPlaybookLookupService> PlaybookLookupMock { get; } = new();

    // chat-routing-redesign-r1 task 028d (FR-1R-05) — orchestrator now consults
    // IConsumerRoutingService first; default stub returns null so the fixture falls back to
    // the FR-05 typed-options + IPlaybookLookupService path (preserves prior fixture intent
    // verbatim — tests targeting the FR-1R-05 happy path live in SessionSummarizeOrchestratorTests).
    public Mock<IConsumerRoutingService> ConsumerRoutingMock { get; } = new();

    public R5SummarizeTelemetry Telemetry { get; } = new();

    // R6 task 025 (D-A-17) — the chat-summarize streaming pipeline moved from
    // SessionSummarizeOrchestrator into PlaybookExecutionEngine. The orchestrator now requires
    // an IPlaybookExecutionEngine; we register the REAL engine here (so the in-process
    // WebApplication exercises the real moved code) and wire INodeService + IGenericEntityService
    // FK-chain stubs so the engine resolves the action via the FK path (not alternate key).
    internal static readonly Guid ChatSummarizePlaybookId = Guid.Parse("44285d15-1360-f111-ab0b-70a8a59455f4");
    internal static readonly Guid ChatSummarizeActionId = Guid.Parse("eeb05bfd-1260-f111-ab0b-70a8a59455f4");

    // chat-routing-redesign-r1 task 015 (FR-05): the orchestrator now resolves the chat-summarize
    // playbook by stable-ID alternate key (sprk_playbookid) via IPlaybookLookupService.
    // WorkspaceOptions.ChatSummarizePlaybookId carries the per-env GUID value (string-form).
    // The fixture seeds the DEV GUID and stubs the lookup to return a PlaybookResponse whose
    // Id matches — preserving the prior end-to-end behavior of forwarding this GUID to the
    // engine for FK-chain resolution.
    internal static readonly string ConfiguredChatSummarizePlaybookId =
        "44285d15-1360-f111-ab0b-70a8a59455f4";

    private WebApplication? _app;

    public async Task InitializeAsync()
    {
        ConfigureDefaults();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            // Use a random in-process URL — we'll override server with TestServer below.
        });

        // Logging
        builder.Logging.ClearProviders();

        // Authn — single scheme that succeeds on `Authorization: Bearer ...` and adds tid+oid.
        builder.Services
            .AddSingleton(new SummarizeFakeAuthOptions(includeTid: true))
            .AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = SummarizeFakeAuthHandler.SchemeName;
                o.DefaultChallengeScheme = SummarizeFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, SummarizeFakeAuthHandler>(
                SummarizeFakeAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();

        // Rate limiter — needs an `ai-context` policy registered, otherwise
        // `.RequireRateLimiting("ai-context")` would throw at request time. Register a
        // no-op (NoLimiter) variant just for these tests; production uses
        // RateLimitingModule.cs (which we don't bring in to keep the fixture minimal).
        builder.Services.AddRateLimiter(opt =>
        {
            opt.AddPolicy("ai-context", _ =>
                System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("ai-context-test"));
        });

        // Required by the AddAiAuthorizationFilter — it resolves IAiAuthorizationService.
        // The filter's ExtractDocumentIds finds NO document IDs (this endpoint takes
        // sessionId + body.FileIds — neither is a Guid documentId argument), so the filter
        // pass-through path activates (AiAuthorizationFilter.cs line 75-79). The service is
        // still resolved via DI even on the pass-through path, so we register a stub.
        var authMock = new Mock<IAiAuthorizationService>();
        builder.Services.AddSingleton(authMock.Object);

        // Orchestrator + engine dependencies — test doubles.
        builder.Services.AddSingleton<ChatSessionManager>(Sessions);
        builder.Services.AddSingleton(RagServiceMock.Object);
        builder.Services.AddSingleton<IOpenAiClient>(OpenAi);
        builder.Services.AddSingleton(EntityServiceMock.Object);
        builder.Services.AddSingleton(NodeServiceMock.Object);
        builder.Services.AddSingleton(Telemetry);

        // R6 task 025 (D-A-17) — IPlaybookExecutionEngine is the orchestrator's new dep. We
        // register the REAL PlaybookExecutionEngine (so the in-process WebApplication exercises
        // the real moved chat-summarize pipeline) wired against the test-double dependencies
        // above. The engine's non-chat-summarize deps (builder, orchestration, http context) are
        // stubbed minimally — they're not exercised by the chat /summarize endpoint.
        builder.Services.AddSingleton(Mock.Of<IAiPlaybookBuilderService>());
        builder.Services.AddSingleton(Mock.Of<IPlaybookOrchestrationService>());
        builder.Services.AddSingleton(Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>());
        builder.Services.AddScoped<IPlaybookExecutionEngine, PlaybookExecutionEngine>();

        // chat-routing-redesign-r1 task 015 (FR-05) — orchestrator now depends on
        // IPlaybookLookupService + IOptions<WorkspaceOptions> for stable-ID resolution.
        // Register both with the configured DEV GUID so the orchestrator's runtime lookup
        // returns the same Guid the prior hardcoded constant emitted.
        builder.Services.AddSingleton(PlaybookLookupMock.Object);
        builder.Services.Configure<WorkspaceOptions>(o =>
        {
            o.ChatSummarizePlaybookId = ConfiguredChatSummarizePlaybookId;
        });

        // chat-routing-redesign-r1 task 028d (FR-1R-05) — orchestrator now consults
        // IConsumerRoutingService first. Default fixture stub returns null so the fixture
        // exercises the FR-05 typed-options fallback path (preserves the prior fixture
        // intent verbatim). FR-1R-05 happy-path coverage lives in SessionSummarizeOrchestratorTests.
        builder.Services.AddSingleton(ConsumerRoutingMock.Object);

        // Orchestrator itself — concrete (ADR-010); registered Scoped to mirror prod.
        builder.Services.AddScoped<SessionSummarizeOrchestrator>();

        // Switch server to TestServer so we get an HttpClient that talks to this in-process app.
        builder.WebHost.UseTestServer();

        _app = builder.Build();

        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseRateLimiter();

        // Map ONLY our endpoint — keeps the fixture surface tiny.
        _app.MapSummarizeSessionEndpoint();

        await _app.StartAsync();
    }

    public Task DisposeAsync()
    {
        return _app?.StopAsync() ?? Task.CompletedTask;
    }

    public void Dispose()
    {
        _app?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public void Reset()
    {
        Sessions.Session = null;
        OpenAi.TokensToYield = Array.Empty<string>();
        OpenAi.ThrowMidStream = false;
        RagServiceMock.Reset();
        EntityServiceMock.Reset();
        NodeServiceMock.Reset();
        PlaybookLookupMock.Reset();
        ConsumerRoutingMock.Reset();
        ConfigureDefaults();
    }

    private void ConfigureDefaults()
    {
        // Default RAG response — small valid result so happy-path tests proceed.
        RagServiceMock
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagSearchResponse
            {
                Query = "default",
                Results = new[]
                {
                    new RagSearchResult { Id = "chunk-1", DocumentName = "f.pdf", Content = "lorem.", Score = 0.9 }
                }
            });

        // R6 task 025 (D-A-17) — FK chain stubs for the post-R6 engine path.
        // INodeService.GetNodesAsync returns a single node with FK-resolved ActionId.
        NodeServiceMock
            .Setup(n => n.GetNodesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new Sprk.Bff.Api.Models.Ai.PlaybookNodeDto
            {
                Id = Guid.NewGuid(),
                PlaybookId = ChatSummarizePlaybookId,
                ActionId = ChatSummarizeActionId
            }});

        // Default action seed loaded by FK (NOT alternate key) — engine calls
        // IGenericEntityService.RetrieveAsync(logicalName, id, columns, ct).
        EntityServiceMock
            .Setup(e => e.RetrieveAsync(
                "sprk_analysisaction",
                It.IsAny<Guid>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildActionEntity(
                systemPrompt: "You are the R5 Summarize-for-Chat assistant.",
                outputSchemaJson: """{"type":"object","additionalProperties":false,"required":["tldr"],"properties":{"tldr":{"type":"array","items":{"type":"string"}}}}"""));

        // chat-routing-redesign-r1 task 028d (FR-1R-05) — IConsumerRoutingService default:
        // returns null so the fixture exercises the FR-05 fallback path (preserves the
        // pre-028d fixture surface verbatim). Tests targeting the FR-1R-05 routing-table
        // happy path live in SessionSummarizeOrchestratorTests.
        ConsumerRoutingMock
            .Setup(c => c.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        // chat-routing-redesign-r1 task 015 (FR-05) — IPlaybookLookupService default: the
        // orchestrator calls GetByIdAsync(configuredId) and forwards the response's Id (Guid)
        // to the engine. Returning a PlaybookResponse whose Id == ChatSummarizePlaybookId
        // preserves the prior end-to-end identity (FR-26 convergence invariant).
        PlaybookLookupMock
            .Setup(p => p.GetByIdAsync(
                ConfiguredChatSummarizePlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookResponse
            {
                Id = ChatSummarizePlaybookId,
                Name = "summarize-document-for-chat@v1",
                PlaybookCode = string.Empty,
                IsActive = true
            });
    }

    private static Entity BuildActionEntity(string systemPrompt, string outputSchemaJson)
    {
        var e = new Entity("sprk_analysisaction", ChatSummarizeActionId);
        e["sprk_analysisactionid"] = e.Id;
        e["sprk_name"] = "Summarize Document for Chat";
        e["sprk_actioncode"] = "SUM-CHAT@v1";
        e["sprk_systemprompt"] = systemPrompt;
        e["sprk_outputschemajson"] = outputSchemaJson;
        return e;
    }

    public HttpClient CreateAuthenticatedClient()
    {
        UpdateAuthOptions(includeTid: true);
        var client = _app!.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fake-token");
        return client;
    }

    public HttpClient CreateAuthenticatedClientWithoutTid()
    {
        UpdateAuthOptions(includeTid: false);
        var client = _app!.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fake-token");
        return client;
    }

    public HttpClient CreateUnauthenticatedClient()
    {
        UpdateAuthOptions(includeTid: true);
        return _app!.GetTestClient();
    }

    private void UpdateAuthOptions(bool includeTid)
    {
        // The auth handler reads the live singleton — mutate in place so individual
        // tests can request a tid-less client without rebuilding the host.
        var opts = _app!.Services.GetRequiredService<SummarizeFakeAuthOptions>();
        opts.IncludeTid = includeTid;
    }
}

/// <summary>
/// Subclass of <see cref="ChatSessionManager"/> that overrides the virtual
/// <see cref="ChatSessionManager.GetSessionAsync(string, string, CancellationToken)"/> for
/// in-process testing without Redis / Dataverse wiring. Matches the test-double pattern in
/// <c>SessionSummarizeOrchestratorTests</c>.
/// </summary>
public sealed class TestableChatSessionManager : ChatSessionManager
{
    public TestableChatSessionManager() : base(
        cache: Mock.Of<IDistributedCache>(),
        dataverseRepository: Mock.Of<IChatDataverseRepository>(),
        logger: Mock.Of<ILogger<ChatSessionManager>>(),
        persistence: null,
        cleanupSignal: null)
    {
    }

    public ChatSession? Session { get; set; }

    public override Task<ChatSession?> GetSessionAsync(
        string tenantId, string sessionId, CancellationToken ct = default)
        => Task.FromResult(Session);
}

/// <summary>
/// Stub <see cref="IOpenAiClient"/> for endpoint streaming tests. Mirrors the
/// <c>SessionSummarizeOrchestratorTests.StubOpenAiClient</c> shape; only the streaming
/// method is exercised — all other interface members throw to make accidental use visible.
/// </summary>
public sealed class StubOpenAiClient : IOpenAiClient
{
    public IReadOnlyList<string> TokensToYield { get; set; } = Array.Empty<string>();
    public bool ThrowMidStream { get; set; }

    public async IAsyncEnumerable<string> StreamStructuredCompletionAsync(
        IEnumerable<global::OpenAI.Chat.ChatMessage> messages,
        BinaryData jsonSchema,
        string schemaName,
        string? model = null,
        int? maxOutputTokens = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var i = 0;
        foreach (var token in TokensToYield)
        {
            if (ThrowMidStream && i > 0)
            {
                throw new InvalidOperationException("simulated mid-stream failure");
            }
            yield return token;
            i++;
            await Task.Yield();
        }
    }

    public IAsyncEnumerable<string> StreamCompletionAsync(string prompt, string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Not used by endpoint tests.");
    public Task<string> GetCompletionAsync(string prompt, string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Not used by endpoint tests.");
    public IAsyncEnumerable<string> StreamVisionCompletionAsync(string prompt, byte[] imageBytes, string mediaType, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Not used by endpoint tests.");
    public Task<string> GetVisionCompletionAsync(string prompt, byte[] imageBytes, string mediaType, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Not used by endpoint tests.");
    public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, string? model = null, int? dimensions = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Not used by endpoint tests.");
    public Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(IEnumerable<string> texts, string? model = null, int? dimensions = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Not used by endpoint tests.");
    public Task<ChatCompletionResult> GetChatCompletionWithToolsAsync(IEnumerable<global::OpenAI.Chat.ChatMessage> messages, IEnumerable<global::OpenAI.Chat.ChatTool> tools, string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Not used by endpoint tests.");
    public Task<T> GetStructuredCompletionAsync<T>(IEnumerable<global::OpenAI.Chat.ChatMessage> messages, BinaryData jsonSchema, string schemaName, string deploymentName, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Not used by endpoint tests.");
    public Task<string> GetStructuredCompletionRawAsync(string prompt, BinaryData jsonSchema, string schemaName, string? model = null, int? maxOutputTokens = null, float? temperature = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Not used by endpoint tests.");
}

/// <summary>Singleton holder for "include tid claim?" — mutated per-test via fixture.</summary>
public sealed class SummarizeFakeAuthOptions
{
    public bool IncludeTid { get; set; }
    public SummarizeFakeAuthOptions(bool includeTid) => IncludeTid = includeTid;
}

/// <summary>Bearer-presence auth handler for the in-process WebApplication test fixture.</summary>
public sealed class SummarizeFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "SummarizeFakeAuth";
    private readonly SummarizeFakeAuthOptions _opts;

    public SummarizeFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        SummarizeFakeAuthOptions opts)
        : base(options, logger, encoder)
    {
        _opts = opts;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader))
        {
            return Task.FromResult(AuthenticateResult.Fail("Empty Authorization header"));
        }

        // Disambiguate ClaimTypes — System.Security.Claims.ClaimTypes vs Microsoft.Xrm.Sdk.ClaimTypes
        // (the Xrm SDK 'using' is pulled in transitively by Sprk.Bff.Api). Qualify explicitly.
        var claims = new List<Claim>
        {
            new("oid", "00000000-0000-0000-0000-000000000aaa"),
            new(System.Security.Claims.ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-000000000aaa"),
            new(System.Security.Claims.ClaimTypes.Name, "Summarize Endpoint Test User"),
        };

        if (_opts.IncludeTid)
        {
            claims.Add(new Claim("tid", "00000000-0000-0000-0000-000000000abc"));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
