using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Contract tests for the direct BFF endpoint
/// <c>POST /api/ai/chat/sessions/{sessionId}/summarize</c> — catalog-driven since FR-P1-01
/// (ai-architecture-redesign-r1 task 020).
/// </summary>
/// <remarks>
/// <para>
/// <b>Hosting approach</b>: builds a minimal in-process <see cref="WebApplication"/>
/// that maps ONLY this endpoint and exercises the REAL
/// <see cref="SessionDispatchOrchestrator"/> + REAL prompted executor
/// (<see cref="ActionRunner"/> + <see cref="PromptSchemaRenderer"/>) against module-boundary
/// test doubles (<see cref="IConsumerRoutingService"/>, <see cref="IScopeResolverService"/>,
/// <see cref="ISessionFileTextSource"/>, <see cref="IOpenAiClient"/>) per ADR-038. The
/// orchestrator is concrete with no interface (ADR-010), so the real class runs with stub
/// dependencies rather than a DI override.
/// </para>
/// <para>
/// <b>Coverage</b>: happy path SSE 200 with terminal <c>complete</c> chunk, tenant/session
/// propagation into the catalog path, 400 invalid GUID, 400 too-many-files, 401 missing tid,
/// 401 unauthenticated, 404 session-not-found, 503 feature-disabled (pre-stream
/// FeatureDisabledException), endpoint registration, auth filter wiring, ADR-028
/// fresh-token-per-request shape.
/// </para>
/// </remarks>
public class SummarizeSessionEndpointContractTests : IClassFixture<SummarizeSessionEndpointTestFixture>
{
    private readonly SummarizeSessionEndpointTestFixture _fx;

    private const string TestTenantId = "00000000-0000-0000-0000-000000000abc";
    private const string TestSessionId = "11111111-2222-3333-4444-555555555555";

    public SummarizeSessionEndpointContractTests(SummarizeSessionEndpointTestFixture fx)
    {
        _fx = fx;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — SSE 200 via the catalog path (Binding → Action → ActionRunner)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_HappyPath_StreamsSseCompleteChunk()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(TestSessionId, fileId: "file-001");
        _fx.OpenAi.RawJsonToReturn =
            """{"tldr":["Key takeaway"],"summary":"Contract summary.","keywords":"contract","entities":{"organizations":[],"persons":[]}}""";

        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/summarize",
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        var body = await response.Content.ReadAsStringAsync();

        // Each SSE frame is "data: {json}\n\n" per the canonical pattern.
        body.Should().Contain("data: ");
        body.Should().Contain("\"type\":\"complete\"",
            "the catalog path yields a terminal Completed chunk carrying the structured result");
        body.Should().Contain("Key takeaway",
            "the LLM's structured output flows through to the wire");
    }

