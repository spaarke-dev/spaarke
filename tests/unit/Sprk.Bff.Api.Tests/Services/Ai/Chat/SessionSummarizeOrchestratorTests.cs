using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
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
/// <b>R6 task 025 (Pillar 4 / D-A-17) update</b>: the orchestrator is now a thin pass-through
/// that forwards to <see cref="IPlaybookExecutionEngine.ExecuteChatSummarizeAsync"/>. Tests
/// here verify the orchestrator's boundary responsibilities ONLY:
/// </para>
/// <list type="bullet">
///   <item>Public <see cref="SessionSummarizeOrchestrator.SummarizeSessionFilesAsync"/>
///         signature unchanged (convergence + ADR-010 reflection).</item>
///   <item>Argument validation (tenant + session required; NFR-02 ≤20 file cap).</item>
///   <item>Session lookup at the orchestrator boundary (missing session →
///         InvalidOperationException; endpoint maps to 404).</item>
///   <item>Forwards <see cref="ChatSummarizeRequest"/> to the engine with the playbook ID
///         resolved at runtime via <see cref="IPlaybookLookupService.GetByIdAsync"/> using
///         <see cref="WorkspaceOptions.ChatSummarizePlaybookId"/> (FR-05 task 015 stable-ID
///         migration) and yields the engine's chunks unchanged (byte-equivalent pass-through).</item>
/// </list>
/// <para>
/// Tests covering the moved logic (RAG retrieval / Structured Outputs / IncrementalJsonParser
/// / FR-04 interjection / ADR-014 session filter / telemetry) live in
/// <c>PlaybookExecutionEngineTests</c> — that's where the chat-Summarize pipeline lives now.
/// </para>
/// </summary>
public class SessionSummarizeOrchestratorTests
{
    private const string TenantId = "tenant-abc";
    private const string SessionId = "session-xyz";
    private const string FileId1 = "file-001";
    private const string FileId2 = "file-002";

    // FR-05 task 015 (chat-routing-redesign-r1): tests configure WorkspaceOptions with the
    // canonical DEV-environment value for the summarize-document-for-chat@v1 playbook (the
    // sprk_playbookid value, mirroring its sprk_analysisplaybookid PK per task 014 backfill).
    // The orchestrator passes this string into IPlaybookLookupService.GetByIdAsync; the
    // mock returns a PlaybookResponse whose Id is the same GUID (parsed) so the engine
    // call site sees the unchanged GUID identifier — FR-26 convergence preserved.
    private const string ConfiguredChatSummarizePlaybookId = "44285d15-1360-f111-ab0b-70a8a59455f4";
    private static readonly Guid ResolvedChatSummarizePlaybookGuid =
        Guid.Parse(ConfiguredChatSummarizePlaybookId);

    private readonly TestableChatSessionManager _sessionManagerStub;
    private readonly Mock<IPlaybookExecutionEngine> _engineMock;
    private readonly Mock<IPlaybookLookupService> _playbookLookupMock;
    private readonly Mock<IConsumerRoutingService> _consumerRoutingMock;
    private readonly IOptions<WorkspaceOptions> _workspaceOptions;
    private readonly Mock<ILogger<SessionSummarizeOrchestrator>> _loggerMock;

    public SessionSummarizeOrchestratorTests()
    {
        _sessionManagerStub = new TestableChatSessionManager();
        _engineMock = new Mock<IPlaybookExecutionEngine>();
        _playbookLookupMock = new Mock<IPlaybookLookupService>();
        _consumerRoutingMock = new Mock<IConsumerRoutingService>();
        _workspaceOptions = Options.Create(new WorkspaceOptions
        {
            ChatSummarizePlaybookId = ConfiguredChatSummarizePlaybookId
        });
        _loggerMock = new Mock<ILogger<SessionSummarizeOrchestrator>>();

        // Default stub: GetByIdAsync(<configured id>, ct) → PlaybookResponse with Id = GUID.
        // The orchestrator forwards playback.Id (Guid) to the engine.
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
    }

    private SessionSummarizeOrchestrator CreateSut() => new(
        _sessionManagerStub,
        _engineMock.Object,
        _playbookLookupMock.Object,
        _consumerRoutingMock.Object,
        _workspaceOptions,
        _loggerMock.Object);

