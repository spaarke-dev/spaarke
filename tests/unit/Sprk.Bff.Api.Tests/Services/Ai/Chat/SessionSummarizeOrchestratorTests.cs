using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Infrastructure.Cache;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="SessionSummarizeOrchestrator"/> — the chat-Summarize convergence
/// orchestrator.
///
/// <para>
/// <b>R7 task 091 (FR-17) update</b>: the orchestrator now dispatches through the canonical
/// <see cref="IPlaybookOrchestrationService.ExecuteAsync"/> per ADR-013 (Option 1 from task 090
/// design — in-zone code may inject the orchestration service directly to preserve per-token
/// SSE UX). Tests verify the orchestrator's boundary responsibilities:
/// </para>
/// <list type="bullet">
///   <item>Public <see cref="SessionSummarizeOrchestrator.SummarizeSessionFilesAsync"/>
///         signature unchanged (convergence + ADR-010 reflection).</item>
///   <item>Argument validation (tenant + session required; NFR-02 ≤20 file cap).</item>
///   <item>Session lookup at the orchestrator boundary (missing session →
///         InvalidOperationException; endpoint maps to 404).</item>
///   <item>FR-1R-05 — routing-table consulted via IConsumerRoutingService.ResolveAsync with
///         ConsumerTypes.ChatSummarize; graceful-degrade to typed-options fallback on
///         null/empty.</item>
///   <item>Dispatch via IPlaybookOrchestrationService.ExecuteAsync (NOT
///         IPlaybookExecutionEngine.ExecuteChatSummarizeAsync — that path was retired by R7
///         task 091).</item>
///   <item>FR-04 multi-file interjection emitted BEFORE the playbook stream begins.</item>
///   <item>SSE adapter projects PlaybookStreamEvent → AnalysisChunk preserving per-token UX
///         (NodeProgress → FromContent, terminal NodeCompleted+DeliverOutput → Completed,
///         RunFailed → FromError).</item>
/// </list>
/// </summary>
public class SessionSummarizeOrchestratorTests
{
    private const string TenantId = "tenant-abc";
    private const string SessionId = "session-xyz";
    private const string FileId1 = "file-001";
    private const string FileId2 = "file-002";

    // FR-05 task 015 (chat-routing-redesign-r1): tests configure WorkspaceOptions with the
    // canonical DEV-environment value for the summarize-document-for-chat@v1 playbook.
    private const string ConfiguredChatSummarizePlaybookId = "44285d15-1360-f111-ab0b-70a8a59455f4";
    private static readonly Guid ResolvedChatSummarizePlaybookGuid =
        Guid.Parse(ConfiguredChatSummarizePlaybookId);

    private readonly TestableChatSessionManager _sessionManagerStub;
    private readonly Mock<IPlaybookOrchestrationService> _orchestrationServiceMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly HttpContext _httpContext;
    private readonly Mock<IPlaybookLookupService> _playbookLookupMock;
    private readonly Mock<IConsumerRoutingService> _consumerRoutingMock;
    private readonly IOptions<WorkspaceOptions> _workspaceOptions;
    private readonly Mock<ILogger<SessionSummarizeOrchestrator>> _loggerMock;