    [Fact]
    public async Task Post_HappyPath_PropagatesTenantSessionAndFileSubsetToCatalogPath()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(TestSessionId, fileId: "file-A", fileId2: "file-B");
        _fx.OpenAi.RawJsonToReturn = """{"tldr":["a"],"summary":"s","keywords":"k","entities":{"organizations":[],"persons":[]}}""";

        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/summarize",
            new { fileIds = new[] { "file-A" }, style = "executive" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The orchestrator propagated tenant + session + the explicit file subset into the
        // session-file text fetch (the catalog path's data boundary — ADR-014).
        _fx.TextSourceMock.Verify(
            t => t.FetchAsync(
                TestTenantId,
                TestSessionId,
                It.Is<IReadOnlyList<ChatSessionFile>>(files =>
                    files.Count == 1 && files[0].FileId == "file-A"),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "endpoint must propagate tenant + session + the requested file subset (ADR-014 / FR-08)");

        // And the Binding was resolved via the ADR-039 routing surface.
        _fx.ConsumerRoutingMock.Verify(
            c => c.ResolveBindingAsync(
                ConsumerTypes.ChatSummarize,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "FR-P1-01 — dispatch resolves through the Binding contract, nothing else");
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
    public async Task Post_FeatureDisabled_Returns503_WithErrorCode()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(TestSessionId, fileId: "file-001");

        // Catalog resolution happens BEFORE the first chunk, so a FeatureDisabledException
        // thrown at the routing boundary escapes the orchestrator pre-stream — exactly the
        // documented ADR-032 P3 path the endpoint maps to a 503 ProblemDetails (the same
        // shape NullSessionDispatchOrchestrator produces when compound AI is OFF).
        _fx.ConsumerRoutingMock
            .Setup(c => c.ResolveBindingAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FeatureDisabledException("ai.summarize.disabled",
                "AI Summarize requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true."));

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/summarize",
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "FeatureDisabledException escaping pre-stream maps to the canonical 503 (ADR-018/ADR-019)");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("ai.summarize.disabled",
            "the kill-switch error code drives the ProblemDetails errorCode extension");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CATALOG MISCONFIGURATION — pre-stream failure maps to 500 (never 404)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_NoBindingRow_Returns500_InternalError()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(TestSessionId, fileId: "file-001");
        _fx.ConsumerRoutingMock
            .Setup(c => c.ResolveBindingAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Binding?)null);

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/summarize",
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError,
            "NFR-08 hard cutover — a missing Binding row is an operator misconfiguration " +
            "surfaced as a 500 (NOT a 404, and NOT a silent fallback)");
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("summarize.internal-error");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REGISTRATION + WIRING
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Endpoint_IsRegistered_AtExpectedRoute()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(TestSessionId, fileId: "file-001");
        _fx.OpenAi.RawJsonToReturn = """{"tldr":["x"],"summary":"s","keywords":"k","entities":{"organizations":[],"persons":[]}}""";

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
        // dispatch seam's constructor is the only token-shaped surface; verify it accepts
        // no `string` parameter (tokens are resolved via DI inside its dependencies, NOT
        // passed in via parameters).
        var ctor = typeof(SessionDispatchOrchestrator)
            .GetConstructors()
            .Single(c => c.IsPublic);
        var parameters = ctor.GetParameters();

        parameters.Should().NotContain(
            p => p.ParameterType == typeof(string),
            "ADR-028: the dispatch seam must not accept a bearer-token string parameter; tokens are " +
            "resolved per-request via DI inside its dependencies");

        // Defense-in-depth: the SessionDispatchRequest contract carries tenantId +
        // sessionId + bindingId + args + correlationId — but NO token field.
        var requestProps = typeof(SessionDispatchRequest)
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
/// Test fixture for <see cref="SummarizeSessionEndpointContractTests"/>. Hosts a minimal
/// <see cref="WebApplication"/> that maps ONLY
/// <see cref="SummarizeSessionEndpoint.MapSummarizeSessionEndpoint"/> against the real
/// orchestrator + real prompted executor with module-boundary test doubles (FR-P1-01
/// catalog path).
/// </summary>
public sealed class SummarizeSessionEndpointTestFixture : IAsyncLifetime, IDisposable
{
    public TestableChatSessionManager Sessions { get; } = new();
    public Mock<IConsumerRoutingService> ConsumerRoutingMock { get; } = new();
    public Mock<IScopeResolverService> ScopeResolverMock { get; } = new();
    public Mock<ISessionFileTextSource> TextSourceMock { get; } = new();
    public StubOpenAiClient OpenAi { get; } = new();

    internal static readonly Guid ChatSummarizeBindingId = Guid.Parse("651194cd-3670-f111-ab0e-70a8a590c51c");
    internal static readonly Guid ChatSummarizeActionId = Guid.Parse("eeb05bfd-1260-f111-ab0b-70a8a59455f4");

    private WebApplication? _app;