    // ─── (a) Forwards to engine with ChatSummarizePlaybookId and resolved manifest ─────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_ForwardsToEngine_WithCorrectPlaybookIdAndRequest()
    {
        _sessionManagerStub.Session = BuildSession(FileId1, FileId2);

        Guid capturedPlaybookId = Guid.Empty;
        ChatSummarizeRequest? capturedRequest = null;
        _engineMock
            .Setup(e => e.ExecuteChatSummarizeAsync(
                It.IsAny<Guid>(), It.IsAny<ChatSummarizeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, ChatSummarizeRequest, CancellationToken>(
                (pid, req, _) => { capturedPlaybookId = pid; capturedRequest = req; })
            .Returns(EmptyChunkStream());

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: "executive",
            Path: SummarizeInvocationPath.DirectEndpoint,
            CorrelationId: "corr-001");

        var sut = CreateSut();
        _ = await Collect(sut.SummarizeSessionFilesAsync(request));

        capturedPlaybookId.Should().Be(ResolvedChatSummarizePlaybookGuid,
            "Pillar 4 binding — chat /summarize MUST route through PlaybookExecutionEngine using " +
            "the summarize-document-for-chat@v1 playbook ID resolved at runtime via " +
            "IPlaybookLookupService.GetByIdAsync(WorkspaceOptions.ChatSummarizePlaybookId) " +
            "(FR-05 task 015 stable-ID migration)");
        _playbookLookupMock.Verify(
            p => p.GetByIdAsync(ConfiguredChatSummarizePlaybookId, It.IsAny<CancellationToken>()),
            Times.Once,
            "FR-05 — orchestrator MUST resolve the playbook via IPlaybookLookupService " +
            "using the configured WorkspaceOptions.ChatSummarizePlaybookId");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.TenantId.Should().Be(TenantId);
        capturedRequest.SessionId.Should().Be(SessionId);
        capturedRequest.FileIds.Should().BeEquivalentTo(new[] { FileId1 });
        capturedRequest.StyleHint.Should().Be("executive");
        capturedRequest.Path.Should().Be(SummarizeInvocationPath.DirectEndpoint);
        capturedRequest.CorrelationId.Should().Be("corr-001");
        capturedRequest.UploadedFiles.Should().HaveCount(2,
            "orchestrator forwards the session's full uploaded-files manifest; engine does the filtering");
    }

    // ─── (b) Pass-through: engine chunks emitted unchanged (regression — byte-equivalent) ──────

    [Fact]
    public async Task SummarizeSessionFilesAsync_YieldsEngineChunksUnchanged_RegressionByteEquivalent()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);

        // Engine emits a representative stream: text (FR-04 would only be multi-file; using
        // single-file here means engine sends text+delta+complete shapes). Orchestrator MUST
        // yield these unchanged — no mutation, no re-shaping.
        var engineChunks = new[]
        {
            AnalysisChunk.FromContent("(engine-emitted preamble)"),
            AnalysisChunk.FromDelta("tldr", "alpha", 1),
            AnalysisChunk.FromDelta("tldr", "beta", 2),
            AnalysisChunk.Completed(new DocumentAnalysisResult
            {
                Summary = "done",
                TlDr = new[] { "alpha", "beta" },
                ParsedSuccessfully = true
            })
        };
        _engineMock
            .Setup(e => e.ExecuteChatSummarizeAsync(
                It.IsAny<Guid>(), It.IsAny<ChatSummarizeRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(engineChunks));

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        var chunks = await Collect(sut.SummarizeSessionFilesAsync(request));

        chunks.Should().HaveCount(engineChunks.Length, "byte-equivalent pass-through — every engine chunk emitted exactly once, in order");
        for (var i = 0; i < engineChunks.Length; i++)
        {
            chunks[i].Should().BeEquivalentTo(engineChunks[i],
                $"chunk {i} forwarded unchanged from engine to orchestrator");
        }
    }

