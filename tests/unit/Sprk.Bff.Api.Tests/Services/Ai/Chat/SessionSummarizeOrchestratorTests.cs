using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="SessionSummarizeOrchestrator"/> — the chat-session boundary for
/// the catalog-driven <c>chat-summarize</c> capability (FR-P1-01, ai-architecture-redesign-r1
/// task 020).
///
/// <para>
/// <b>FR-P1-01 cutover</b>: the orchestrator dual-path (Playbook Engine dispatch +
/// <c>Workspace:ChatSummarizePlaybookId</c> typed-options fallback) is DELETED. The single
/// remaining path is: <see cref="IConsumerRoutingService.ResolveBindingAsync"/> (the
/// ADR-039 Binding contract) → <see cref="IScopeResolverService.GetActionAsync"/> (the
/// SUM-CHAT@v1 <c>kind: prompted</c> Action row) → <see cref="IActionRunner"/> (the prompted
/// executor: ActionRunner + PromptSchemaRenderer). These tests verify:
/// </para>
/// <list type="bullet">
///   <item>Catalog dispatch: Binding resolved with <see cref="ConsumerTypes.ChatSummarize"/>;
///         the Binding's Action row loaded and executed via the prompted executor.</item>
///   <item>Hard cutover fail-fast: no Binding / no Action target / non-prompted kind /
///         unloadable Action row → <see cref="InvalidOperationException"/> BEFORE any chunk
///         (no fallback of any kind, NFR-08).</item>
///   <item>Boundary responsibilities preserved: arg validation, NFR-02 20-file cap, session
///         lookup (404 mapping contract), FR-04 multi-file interjection, FR-08 file-id
///         defaulting.</item>
///   <item>Wire contract: terminal <see cref="AnalysisChunk.Completed(DocumentAnalysisResult)"/>
///         from the executor's structured JSON; graceful text-only degrade on parse failure;
///         FromError chunks for fetch/LLM runtime failures.</item>
/// </list>
/// </summary>
public class SessionSummarizeOrchestratorTests
{
    private const string TenantId = "tenant-abc";
    private const string SessionId = "session-xyz";
    private const string FileId1 = "file-001";
    private const string FileId2 = "file-002";

    private static readonly Guid BindingId = Guid.Parse("651194cd-3670-f111-ab0e-70a8a590c51c");
    private static readonly Guid ActionId = Guid.Parse("eeb05bfd-1260-f111-ab0b-70a8a59455f4");

    private const string ValidResultJson =
        """{"tldr":["Point A"],"summary":"A summary.","keywords":"alpha, beta","entities":{"organizations":[],"persons":[]}}""";

    private readonly TestableChatSessionManager _sessionManagerStub;
    private readonly Mock<IConsumerRoutingService> _consumerRoutingMock;
    private readonly Mock<IScopeResolverService> _scopeResolverMock;
    private readonly Mock<IActionRunner> _actionRunnerMock;
    private readonly Mock<ISessionFileTextSource> _sessionFileTextSourceMock;
    private readonly Mock<ILogger<SessionSummarizeOrchestrator>> _loggerMock;

