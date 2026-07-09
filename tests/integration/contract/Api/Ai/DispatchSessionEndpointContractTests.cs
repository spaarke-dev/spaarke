using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Contract tests for the Click entry path's server surface
/// <c>POST /api/ai/chat/sessions/{sessionId}/dispatch</c> (FR-P1-04,
/// ai-architecture-redesign-r1 task 023b / ADR-039).
/// </summary>
/// <remarks>
/// <para>
/// <b>Hosting approach</b>: mirrors <see cref="SummarizeSessionEndpointContractTests"/> —
/// a minimal in-process <see cref="WebApplication"/> mapping ONLY this endpoint with the
/// REAL <see cref="SessionDispatchOrchestrator"/> + REAL prompted executor
/// (<see cref="ActionRunner"/> + <see cref="PromptSchemaRenderer"/>) + REAL
/// <see cref="OutputRouter"/> (wrapped in a recording decorator) against module-boundary
/// test doubles per ADR-038. Reuses the sibling fixture's public helpers
/// (<see cref="TestableChatSessionManager"/>, <see cref="StubOpenAiClient"/>,
/// <see cref="SummarizeFakeAuthHandler"/>).
/// </para>
/// <para>
/// <b>Coverage (task 023b scope)</b>: happy path streams via the catalog
/// (binding-id resolution → ActionRunner → terminal complete chunk); ADR-040
/// ledger-first ordering (the terminal chunk renders FROM the stored entry, proven by
/// payload substitution); args pass-through (chip <c>fileIds</c> reach the text fetch);
/// unknown binding → 404 <c>dispatch.binding-not-found</c>; non-GUID bindingId → 400;
/// kill-switch 503 (<see cref="FeatureDisabledException"/> pre-stream); disposition /
/// kind P1-envelope rejections → 422; session-not-found 404; auth 401s.
/// </para>
/// </remarks>
public class DispatchSessionEndpointContractTests : IClassFixture<DispatchSessionEndpointTestFixture>
{
    private readonly DispatchSessionEndpointTestFixture _fx;

    private const string TestSessionId = "11111111-2222-3333-4444-555555555555";
    private static readonly Guid TestBindingId = DispatchSessionEndpointTestFixture.DispatchBindingId;