    // ─── (c) FK-chain routing — playbookId is the constant, not the alternate-key code ────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_UsesFkChainPlaybookId_NotAlternateKeyCode()
    {
        // The pre-R6 path loaded the action seed via sprk_actioncode = "SUM-CHAT@v1" alternate key.
        // Post-R6 task 025, the orchestrator MUST forward the playbook ID (a Guid) to the engine,
        // and the engine resolves the action via the FK chain. This test pins the FR-26 invariant.
        _sessionManagerStub.Session = BuildSession(FileId1);

        Guid capturedPlaybookId = Guid.Empty;
        _engineMock
            .Setup(e => e.ExecuteChatSummarizeAsync(
                It.IsAny<Guid>(), It.IsAny<ChatSummarizeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, ChatSummarizeRequest, CancellationToken>(
                (pid, _, _) => capturedPlaybookId = pid)
            .Returns(EmptyChunkStream());

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        _ = await Collect(sut.SummarizeSessionFilesAsync(request));

        // FR-26 binding: playbook ID is a Guid (not the alternate-key string code) and is
        // sourced from the IPlaybookLookupService.GetByIdAsync resolution path — not the
        // pre-task-015 hardcoded constant. The convergence invariant is preserved as long
        // as both slash /summarize and NL agent-tool dispatch end up here with the same
        // resolved Guid.
        capturedPlaybookId.Should().NotBe(Guid.Empty);
        capturedPlaybookId.Should().Be(ResolvedChatSummarizePlaybookGuid,
            "Pillar 4 chat-summarize uses the FK-resolved playbook 'summarize-document-for-chat@v1' ID, " +
            "now resolved at runtime via WorkspaceOptions.ChatSummarizePlaybookId + IPlaybookLookupService (task 015 FR-05)");
    }

    // ─── (d) ADR-014 — tenant + session forwarded to engine for downstream RAG isolation ─────

    [Fact]
    public async Task SummarizeSessionFilesAsync_PropagatesTenantAndSessionIdToEngine()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);

        ChatSummarizeRequest? captured = null;
        _engineMock
            .Setup(e => e.ExecuteChatSummarizeAsync(
                It.IsAny<Guid>(), It.IsAny<ChatSummarizeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, ChatSummarizeRequest, CancellationToken>((_, req, _) => captured = req)
            .Returns(EmptyChunkStream());

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        _ = await Collect(sut.SummarizeSessionFilesAsync(request));

        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(TenantId, "ADR-014 tenant isolation forwarded to engine");
        captured.SessionId.Should().Be(SessionId, "ADR-014 session isolation forwarded to engine");
    }

    // ─── (e) NFR-02 — hard cap 20 files per session — orchestrator boundary ───────────────────

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

        _engineMock.Verify(
            e => e.ExecuteChatSummarizeAsync(It.IsAny<Guid>(), It.IsAny<ChatSummarizeRequest>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "NFR-02 cap fails fast at orchestrator boundary; engine MUST NOT be called");
    }

    // ─── (f) Session not found → InvalidOperationException (endpoint maps to 404) ────────────

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

        _engineMock.Verify(
            e => e.ExecuteChatSummarizeAsync(It.IsAny<Guid>(), It.IsAny<ChatSummarizeRequest>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "missing session fails at orchestrator boundary; engine MUST NOT be called");
    }

    // ─── (g) ADR-010 — class has no orchestrator-authored interface ──────────────────────────

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

    // ─── (h) Convergence — exactly ONE public streaming entry point ──────────────────────────

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

    // ─── (i) Path discriminator forwarded — agent_tool ──────────────────────────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_AgentToolPath_ForwardsAgentToolDiscriminator()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);

        ChatSummarizeRequest? captured = null;
        _engineMock
            .Setup(e => e.ExecuteChatSummarizeAsync(
                It.IsAny<Guid>(), It.IsAny<ChatSummarizeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, ChatSummarizeRequest, CancellationToken>((_, req, _) => captured = req)
            .Returns(EmptyChunkStream());

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.AgentTool);

        var sut = CreateSut();
        _ = await Collect(sut.SummarizeSessionFilesAsync(request));

        captured.Should().NotBeNull();
        captured!.Path.Should().Be(SummarizeInvocationPath.AgentTool);
        captured.Path.ToTelemetryValue().Should().Be("agent_tool");
    }

    // ─── (j) Path discriminator forwarded — direct_endpoint ─────────────────────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_DirectEndpointPath_ForwardsDirectEndpointDiscriminator()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);

        ChatSummarizeRequest? captured = null;
        _engineMock
            .Setup(e => e.ExecuteChatSummarizeAsync(
                It.IsAny<Guid>(), It.IsAny<ChatSummarizeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, ChatSummarizeRequest, CancellationToken>((_, req, _) => captured = req)
            .Returns(EmptyChunkStream());

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        _ = await Collect(sut.SummarizeSessionFilesAsync(request));

        captured.Should().NotBeNull();
        captured!.Path.Should().Be(SummarizeInvocationPath.DirectEndpoint);
        captured.Path.ToTelemetryValue().Should().Be("direct_endpoint");
    }

    // ─── (k) Empty input validation — required tenant + session ID ───────────────────────────

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

    // ─── (l) FR-26 invariant — orchestrator no longer references alternate-key constants ─────

    [Fact]
    public void SessionSummarizeOrchestrator_HasNoAlternateKeyConstants_FR26()
    {
        // The pre-R6 path used SessionSummarizeOrchestrator.SummarizeActionCode +
        // ActionEntityLogicalName constants for the alternate-key bypass. R6 task 025 removed
        // these. Reflection assert: neither constant exists on the orchestrator anymore.
        var members = typeof(SessionSummarizeOrchestrator)
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToArray();

        members.Should().NotContain("SummarizeActionCode",
            "FR-26 — alternate-key bypass removed in R6 task 025 (D-A-17)");
        members.Should().NotContain("ActionEntityLogicalName",
            "FR-26 — alternate-key bypass removed in R6 task 025 (D-A-17)");
    }

    // ─── (m) FR-05 task 015 — hardcoded ChatSummarizePlaybookId constant removed ─────────────

    [Fact]
    public void SessionSummarizeOrchestrator_HasNoHardcodedChatSummarizePlaybookIdConstant_FR05()
    {
        // FR-05 task 015 (chat-routing-redesign-r1): the prior internal static readonly Guid
        // ChatSummarizePlaybookId = Guid.Parse("44285d15-1360-f111-ab0b-70a8a59455f4")
        // constant was removed. Resolution now flows through
        // WorkspaceOptions.ChatSummarizePlaybookId + IPlaybookLookupService.GetByIdAsync.
        // Reflection assert: the constant no longer exists on the orchestrator.
        var members = typeof(SessionSummarizeOrchestrator)
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToArray();

        members.Should().NotContain("ChatSummarizePlaybookId",
            "FR-05 task 015 — hardcoded ChatSummarizePlaybookId Guid constant removed; " +
            "playbook resolved at runtime via WorkspaceOptions.ChatSummarizePlaybookId + " +
            "IPlaybookLookupService.GetByIdAsync per ADR-018 typed options + Pattern A stable-ID");
    }

    // ─── (n) FR-05 — orchestrator calls IPlaybookLookupService with configured options value ─

    [Fact]
    public async Task SummarizeSessionFilesAsync_ResolvesPlaybookViaLookupService_UsingConfiguredOptionValue()
    {
        // FR-05 task 015: verify the orchestrator passes the EXACT configured
        // WorkspaceOptions.ChatSummarizePlaybookId value into the lookup service. This pins
        // the typed-options contract: WorkspaceOptions is the only place the per-env GUID
        // string lives in the orchestrator path.
        _sessionManagerStub.Session = BuildSession(FileId1);
        _engineMock
            .Setup(e => e.ExecuteChatSummarizeAsync(
                It.IsAny<Guid>(), It.IsAny<ChatSummarizeRequest>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyChunkStream());

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        _ = await Collect(sut.SummarizeSessionFilesAsync(request));

        _playbookLookupMock.Verify(
            p => p.GetByIdAsync(ConfiguredChatSummarizePlaybookId, It.IsAny<CancellationToken>()),
            Times.Once,
            "FR-05 — orchestrator MUST invoke IPlaybookLookupService.GetByIdAsync with the " +
            "exact WorkspaceOptions.ChatSummarizePlaybookId value (no string mutation, no fallback)");
    }

    // ─── (o) FR-05 — fail-fast on missing configuration (no hardcoded fallback at convergence) ─

    [Fact]
    public async Task SummarizeSessionFilesAsync_EmptyConfiguredId_ThrowsInvalidOperationException()
    {
        // FR-05 task 015 + R6 FR-26: at the chat /summarize convergence point, missing
        // per-env config MUST fail fast (no hardcoded fallback). The error must surface
        // upstream so operators see misconfiguration rather than silent routing to a
        // (potentially) stale dev GUID.
        _sessionManagerStub.Session = BuildSession(FileId1);
        var emptyOptions = Options.Create(new WorkspaceOptions
        {
            ChatSummarizePlaybookId = string.Empty
        });
        var sut = new SessionSummarizeOrchestrator(
            _sessionManagerStub,
            _engineMock.Object,
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
        _engineMock.Verify(
            e => e.ExecuteChatSummarizeAsync(
                It.IsAny<Guid>(), It.IsAny<ChatSummarizeRequest>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "fail-fast — engine MUST NOT be called when playbook resolution failed at config layer");
    }

    // ─── (p) FR-1R-05 task 028d — routing-table happy path resolves via IConsumerRoutingService ─

    [Fact]
    public async Task SummarizeSessionFilesAsync_RoutingTableReturnsId_UsesRoutingTablePlaybookIdAndSkipsLookup()
    {
        // FR-1R-05 task 028d: when IConsumerRoutingService.ResolveAsync returns a non-null
        // Guid for ConsumerTypes.ChatSummarize, the orchestrator MUST use that GUID as the
        // engine's playbookId AND MUST NOT fall through to the typed-options /
        // IPlaybookLookupService path. This pins the new primary resolution path.
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

        Guid capturedPlaybookId = Guid.Empty;
        _engineMock
            .Setup(e => e.ExecuteChatSummarizeAsync(
                It.IsAny<Guid>(), It.IsAny<ChatSummarizeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, ChatSummarizeRequest, CancellationToken>(
                (pid, _, _) => capturedPlaybookId = pid)
            .Returns(EmptyChunkStream());

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        _ = await Collect(sut.SummarizeSessionFilesAsync(request));

        capturedPlaybookId.Should().Be(routingTableGuid,
            "FR-1R-05 — routing-table resolution is the new primary path; the GUID returned by " +
            "IConsumerRoutingService MUST be forwarded to the engine verbatim");
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

    // ─── (q) FR-1R-05 task 028d — routing-table returns null → fallback to typed-options ────

    [Fact]
    public async Task SummarizeSessionFilesAsync_RoutingTableReturnsNull_FallsBackToTypedOptionsPath()
    {
        // FR-1R-05 task 028d graceful-degrade: when IConsumerRoutingService returns null
        // (no row), the orchestrator MUST fall through to the FR-05 typed-options +
        // IPlaybookLookupService path (preserves pre-028d behavior verbatim). The default
        // _consumerRoutingMock setup in the ctor returns null → this is the implicit path.
        _sessionManagerStub.Session = BuildSession(FileId1);

        Guid capturedPlaybookId = Guid.Empty;
        _engineMock
            .Setup(e => e.ExecuteChatSummarizeAsync(
                It.IsAny<Guid>(), It.IsAny<ChatSummarizeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, ChatSummarizeRequest, CancellationToken>(
                (pid, _, _) => capturedPlaybookId = pid)
            .Returns(EmptyChunkStream());

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        _ = await Collect(sut.SummarizeSessionFilesAsync(request));

        capturedPlaybookId.Should().Be(ResolvedChatSummarizePlaybookGuid,
            "FR-1R-05 graceful-degrade — null from IConsumerRoutingService → orchestrator MUST " +
            "resolve via WorkspaceOptions.ChatSummarizePlaybookId + IPlaybookLookupService " +
            "(pre-028d behavior preserved verbatim for the FR-1R-06 deprecation window)");
        _playbookLookupMock.Verify(
            p => p.GetByIdAsync(ConfiguredChatSummarizePlaybookId, It.IsAny<CancellationToken>()),
            Times.Once,
            "FR-1R-05 graceful-degrade — fallback path invokes IPlaybookLookupService with the " +
            "configured typed-options value when the routing-table has no matching row");
    }

    // ─── (r) FR-1R-05 task 028d — routing-table returns empty Guid → treats as null, falls back ─

    [Fact]
    public async Task SummarizeSessionFilesAsync_RoutingTableReturnsEmptyGuid_FallsBackToTypedOptionsPath()
    {
        // FR-1R-05 task 028d defensive edge — Guid.Empty is semantically equivalent to null
        // for routing purposes; the orchestrator MUST treat it as "no match" and fall back
        // to the typed-options path rather than forwarding Guid.Empty to the engine.
        _sessionManagerStub.Session = BuildSession(FileId1);
        _consumerRoutingMock
            .Setup(c => c.ResolveAsync(
                ConsumerTypes.ChatSummarize,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.Empty);

        Guid capturedPlaybookId = Guid.Empty;
        _engineMock
            .Setup(e => e.ExecuteChatSummarizeAsync(
                It.IsAny<Guid>(), It.IsAny<ChatSummarizeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, ChatSummarizeRequest, CancellationToken>(
                (pid, _, _) => capturedPlaybookId = pid)
            .Returns(EmptyChunkStream());

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var sut = CreateSut();
        _ = await Collect(sut.SummarizeSessionFilesAsync(request));

        capturedPlaybookId.Should().Be(ResolvedChatSummarizePlaybookGuid,
            "FR-1R-05 defensive edge — Guid.Empty from the routing service is semantically " +
            "no-match and MUST trigger the same fallback as null");
        capturedPlaybookId.Should().NotBe(Guid.Empty,
            "engine MUST NOT be called with Guid.Empty — the fallback path resolves a real GUID");
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

    private static async IAsyncEnumerable<AnalysisChunk> EmptyChunkStream(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<AnalysisChunk> ToAsyncEnumerable(
        IEnumerable<AnalysisChunk> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var c in chunks)
        {
            await Task.Yield();
            yield return c;
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
}
