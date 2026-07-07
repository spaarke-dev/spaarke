using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Xunit;

// Explicit alias to resolve ChatMessage ambiguity between domain model and Extensions.AI.
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="AnalysisExecutionHandler"/> — the FR-P2-07 (task 036) typed-handler
/// migration of the last live legacy chat-tool group (analysis rerun + analysis refine).
///
/// Verifies:
/// - The 4 handler contract tests (R6 Pillar 2 conventions)
/// - method=rerun: playbook execution via session context ids, progress + document_replace SSE
///   emission through ChatInvocationContext.SseWriter, missing-context diagnostics
/// - method=refine: analysis fetch + focused inner LLM call, missing-analysis/instruction diagnostics
/// - Chat-only invocation contract (playbook path refused)
/// - ADR-015 telemetry (no instruction/content leakage)
/// </summary>
public sealed class AnalysisExecutionHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly Guid TestPlaybookId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid TestAnalysisId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string TestDocumentId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string TestRefinementInstruction = "Make the recommendations more actionable.";

    private readonly Mock<IAnalysisOrchestrationService> _analysisServiceMock = new();
    private readonly Mock<IChatClient> _chatClientMock = new();
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock = new();
    // Task 044: E-2 adapter re-homed onto the rerun leg (ledger write BEFORE document_replace).
    // Loose mock returns null (no resolvable chat session) by default — the render still
    // proceeds per the adapter's documented session-scope boundary.
    private readonly Mock<Sprk.Bff.Api.Services.Ai.IEngineOutputLedgerAdapter> _engineOutputLedgerMock = new();
    private readonly List<ChatSseEvent> _capturedEvents = new();

    public AnalysisExecutionHandlerTests()
    {
        _httpContextAccessorMock.SetupGet(a => a.HttpContext).Returns(new DefaultHttpContext());
    }

    private AnalysisExecutionHandler CreateHandler() => new(
        _analysisServiceMock.Object,
        _chatClientMock.Object,
        _httpContextAccessorMock.Object,
        _engineOutputLedgerMock.Object,
        CreateLogger<AnalysisExecutionHandler>());

    private static AnalysisTool BuildExecutionTool(string method) =>
        BuildAnalysisTool(nameof(AnalysisExecutionHandler), configuration: $"{{\"method\":\"{method}\"}}");

    private ChatInvocationContext BuildRerunContext(string argsJson = "{}") =>
        BuildChatInvocationContext(toolArgumentsJson: argsJson) with
        {
            PlaybookId = TestPlaybookId,
            DocumentId = TestDocumentId,
            SseWriter = (evt, ct) => { _capturedEvents.Add(evt); return Task.CompletedTask; }
        };

    private void SetupPlaybookExecutionReturns(params AnalysisStreamChunk[] chunks)
    {
        _analysisServiceMock
            .Setup(s => s.ExecutePlaybookAsync(
                It.IsAny<PlaybookExecuteRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(chunks));
    }

    private static async IAsyncEnumerable<AnalysisStreamChunk> ToAsyncEnumerable(
        AnalysisStreamChunk[] chunks)
    {
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return chunk;
        }
    }

    private void SetupChatResponse(string text)
    {
        _chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new AiChatMessage(ChatRole.Assistant, text)]));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 1-4. Handler contract tests (R6 Pillar 2 conventions)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void HandlerType_IsRegisteredInDi()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolFramework(configuration);

        var registeredImplementations = services
            .Where(d => d.ServiceType == typeof(IToolHandler) && d.ImplementationType is not null)
            .Select(d => d.ImplementationType!)
            .ToList();

        registeredImplementations.Should().Contain(
            typeof(AnalysisExecutionHandler),
            because: "the handler type must be auto-discovered by the assembly scan (ADR-010: no manual DI lines per handler)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        CreateHandler().HandlerId.Should().Be(
            nameof(AnalysisExecutionHandler),
            because: "sprk_handlerclass routes runtime invocation by the C# class name");
    }

    [Fact]
    public void Metadata_IsValid()
    {
        var metadata = CreateHandler().Metadata;

        metadata.Name.Should().NotBeNullOrWhiteSpace();
        metadata.Description.Should().NotBeNullOrWhiteSpace();
        metadata.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

    [Fact]
    public void SupportedToolTypes_IsNonEmpty()
    {
        CreateHandler().SupportedToolTypes.Should().NotBeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Chat-only invocation contract
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SupportedInvocationContexts_IsChatOnly()
    {
        CreateHandler().SupportedInvocationContexts.Should().Be(
            InvocationContextKind.Chat,
            because: "the playbook engine must never re-enter itself via a playbook-orchestrated tool");
    }

    [Fact]
    public async Task ExecuteAsync_PlaybookPath_ReturnsErrorResult()
    {
        var handler = CreateHandler();
        var tool = BuildExecutionTool("rerun");

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeFalse(because: "playbook invocation is not supported (chat-only handler)");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ValidateChat
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_RerunTool_WithValidContext_Succeeds()
    {
        var result = CreateHandler().ValidateChat(BuildRerunContext(), BuildExecutionTool("rerun"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_MissingMethodConfiguration_Fails()
    {
        var tool = BuildAnalysisTool(nameof(AnalysisExecutionHandler), configuration: null);

        var result = CreateHandler().ValidateChat(BuildRerunContext(), tool);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateChat_RefineTool_WithoutInstruction_Fails()
    {
        var result = CreateHandler().ValidateChat(
            BuildChatInvocationContext(toolArgumentsJson: "{}"),
            BuildExecutionTool("refine"));

        result.IsValid.Should().BeFalse(because: "method 'refine' requires a non-empty refinementInstruction argument");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // method=rerun
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_Rerun_CallsExecutePlaybookAsync_WithSessionContextIds()
    {
        SetupPlaybookExecutionReturns(
            AnalysisStreamChunk.Metadata(TestAnalysisId, "test-doc.pdf"),
            AnalysisStreamChunk.TextChunk("Analysis output content"));
        var handler = CreateHandler();

        var result = await handler.ExecuteChatAsync(
            BuildRerunContext("{\"additionalInstructions\":\"Focus on financial risks.\"}"),
            BuildExecutionTool("rerun"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        _analysisServiceMock.Verify(s => s.ExecutePlaybookAsync(
            It.Is<PlaybookExecuteRequest>(r =>
                r.PlaybookId == TestPlaybookId &&
                r.DocumentIds!.Length == 1 &&
                r.DocumentIds[0] == Guid.Parse(TestDocumentId) &&
                r.AdditionalContext == "Focus on financial risks."),
            It.IsAny<HttpContext>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteChatAsync_Rerun_EmitsProgressAndDocumentReplaceEvents()
    {
        const string analysisHtml = "<h1>Analysis Results</h1><p>Key findings...</p>";
        SetupPlaybookExecutionReturns(
            AnalysisStreamChunk.Metadata(TestAnalysisId, "test-doc.pdf"),
            AnalysisStreamChunk.TextChunk("[Executing: Extractor]\n"),
            AnalysisStreamChunk.TextChunk(analysisHtml));
        var handler = CreateHandler();

        await handler.ExecuteChatAsync(BuildRerunContext(), BuildExecutionTool("rerun"), CancellationToken.None);

        var progressEvents = _capturedEvents.Where(e => e.Type == "progress").ToList();
        progressEvents.Should().HaveCountGreaterThanOrEqualTo(3,
            because: "the rerun emits per-stage progress (start, playbook load, tool stages, completion)");
        (progressEvents.First().Data as ChatSseProgressData)!.Percent.Should().Be(0);
        (progressEvents.Last().Data as ChatSseProgressData)!.Percent.Should().Be(100);

        var replaceEvents = _capturedEvents.Where(e => e.Type == "document_replace").ToList();
        replaceEvents.Should().HaveCount(1);
        var replaceData = replaceEvents[0].Data as ChatSseDocumentReplaceData;
        replaceData!.Html.Should().Contain(analysisHtml);
        replaceData.Metadata.PlaybookId.Should().Be(TestPlaybookId.ToString());
    }

    [Fact]
    public async Task ExecuteChatAsync_Rerun_WritesLedgerEntry_BeforeDocumentReplaceRender()
    {
        // ADR-040 store-precedes-render — E-2 adapter re-homed onto the rerun leg (task 044).
        const string analysisHtml = "<h1>Analysis Results</h1>";
        SetupPlaybookExecutionReturns(
            AnalysisStreamChunk.Metadata(TestAnalysisId, "test-doc.pdf"),
            AnalysisStreamChunk.TextChunk(analysisHtml));
        var ledgerWrittenBeforeReplace = false;
        _engineOutputLedgerMock
            .Setup(l => l.RecordAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), TestPlaybookId,
                It.Is<Sprk.Bff.Api.Services.Ai.EngineRunOutput>(o =>
                    o.RunId == TestAnalysisId && o.TextContent!.Contains(analysisHtml)),
                It.IsAny<CancellationToken>()))
            .Callback(() => ledgerWrittenBeforeReplace = !_capturedEvents.Any(e => e.Type == "document_replace"))
            .ReturnsAsync((Sprk.Bff.Api.Models.Ai.Chat.SessionOutput?)null);
        var handler = CreateHandler();

        var result = await handler.ExecuteChatAsync(BuildRerunContext(), BuildExecutionTool("rerun"), CancellationToken.None);

        result.Success.Should().BeTrue();
        _engineOutputLedgerMock.Verify(l => l.RecordAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), TestPlaybookId,
            It.IsAny<Sprk.Bff.Api.Services.Ai.EngineRunOutput>(),
            It.IsAny<CancellationToken>()), Times.Once);
        ledgerWrittenBeforeReplace.Should().BeTrue(
            because: "the ledger write must precede the document_replace render (ADR-040 D2/D8)");
    }

    [Fact]
    public async Task ExecuteChatAsync_Rerun_WhenLedgerWriteFails_FailsToolCall_AndNothingRenders()
    {
        SetupPlaybookExecutionReturns(
            AnalysisStreamChunk.Metadata(TestAnalysisId, "test-doc.pdf"),
            AnalysisStreamChunk.TextChunk("output"));
        _engineOutputLedgerMock
            .Setup(l => l.RecordAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<Sprk.Bff.Api.Services.Ai.EngineRunOutput>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ledger store unavailable"));
        var handler = CreateHandler();

        var result = await handler.ExecuteChatAsync(BuildRerunContext(), BuildExecutionTool("rerun"), CancellationToken.None);

        result.Success.Should().BeFalse(
            because: "unstored output is never rendered — a ledger-write failure fails the tool call (ADR-040)");
        _capturedEvents.Should().NotContain(e => e.Type == "document_replace");
    }

    [Fact]
    public async Task ExecuteChatAsync_Rerun_WithoutSseWriter_StillSucceeds()
    {
        SetupPlaybookExecutionReturns(
            AnalysisStreamChunk.Metadata(TestAnalysisId, "test-doc.pdf"),
            AnalysisStreamChunk.TextChunk("output"));
        var context = BuildRerunContext() with { SseWriter = null };

        var result = await CreateHandler().ExecuteChatAsync(context, BuildExecutionTool("rerun"), CancellationToken.None);

        result.Success.Should().BeTrue(because: "SSE emission is a side channel — a missing writer degrades silently");
    }

    [Fact]
    public async Task ExecuteChatAsync_Rerun_WithoutPlaybookContext_ReturnsError()
    {
        var context = BuildRerunContext() with { PlaybookId = null };

        var result = await CreateHandler().ExecuteChatAsync(context, BuildExecutionTool("rerun"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("no playbook context");
    }

    [Fact]
    public async Task ExecuteChatAsync_Rerun_WithoutDocumentContext_ReturnsError()
    {
        var context = BuildRerunContext() with { DocumentId = null };

        var result = await CreateHandler().ExecuteChatAsync(context, BuildExecutionTool("rerun"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("no active document");
    }

    [Fact]
    public async Task ExecuteChatAsync_Rerun_WithoutHttpContext_ReturnsError()
    {
        _httpContextAccessorMock.SetupGet(a => a.HttpContext).Returns((HttpContext?)null);

        var result = await CreateHandler().ExecuteChatAsync(BuildRerunContext(), BuildExecutionTool("rerun"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("HTTP context not available");
    }

    [Fact]
    public async Task ExecuteChatAsync_Rerun_EngineErrorChunk_ReturnsError()
    {
        SetupPlaybookExecutionReturns(
            AnalysisStreamChunk.Metadata(TestAnalysisId, "test-doc.pdf"),
            new AnalysisStreamChunk("error", null, Done: true, Error: "engine unavailable"));

        var result = await CreateHandler().ExecuteChatAsync(BuildRerunContext(), BuildExecutionTool("rerun"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("engine unavailable");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // method=refine
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_Refine_FetchesAnalysisAndReturnsRefinedText()
    {
        const string refined = "Refined analysis output.";
        _analysisServiceMock
            .Setup(s => s.GetAnalysisAsync(TestAnalysisId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisDetailResult { WorkingDocument = "Current analysis output." });
        SetupChatResponse(refined);

        var context = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"refinementInstruction\":\"{TestRefinementInstruction}\"}}")
            with
        { AnalysisId = TestAnalysisId };

        var result = await CreateHandler().ExecuteChatAsync(context, BuildExecutionTool("refine"), CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = result.GetData<AnalysisExecutionHandler.AnalysisExecutionResult>();
        data.Should().NotBeNull();
        data!.Method.Should().Be("refine");
        data.Text.Should().Be(refined);
        _chatClientMock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<AiChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteChatAsync_Refine_WithoutAnalysisContext_ReturnsError()
    {
        var context = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"refinementInstruction\":\"{TestRefinementInstruction}\"}}");

        var result = await CreateHandler().ExecuteChatAsync(context, BuildExecutionTool("refine"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("no analysis output available");
    }

    [Fact]
    public async Task ExecuteChatAsync_Refine_WithoutInstruction_ReturnsValidationError()
    {
        var context = BuildChatInvocationContext(toolArgumentsJson: "{}") with { AnalysisId = TestAnalysisId };

        var result = await CreateHandler().ExecuteChatAsync(context, BuildExecutionTool("refine"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("refinementInstruction");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 telemetry
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_Refine_Telemetry_DoesNotLeakInstructionOrContent()
    {
        const string analysisBody = "Highly confidential analysis body text for governance test.";
        const string instruction = "Rewrite the confidential recommendations entirely.";
        _analysisServiceMock
            .Setup(s => s.GetAnalysisAsync(TestAnalysisId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisDetailResult { WorkingDocument = analysisBody });
        SetupChatResponse("Refined governance-safe output body for the assertion.");

        var context = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"refinementInstruction\":\"{instruction}\"}}")
            with
        { AnalysisId = TestAnalysisId };

        await CreateHandler().ExecuteChatAsync(context, BuildExecutionTool("refine"), CancellationToken.None);

        AssertTelemetryRespectsAdr015(analysisBody, instruction);
    }
}
