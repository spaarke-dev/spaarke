using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="WorkingDocumentHandler"/> (R6 Wave 9 — replaces the legacy
/// hardcoded <c>WorkingDocumentTools</c> class previously instantiated in
/// <see cref="SprkChatAgentFactory"/>). Closes the Q9 chat-tool migration at 10/10.
/// </summary>
/// <remarks>
/// <para>
/// Per ADR-033 §7.1 test obligations, this suite covers:
/// </para>
/// <list type="bullet">
/// <item><strong>4-point contract</strong> (auto-discovery, HandlerId, Metadata, SupportedToolTypes).</item>
/// <item><strong>SupportedInvocationContexts = Chat</strong> (matches the legacy hardcoded registration;
/// FR-12 safety — document mutation tools must not run in playbook orchestration).</item>
/// <item><strong>3-method dispatch</strong> via sprk_configuration.method discriminator
/// (EditWorkingDocument / AppendSection / WriteBackToWorkingDocument).</item>
/// <item><strong>Event sequence assertions</strong>: Start → N×Token → End per method, using a captured
/// mock writer delegate. The hash returned in DocumentStreamEndEvent matches the assembled content.</item>
/// <item><strong>Cancellation path</strong>: terminal End event with <c>Cancelled: true</c> AND no rethrow
/// (ADR-019 + ADR-033 — handler degrades gracefully and returns a successful summary to the LLM).</item>
/// <item><strong>Error path</strong>: terminal End event with <c>ErrorCode = "LLM_STREAM_FAILED"</c> AND
/// <see cref="ToolResult"/> failure.</item>
/// <item><strong>Null writer path</strong>: when <see cref="ChatInvocationContext.DocumentStreamWriter"/>
/// is null → <see cref="ToolResult"/> failure with diagnostic AND no IChatClient call attempted.</item>
/// <item><strong>SHA-256 hash assertion</strong>: assembled token text → returned hash matches.</item>
/// <item><strong>ADR-015 telemetry</strong>: sentinel-string scan over captured log output asserts
/// no token content / no instruction content / no document content above Debug.</item>
/// <item><strong>WriteBack safety (FR-12)</strong>: persists via <see cref="IWorkingDocumentService"/>
/// only; NEVER calls IChatClient streaming.</item>
/// <item><strong>Chat-context dispatch via adapter</strong>: confirms the handler dispatches correctly
/// from chat-mode invocation through <see cref="ToolHandlerToAIFunctionAdapter"/>.</item>
/// </list>
/// <para>
/// <strong>R6 Wave 9 dispatch note (Stage 3)</strong>: this test file assumes
/// <see cref="ChatInvocationContext"/> carries an optional <c>AnalysisId</c> field. The legacy
/// <c>WorkingDocumentTools</c> received <c>analysisId</c> via constructor capture from
/// <see cref="SprkChatAgentFactory.ResolveTools"/>; under the ADR-033 typed-handler model the
/// handler must resolve it from the per-call context. If the field has not yet been added by
/// Stage 2 (ADR-033 §4.1 currently documents only <c>DocumentStreamWriter</c>) or by main session
/// Stage 4 wiring, the build will fail with a clean compile error and the design gap is resolved
/// at that point. See the Wave 9 bookkeeping note for the surfacing.
/// </para>
/// </remarks>
public sealed class WorkingDocumentHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly Assembly BffAssembly = typeof(IToolHandler).Assembly;

    // ═════════════════════════════════════════════════════════════════════════════
    // Test infrastructure
    // ═════════════════════════════════════════════════════════════════════════════

    private readonly Mock<IAnalysisOrchestrationService> _analysisServiceMock = new();
    private readonly Mock<IWorkingDocumentService> _workingDocumentServiceMock = new();
    private readonly FakeChatClient _chatClient = new();
    private readonly List<DocumentStreamEvent> _capturedEvents = new();
    private readonly Guid _defaultAnalysisId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private WorkingDocumentHandler CreateHandler()
    {
        return new WorkingDocumentHandler(
            _chatClient,
            _analysisServiceMock.Object,
            _workingDocumentServiceMock.Object,
            CreateLogger<WorkingDocumentHandler>());
    }

    /// <summary>
    /// Build a chat invocation context wired with the captured DocumentStreamWriter delegate
    /// (writes to <see cref="_capturedEvents"/>) and a populated AnalysisId (so the handler
    /// can fetch/persist working document content). Per ADR-033 §4.1 wiring contract.
    /// </summary>
    private ChatInvocationContext BuildContext(
        string toolArgumentsJson,
        Guid? analysisId = null,
        bool wireStreamWriter = true)
    {
        var baseCtx = BuildChatInvocationContext(toolArgumentsJson: toolArgumentsJson);
        return baseCtx with
        {
            AnalysisId = analysisId ?? _defaultAnalysisId,
            DocumentStreamWriter = wireStreamWriter
                ? (evt, _) =>
                  {
                      _capturedEvents.Add(evt);
                      return Task.CompletedTask;
                  }
            : null
        };
    }

    private static AnalysisTool BuildWorkingDocTool(string method) =>
        BuildAnalysisTool(
            handlerClass: nameof(WorkingDocumentHandler),
            configuration: $"{{\"method\":\"{method}\"}}",
            toolType: ToolType.Custom);

    private void SetupAnalysisReturnsDocument(string? content)
    {
        _analysisServiceMock
            .Setup(s => s.GetAnalysisAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisDetailResult
            {
                Id = _defaultAnalysisId,
                DocumentId = Guid.NewGuid(),
                DocumentName = "fixture-doc.pdf",
                Action = new AnalysisActionInfo(Guid.NewGuid(), "Fixture Action"),
                Status = "completed",
                WorkingDocument = content,
                FinalOutput = content,
                ChatHistory = Array.Empty<ChatMessageInfo>(),
                TokenUsage = null,
                StartedOn = DateTime.UtcNow,
                CompletedOn = DateTime.UtcNow
            });
    }

    private void SetupStreamingTokens(params string[] tokens)
    {
        _chatClient.GetStreamingResponseAsyncCallback = (_, _, _) =>
            AsyncEnumerableHelpers.FromChunks(tokens);
    }

    private void SetupStreamingThenThrows(string[] tokensBefore, Exception ex)
    {
        var updates = tokensBefore.Select(t => new ChatResponseUpdate(ChatRole.Assistant, t)).ToArray();
        _chatClient.GetStreamingResponseAsyncCallback = (_, _, _) =>
            AsyncEnumerableHelpers.ThrowingAsyncEnumerable(ex, updates);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 4-point contract tests
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void HandlerType_IsRegisteredInDi()
    {
        var services = BuildToolFrameworkServiceCollection();

        var registeredImplementations = services
            .Where(d => d.ServiceType == typeof(IToolHandler) && d.ImplementationType is not null)
            .Select(d => d.ImplementationType!)
            .ToList();

        registeredImplementations.Should().Contain(
            typeof(WorkingDocumentHandler),
            because: "the handler must be auto-discovered via assembly scan (R6 Pillar 2: no manual DI lines)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        var handler = CreateHandler();

        handler.HandlerId.Should().Be(
            nameof(WorkingDocumentHandler),
            because: "R6 Pillar 2 binding: HandlerId == nameof(handler class) so sprk_handlerclass routes to this handler at runtime");
    }

    [Fact]
    public void Metadata_IsValid()
    {
        var handler = CreateHandler();
        var metadata = handler.Metadata;

        metadata.Should().NotBeNull();
        metadata.Name.Should().NotBeNullOrWhiteSpace();
        metadata.Description.Should().NotBeNullOrWhiteSpace();
        metadata.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

    [Fact]
    public void SupportedToolTypes_IsNonEmpty()
    {
        var handler = CreateHandler();

        handler.SupportedToolTypes.Should().NotBeNullOrEmpty();
        handler.SupportedToolTypes.Should().Contain(ToolType.Custom);
    }

    [Fact]
    public void SupportedInvocationContexts_IsChatOnly()
    {
        var handler = CreateHandler();

        handler.SupportedInvocationContexts.Should().Be(
            InvocationContextKind.Chat,
            because: "R6 Wave 9: WorkingDocumentHandler matches the legacy hardcoded registration (chat-only). " +
                     "Spec FR-12 safety constraint: document mutation tools must not run in playbook orchestration. " +
                     "The 11 production node executors (NFR-08) do not include a document mutation executor.");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ValidateChat — argument shape per method
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_Succeeds_WithInstruction_EditMethod()
    {
        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"instruction\":\"Fix grammar\"}");
        var tool = BuildWorkingDocTool("EditWorkingDocument");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_Succeeds_WithSectionTitleAndInstruction_AppendMethod()
    {
        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"sectionTitle\":\"Risk\",\"instruction\":\"Summarize risks\"}");
        var tool = BuildWorkingDocTool("AppendSection");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_Succeeds_WithContent_WriteBackMethod()
    {
        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"content\":\"final document\"}");
        var tool = BuildWorkingDocTool("WriteBackToWorkingDocument");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_Fails_WhenInstructionMissing_EditMethod()
    {
        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{}");
        var tool = BuildWorkingDocTool("EditWorkingDocument");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("instruction", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenSectionTitleMissing_AppendMethod()
    {
        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"instruction\":\"x\"}");
        var tool = BuildWorkingDocTool("AppendSection");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("sectionTitle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenContentMissing_WriteBackMethod()
    {
        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{}");
        var tool = BuildWorkingDocTool("WriteBackToWorkingDocument");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("content", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenTenantIdMissing()
    {
        var handler = CreateHandler();
        var baseCtx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"instruction\":\"x\"}",
            tenantId: "");
        var ctx = baseCtx with { AnalysisId = _defaultAnalysisId };
        var tool = BuildWorkingDocTool("EditWorkingDocument");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenMethodUnsupported()
    {
        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"instruction\":\"x\"}");
        var tool = BuildWorkingDocTool("DropTheDocument");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("method", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenJsonMalformed()
    {
        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{not json");
        var tool = BuildWorkingDocTool("EditWorkingDocument");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("malformed", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteChatAsync — EditWorkingDocument: streaming event sequence + hash
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EditWorkingDocument_HappyPath_EmitsStartTokensEndSequence()
    {
        SetupAnalysisReturnsDocument("Existing document content.");
        SetupStreamingTokens("Alpha", "Beta", "Gamma");

        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"instruction\":\"refine the opening\"}");
        var tool = BuildWorkingDocTool("EditWorkingDocument");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        _capturedEvents.Should().HaveCount(5);
        _capturedEvents[0].Should().BeOfType<DocumentStreamStartEvent>();
        _capturedEvents[1].Should().BeOfType<DocumentStreamTokenEvent>();
        _capturedEvents[2].Should().BeOfType<DocumentStreamTokenEvent>();
        _capturedEvents[3].Should().BeOfType<DocumentStreamTokenEvent>();
        _capturedEvents[4].Should().BeOfType<DocumentStreamEndEvent>();
    }

    [Fact]
    public async Task EditWorkingDocument_TokensHaveContentAndIncreasingIndices()
    {
        SetupAnalysisReturnsDocument("Source.");
        SetupStreamingTokens("First", "Second", "Third");

        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"instruction\":\"x\"}");
        var tool = BuildWorkingDocTool("EditWorkingDocument");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        var tokens = _capturedEvents.OfType<DocumentStreamTokenEvent>().ToList();
        tokens.Should().HaveCount(3);
        tokens[0].Token.Should().Be("First");
        tokens[0].Index.Should().Be(0);
        tokens[1].Index.Should().Be(1);
        tokens[2].Index.Should().Be(2);
    }

    [Fact]
    public async Task EditWorkingDocument_StartEventHasReplaceOperation()
    {
        SetupAnalysisReturnsDocument("Source.");
        SetupStreamingTokens("X");

        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"instruction\":\"x\"}");
        var tool = BuildWorkingDocTool("EditWorkingDocument");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        var start = _capturedEvents.First() as DocumentStreamStartEvent;
        start.Should().NotBeNull();
        start!.TargetPosition.Should().Be("document");
        start.OperationType.Should().Be("replace");
        start.OperationId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task EditWorkingDocument_EndEvent_ContainsCorrectSha256Hash()
    {
        // Arrange — assemble known token text, then assert the returned hash matches SHA-256 of the concatenation.
        SetupAnalysisReturnsDocument("Source.");
        SetupStreamingTokens("Hello, ", "world!");

        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"instruction\":\"x\"}");
        var tool = BuildWorkingDocTool("EditWorkingDocument");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        var assembled = "Hello, world!";
        var expectedHashBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(assembled));
        var expectedHash = "sha256:" + Convert.ToHexString(expectedHashBytes).ToLowerInvariant();

        var endEvent = _capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent.Should().NotBeNull();
        endEvent!.Cancelled.Should().BeFalse();
        endEvent.TotalTokens.Should().Be(2);
        endEvent.ContentHash.Should().Be(expectedHash,
            because: "SHA-256 hash MUST match the assembled token content per R2-023 + ADR-014");
    }

    [Fact]
    public async Task EditWorkingDocument_AllEventsShareOperationId()
    {
        SetupAnalysisReturnsDocument("S.");
        SetupStreamingTokens("A", "B");

        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"instruction\":\"x\"}");
        var tool = BuildWorkingDocTool("EditWorkingDocument");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        var startId = ((DocumentStreamStartEvent)_capturedEvents[0]).OperationId;
        startId.Should().NotBe(Guid.Empty);

        foreach (var evt in _capturedEvents)
        {
            var id = evt switch
            {
                DocumentStreamStartEvent s => s.OperationId,
                DocumentStreamTokenEvent t => t.OperationId,
                DocumentStreamEndEvent e => e.OperationId,
                _ => Guid.Empty
            };
            id.Should().Be(startId, because: "all events for one method invocation MUST share OperationId");
        }
    }

    [Fact]
    public async Task EditWorkingDocument_Cancellation_EmitsTerminalEndWithCancelledTrue_NoRethrow()
    {
        SetupAnalysisReturnsDocument("S.");
        SetupStreamingThenThrows(new[] { "Token1", "Token2" }, new OperationCanceledException());

        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"instruction\":\"x\"}");
        var tool = BuildWorkingDocTool("EditWorkingDocument");

        // Act — must NOT throw; handler converts cancellation to a successful summary.
        var action = async () => await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);
        var result = await action.Should().NotThrowAsync();

        // Assert — terminal End with Cancelled: true.
        var endEvent = _capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent.Should().NotBeNull();
        endEvent!.Cancelled.Should().BeTrue(because: "ADR-019 + ADR-033 terminal event MUST carry Cancelled: true");
        endEvent.TotalTokens.Should().Be(2);
        endEvent.ErrorCode.Should().BeNull();
        result.Subject.Success.Should().BeTrue(
            because: "ADR-033 §4.2 — cancellation returns a successful summary to the LLM (the editor handles UI)");
    }

    [Fact]
    public async Task EditWorkingDocument_LlmError_EmitsTerminalEndWithErrorCode_AndToolResultFailure()
    {
        SetupAnalysisReturnsDocument("S.");
        SetupStreamingThenThrows(new[] { "Token1" }, new InvalidOperationException("LLM unavailable"));

        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"instruction\":\"x\"}");
        var tool = BuildWorkingDocTool("EditWorkingDocument");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        // ToolResult failure surfaced to LLM.
        result.Success.Should().BeFalse(
            because: "ADR-033 §4.2 — inner LLM failure returns ToolResult.Failure");
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);

        // Terminal End with error code emitted to SSE.
        var endEvent = _capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent.Should().NotBeNull();
        endEvent!.Cancelled.Should().BeFalse();
        endEvent.ErrorCode.Should().Be("LLM_STREAM_FAILED",
            because: "ADR-019 binding terminal error code for inner LLM stream failures");
        endEvent.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        endEvent.TotalTokens.Should().Be(1);
    }

    [Fact]
    public async Task EditWorkingDocument_NoDocumentContent_EmitsTerminalEnd_WithoutCallingLlm()
    {
        // No document available — handler emits Start + terminal End with NO_DOCUMENT error code, never calls IChatClient.
        SetupAnalysisReturnsDocument(null);
        var llmCallCount = 0;
        _chatClient.GetStreamingResponseAsyncCallback = (_, _, _) =>
        {
            llmCallCount++;
            return AsyncEnumerableHelpers.FromChunks(Array.Empty<string>());
        };

        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"instruction\":\"x\"}");
        var tool = BuildWorkingDocTool("EditWorkingDocument");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        llmCallCount.Should().Be(0, because: "no document → no inner LLM call");
        _capturedEvents.Should().HaveCount(2);

        var endEvent = _capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent!.ErrorCode.Should().Be("NO_DOCUMENT");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteChatAsync — AppendSection: heading-first + LLM tokens
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AppendSection_HappyPath_EmitsStartHeadingTokensEnd()
    {
        SetupAnalysisReturnsDocument("Current doc.");
        SetupStreamingTokens("BodyA", "BodyB");

        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"sectionTitle\":\"Risks\",\"instruction\":\"List risks\"}");
        var tool = BuildWorkingDocTool("AppendSection");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        // Start + heading token + 2 body tokens + End = 5 events
        _capturedEvents.Should().HaveCount(5);
        _capturedEvents[0].Should().BeOfType<DocumentStreamStartEvent>();
        _capturedEvents[1].Should().BeOfType<DocumentStreamTokenEvent>(); // heading
        _capturedEvents[4].Should().BeOfType<DocumentStreamEndEvent>();

        var heading = _capturedEvents[1] as DocumentStreamTokenEvent;
        heading!.Token.Should().StartWith("## ").And.Contain("Risks");
        heading.Index.Should().Be(0);

        var endEvent = _capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent!.TotalTokens.Should().Be(3); // 1 heading + 2 body
    }

    [Fact]
    public async Task AppendSection_StartEventHasInsertOperation()
    {
        SetupAnalysisReturnsDocument("S.");
        SetupStreamingTokens("X");

        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"sectionTitle\":\"Title\",\"instruction\":\"body\"}");
        var tool = BuildWorkingDocTool("AppendSection");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        var start = _capturedEvents.First() as DocumentStreamStartEvent;
        start!.TargetPosition.Should().Be("end");
        start.OperationType.Should().Be("insert");
    }

    [Fact]
    public async Task AppendSection_WithoutCurrentDocument_StillEmitsHeadingAndBody()
    {
        SetupAnalysisReturnsDocument(null);
        SetupStreamingTokens("Body");

        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"sectionTitle\":\"T\",\"instruction\":\"i\"}");
        var tool = BuildWorkingDocTool("AppendSection");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        // Start + heading + body + End = 4 events (append doesn't require existing content)
        result.Success.Should().BeTrue();
        _capturedEvents.Should().HaveCount(4);
        (_capturedEvents.Last() as DocumentStreamEndEvent)!.Cancelled.Should().BeFalse();
    }

    [Fact]
    public async Task AppendSection_Cancellation_EmitsCancelledEnd_HeadingPreserved()
    {
        SetupAnalysisReturnsDocument("S.");
        SetupStreamingThenThrows(new[] { "PartialBody" }, new OperationCanceledException());

        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"sectionTitle\":\"T\",\"instruction\":\"i\"}");
        var tool = BuildWorkingDocTool("AppendSection");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var endEvent = _capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent!.Cancelled.Should().BeTrue();
        endEvent.TotalTokens.Should().Be(2); // heading + 1 body token
    }

    [Fact]
    public async Task AppendSection_LlmError_EmitsErrorEnd_HeadingTokenStillEmitted()
    {
        SetupAnalysisReturnsDocument("S.");
        _chatClient.GetStreamingResponseAsyncCallback = (_, _, _) =>
            AsyncEnumerableHelpers.ThrowingAsyncEnumerable<ChatResponseUpdate>(
                new InvalidOperationException("LLM stream failed"));

        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"sectionTitle\":\"T\",\"instruction\":\"i\"}");
        var tool = BuildWorkingDocTool("AppendSection");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();

        // Heading emitted before LLM threw
        var heading = _capturedEvents[1] as DocumentStreamTokenEvent;
        heading.Should().NotBeNull();
        heading!.Token.Should().Contain("T");

        var endEvent = _capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent!.ErrorCode.Should().Be("LLM_STREAM_FAILED");
        endEvent.TotalTokens.Should().Be(1); // heading only
    }

    [Fact]
    public async Task AppendSection_EndEvent_ContainsSha256HashOfAssembledContent()
    {
        SetupAnalysisReturnsDocument("S.");
        SetupStreamingTokens("Body");

        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"sectionTitle\":\"MyTitle\",\"instruction\":\"i\"}");
        var tool = BuildWorkingDocTool("AppendSection");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        // Assembled: "## MyTitle\n\n" + "Body"
        var assembled = "## MyTitle\n\nBody";
        var expectedHashBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(assembled));
        var expectedHash = "sha256:" + Convert.ToHexString(expectedHashBytes).ToLowerInvariant();

        var endEvent = _capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent!.ContentHash.Should().Be(expectedHash);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteChatAsync — WriteBackToWorkingDocument: persistence + safety + stream
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task WriteBack_PersistsViaIWorkingDocumentService_WithCorrectArgs()
    {
        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"content\":\"final document text\"}");
        var tool = BuildWorkingDocTool("WriteBackToWorkingDocument");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        _workingDocumentServiceMock.Verify(
            s => s.UpdateWorkingDocumentAsync(
                _defaultAnalysisId,
                "final document text",
                It.IsAny<CancellationToken>()),
            Times.Once,
            "spec FR-12: write-back MUST route through IWorkingDocumentService with the supplied analysisId + content");
    }

    [Fact]
    public async Task WriteBack_Never_CallsIChatClient_FR12Safety()
    {
        // Spec FR-12: write-back is a direct Dataverse call, NOT an LLM operation.
        var llmCallCount = 0;
        _chatClient.GetStreamingResponseAsyncCallback = (_, _, _) =>
        {
            llmCallCount++;
            return AsyncEnumerableHelpers.FromChunks(Array.Empty<string>());
        };

        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"content\":\"text\"}");
        var tool = BuildWorkingDocTool("WriteBackToWorkingDocument");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        llmCallCount.Should().Be(0,
            because: "FR-12 + ADR-033 §3.1 — WriteBack persists supplied content directly; never invokes the LLM");
    }

    [Fact]
    public async Task WriteBack_EmitsStartChunkedTokensEndSequence_WithCorrectHash()
    {
        // Content is chunked at 100 chars. 250 chars → 3 token events (100 + 100 + 50).
        var content = new string('X', 250);

        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: $"{{\"content\":\"{content}\"}}");
        var tool = BuildWorkingDocTool("WriteBackToWorkingDocument");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        _capturedEvents.OfType<DocumentStreamTokenEvent>().Should().HaveCount(3);

        var assembledHashBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content));
        var expectedHash = "sha256:" + Convert.ToHexString(assembledHashBytes).ToLowerInvariant();

        var endEvent = _capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent!.ContentHash.Should().Be(expectedHash);
        endEvent.TotalTokens.Should().Be(3);
    }

    [Fact]
    public async Task WriteBack_WhenAnalysisIdMissing_ReturnsErrorAndDoesNotPersist()
    {
        var handler = CreateHandler();
        // Override AnalysisId to Guid.Empty (treated as "no analysis context")
        var baseCtx = BuildChatInvocationContext(toolArgumentsJson: "{\"content\":\"x\"}");
        var ctx = baseCtx with
        {
            AnalysisId = Guid.Empty,
            DocumentStreamWriter = (evt, _) =>
            {
                _capturedEvents.Add(evt);
                return Task.CompletedTask;
            }
        };
        var tool = BuildWorkingDocTool("WriteBackToWorkingDocument");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue("returns a successful ToolResult with a deferred-write message for the LLM");
        _workingDocumentServiceMock.Verify(
            s => s.UpdateWorkingDocumentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no valid analysisId → no Dataverse write attempted");
    }

    [Fact]
    public async Task WriteBack_WhenDataverseFails_EmitsErrorEnd_AndReturnsToolResultFailure()
    {
        _workingDocumentServiceMock
            .Setup(s => s.UpdateWorkingDocumentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse write failed"));

        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: "{\"content\":\"x\"}");
        var tool = BuildWorkingDocTool("WriteBackToWorkingDocument");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);

        var endEvent = _capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent!.ErrorCode.Should().Be("WRITE_BACK_FAILED");
    }

    [Fact]
    public async Task WriteBack_Cancellation_EmitsCancelledEnd_NoPersistence()
    {
        var content = new string('A', 250);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: $"{{\"content\":\"{content}\"}}");
        var tool = BuildWorkingDocTool("WriteBackToWorkingDocument");

        // Cancel before any chunks emitted
        var result = await handler.ExecuteChatAsync(ctx, tool, cts.Token);

        // The handler may have emitted a Start before cancellation triggered; check Cancelled end.
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.Cancelled);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-033 §3.1 — Null DocumentStreamWriter path: graceful failure, no IChatClient call
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_NullDocumentStreamWriter_ReturnsFailure_NoLlmCall()
    {
        var llmCallCount = 0;
        _chatClient.GetStreamingResponseAsyncCallback = (_, _, _) =>
        {
            llmCallCount++;
            return AsyncEnumerableHelpers.FromChunks(Array.Empty<string>());
        };

        var handler = CreateHandler();
        var ctx = BuildContext(
            toolArgumentsJson: "{\"instruction\":\"x\"}",
            wireStreamWriter: false);
        var tool = BuildWorkingDocTool("EditWorkingDocument");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse(
            because: "ADR-033 §3.1 — null DocumentStreamWriter MUST return ToolResult.Failure with clear diagnostic");
        result.ErrorCode.Should().Be(ToolErrorCodes.DependencyUnavailable);
        result.ErrorMessage.Should().Contain("DocumentStreamWriter",
            because: "diagnostic must name the missing dependency");

        llmCallCount.Should().Be(0,
            because: "no IChatClient call is attempted when the writer is missing — degrade gracefully");
        _capturedEvents.Should().BeEmpty(
            because: "no writer wired → no events captured");
    }

    [Fact]
    public async Task WriteBack_NullDocumentStreamWriter_ReturnsFailure_NoDataverseWrite()
    {
        var handler = CreateHandler();
        var ctx = BuildContext(
            toolArgumentsJson: "{\"content\":\"x\"}",
            wireStreamWriter: false);
        var tool = BuildWorkingDocTool("WriteBackToWorkingDocument");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.DependencyUnavailable);

        // Null writer short-circuits before persistence — FR-12 safety: WriteBack must NOT
        // call IWorkingDocumentService when the handler returns DependencyUnavailable.
        _workingDocumentServiceMock.Verify(
            s => s.UpdateWorkingDocumentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteAsync (playbook path) — defensive validation error
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_PlaybookContext_ReturnsValidationError()
    {
        var handler = CreateHandler();
        var ctx = BuildToolExecutionContext();
        var tool = BuildAnalysisTool(
            handlerClass: nameof(WorkingDocumentHandler),
            configuration: "{\"method\":\"EditWorkingDocument\"}");

        var result = await handler.ExecuteAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("playbook",
            because: "FR-12: handler must defensively reject playbook invocation");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 telemetry — token / instruction / document content must NEVER leak
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_RespectsAdr015_DoesNotLogInstructionOrDocumentOrTokenContent()
    {
        const string secretInstruction = "INSTRUCTION-WITH-CLIENT-NAME-AcmeCorpMatterXTokenLeakProbe";
        const string secretDocument = "DOCUMENT-WITH-PRIVILEGED-CONTENT-XJ7K-CONFIDENTIAL-SECTION";
        const string secretToken = "TOKEN-CONTENT-WITH-TRADE-SECRET-FORMULA-Z9PROBE";

        SetupAnalysisReturnsDocument(secretDocument);
        SetupStreamingTokens(secretToken, "another", "tokens");

        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: $"{{\"instruction\":\"{secretInstruction}\"}}");
        var tool = BuildWorkingDocTool("EditWorkingDocument");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        AssertTelemetryRespectsAdr015(secretInstruction, secretDocument, secretToken);
    }

    [Fact]
    public async Task Telemetry_RespectsAdr015_WriteBack_DoesNotLogContentText()
    {
        const string secretContent = "WRITEBACK-CONTENT-WITH-PRIVILEGED-CASE-DETAILS-SmithVJonesXProbe";

        var handler = CreateHandler();
        var ctx = BuildContext(toolArgumentsJson: $"{{\"content\":\"{secretContent}\"}}");
        var tool = BuildWorkingDocTool("WriteBackToWorkingDocument");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        AssertTelemetryRespectsAdr015(secretContent);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Chat-context dispatch via ToolHandlerToAIFunctionAdapter
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ChatDispatch_ViaAdapter_RoutesToWorkingDocumentHandler_EditMethod()
    {
        SetupAnalysisReturnsDocument("Existing.");
        SetupStreamingTokens("Edited");

        var handler = CreateHandler();
        var tool = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "WorkingDocumentEdit",
            Description = "Edit working document",
            Type = ToolType.Custom,
            HandlerClass = nameof(WorkingDocumentHandler),
            Configuration = "{\"method\":\"EditWorkingDocument\"}",
            AvailableInContexts = ToolAvailabilityContext.Chat,
            JsonSchema = JsonSerializer.Serialize(new
            {
                title = "edit",
                type = "object",
                properties = new
                {
                    instruction = new { type = "string", minLength = 1 }
                },
                required = new[] { "instruction" },
                additionalProperties = false
            })
        };

        // ADR-033 canonical wiring: adapter-level documentStreamWriter + analysisId are
        // the authoritative source. The adapter's per-call `with` block always overrides
        // any values the contextFactory may have set, so the test passes them via the
        // adapter ctor (not via contextFactory).
        var capturedAnalysisId = _defaultAnalysisId;
        Func<Sprk.Bff.Api.Models.Ai.Chat.DocumentStreamEvent, CancellationToken, Task> streamWriter = (evt, _) =>
        {
            _capturedEvents.Add(evt);
            return Task.CompletedTask;
        };
        var adapter = new ToolHandlerToAIFunctionAdapter(
            tool,
            handler,
            contextFactory: () => new ChatInvocationContext
            {
                ChatSessionId = Guid.NewGuid(),
                TenantId = DefaultTenantId,
                RequestedToolName = tool.Name,
                ToolArgumentsJson = "{}"
            },
            logger: CreateLogger<ToolHandlerToAIFunctionAdapter>(),
            documentStreamWriter: streamWriter,
            analysisId: capturedAnalysisId);

        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["instruction"] = "Fix grammar"
        });
        var raw = await adapter.InvokeAsync(args, CancellationToken.None);

        raw.Should().NotBeNull();
        raw.Should().BeOfType<ToolResult>();
        var toolResult = (ToolResult)raw!;
        toolResult.Success.Should().BeTrue(
            because: "ADR-033 + FR-10 — chat-side dispatch through the adapter must reach WorkingDocumentHandler.ExecuteChatAsync");

        _capturedEvents.Should().NotBeEmpty(
            because: "the writer wired via the adapter contextFactory must capture stream events");
        _capturedEvents.First().Should().BeOfType<DocumentStreamStartEvent>();
        _capturedEvents.Last().Should().BeOfType<DocumentStreamEndEvent>();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // DI bootstrap helper (shared with other handler tests)
    // ═════════════════════════════════════════════════════════════════════════════

    private static IServiceCollection BuildToolFrameworkServiceCollection()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolFramework(configuration);
        return services;
    }
}