    public SessionSummarizeOrchestratorTests()
    {
        _sessionManagerStub = new TestableChatSessionManager();
        _orchestrationServiceMock = new Mock<IPlaybookOrchestrationService>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _httpContext = new DefaultHttpContext();
        _httpContextAccessorMock.SetupGet(a => a.HttpContext).Returns(_httpContext);
        _playbookLookupMock = new Mock<IPlaybookLookupService>();
        _consumerRoutingMock = new Mock<IConsumerRoutingService>();
        _workspaceOptions = Options.Create(new WorkspaceOptions
        {
            ChatSummarizePlaybookId = ConfiguredChatSummarizePlaybookId
        });
        _loggerMock = new Mock<ILogger<SessionSummarizeOrchestrator>>();

        // Default stub: GetByIdAsync(<configured id>, ct) → PlaybookResponse with Id = GUID.
        _playbookLookupMock
            .Setup(p => p.GetByIdAsync(ConfiguredChatSummarizePlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookResponse
            {
                Id = ResolvedChatSummarizePlaybookGuid,
                Name = "summarize-document-for-chat@v1",
                PlaybookCode = string.Empty,
                IsActive = true
            });

        // Default stub: routing-table returns null → fallback to typed-options path
        // (preserves the pre-028d behavior verbatim across the existing test surface).
        // Tests covering the FR-1R-05 happy path override this setup explicitly.
        _consumerRoutingMock
            .Setup(c => c.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        // Default orchestration stub: emit an empty stream. Tests that exercise dispatch
        // override this with explicit event sequences.
        _orchestrationServiceMock
            .Setup(o => o.ExecuteAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(EmptyEventStream());
    }

    private SessionSummarizeOrchestrator CreateSut() => new(
        _sessionManagerStub,
        _orchestrationServiceMock.Object,
        _httpContextAccessorMock.Object,
        _playbookLookupMock.Object,
        _consumerRoutingMock.Object,
        _workspaceOptions,
        _loggerMock.Object);

    // ─── (a) R7 — dispatches through IPlaybookOrchestrationService with resolved playbookId ────

    [Fact]
    public async Task SummarizeSessionFilesAsync_DispatchesThroughOrchestrationService_WithResolvedPlaybookId()
    {
        _sessionManagerStub.Session = BuildSession(FileId1, FileId2);

        PlaybookRunRequest? capturedRequest = null;
        _orchestrationServiceMock
            .Setup(o => o.ExecuteAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<PlaybookRunRequest, HttpContext, CancellationToken>(
                (req, _, _) => capturedRequest = req)
            .Returns(EmptyEventStream());

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: "executive",
            Path: SummarizeInvocationPath.DirectEndpoint,
            CorrelationId: "corr-001");

        var sut = CreateSut();
        _ = await Collect(sut.SummarizeSessionFilesAsync(request));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.PlaybookId.Should().Be(ResolvedChatSummarizePlaybookGuid,
            "R7 task 091 binding — chat /summarize MUST route through IPlaybookOrchestrationService " +
            "with the summarize-document-for-chat@v1 playbook ID resolved via the routing-table fallback path");
        _orchestrationServiceMock.Verify(
            o => o.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "FR-17 — dispatch goes through the canonical orchestration triangle (not the retired ExecuteChatSummarizeAsync)");
    }

    // ─── (b) R7 — Parameters dictionary carries tenant/session/file-manifest discriminators ────

    [Fact]
    public async Task SummarizeSessionFilesAsync_ParametersDictionary_CarriesDeterministicIdentifiers()
    {
        _sessionManagerStub.Session = BuildSession(FileId1, FileId2);

        PlaybookRunRequest? capturedRequest = null;
        _orchestrationServiceMock
            .Setup(o => o.ExecuteAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<PlaybookRunRequest, HttpContext, CancellationToken>(
                (req, _, _) => capturedRequest = req)
            .Returns(EmptyEventStream());

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1, FileId2 }, StyleHint: "executive",
            Path: SummarizeInvocationPath.DirectEndpoint,
            CorrelationId: "corr-001");

        var sut = CreateSut();
        _ = await Collect(sut.SummarizeSessionFilesAsync(request));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Parameters.Should().NotBeNull();
        capturedRequest.Parameters!["tenantId"].Should().Be(TenantId, "ADR-014 tenant isolation parameter");
        capturedRequest.Parameters["sessionId"].Should().Be(SessionId, "ADR-014 session isolation parameter");
        capturedRequest.Parameters["styleHint"].Should().Be("executive");
        capturedRequest.Parameters["fileCount"].Should().Be("2");
        capturedRequest.Parameters["isMultiFile"].Should().Be("true");
        capturedRequest.Parameters["invocationPath"].Should().Be("direct_endpoint",
            "Path discriminator preserved in parameters for telemetry consistency");
        capturedRequest.Parameters["correlationId"].Should().Be("corr-001",
            "NFR-17 correlation ID propagation");
        capturedRequest.Parameters["sessionFilesManifest"].Should().Contain(FileId1)
            .And.Contain(FileId2, "Manifest serialized as JSON for the RAG node's session+file filter");
    }

    // ─── (c) FR-04 — multi-file interjection emitted BEFORE the playbook stream ────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_MultiFile_EmitsInterjectionBeforePlaybookStream()
    {
        _sessionManagerStub.Session = BuildSession(FileId1, FileId2);

        // Playbook emits a single NodeProgress event after the interjection.
        _orchestrationServiceMock
            .Setup(o => o.ExecuteAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(EventStream(
                PlaybookStreamEvent.NodeProgress(Guid.NewGuid(), ResolvedChatSummarizePlaybookGuid, Guid.NewGuid(), "playbook-token")));

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1, FileId2 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        var chunks = await Collect(sut.SummarizeSessionFilesAsync(request));

        chunks.Should().HaveCount(2,
            "FR-04 — multi-file interjection chunk emitted BEFORE the playbook stream begins");
        chunks[0].Type.Should().Be("text");
        chunks[0].Content.Should().Contain("Multiple files",
            "FR-04 interjection is a 'text' AnalysisChunk introducing the combined summary");
        chunks[1].Type.Should().Be("text");
        chunks[1].Content.Should().Be("playbook-token",
            "playbook NodeProgress events translate to FromContent chunks preserving per-token cadence");
    }

    [Fact]
    public async Task SummarizeSessionFilesAsync_SingleFile_DoesNotEmitInterjection()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);

        _orchestrationServiceMock
            .Setup(o => o.ExecuteAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(EventStream(
                PlaybookStreamEvent.NodeProgress(Guid.NewGuid(), ResolvedChatSummarizePlaybookGuid, Guid.NewGuid(), "single-token")));

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        var chunks = await Collect(sut.SummarizeSessionFilesAsync(request));

        chunks.Should().HaveCount(1,
            "single-file requests skip the FR-04 interjection — only the playbook stream surfaces");
        chunks[0].Content.Should().Be("single-token");
    }

    // ─── (d) SSE adapter — NodeProgress → FromContent (per-token preservation) ─────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_NodeProgressEvents_TranslateToFromContentInOrder()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        _orchestrationServiceMock
            .Setup(o => o.ExecuteAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(EventStream(
                PlaybookStreamEvent.NodeProgress(runId, ResolvedChatSummarizePlaybookGuid, nodeId, "alpha"),
                PlaybookStreamEvent.NodeProgress(runId, ResolvedChatSummarizePlaybookGuid, nodeId, "beta"),
                PlaybookStreamEvent.NodeProgress(runId, ResolvedChatSummarizePlaybookGuid, nodeId, "gamma")));

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        var chunks = await Collect(sut.SummarizeSessionFilesAsync(request));

        chunks.Should().HaveCount(3, "every NodeProgress emits one FromContent chunk in order");
        chunks.Select(c => c.Content).Should().ContainInOrder("alpha", "beta", "gamma");
        chunks.Should().AllSatisfy(c => c.Type.Should().Be("text"));
        chunks.Should().AllSatisfy(c => c.Done.Should().BeFalse());
    }

    // ─── (e) SSE adapter — RunFailed → FromError ───────────────────────────────────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_RunFailedEvent_TranslatesToFromErrorChunk()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);

        _orchestrationServiceMock
            .Setup(o => o.ExecuteAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(EventStream(
                PlaybookStreamEvent.RunFailed(Guid.NewGuid(), ResolvedChatSummarizePlaybookGuid, "LLM upstream failure")));

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        var chunks = await Collect(sut.SummarizeSessionFilesAsync(request));

        chunks.Should().HaveCount(1);
        chunks[0].Type.Should().Be("error");
        chunks[0].Error.Should().Be("LLM upstream failure");
        chunks[0].Done.Should().BeTrue("error chunks are terminal per the AnalysisChunk envelope");
    }

    // ─── (f) SSE adapter — lifecycle events (RunStarted/NodeStarted) filtered out ──────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_LifecycleEvents_AreFilteredFromStream()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        _orchestrationServiceMock
            .Setup(o => o.ExecuteAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(EventStream(
                PlaybookStreamEvent.RunStarted(runId, ResolvedChatSummarizePlaybookGuid, nodeCount: 3),
                PlaybookStreamEvent.NodeStarted(runId, ResolvedChatSummarizePlaybookGuid, nodeId, "ChatSummarize"),
                PlaybookStreamEvent.NodeProgress(runId, ResolvedChatSummarizePlaybookGuid, nodeId, "alpha"),
                PlaybookStreamEvent.RunCompleted(runId, ResolvedChatSummarizePlaybookGuid, new PlaybookRunMetrics())));

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        var chunks = await Collect(sut.SummarizeSessionFilesAsync(request));

        chunks.Should().HaveCount(1,
            "only NodeProgress reaches the chat client — RunStarted/NodeStarted/RunCompleted have no AnalysisChunk equivalent");
        chunks[0].Content.Should().Be("alpha");
    }

    // ─── (g) ADR-014 — tenant + session forwarded via parameters dictionary ────────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_PropagatesTenantAndSessionIdViaParameters()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);

        PlaybookRunRequest? captured = null;
        _orchestrationServiceMock
            .Setup(o => o.ExecuteAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<PlaybookRunRequest, HttpContext, CancellationToken>((req, _, _) => captured = req)
            .Returns(EmptyEventStream());

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        _ = await Collect(sut.SummarizeSessionFilesAsync(request));

        captured.Should().NotBeNull();
        captured!.Parameters!["tenantId"].Should().Be(TenantId, "ADR-014 tenant isolation forwarded");
        captured.Parameters["sessionId"].Should().Be(SessionId, "ADR-014 session isolation forwarded");
    }

    // ─── (h) NFR-02 — hard cap 20 files per session — orchestrator boundary ────────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_RejectsMoreThanTwentyFileIds()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);
        var tooMany = Enumerable.Range(1, ChatSession.MaxUploadedFiles + 1).Select(i => $"f-{i}").ToList();
        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, tooMany, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();

        var act = async () => { await foreach (var _ in sut.SummarizeSessionFilesAsync(request)) { } };
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*NFR-02*");

        _orchestrationServiceMock.Verify(
            o => o.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "NFR-02 cap fails fast at orchestrator boundary; orchestration MUST NOT be called");
    }

    // ─── (i) Session not found → InvalidOperationException (endpoint maps to 404) ──────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_SessionNotFound_ThrowsInvalidOperationException()
    {
        _sessionManagerStub.Session = null; // session lookup returns null

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, FileIds: null, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();

        var act = async () => { await foreach (var _ in sut.SummarizeSessionFilesAsync(request)) { } };
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");

        _orchestrationServiceMock.Verify(
            o => o.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "missing session fails at orchestrator boundary; orchestration MUST NOT be called");
    }

    // ─── (j) ADR-010 — class has no orchestrator-authored interface ────────────────────────────

    [Fact]
    public void SessionSummarizeOrchestrator_HasNoOrchestratorAuthoredInterface()
    {
        var ifaces = typeof(SessionSummarizeOrchestrator).GetInterfaces();
        // Filter out framework-supplied interfaces (System.*, Microsoft.*); any remaining
        // would indicate an orchestrator-authored interface, which ADR-010 forbids unless a
        // genuine seam exists.
        var authored = ifaces.Where(i =>
            !i.Namespace?.StartsWith("System", StringComparison.Ordinal) is true
            && !i.Namespace?.StartsWith("Microsoft", StringComparison.Ordinal) is true).ToList();
        authored.Should().BeEmpty(
            "ADR-010 forbids interfaces-for-testability-alone; SessionSummarizeOrchestrator is concrete by design");
    }

    // ─── (k) Convergence — exactly ONE public streaming entry point ────────────────────────────

    [Fact]
    public void SessionSummarizeOrchestrator_ExposesExactlyOneConvergenceMethod()
    {
        var publicMethods = typeof(SessionSummarizeOrchestrator)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName) // exclude property accessors / operators
            .ToList();

        // The convergence shape: returns IAsyncEnumerable<AnalysisChunk>.
        var convergence = publicMethods
            .Where(m => m.ReturnType.IsGenericType
                && m.ReturnType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>)
                && m.ReturnType.GetGenericArguments()[0] == typeof(AnalysisChunk))
            .ToList();