    public SessionSummarizeOrchestratorTests()
    {
        _sessionManagerStub = new TestableChatSessionManager();
        _consumerRoutingMock = new Mock<IConsumerRoutingService>();
        _scopeResolverMock = new Mock<IScopeResolverService>();
        _actionRunnerMock = new Mock<IActionRunner>();
        _sessionFileTextSourceMock = new Mock<ISessionFileTextSource>();
        _loggerMock = new Mock<ILogger<SessionSummarizeOrchestrator>>();

        // Default: catalog fully wired — Binding row → prompted Action → executor result.
        _consumerRoutingMock
            .Setup(c => c.ResolveBindingAsync(
                ConsumerTypes.ChatSummarize,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildBinding());

        _scopeResolverMock
            .Setup(s => s.GetActionAsync(ActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildAction());

        _sessionFileTextSourceMock
            .Setup(t => t.FetchAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<ChatSessionFile>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionFileText
            {
                ExtractedText = "lorem ipsum extracted text",
                DisplayName = $"{FileId1}.pdf",
                ChunkCount = 1
            });

        _actionRunnerMock
            .Setup(r => r.RunAsync(
                It.IsAny<AnalysisAction>(), It.IsAny<DocumentText>(),
                It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ParseJson(ValidResultJson));
    }

    private SessionSummarizeOrchestrator CreateSut() => new(
        _sessionManagerStub,
        _consumerRoutingMock.Object,
        _scopeResolverMock.Object,
        _actionRunnerMock.Object,
        _sessionFileTextSourceMock.Object,
        // REAL OutputRouter over the testable session manager (FR-P1-02, task 021):
        // every behavior test below now exercises the live ledger-write-before-render seam.
        new OutputRouter(_sessionManagerStub, Mock.Of<ILogger<OutputRouter>>()),
        _loggerMock.Object);

    // ─── (a) FR-P1-01 — catalog dispatch: Binding → Action → prompted executor ────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_ResolvesBindingAndExecutesPromptedAction()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);

        AnalysisAction? capturedAction = null;
        LinearRunContext? capturedContext = null;
        _actionRunnerMock
            .Setup(r => r.RunAsync(
                It.IsAny<AnalysisAction>(), It.IsAny<DocumentText>(),
                It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()))
            .Callback<AnalysisAction, DocumentText, LinearRunContext, CancellationToken>(
                (a, _, ctx, _) => { capturedAction = a; capturedContext = ctx; })
            .ReturnsAsync(ParseJson(ValidResultJson));

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: "executive",
            Path: SummarizeInvocationPath.DirectEndpoint,
            CorrelationId: "corr-001");

        var chunks = await Collect(CreateSut().SummarizeSessionFilesAsync(request));

        _consumerRoutingMock.Verify(
            c => c.ResolveBindingAsync(
                ConsumerTypes.ChatSummarize,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "FR-P1-01 — the Binding row is THE routing surface (ADR-039); resolution goes " +
            "through ResolveBindingAsync with the compile-safe ConsumerTypes constant");

        capturedAction.Should().NotBeNull("the Binding's Action row is what executes");
        capturedAction!.Id.Should().Be(ActionId);
        capturedContext.Should().NotBeNull();
        capturedContext!.ConsumerType.Should().Be(ConsumerTypes.ChatSummarize,
            "ADR-015 — run context carries deterministic identifiers only");
        capturedContext.TenantId.Should().Be(TenantId);
        capturedContext.CorrelationId.Should().Be("corr-001", "NFR-17 correlation propagation");

        chunks.Should().ContainSingle(c => c.Type == "complete",
            "the prompted executor's structured output surfaces as the terminal complete chunk");
    }

    [Fact]
    public async Task SummarizeSessionFilesAsync_CompleteChunk_CarriesParsedDocumentAnalysisResult()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);

        var request = DefaultRequest(FileId1);
        var chunks = await Collect(CreateSut().SummarizeSessionFilesAsync(request));

        chunks.Should().HaveCount(1);
        chunks[0].Type.Should().Be("complete");
        chunks[0].Done.Should().BeTrue();
        chunks[0].Result.Should().NotBeNull();
        chunks[0].Result!.Summary.Should().Be("A summary.");
        chunks[0].Result!.TlDr.Should().BeEquivalentTo(new[] { "Point A" });
        chunks[0].Result!.ParsedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task SummarizeSessionFilesAsync_UnparsableExecutorOutput_DegradesToTextCompletion()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);
        _actionRunnerMock
            .Setup(r => r.RunAsync(
                It.IsAny<AnalysisAction>(), It.IsAny<DocumentText>(),
                It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ParseJson("\"just a string, not the schema shape\""));

        var chunks = await Collect(CreateSut().SummarizeSessionFilesAsync(DefaultRequest(FileId1)));

        chunks.Should().HaveCount(1);
        chunks[0].Type.Should().Be("complete",
            "graceful degrade — the client always sees a terminal complete chunk, never silent EOF");
    }