    public async Task InitializeAsync()
    {
        ConfigureDefaults();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
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
        // no-op (NoLimiter) variant just for these tests.
        builder.Services.AddRateLimiter(opt =>
        {
            opt.AddPolicy("ai-context", _ =>
                System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("ai-context-test"));
        });

        // Required by the AddAiAuthorizationFilter — it resolves IAiAuthorizationService.
        // The filter's ExtractDocumentIds finds NO document IDs on this endpoint, so the
        // pass-through path activates; the service is still resolved via DI.
        var authMock = new Mock<IAiAuthorizationService>();
        builder.Services.AddSingleton(authMock.Object);

        // ── FR-P1-01 catalog-path dependencies ──────────────────────────────────────────
        // Module-boundary test doubles (ADR-038): routing (Binding), scope resolver (Action
        // row), session-file text source. The prompted executor is REAL — ActionRunner +
        // PromptSchemaRenderer over the stub IOpenAiClient — so this fixture exercises the
        // production execution stack end-to-end below the LLM boundary.
        builder.Services.AddSingleton<ChatSessionManager>(Sessions);
        builder.Services.AddSingleton(ConsumerRoutingMock.Object);
        builder.Services.AddSingleton(ScopeResolverMock.Object);
        builder.Services.AddSingleton(TextSourceMock.Object);
        builder.Services.AddSingleton<IOpenAiClient>(OpenAi);
        builder.Services.AddSingleton<PromptSchemaRenderer>();
        builder.Services.AddSingleton<IActionRunner, ActionRunner>();
        // ADR-043 E-10: the dispatch seam resolves inputs via the REAL ContextBinder over the fixture's
        // session manager (writes the ContextEnvelope fingerprint; resolves the file operand for summarize).
        builder.Services.AddSingleton<IContextBinder, ContextBinder>();

        // FR-P1-02 (task 021) — REAL OutputRouter over the fixture's session manager: the
        // contract tests now exercise the live ledger write-before-render seam (the stored
        // SessionOutput is observable via Sessions.Session.Outputs after a happy-path POST).
        builder.Services.AddScoped<Sprk.Bff.Api.Services.Ai.IOutputRouter,
                                   Sprk.Bff.Api.Services.Ai.OutputRouter>();

        // FR-P2-03 (task 032): the dispatch seam resolves pending elicitation gates —
        // the REAL unified store over the test session manager + in-memory tenant cache.
        builder.Services.AddSingleton(sp => new PendingPlanManager(
            new Sprk.Bff.Api.Tests.Infrastructure.Cache.InMemoryTenantCache(),
            Sessions,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PendingPlanManager>>()));

        // FR-P3-05 (task 044): /summarize converged onto the ONE dispatch seam — the
        // endpoint resolves the chat-summarize Binding and delegates to the REAL
        // SessionDispatchOrchestrator (concrete per ADR-010; Scoped mirrors prod).
        // FR-P4-05 (task 054): metering counters emitted at the dispatch seam.
        builder.Services.AddSingleton<Sprk.Bff.Api.Telemetry.AiTelemetry>();
        // ADR-043 E-30: orchestrator ctor gained ICodedWorkflowRegistry — register the real
        // registry with an empty ICodedWorkflow set (prompted path doesn't exercise coded workflows).
        // Fixes E-30 ctor-drift that shipped this class red on master (see compose-r2 merge note).
        builder.Services.AddScoped<Sprk.Bff.Api.Services.Ai.ICodedWorkflowRegistry,
                                   Sprk.Bff.Api.Services.Ai.CodedWorkflowRegistry>();
        builder.Services.AddScoped<SessionDispatchOrchestrator>();

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
        OpenAi.RawJsonToReturn = """{"tldr":["default"],"summary":"default","keywords":"default","entities":{"organizations":[],"persons":[]}}""";
        ConsumerRoutingMock.Reset();
        ScopeResolverMock.Reset();
        TextSourceMock.Reset();
        ConfigureDefaults();
    }