        convergence.Should().HaveCount(1,
            "spec FR-01 + FR-08 + SC-08 require a single convergence method that both the " +
            "direct endpoint and the agent-tool path delegate to");
        convergence[0].Name.Should().Be(nameof(SessionSummarizeOrchestrator.SummarizeSessionFilesAsync));
    }

    // ─── (l) Empty input validation — required tenant + session ID ────────────────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_EmptyTenantId_Throws()
    {
        var request = new SummarizeSessionFilesRequest(
            TenantId: "", SessionId: SessionId, FileIds: null, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();

        var act = async () => { await foreach (var _ in sut.SummarizeSessionFilesAsync(request)) { } };
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SummarizeSessionFilesAsync_EmptySessionId_Throws()
    {
        var request = new SummarizeSessionFilesRequest(
            TenantId: TenantId, SessionId: "", FileIds: null, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();

        var act = async () => { await foreach (var _ in sut.SummarizeSessionFilesAsync(request)) { } };
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─── (m) FR-05 — fail-fast on missing configuration (no hardcoded fallback at convergence) ─

    [Fact]
    public async Task SummarizeSessionFilesAsync_EmptyConfiguredId_ThrowsInvalidOperationException()
    {
        // FR-05 task 015 + R6 FR-26: at the chat /summarize convergence point, missing
        // per-env config MUST fail fast (no hardcoded fallback).
        _sessionManagerStub.Session = BuildSession(FileId1);
        var emptyOptions = Options.Create(new WorkspaceOptions
        {
            ChatSummarizePlaybookId = string.Empty
        });
        var sut = new SessionSummarizeOrchestrator(
            _sessionManagerStub,
            _orchestrationServiceMock.Object,
            _httpContextAccessorMock.Object,
            _playbookLookupMock.Object,
            _consumerRoutingMock.Object,
            emptyOptions,
            _loggerMock.Object);

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var act = async () => { await foreach (var _ in sut.SummarizeSessionFilesAsync(request)) { } };
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Contain("routing-table",
            "FR-1R-05 error message MUST point operators at the routing-table + " +
            "Workspace:ChatSummarizePlaybookId fallback path");

        _playbookLookupMock.Verify(
            p => p.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "fail-fast — lookup MUST NOT be attempted with an empty configured value");
        _orchestrationServiceMock.Verify(
            o => o.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "fail-fast — orchestration MUST NOT be called when playbook resolution failed at config layer");
    }

    // ─── (n) FR-1R-05 task 028d — routing-table happy path resolves via IConsumerRoutingService ─

    [Fact]
    public async Task SummarizeSessionFilesAsync_RoutingTableReturnsId_UsesRoutingTablePlaybookIdAndSkipsLookup()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);
        var routingTableGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        _consumerRoutingMock
            .Setup(c => c.ResolveAsync(
                ConsumerTypes.ChatSummarize,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(routingTableGuid);

        PlaybookRunRequest? captured = null;
        _orchestrationServiceMock
            .Setup(o => o.ExecuteAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<PlaybookRunRequest, HttpContext, CancellationToken>((req, _, _) => captured = req)
            .Returns(EmptyEventStream());

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        _ = await Collect(sut.SummarizeSessionFilesAsync(request));

        captured.Should().NotBeNull();
        captured!.PlaybookId.Should().Be(routingTableGuid,
            "FR-1R-05 — routing-table resolution is the primary path; the GUID returned by " +
            "IConsumerRoutingService MUST be forwarded as PlaybookRunRequest.PlaybookId verbatim");
        _consumerRoutingMock.Verify(
            c => c.ResolveAsync(
                ConsumerTypes.ChatSummarize,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "FR-1R-05 — orchestrator MUST consult IConsumerRoutingService with the ConsumerTypes constant");
        _playbookLookupMock.Verify(
            p => p.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "FR-1R-05 — fallback path (typed-options → IPlaybookLookupService) MUST NOT execute when " +
            "the routing-table returned a non-null GUID");
    }

    // ─── (o) FR-1R-05 task 028d — routing-table returns null → fallback to typed-options ──────

    [Fact]
    public async Task SummarizeSessionFilesAsync_RoutingTableReturnsNull_FallsBackToTypedOptionsPath()
    {
        // Default _consumerRoutingMock setup returns null → implicit fallback path test.
        _sessionManagerStub.Session = BuildSession(FileId1);

        PlaybookRunRequest? captured = null;
        _orchestrationServiceMock
            .Setup(o => o.ExecuteAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<PlaybookRunRequest, HttpContext, CancellationToken>((req, _, _) => captured = req)
            .Returns(EmptyEventStream());

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        _ = await Collect(sut.SummarizeSessionFilesAsync(request));

        captured.Should().NotBeNull();
        captured!.PlaybookId.Should().Be(ResolvedChatSummarizePlaybookGuid,
            "FR-1R-05 graceful-degrade — null from IConsumerRoutingService → orchestrator MUST " +
            "resolve via WorkspaceOptions.ChatSummarizePlaybookId + IPlaybookLookupService " +
            "(pre-028d behavior preserved verbatim for the FR-1R-06 deprecation window)");
        _playbookLookupMock.Verify(
            p => p.GetByIdAsync(ConfiguredChatSummarizePlaybookId, It.IsAny<CancellationToken>()),
            Times.Once,
            "FR-1R-05 graceful-degrade — fallback path invokes IPlaybookLookupService with the " +
            "configured typed-options value when the routing-table has no matching row");
    }

    // ─── (p) FR-1R-05 task 028d — routing-table returns empty Guid → treats as null, falls back ─

    [Fact]
    public async Task SummarizeSessionFilesAsync_RoutingTableReturnsEmptyGuid_FallsBackToTypedOptionsPath()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);
        _consumerRoutingMock
            .Setup(c => c.ResolveAsync(
                ConsumerTypes.ChatSummarize,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.Empty);

        PlaybookRunRequest? captured = null;
        _orchestrationServiceMock
            .Setup(o => o.ExecuteAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<PlaybookRunRequest, HttpContext, CancellationToken>((req, _, _) => captured = req)
            .Returns(EmptyEventStream());

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        _ = await Collect(sut.SummarizeSessionFilesAsync(request));

        captured.Should().NotBeNull();
        captured!.PlaybookId.Should().Be(ResolvedChatSummarizePlaybookGuid,
            "FR-1R-05 defensive edge — Guid.Empty from the routing service is semantically " +
            "no-match and MUST trigger the same fallback as null");
        captured.PlaybookId.Should().NotBe(Guid.Empty,
            "orchestration MUST NOT be called with Guid.Empty — the fallback path resolves a real GUID");
    }

    // ─── (q) R7 — orchestrator throws when HttpContextAccessor returns null ────────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_HttpContextNull_ThrowsInvalidOperationException()
    {
        // R7 task 091 — orchestrator requires HttpContext for OBO auth in downstream node
        // executors. If HttpContextAccessor returns null (invoked outside a request scope),
        // fail fast with a clear diagnostic.
        _sessionManagerStub.Session = BuildSession(FileId1);
        _httpContextAccessorMock.SetupGet(a => a.HttpContext).Returns((HttpContext?)null);

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();

        var act = async () => { await foreach (var _ in sut.SummarizeSessionFilesAsync(request)) { } };
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Contain("HttpContext",
            "diagnostic must reference HttpContext so operators can diagnose misuse");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────────────────

    private static async Task<List<AnalysisChunk>> Collect(IAsyncEnumerable<AnalysisChunk> source)
    {
        var list = new List<AnalysisChunk>();
        await foreach (var chunk in source)
        {
            list.Add(chunk);
        }
        return list;
    }

    private static async IAsyncEnumerable<PlaybookStreamEvent> EmptyEventStream(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<PlaybookStreamEvent> EventStream(
        params PlaybookStreamEvent[] events)
    {
        foreach (var ev in events)
        {
            await Task.Yield();
            yield return ev;
        }
    }

    private static ChatSession BuildSession(params string[] fileIds)
    {
        var files = fileIds
            .Select(id => new ChatSessionFile(
                FileId: id,
                FileName: $"{id}.pdf",
                ContentType: "application/pdf",
                SizeBytes: 1024,
                SearchDocumentIdsCsv: $"doc-{id}-1",
                UploadedAt: DateTimeOffset.UtcNow))
            .ToList();
        return new ChatSession(
            SessionId: SessionId,
            TenantId: TenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: files);
    }

    // ─── Test doubles ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Subclass of <see cref="ChatSessionManager"/> that overrides the virtual
    /// <see cref="ChatSessionManager.GetSessionAsync(string, string, CancellationToken)"/> so we can
    /// inject a fixed session without wiring Redis/Dataverse.
    /// </summary>
    private sealed class TestableChatSessionManager : ChatSessionManager
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
}