    // ─── (b) NFR-08 hard cutover — fail-fast, no fallback of any kind ──────────────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_NoBindingRow_ThrowsInvalidOperationException()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);
        _consumerRoutingMock
            .Setup(c => c.ResolveBindingAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Binding?)null);

        var act = async () => { await foreach (var _ in CreateSut().SummarizeSessionFilesAsync(DefaultRequest(FileId1))) { } };
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Contain("sprk_playbookconsumer",
            "operator diagnostic must point at the Binding table");
        thrown.Which.Message.Should().NotContain("not found",
            "endpoint maps InvalidOperationException containing 'not found' to 404 " +
            "session-not-found; catalog failures must be a 500, not a 404");

        _actionRunnerMock.Verify(
            r => r.RunAsync(It.IsAny<AnalysisAction>(), It.IsAny<DocumentText>(),
                It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "NFR-08 hard cutover — nothing executes without a Binding row; there is NO fallback");
    }

    [Fact]
    public async Task SummarizeSessionFilesAsync_BindingWithoutActionTarget_ThrowsInvalidOperationException()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);
        _consumerRoutingMock
            .Setup(c => c.ResolveBindingAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildBinding() with { ActionId = null, PlaybookId = Guid.NewGuid() });

        var act = async () => { await foreach (var _ in CreateSut().SummarizeSessionFilesAsync(DefaultRequest(FileId1))) { } };
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Contain("sprk_action",
            "a legacy playbook-only row is a misconfiguration on this path — the engine " +
            "dispatch was deleted (FR-P1-01); the message must direct the operator to set sprk_action");
    }

    [Fact]
    public async Task SummarizeSessionFilesAsync_CodedActionKind_ThrowsInvalidOperationException()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);
        _consumerRoutingMock
            .Setup(c => c.ResolveBindingAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildBinding() with { ActionKind = ActionKind.Coded });

        var act = async () => { await foreach (var _ in CreateSut().SummarizeSessionFilesAsync(DefaultRequest(FileId1))) { } };
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Contain("prompted",
            "chat-summarize executes prompted Actions only (SUM-CHAT@v1, kind: prompted)");
    }

    [Fact]
    public async Task SummarizeSessionFilesAsync_ActionRowUnloadable_ThrowsInvalidOperationException()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);
        _scopeResolverMock
            .Setup(s => s.GetActionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AnalysisAction?)null);

        var act = async () => { await foreach (var _ in CreateSut().SummarizeSessionFilesAsync(DefaultRequest(FileId1))) { } };
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Contain("sprk_action lookup",
            "operator diagnostic points at the broken Binding→Action FK");
        thrown.Which.Message.Should().NotContain("not found",
            "must not trip the endpoint's 404 session-not-found mapping");
    }

    // ─── (c) FR-04 — multi-file interjection ───────────────────────────────────────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_MultiFile_EmitsInterjectionBeforeResult()
    {
        _sessionManagerStub.Session = BuildSession(FileId1, FileId2);

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1, FileId2 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var chunks = await Collect(CreateSut().SummarizeSessionFilesAsync(request));

        chunks.Should().HaveCount(2,
            "FR-04 — multi-file interjection chunk emitted BEFORE the executor result");
        chunks[0].Type.Should().Be("text");
        chunks[0].Content.Should().Contain("Multiple files");
        chunks[1].Type.Should().Be("complete");
    }

    [Fact]
    public async Task SummarizeSessionFilesAsync_SingleFile_DoesNotEmitInterjection()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);

        var chunks = await Collect(CreateSut().SummarizeSessionFilesAsync(DefaultRequest(FileId1)));

        chunks.Should().HaveCount(1,
            "single-file requests skip the FR-04 interjection — only the result chunk surfaces");
        chunks[0].Type.Should().Be("complete");
    }

    // ─── (d) FR-08 — file-id defaulting + no-files edge ────────────────────────────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_NullFileIds_DefaultsToAllSessionFiles()
    {
        _sessionManagerStub.Session = BuildSession(FileId1, FileId2);

        IReadOnlyList<ChatSessionFile>? capturedFiles = null;
        _sessionFileTextSourceMock
            .Setup(t => t.FetchAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<ChatSessionFile>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, IReadOnlyList<ChatSessionFile>, CancellationToken>(
                (_, _, files, _) => capturedFiles = files)
            .ReturnsAsync(new SessionFileText
            {
                ExtractedText = "combined text",
                DisplayName = "session-files-combined"
            });

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, FileIds: null, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        _ = await Collect(CreateSut().SummarizeSessionFilesAsync(request));

        capturedFiles.Should().NotBeNull();
        capturedFiles!.Select(f => f.FileId).Should().BeEquivalentTo(new[] { FileId1, FileId2 },
            "FR-08 — omitted fileIds default to ALL files in the session manifest");
    }

    [Fact]
    public async Task SummarizeSessionFilesAsync_NoMatchingFiles_YieldsFromErrorChunk()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { "file-does-not-exist" }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var chunks = await Collect(CreateSut().SummarizeSessionFilesAsync(request));

        chunks.Should().HaveCount(1);
        chunks[0].Type.Should().Be("error");
        _actionRunnerMock.Verify(
            r => r.RunAsync(It.IsAny<AnalysisAction>(), It.IsAny<DocumentText>(),
                It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── (e) Runtime failures surface as FromError chunks (never silent EOF) ───────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_TextFetchFails_YieldsFromErrorChunk()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);
        _sessionFileTextSourceMock
            .Setup(t => t.FetchAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<ChatSessionFile>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("search transport failure"));

        var chunks = await Collect(CreateSut().SummarizeSessionFilesAsync(DefaultRequest(FileId1)));

        chunks.Should().HaveCount(1);
        chunks[0].Type.Should().Be("error");
        chunks[0].Error.Should().Contain("retrieve session file content",
            "fetch failures surface as a friendly FromError chunk, not a raw exception");
    }

    [Fact]
    public async Task SummarizeSessionFilesAsync_EmptyExtractedText_YieldsFromErrorChunk()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);
        _sessionFileTextSourceMock
            .Setup(t => t.FetchAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<ChatSessionFile>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionFileText { ExtractedText = "", DisplayName = "f.pdf" });

        var chunks = await Collect(CreateSut().SummarizeSessionFilesAsync(DefaultRequest(FileId1)));

        chunks.Should().HaveCount(1);
        chunks[0].Type.Should().Be("error");
        _actionRunnerMock.Verify(
            r => r.RunAsync(It.IsAny<AnalysisAction>(), It.IsAny<DocumentText>(),
                It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no text → no LLM call");
    }

    [Fact]
    public async Task SummarizeSessionFilesAsync_ExecutorFails_YieldsFromErrorChunk()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);
        _actionRunnerMock
            .Setup(r => r.RunAsync(
                It.IsAny<AnalysisAction>(), It.IsAny<DocumentText>(),
                It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Azure OpenAI 503"));

        var chunks = await Collect(CreateSut().SummarizeSessionFilesAsync(DefaultRequest(FileId1)));

        chunks.Should().HaveCount(1);
        chunks[0].Type.Should().Be("error");
        chunks[0].Done.Should().BeTrue("error chunks are terminal per the AnalysisChunk envelope");
    }

    // ─── (f) Boundary validation (unchanged responsibilities) ──────────────────────────────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_RejectsMoreThanTwentyFileIds()
    {
        _sessionManagerStub.Session = BuildSession(FileId1);
        var tooMany = Enumerable.Range(1, ChatSession.MaxUploadedFiles + 1).Select(i => $"f-{i}").ToList();
        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, tooMany, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var act = async () => { await foreach (var _ in CreateSut().SummarizeSessionFilesAsync(request)) { } };
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*NFR-02*");

        _actionRunnerMock.Verify(
            r => r.RunAsync(It.IsAny<AnalysisAction>(), It.IsAny<DocumentText>(),
                It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "NFR-02 cap fails fast at the boundary; the executor MUST NOT be called");
    }

    [Fact]
    public async Task SummarizeSessionFilesAsync_SessionNotFound_ThrowsInvalidOperationException()
    {
        _sessionManagerStub.Session = null;

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, FileIds: null, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var act = async () => { await foreach (var _ in CreateSut().SummarizeSessionFilesAsync(request)) { } };
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");

        _consumerRoutingMock.Verify(
            c => c.ResolveBindingAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "missing session fails at the boundary BEFORE catalog resolution");
    }

    [Fact]
    public async Task SummarizeSessionFilesAsync_EmptyTenantId_Throws()
    {
        var request = new SummarizeSessionFilesRequest(
            TenantId: "", SessionId: SessionId, FileIds: null, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var act = async () => { await foreach (var _ in CreateSut().SummarizeSessionFilesAsync(request)) { } };
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SummarizeSessionFilesAsync_EmptySessionId_Throws()
    {
        var request = new SummarizeSessionFilesRequest(
            TenantId: TenantId, SessionId: "", FileIds: null, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        var act = async () => { await foreach (var _ in CreateSut().SummarizeSessionFilesAsync(request)) { } };
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─── (g) ADR-010 shape contracts ────────────────────────────────────────────────────────────

    [Fact]
    public void SessionSummarizeOrchestrator_HasNoOrchestratorAuthoredInterface()
    {
        var ifaces = typeof(SessionSummarizeOrchestrator).GetInterfaces();
        var authored = ifaces.Where(i =>
            !i.Namespace?.StartsWith("System", StringComparison.Ordinal) is true
            && !i.Namespace?.StartsWith("Microsoft", StringComparison.Ordinal) is true).ToList();
        authored.Should().BeEmpty(
            "ADR-010 forbids interfaces-for-testability-alone; SessionSummarizeOrchestrator is concrete by design");
    }

    [Fact]
    public void SessionSummarizeOrchestrator_ExposesExactlyOneConvergenceMethod()
    {
        var publicMethods = typeof(SessionSummarizeOrchestrator)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToList();

        var convergence = publicMethods
            .Where(m => m.ReturnType.IsGenericType
                && m.ReturnType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>)
                && m.ReturnType.GetGenericArguments()[0] == typeof(AnalysisChunk))
            .ToList();

        convergence.Should().HaveCount(1,
            "one convergence method that every dispatch leg delegates to");
        convergence[0].Name.Should().Be(nameof(SessionSummarizeOrchestrator.SummarizeSessionFilesAsync));
    }

    // ─── (h) FR-P1-02 / ADR-040 — universal ledger write BEFORE rendering (task 021) ──────────

    [Fact]
    public async Task SummarizeSessionFilesAsync_LedgerWrite_StrictlyPrecedesResultChunk()
    {
        // THE ordering proof (spec FR-P1-02 acceptance: "render follows store — test-proven
        // ordering"). The persist callback fires synchronously inside the session-store write;
        // the consumer records each yielded chunk. Store MUST come first.
        _sessionManagerStub.Session = BuildSession(FileId1);
        var events = new List<string>();
        _sessionManagerStub.OnPersist = _ => events.Add("ledger-write");

        await foreach (var chunk in CreateSut().SummarizeSessionFilesAsync(DefaultRequest(FileId1)))
        {
            events.Add($"render:{chunk.Type}");
        }

        events.Should().Equal(new[] { "ledger-write", "render:complete" },
            "ADR-040 storage precedes rendering — the SessionOutput ledger write completes " +
            "BEFORE the terminal result chunk is emitted to the stream");
    }

    [Fact]
    public async Task SummarizeSessionFilesAsync_Execution_ProducesAddressableSessionOutput()
    {
        // Addressability (spec FR-P1-02 acceptance: "every execution produces an addressable
        // SessionOutput"). Retrieval goes through the production read path (GetSessionAsync
        // over the persisted state), keyed {bindingId}@t{n}.
        _sessionManagerStub.Session = BuildSession(FileId1);

        _ = await Collect(CreateSut().SummarizeSessionFilesAsync(DefaultRequest(FileId1)));

        var persisted = await _sessionManagerStub.GetSessionAsync(TenantId, SessionId);
        persisted!.Outputs.Should().NotBeNull();
        var expectedKey = SessionLedger.BuildOutputKey(BindingId.ToString(), 1);
        var entry = persisted.Outputs!.Should().ContainSingle(o => o.Key == expectedKey).Subject;

        entry.BindingId.Should().Be(BindingId.ToString());
        entry.UcId.Should().Be("UC-A-1", "the Binding row's sprk_ucid flows onto the entry");
        entry.Turn.Should().Be(1);
        entry.Disposition.Should().Be("informational",
            "the Binding row's sprk_disposition is the ONLY rendering contract (ADR-039/ADR-040)");
        entry.Payload.GetRawText().Should().Be(ParseJson(ValidResultJson).GetRawText(),
            "the FULL structured output is the stored composition carrier");
        entry.SourceRefs.Should().BeEquivalentTo(new[] { FileId1 },
            "grounding file ids ride along as source refs (identifiers only, NFR-07)");
    }

    [Fact]
    public async Task SummarizeSessionFilesAsync_SequentialExecutions_IncrementTurnOrdinal()
    {
        // {bindingId}@t{n} uniqueness within the session: the second execution of the SAME
        // binding allocates the next monotonic ordinal (documented turn-numbering decision).
        _sessionManagerStub.Session = BuildSession(FileId1);
        var sut = CreateSut();

        _ = await Collect(sut.SummarizeSessionFilesAsync(DefaultRequest(FileId1)));
        _ = await Collect(sut.SummarizeSessionFilesAsync(DefaultRequest(FileId1)));

        var persisted = await _sessionManagerStub.GetSessionAsync(TenantId, SessionId);
        persisted!.Outputs.Should().HaveCount(2, "the ledger is append-only — no overwrites");
        persisted.Outputs!.Select(o => o.Key).Should().Equal(
            SessionLedger.BuildOutputKey(BindingId.ToString(), 1),
            SessionLedger.BuildOutputKey(BindingId.ToString(), 2));
    }

    [Fact]
    public async Task SummarizeSessionFilesAsync_NonInformationalDisposition_ThrowsLoudStub_AfterLedgerWrite()
    {
        // P1 routes ONLY the informational disposition. Any other declared disposition hits a
        // LOUD NotSupported stub (task 021 contract — no silent fallback to inline render),
        // AND the ledger write still happened first (storage is never coupled to rendering).
        _sessionManagerStub.Session = BuildSession(FileId1);
        _consumerRoutingMock
            .Setup(c => c.ResolveBindingAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildBinding() with { Disposition = BindingDisposition.Email });

        var rendered = new List<AnalysisChunk>();
        var act = async () =>
        {
            await foreach (var chunk in CreateSut().SummarizeSessionFilesAsync(DefaultRequest(FileId1)))
            {
                rendered.Add(chunk);
            }
        };

        var thrown = await act.Should().ThrowAsync<NotSupportedException>(
            "unimplemented disposition legs FAIL LOUDLY (they land at P3) — never a silent inline render");
        thrown.Which.Message.Should().Contain("email").And.Contain("P3");

        rendered.Should().BeEmpty("nothing may render through an unimplemented disposition leg");
        _sessionManagerStub.PersistedSessions.Should().ContainSingle()
            .Which.Outputs.Should().ContainSingle(o => o.Disposition == "email",
                "storage precedes rendering UNIVERSALLY — the entry is stored and addressable " +
                "even when the rendering leg is missing (ADR-040: storage never couples to rendering)");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────────────────

    private static SummarizeSessionFilesRequest DefaultRequest(params string[] fileIds) => new(
        TenantId, SessionId, fileIds, StyleHint: null,
        Path: SummarizeInvocationPath.DirectEndpoint);

    private static Binding BuildBinding() => new()
    {
        BindingId = BindingId,
        ConsumerType = ConsumerTypes.ChatSummarize,
        ConsumerCode = "default",
        Environment = "*",
        ActionId = ActionId,
        ActionKind = ActionKind.Prompted,
        Ucid = "UC-A-1",
        Disposition = BindingDisposition.Informational,
    };

    private static AnalysisAction BuildAction() => new()
    {
        Id = ActionId,
        Name = "Summarize Document for Chat",
        SystemPrompt = """{"$schema":"https://spaarke.com/schemas/prompt/v1","instruction":{"role":"Summarizer","task":"Summarize the document."}}""",
        OutputSchemaJson = """{"type":"object","additionalProperties":false,"required":["tldr"],"properties":{"tldr":{"type":"array","items":{"type":"string"}}}}""",
        Temperature = 0.0m,
    };

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static async Task<List<AnalysisChunk>> Collect(IAsyncEnumerable<AnalysisChunk> source)
    {
        var list = new List<AnalysisChunk>();
        await foreach (var chunk in source)
        {
            list.Add(chunk);
        }
        return list;
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
    /// inject a fixed session without wiring Redis/Dataverse, and the internal-virtual
    /// <see cref="ChatSessionManager.UpdateSessionCacheAsync"/> so FR-P1-02 tests can observe
    /// the ledger write (persisted sessions + a persist callback for ordering proof).
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

        /// <summary>Every session instance written through the persistence seam, in order.</summary>
        public List<ChatSession> PersistedSessions { get; } = new();

        /// <summary>Invoked synchronously inside the persistence write (ordering capture).</summary>
        public Action<ChatSession>? OnPersist { get; set; }

        public override Task<ChatSession?> GetSessionAsync(
            string tenantId, string sessionId, CancellationToken ct = default)
            => Task.FromResult(Session);

        internal override Task UpdateSessionCacheAsync(ChatSession session, CancellationToken ct = default)
        {
            PersistedSessions.Add(session);
            OnPersist?.Invoke(session);
            // Store-backed read model: subsequent GetSessionAsync returns the persisted state,
            // so addressability tests retrieve the SessionOutput through the production read path.
            Session = session;
            return Task.CompletedTask;
        }
    }
}