    private void ConfigureDefaults()
    {
        // Default: the chat-summarize Binding row resolves (ADR-039 routing surface) with a
        // prompted Action target — mirrors the seeded spaarkedev1 row (task 020).
        ConsumerRoutingMock
            .Setup(c => c.ResolveBindingAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Binding
            {
                BindingId = ChatSummarizeBindingId,
                ConsumerType = ConsumerTypes.ChatSummarize,
                ConsumerCode = "default",
                Environment = "*",
                ActionId = ChatSummarizeActionId,
                ActionKind = ActionKind.Prompted,
                Ucid = "UC-A-1",
                Disposition = BindingDisposition.Informational,
            });

        // FR-P3-05 (task 044): the endpoint delegates to the dispatch seam, which re-loads
        // the Binding BY ID — mirror the resolve-by-type default above.
        ConsumerRoutingMock
            .Setup(c => c.GetBindingByIdAsync(
                ChatSummarizeBindingId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Binding
            {
                BindingId = ChatSummarizeBindingId,
                ConsumerType = ConsumerTypes.ChatSummarize,
                ConsumerCode = "default",
                Environment = "*",
                ActionId = ChatSummarizeActionId,
                ActionKind = ActionKind.Prompted,
                Ucid = "UC-A-1",
                Disposition = BindingDisposition.Informational,
            });

        // Default: the SUM-CHAT@v1 Action row (JPS system prompt + strict output schema).
        ScopeResolverMock
            .Setup(s => s.GetActionAsync(ChatSummarizeActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction
            {
                Id = ChatSummarizeActionId,
                Name = "Summarize Document for Chat",
                SystemPrompt = """{"$schema":"https://spaarke.com/schemas/prompt/v1","instruction":{"role":"You are the Spaarke Summarize-for-Chat assistant.","task":"Summarize the session file text in the ## Document section."},"output":{"fields":[{"name":"tldr","type":"array","description":"bullets"}],"structuredOutput":true}}""",
                OutputSchemaJson = """{"type":"object","additionalProperties":false,"required":["tldr"],"properties":{"tldr":{"type":"array","items":{"type":"string"}}}}""",
                Temperature = 0.0m,
            });

        // Default: session-file text fetch succeeds with inline text.
        TextSourceMock
            .Setup(t => t.FetchAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<ChatSessionFile>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionFileText
            {
                ExtractedText = "lorem ipsum session file text.",
                DisplayName = "f.pdf",
                ChunkCount = 1
            });
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
/// the dispatch-seam unit tests.
/// </summary>
public sealed class TestableChatSessionManager : ChatSessionManager
{
    public TestableChatSessionManager() : base(
        cache: Mock.Of<ITenantCache>(),
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
/// Stub <see cref="IOpenAiClient"/> for the catalog-path endpoint tests. Only
/// <see cref="GetStructuredCompletionRawAsync"/> (the prompted executor's LLM boundary) is
/// exercised — all other interface members throw to make accidental use visible.
/// </summary>
public sealed class StubOpenAiClient : IOpenAiClient
{
    public string RawJsonToReturn { get; set; } = "{}";

    /// <summary>The last assembled prompt the prompted executor sent to the LLM boundary. Lets a
    /// caller assert the resolved operand reached the prompt (e.g. the compose `## Input` section)
    /// without a live model — mirrors the seam-test stub's capture.</summary>
    public string? LastPrompt { get; private set; }

    public Task<string> GetStructuredCompletionRawAsync(
        string prompt, BinaryData jsonSchema, string schemaName, string? model = null,
        int? maxOutputTokens = null, float? temperature = null,
        CancellationToken cancellationToken = default)
    {
        LastPrompt = prompt;
        return Task.FromResult(RawJsonToReturn);
    }

    public IAsyncEnumerable<string> StreamStructuredCompletionAsync(
        IEnumerable<global::OpenAI.Chat.ChatMessage> messages, BinaryData jsonSchema,
        string schemaName, string? model = null, int? maxOutputTokens = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Not used by endpoint tests.");
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