    public DispatchSessionEndpointContractTests(DispatchSessionEndpointTestFixture fx)
    {
        _fx = fx;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — SSE 200 via the catalog (binding-id → Action → ActionRunner)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_HappyPath_StreamsSseCompleteChunkViaBindingIdCatalogPath()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(TestSessionId, fileId: "file-001");
        _fx.OpenAi.RawJsonToReturn =
            """{"tldr":["Dispatch takeaway"],"summary":"Dispatch summary.","keywords":"dispatch","entities":{"organizations":[],"persons":[]}}""";

        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/dispatch",
            new { bindingId = TestBindingId.ToString(), args = new { } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("data: ", "AnalysisChunk frames use the canonical `data: {json}\\n\\n` framing");
        body.Should().Contain("\"type\":\"complete\"",
            "the catalog path yields a terminal Completed chunk (same wire shape as /summarize)");
        body.Should().Contain("Dispatch takeaway",
            "the executor's structured output flows to the wire via the stored ledger entry");

        // ADR-039: the chip's binding_id IS the routing decision — resolution went through
        // the by-id read on the ONE routing service, nothing else.
        _fx.ConsumerRoutingMock.Verify(
            c => c.GetBindingByIdAsync(TestBindingId, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "FR-P1-04 — dispatch resolves the Binding BY ID via IConsumerRoutingService");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ADR-040 — ledger write BEFORE render; the terminal chunk renders FROM the
    // STORED entry (proven by substituting the stored payload)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_HappyPath_LedgerWritePrecedesRender_TerminalChunkRendersStoredEntry()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(TestSessionId, fileId: "file-001");
        _fx.OpenAi.RawJsonToReturn =
            """{"tldr":["executor-raw"],"summary":"executor raw output","keywords":"raw","entities":{"organizations":[],"persons":[]}}""";
        // The router stores a DIFFERENT payload than the executor produced. If the endpoint
        // rendered pre-store output, the wire would show "executor raw output".
        _fx.Router.SubstituteStoredPayloadJson =
            """{"tldr":["stored-entry"],"summary":"stored ledger payload","keywords":"stored","entities":{"organizations":[],"persons":[]}}""";

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/dispatch",
            new { bindingId = TestBindingId.ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("stored ledger payload",
            "ADR-040 render-follows-store — the terminal chunk is deserialized from the " +
            "STORED SessionOutput payload returned by IOutputRouter.RouteAsync");
        body.Should().NotContain("executor raw output",
            "the pre-store executor output must NEVER render directly");

        // And the router received the executor's raw output (the ledger write happened,
        // with the real OutputRouter storing {bindingId}@t{n} before the render chunk).
        _fx.Router.LastExecutorOutput.Should().NotBeNull("RouteAsync is the mandatory ledger seam");
        _fx.Router.LastExecutorOutput!.Value.GetRawText().Should().Contain("executor-raw");
        _fx.Router.LastRouted.Should().NotBeNull();
        _fx.Router.LastRouted!.Entry.Key.Should().Be($"{TestBindingId}@t1",
            "the stored entry is addressable as {bindingId}@t{n} (ADR-040)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ARGS PASS-THROUGH — chip args (fileIds) reach the execution boundary
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_ArgsFileIds_PassThroughToSessionFileTextFetch()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(TestSessionId, fileId: "file-A", fileId2: "file-B");

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/dispatch",
            new
            {
                bindingId = TestBindingId.ToString(),
                // Chip args forwarded verbatim by the client (e.g. the M4 confirm chip's
                // shape) — the SERVER owns the typed parse; unknown members are ignored.
                args = new { fileIds = new[] { "file-A" }, confirmedDocType = "nda" },
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        _fx.TextSourceMock.Verify(
            t => t.FetchAsync(
                It.IsAny<string>(),
                TestSessionId,
                It.Is<IReadOnlyList<ChatSessionFile>>(files =>
                    files.Count == 1 && files[0].FileId == "file-A"),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "the chip's fileIds arg selects the session-file subset (FR-08); " +
            "unknown args members are accepted and ignored at P1");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NEXT-STEP CHIPS — the dispatched Binding's sprk_chiptransitions stream as
    // a `chips` chunk AFTER the terminal complete chunk (G-P1 UAT fix 2026-07-05:
    // previously only the Event path emitted chips, so every chip click
    // permanently emptied the strip)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_BindingWithChipTransitions_StreamsChipsChunkAfterComplete()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(TestSessionId, fileId: "file-001");
        _fx.ConsumerRoutingMock
            .Setup(c => c.GetBindingByIdAsync(TestBindingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_fx.BuildDefaultBinding() with
            {
                ChipTransitions = new[]
                {
                    new ChipTransition
                    {
                        TargetBindingId = TestBindingId.ToString(),
                        ChipLabel = "Summarize again",
                        RequiresAttachments = true,
                    },
                },
            });

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/dispatch",
            new { bindingId = TestBindingId.ToString(), args = new { fileIds = new[] { "file-001" } } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("\"type\":\"chips\"",
            "the dispatched Binding's chip transitions follow the stream so the client " +
            "always sees the CURRENT next-step chips after a Click dispatch");
        body.IndexOf("\"type\":\"chips\"", StringComparison.Ordinal).Should().BeGreaterThan(
            body.IndexOf("\"type\":\"complete\"", StringComparison.Ordinal),
            "chips are next-steps — they follow the terminal output chunk");
        body.Should().Contain("\"targetBindingId\":\"" + TestBindingId + "\"",
            "chips use the unified EventChip wire vocabulary (targetBindingId IS the routing decision, ADR-039)");
        body.Should().Contain("Summarize again");
        body.Should().Contain("\"requiresAttachments\":true",
            "the authored requires_attachments flag survives the transition → chip mapping");
        var chipsSegment = body[body.IndexOf("\"type\":\"chips\"", StringComparison.Ordinal)..];
        chipsSegment.Should().NotContain("\"args\"",
            "transition chips carry NO frozen fileIds — a follow-up click (e.g. 'Summarize again') " +
            "resolves the file set AT DISPATCH TIME (FR-08 default = the CURRENT session manifest). " +
            "G-P1 UAT round-2 fix 2026-07-06: pre-filled fileIds froze the follow-up to the ORIGINAL " +
            "files, silently excluding files uploaded after the first dispatch");
    }

    [Fact]
    public async Task Post_BindingWithoutChipTransitions_StreamsNoChipsChunk()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(TestSessionId, fileId: "file-001");

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/dispatch",
            new { bindingId = TestBindingId.ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("\"type\":\"chips\"",
            "no transitions ⇒ no chips chunk (the client keeps its previously-rendered chips)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ADR-039 — unknown binding id: clean 4xx, stable errorCode, NO fallback
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_UnknownBindingId_Returns404_BindingNotFound()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(TestSessionId, fileId: "file-001");
        _fx.ConsumerRoutingMock
            .Setup(c => c.GetBindingByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Binding?)null);

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/dispatch",
            new { bindingId = Guid.NewGuid().ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "ADR-039 — the server resolves the Binding; unknown ids get a clean error, no fallback");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("dispatch.binding-not-found",
            "ADR-019: stable errorCode the client dispatchConsumer surfaces verbatim");
        raw.Should().Contain("correlationId");
    }

    [Fact]
    public async Task Post_NonGuidBindingId_Returns400_BindingIdInvalid()
    {
        _fx.Reset();
        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/dispatch",
            new { bindingId = "chat-summarize" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the binding-id resolution rule is GUID-only (every chip emitter sends the " +
            "sprk_playbookconsumer row GUID) — consumer-type strings are NOT a second vocabulary");
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("dispatch.binding-id-invalid");
    }

    [Fact]
    public async Task Post_MissingBindingId_Returns400_BindingRequired()
    {
        _fx.Reset();
        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/dispatch",
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("dispatch.binding-required");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // P1 ENVELOPE — non-prompted kind / non-informational disposition reject 422
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_CodedActionKind_Returns422_ActionKindUnsupported()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(TestSessionId, fileId: "file-001");
        _fx.ConsumerRoutingMock
            .Setup(c => c.GetBindingByIdAsync(TestBindingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_fx.BuildDefaultBinding() with { ActionKind = ActionKind.Coded });

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/dispatch",
            new { bindingId = TestBindingId.ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("dispatch.action-kind-unsupported");
    }

    [Fact]
    public async Task Post_StubbedDisposition_Returns422_DispositionNotSupported()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(TestSessionId, fileId: "file-001");
        // Overlay's OutputRouter leg is still a loud stub — the dispatch seam rejects it
        // PRE-RUN (before any LLM spend). WorkProduct left this rejection at task 047
        // (FR-P3-08) when its routing leg landed; Informational left it at P1.
        _fx.ConsumerRoutingMock
            .Setup(c => c.GetBindingByIdAsync(TestBindingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_fx.BuildDefaultBinding() with { Disposition = BindingDisposition.Overlay });

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/dispatch",
            new { bindingId = TestBindingId.ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "a disposition whose routing leg is still a stub is rejected PRE-RUN (before any LLM spend)");
        (await response.Content.ReadAsStringAsync()).Should().Contain("dispatch.disposition-not-supported");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // KILL SWITCH — 503 via canonical FeatureDisabled shape (ADR-032 Null peer)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_FeatureDisabled_Returns503_WithDispatchDisabledErrorCode()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(TestSessionId, fileId: "file-001");

        // FeatureDisabledException at the routing boundary escapes the orchestrator
        // pre-stream — the same ADR-032 P3 path NullSessionDispatchOrchestrator produces
        // when compound AI is OFF (throws on the first MoveNextAsync with this errorCode).
        _fx.ConsumerRoutingMock
            .Setup(c => c.GetBindingByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FeatureDisabledException("ai.dispatch.disabled",
                "AI capability dispatch requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true."));

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/dispatch",
            new { bindingId = TestBindingId.ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "FeatureDisabledException pre-stream maps to the canonical 503 (ADR-018/ADR-019)");
        (await response.Content.ReadAsStringAsync()).Should().Contain("ai.dispatch.disabled");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SESSION + ROUTE VALIDATION
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_SessionNotFound_Returns404_SessionNotFound()
    {
        _fx.Reset();
        _fx.Sessions.Session = null;

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/dispatch",
            new { bindingId = TestBindingId.ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("dispatch.session-not-found");
        raw.Should().NotContain(TestSessionId,
            "ADR-019: do not echo identifiers in error detail strings");
    }

    [Fact]
    public async Task Post_InvalidGuidSessionId_Returns400_SessionIdInvalid()
    {
        _fx.Reset();
        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/api/ai/chat/sessions/not-a-guid/dispatch",
            new { bindingId = TestBindingId.ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("sessionId.invalid");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AUTH — 401 unauthenticated + 401 missing tid
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Unauthenticated_Returns401()
    {
        _fx.Reset();
        var client = _fx.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/dispatch",
            new { bindingId = TestBindingId.ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().NotBe("text/event-stream");
    }

    [Fact]
    public async Task Post_MissingTenantClaim_Returns401_TidMissing()
    {
        _fx.Reset();
        var client = _fx.CreateAuthenticatedClientWithoutTid();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/dispatch",
            new { bindingId = TestBindingId.ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Contain("auth.tid-missing");
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
            TenantId: "00000000-0000-0000-0000-000000000abc",
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
/// Test fixture for <see cref="DispatchSessionEndpointContractTests"/>. Hosts a minimal
/// <see cref="WebApplication"/> mapping ONLY
/// <see cref="DispatchSessionEndpoint.MapDispatchSessionEndpoint"/> against the real
/// orchestrator + real prompted executor + real (recorded) OutputRouter. Reuses the
/// sibling summarize fixture's public helper types.
/// </summary>
public sealed class DispatchSessionEndpointTestFixture : IAsyncLifetime, IDisposable
{
    public TestableChatSessionManager Sessions { get; } = new();
    public Mock<IConsumerRoutingService> ConsumerRoutingMock { get; } = new();
    public Mock<IScopeResolverService> ScopeResolverMock { get; } = new();
    public Mock<ISessionFileTextSource> TextSourceMock { get; } = new();
    public StubOpenAiClient OpenAi { get; } = new();
    public RecordingOutputRouter Router { get; private set; } = null!;

    internal static readonly Guid DispatchBindingId = Guid.Parse("651194cd-3670-f111-ab0e-70a8a590c51c");
    internal static readonly Guid DispatchActionId = Guid.Parse("eeb05bfd-1260-f111-ab0b-70a8a59455f4");

    private WebApplication? _app;

    public async Task InitializeAsync()
    {
        Router = new RecordingOutputRouter(Sessions);
        ConfigureDefaults();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
        });

        builder.Logging.ClearProviders();

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

        builder.Services.AddRateLimiter(opt =>
        {
            opt.AddPolicy("ai-context", _ =>
                System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("ai-context-test"));
        });

        var authMock = new Mock<IAiAuthorizationService>();
        builder.Services.AddSingleton(authMock.Object);

        // FR-P1-04 catalog-path dependencies — module-boundary doubles (ADR-038); the
        // prompted executor is REAL (ActionRunner + PromptSchemaRenderer over the stub
        // IOpenAiClient), and the OutputRouter seam is the REAL router inside a recorder.
        builder.Services.AddSingleton<ChatSessionManager>(Sessions);
        builder.Services.AddSingleton(ConsumerRoutingMock.Object);
        builder.Services.AddSingleton(ScopeResolverMock.Object);
        builder.Services.AddSingleton(TextSourceMock.Object);
        builder.Services.AddSingleton<IOpenAiClient>(OpenAi);
        builder.Services.AddSingleton<PromptSchemaRenderer>();
        builder.Services.AddSingleton<IActionRunner, ActionRunner>();
        // ADR-043 E-10: the dispatch seam resolves inputs via the REAL ContextBinder.
        builder.Services.AddSingleton<Sprk.Bff.Api.Services.Ai.Context.IContextBinder,
                                      Sprk.Bff.Api.Services.Ai.Context.ContextBinder>();
        builder.Services.AddSingleton<IOutputRouter>(Router);

        // FR-P2-03 (task 032): the orchestrator resolves pending elicitation gates at
        // the dispatch seam — the REAL unified store over the test session manager +
        // in-memory tenant cache (no Redis).
        builder.Services.AddSingleton(sp => new PendingPlanManager(
            new Sprk.Bff.Api.Tests.Infrastructure.Cache.InMemoryTenantCache(),
            Sessions,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PendingPlanManager>>()));

        // FR-P4-05 (task 054): metering counters emitted at the dispatch seam.
        builder.Services.AddSingleton<Sprk.Bff.Api.Telemetry.AiTelemetry>();
        builder.Services.AddScoped<SessionDispatchOrchestrator>();

        builder.WebHost.UseTestServer();

        _app = builder.Build();

        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseRateLimiter();

        _app.MapDispatchSessionEndpoint();

        await _app.StartAsync();
    }

    public Task DisposeAsync() => _app?.StopAsync() ?? Task.CompletedTask;

    public void Dispose() => _app?.DisposeAsync().AsTask().GetAwaiter().GetResult();

    public void Reset()
    {
        Sessions.Session = null;
        OpenAi.RawJsonToReturn = """{"tldr":["default"],"summary":"default","keywords":"default","entities":{"organizations":[],"persons":[]}}""";
        Router.ResetRecording();
        ConsumerRoutingMock.Reset();
        ScopeResolverMock.Reset();
        TextSourceMock.Reset();
        ConfigureDefaults();
    }

    /// <summary>The default resolvable Binding — mirrors the seeded chat-summarize row (task 020/022).</summary>
    public Binding BuildDefaultBinding() => new()
    {
        BindingId = DispatchBindingId,
        ConsumerType = ConsumerTypes.ChatSummarize,
        ConsumerCode = "default",
        Environment = "*",
        ActionId = DispatchActionId,
        ActionKind = ActionKind.Prompted,
        Ucid = "UC-A-1",
        Disposition = BindingDisposition.Informational,
    };

    private void ConfigureDefaults()
    {
        ConsumerRoutingMock
            .Setup(c => c.GetBindingByIdAsync(DispatchBindingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildDefaultBinding());

        ScopeResolverMock
            .Setup(s => s.GetActionAsync(DispatchActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction
            {
                Id = DispatchActionId,
                Name = "Summarize Document for Chat",
                SystemPrompt = """{"$schema":"https://spaarke.com/schemas/prompt/v1","instruction":{"role":"You are the Spaarke Summarize-for-Chat assistant.","task":"Summarize the session file text in the ## Document section."},"output":{"fields":[{"name":"tldr","type":"array","description":"bullets"}],"structuredOutput":true}}""",
                OutputSchemaJson = """{"type":"object","additionalProperties":false,"required":["tldr"],"properties":{"tldr":{"type":"array","items":{"type":"string"}}}}""",
                Temperature = 0.0m,
            });

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
        var opts = _app!.Services.GetRequiredService<SummarizeFakeAuthOptions>();
        opts.IncludeTid = includeTid;
    }
}

/// <summary>
/// Recording decorator over the REAL <see cref="OutputRouter"/> (the ADR-040 ledger
/// seam). Records the executor output RouteAsync received and the RoutedOutput it
/// returned; optionally SUBSTITUTES the stored payload so tests can prove the terminal
/// chunk renders from the STORED entry (render follows store) rather than from
/// pre-store executor output.
/// </summary>
public sealed class RecordingOutputRouter : IOutputRouter
{
    private readonly OutputRouter _inner;

    public RecordingOutputRouter(ChatSessionManager sessions)
    {
        _inner = new OutputRouter(sessions, Mock.Of<ILogger<OutputRouter>>());
    }

    /// <summary>The raw executor output the last RouteAsync call received (pre-store).</summary>
    public JsonElement? LastExecutorOutput { get; private set; }

    /// <summary>The last RoutedOutput returned (the STORED entry the caller renders from).</summary>
    public RoutedOutput? LastRouted { get; private set; }

    /// <summary>When set, the stored entry's payload is replaced with this JSON (render-follows-store proof).</summary>
    public string? SubstituteStoredPayloadJson { get; set; }

    public void ResetRecording()
    {
        LastExecutorOutput = null;
        LastRouted = null;
        SubstituteStoredPayloadJson = null;
    }

    public async Task<RoutedOutput> RouteAsync(
        ChatSession session,
        Binding binding,
        JsonElement output,
        IReadOnlyList<string>? sourceRefs = null,
        CancellationToken cancellationToken = default)
    {
        LastExecutorOutput = output.Clone();

        var routed = await _inner.RouteAsync(session, binding, output, sourceRefs, cancellationToken);

        if (SubstituteStoredPayloadJson is not null)
        {
            using var doc = JsonDocument.Parse(SubstituteStoredPayloadJson);
            routed = new RoutedOutput
            {
                Entry = routed.Entry with { Payload = doc.RootElement.Clone() },
                Session = routed.Session,
            };
        }

        LastRouted = routed;
        return routed;
    }
}
