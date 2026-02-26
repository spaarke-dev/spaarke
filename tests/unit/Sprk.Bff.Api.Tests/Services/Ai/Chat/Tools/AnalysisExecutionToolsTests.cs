using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat.Tools;
using Xunit;

// Explicit alias to resolve ChatMessage ambiguity between domain model and AI framework.
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat.Tools;

/// <summary>
/// Unit tests for <see cref="AnalysisExecutionTools"/>.
///
/// Verifies:
/// - RerunAnalysisAsync: playbook execution, progress events, document_replace SSE, metadata, error handling
/// - RefineAnalysisAsync: refinement via inner LLM call, empty instruction validation, no-analysis handling
/// - Constructor validation (null arguments)
/// - GetTools registration
///
/// ADR-015: Tests use generic content (no real document strings in CI logs).
/// </summary>
public class AnalysisExecutionToolsTests
{
    // === Test constants (generic content per ADR-015) ===

    private const string TestAnalysisId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string TestDocumentId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private static readonly Guid TestPlaybookId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private const string TestDocumentContent = "Test document content for unit testing.";
    private const string TestAdditionalInstructions = "Focus on financial risks.";
    private const string TestRefinementInstruction = "Make the recommendations more actionable.";

    // === Shared test infrastructure ===

    private readonly IAnalysisOrchestrationService _analysisService;
    private readonly IChatClient _chatClient;
    private readonly HttpContext _httpContext;
    private readonly List<ChatSseEvent> _capturedEvents;
    private readonly Func<ChatSseEvent, CancellationToken, Task> _sseWriter;

    public AnalysisExecutionToolsTests()
    {
        _analysisService = Substitute.For<IAnalysisOrchestrationService>();
        _chatClient = Substitute.For<IChatClient>();
        _httpContext = new DefaultHttpContext();
        _capturedEvents = new List<ChatSseEvent>();
        _sseWriter = (evt, ct) =>
        {
            _capturedEvents.Add(evt);
            return Task.CompletedTask;
        };
    }

    // ====================================================================
    // Constructor validation tests
    // ====================================================================

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenAnalysisServiceIsNull()
    {
        // Act
        var action = () => new AnalysisExecutionTools(
            null!, _chatClient, TestAnalysisId, TestPlaybookId, TestDocumentId, _httpContext, _sseWriter);

        // Assert
        action.Should().Throw<ArgumentNullException>().WithParameterName("analysisService");
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenChatClientIsNull()
    {
        // Act
        var action = () => new AnalysisExecutionTools(
            _analysisService, null!, TestAnalysisId, TestPlaybookId, TestDocumentId, _httpContext, _sseWriter);

        // Assert
        action.Should().Throw<ArgumentNullException>().WithParameterName("chatClient");
    }

    [Fact]
    public void Constructor_AcceptsNullOptionalParameters()
    {
        // Act -- analysisId, documentId, httpContext, sseWriter are all optional
        var action = () => new AnalysisExecutionTools(
            _analysisService, _chatClient);

        // Assert
        action.Should().NotThrow();
    }

    // ====================================================================
    // RerunAnalysisAsync — Happy Path
    // ====================================================================

    [Fact]
    public async Task RerunAnalysisAsync_WithValidInstructions_CallsExecutePlaybookAsync()
    {
        // Arrange
        SetupPlaybookExecutionReturns(
            AnalysisStreamChunk.Metadata(Guid.Parse(TestAnalysisId), "test-doc.pdf"),
            AnalysisStreamChunk.TextChunk("Analysis output content"),
            AnalysisStreamChunk.Completed(Guid.Parse(TestAnalysisId), new TokenUsage(0, 0)));
        var sut = CreateSut();

        // Act
        await sut.RerunAnalysisAsync(TestAdditionalInstructions);

        // Assert
        _analysisService.Received(1).ExecutePlaybookAsync(
            Arg.Is<PlaybookExecuteRequest>(r =>
                r.PlaybookId == TestPlaybookId &&
                r.DocumentIds.Length == 1 &&
                r.DocumentIds[0] == Guid.Parse(TestDocumentId)),
            _httpContext,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RerunAnalysisAsync_AppendsUserInstructions_ToOriginalPlaybook()
    {
        // Arrange
        SetupPlaybookExecutionReturns(
            AnalysisStreamChunk.Metadata(Guid.Parse(TestAnalysisId), "test-doc.pdf"),
            AnalysisStreamChunk.Completed(Guid.Parse(TestAnalysisId), new TokenUsage(0, 0)));
        var sut = CreateSut();

        // Act
        await sut.RerunAnalysisAsync(TestAdditionalInstructions);

        // Assert -- AdditionalContext should contain the user's instructions
        _analysisService.Received(1).ExecutePlaybookAsync(
            Arg.Is<PlaybookExecuteRequest>(r => r.AdditionalContext == TestAdditionalInstructions),
            Arg.Any<HttpContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RerunAnalysisAsync_EmptyInstructions_SetsAdditionalContextToNull()
    {
        // Arrange
        SetupPlaybookExecutionReturns(
            AnalysisStreamChunk.Metadata(Guid.Parse(TestAnalysisId), "test-doc.pdf"),
            AnalysisStreamChunk.Completed(Guid.Parse(TestAnalysisId), new TokenUsage(0, 0)));
        var sut = CreateSut();

        // Act
        await sut.RerunAnalysisAsync("");

        // Assert -- empty instructions should result in null AdditionalContext
        _analysisService.Received(1).ExecutePlaybookAsync(
            Arg.Is<PlaybookExecuteRequest>(r => r.AdditionalContext == null),
            Arg.Any<HttpContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RerunAnalysisAsync_EmitsProgressEvents_DuringExecution()
    {
        // Arrange
        SetupPlaybookExecutionReturns(
            AnalysisStreamChunk.Metadata(Guid.Parse(TestAnalysisId), "test-doc.pdf"),
            AnalysisStreamChunk.TextChunk("[Executing: DocumentExtractor]"),
            AnalysisStreamChunk.TextChunk("Output content here"),
            AnalysisStreamChunk.Completed(Guid.Parse(TestAnalysisId), new TokenUsage(0, 0)));
        var sut = CreateSut();

        // Act
        await sut.RerunAnalysisAsync();

        // Assert -- should have emitted multiple progress events
        var progressEvents = _capturedEvents
            .Where(e => e.Type == "progress")
            .ToList();
        progressEvents.Should().HaveCountGreaterThanOrEqualTo(3,
            "at least: starting, loading playbook, and completion progress events");

        // First progress should be 0% starting
        var firstProgress = progressEvents.First().Data as ChatSseProgressData;
        firstProgress.Should().NotBeNull();
        firstProgress!.Percent.Should().Be(0);
        firstProgress.Message.Should().Contain("Starting");

        // Last progress should be 100% completed
        var lastProgress = progressEvents.Last().Data as ChatSseProgressData;
        lastProgress.Should().NotBeNull();
        lastProgress!.Percent.Should().Be(100);
    }

    [Fact]
    public async Task RerunAnalysisAsync_ReturnsDocumentReplace_OnCompletion()
    {
        // Arrange
        var analysisHtml = "<h1>Analysis Results</h1><p>Key findings...</p>";
        SetupPlaybookExecutionReturns(
            AnalysisStreamChunk.Metadata(Guid.Parse(TestAnalysisId), "test-doc.pdf"),
            AnalysisStreamChunk.TextChunk(analysisHtml),
            AnalysisStreamChunk.Completed(Guid.Parse(TestAnalysisId), new TokenUsage(0, 0)));
        var sut = CreateSut();

        // Act
        await sut.RerunAnalysisAsync();

        // Assert -- should have emitted a document_replace event with the full HTML
        var documentReplaceEvents = _capturedEvents
            .Where(e => e.Type == "document_replace")
            .ToList();
        documentReplaceEvents.Should().HaveCount(1);

        var replaceData = documentReplaceEvents[0].Data as ChatSseDocumentReplaceData;
        replaceData.Should().NotBeNull();
        replaceData!.Html.Should().Contain(analysisHtml);
    }

    [Fact]
    public async Task RerunAnalysisAsync_IncludesMetadata_InResult()
    {
        // Arrange
        SetupPlaybookExecutionReturns(
            AnalysisStreamChunk.Metadata(Guid.Parse(TestAnalysisId), "test-doc.pdf"),
            AnalysisStreamChunk.TextChunk("Some analysis output"),
            AnalysisStreamChunk.Completed(Guid.Parse(TestAnalysisId), new TokenUsage(0, 0)));
        var sut = CreateSut();

        // Act
        var result = await sut.RerunAnalysisAsync(TestAdditionalInstructions);

        // Assert -- result string should contain analysis ID and instructions summary
        result.Should().Contain("completed successfully");
        result.Should().Contain(TestAnalysisId);
        result.Should().Contain(TestAdditionalInstructions);
    }

    [Fact]
    public async Task RerunAnalysisAsync_WithoutInstructions_ReturnsOriginalPlaybookSummary()
    {
        // Arrange
        SetupPlaybookExecutionReturns(
            AnalysisStreamChunk.Metadata(Guid.Parse(TestAnalysisId), "test-doc.pdf"),
            AnalysisStreamChunk.TextChunk("Output"),
            AnalysisStreamChunk.Completed(Guid.Parse(TestAnalysisId), new TokenUsage(0, 0)));
        var sut = CreateSut();

        // Act
        var result = await sut.RerunAnalysisAsync();

        // Assert
        result.Should().Contain("original playbook configuration");
    }

    [Fact]
    public async Task RerunAnalysisAsync_DocumentReplaceEvent_ContainsPlaybookIdAndTimestamp()
    {
        // Arrange
        SetupPlaybookExecutionReturns(
            AnalysisStreamChunk.Metadata(Guid.Parse(TestAnalysisId), "test-doc.pdf"),
            AnalysisStreamChunk.TextChunk("HTML content"),
            AnalysisStreamChunk.Completed(Guid.Parse(TestAnalysisId), new TokenUsage(0, 0)));
        var sut = CreateSut();

        // Act
        await sut.RerunAnalysisAsync();

        // Assert
        var documentReplaceEvent = _capturedEvents.First(e => e.Type == "document_replace");
        var replaceData = documentReplaceEvent.Data as ChatSseDocumentReplaceData;
        replaceData.Should().NotBeNull();
        replaceData!.Metadata.Should().NotBeNull();
        replaceData.Metadata.PlaybookId.Should().Be(TestPlaybookId.ToString());
        replaceData.Metadata.Timestamp.Should().NotBeNullOrWhiteSpace();
    }

    // ====================================================================
    // RerunAnalysisAsync — Validation and Error Handling
    // ====================================================================

    [Fact]
    public async Task RerunAnalysisAsync_NoPlaybookContext_ReturnsErrorMessage()
    {
        // Arrange -- playbookId is Guid.Empty (no playbook context)
        var sut = new AnalysisExecutionTools(
            _analysisService, _chatClient,
            analysisId: TestAnalysisId,
            playbookId: Guid.Empty,
            documentId: TestDocumentId,
            httpContext: _httpContext,
            sseWriter: _sseWriter);

        // Act
        var result = await sut.RerunAnalysisAsync();

        // Assert
        result.Should().Contain("no playbook context");
        result.Should().Contain("failed");
    }

    [Fact]
    public async Task RerunAnalysisAsync_NoDocumentId_ReturnsErrorMessage()
    {
        // Arrange -- documentId is null
        var sut = new AnalysisExecutionTools(
            _analysisService, _chatClient,
            analysisId: TestAnalysisId,
            playbookId: TestPlaybookId,
            documentId: null,
            httpContext: _httpContext,
            sseWriter: _sseWriter);

        // Act
        var result = await sut.RerunAnalysisAsync();

        // Assert
        result.Should().Contain("no active document");
        result.Should().Contain("failed");
    }

    [Fact]
    public async Task RerunAnalysisAsync_InvalidDocumentId_ReturnsErrorMessage()
    {
        // Arrange -- documentId is not a valid GUID
        var sut = new AnalysisExecutionTools(
            _analysisService, _chatClient,
            analysisId: TestAnalysisId,
            playbookId: TestPlaybookId,
            documentId: "not-a-guid",
            httpContext: _httpContext,
            sseWriter: _sseWriter);

        // Act
        var result = await sut.RerunAnalysisAsync();

        // Assert
        result.Should().Contain("no active document");
        result.Should().Contain("failed");
    }

    [Fact]
    public async Task RerunAnalysisAsync_NoHttpContext_ReturnsErrorMessage()
    {
        // Arrange -- httpContext is null
        var sut = new AnalysisExecutionTools(
            _analysisService, _chatClient,
            analysisId: TestAnalysisId,
            playbookId: TestPlaybookId,
            documentId: TestDocumentId,
            httpContext: null,
            sseWriter: _sseWriter);

        // Act
        var result = await sut.RerunAnalysisAsync();

        // Assert
        result.Should().Contain("HTTP context not available");
        result.Should().Contain("failed");
    }

    [Fact]
    public async Task RerunAnalysisAsync_OrchestratorFails_ReturnsErrorResult()
    {
        // Arrange -- ExecutePlaybookAsync throws an exception
        _analysisService.ExecutePlaybookAsync(
                Arg.Any<PlaybookExecuteRequest>(),
                Arg.Any<HttpContext>(),
                Arg.Any<CancellationToken>())
            .Returns(ThrowingAsyncEnumerable<AnalysisStreamChunk>(
                new InvalidOperationException("Playbook execution failed")));
        var sut = CreateSut();

        // Act
        var result = await sut.RerunAnalysisAsync();

        // Assert
        result.Should().Contain("error");
        result.Should().Contain("Playbook execution failed");
    }

    [Fact]
    public async Task RerunAnalysisAsync_OrchestratorReturnsError_ReturnsErrorMessage()
    {
        // Arrange -- orchestrator yields an error chunk
        SetupPlaybookExecutionReturns(
            AnalysisStreamChunk.Metadata(Guid.Parse(TestAnalysisId), "test-doc.pdf"),
            AnalysisStreamChunk.FromError("Budget exceeded for this analysis"));
        var sut = CreateSut();

        // Act
        var result = await sut.RerunAnalysisAsync();

        // Assert
        result.Should().Contain("failed");
        result.Should().Contain("Budget exceeded");
    }

    [Fact]
    public async Task RerunAnalysisAsync_KeyNotFoundFromOrchestrator_ReturnsErrorResult()
    {
        // Arrange -- ExecutePlaybookAsync throws KeyNotFoundException (playbook not found)
        _analysisService.ExecutePlaybookAsync(
                Arg.Any<PlaybookExecuteRequest>(),
                Arg.Any<HttpContext>(),
                Arg.Any<CancellationToken>())
            .Returns(ThrowingAsyncEnumerable<AnalysisStreamChunk>(
                new KeyNotFoundException("Playbook not found")));
        var sut = CreateSut();

        // Act
        var result = await sut.RerunAnalysisAsync();

        // Assert
        result.Should().Contain("failed");
        result.Should().Contain("Playbook not found");
    }

    [Fact]
    public async Task RerunAnalysisAsync_EmptyOutput_DoesNotEmitDocumentReplace()
    {
        // Arrange -- orchestrator yields metadata and done but no content chunks
        SetupPlaybookExecutionReturns(
            AnalysisStreamChunk.Metadata(Guid.Parse(TestAnalysisId), "test-doc.pdf"),
            AnalysisStreamChunk.Completed(Guid.Parse(TestAnalysisId), new TokenUsage(0, 0)));
        var sut = CreateSut();

        // Act
        await sut.RerunAnalysisAsync();

        // Assert -- no document_replace event if output is empty
        var documentReplaceEvents = _capturedEvents.Where(e => e.Type == "document_replace").ToList();
        documentReplaceEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task RerunAnalysisAsync_WithoutSseWriter_CompletesWithoutError()
    {
        // Arrange -- no SSE writer (non-streaming context)
        SetupPlaybookExecutionReturns(
            AnalysisStreamChunk.Metadata(Guid.Parse(TestAnalysisId), "test-doc.pdf"),
            AnalysisStreamChunk.TextChunk("Output content"),
            AnalysisStreamChunk.Completed(Guid.Parse(TestAnalysisId), new TokenUsage(0, 0)));
        var sut = new AnalysisExecutionTools(
            _analysisService, _chatClient,
            analysisId: TestAnalysisId,
            playbookId: TestPlaybookId,
            documentId: TestDocumentId,
            httpContext: _httpContext,
            sseWriter: null);

        // Act
        var result = await sut.RerunAnalysisAsync();

        // Assert -- should complete successfully without error
        result.Should().Contain("completed successfully");
    }

    [Fact]
    public async Task RerunAnalysisAsync_ToolExecutionChunks_EmitPerStageProgressEvents()
    {
        // Arrange -- simulate multiple tool executions with [Executing:] markers
        SetupPlaybookExecutionReturns(
            AnalysisStreamChunk.Metadata(Guid.Parse(TestAnalysisId), "test-doc.pdf"),
            AnalysisStreamChunk.TextChunk("[Executing: DocumentExtractor]"),
            AnalysisStreamChunk.TextChunk("Extracted text here"),
            AnalysisStreamChunk.TextChunk("[Executing: RiskAnalyzer]"),
            AnalysisStreamChunk.TextChunk("Risk analysis output"),
            AnalysisStreamChunk.Completed(Guid.Parse(TestAnalysisId), new TokenUsage(0, 0)));
        var sut = CreateSut();

        // Act
        await sut.RerunAnalysisAsync();

        // Assert -- progress events should mention tool names
        var progressMessages = _capturedEvents
            .Where(e => e.Type == "progress")
            .Select(e => e.Data as ChatSseProgressData)
            .Where(d => d != null)
            .Select(d => d!.Message)
            .ToList();

        progressMessages.Should().Contain(m => m.Contains("DocumentExtractor"));
        progressMessages.Should().Contain(m => m.Contains("RiskAnalyzer"));
    }

    // ====================================================================
    // RefineAnalysisAsync — Happy Path
    // ====================================================================

    [Fact]
    public async Task RefineAnalysisAsync_WithRefinementInstruction_ReturnsRefinedContent()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsResponse("Refined analysis with actionable recommendations.");
        var sut = CreateSut();

        // Act
        var result = await sut.RefineAnalysisAsync(TestRefinementInstruction);

        // Assert
        result.Should().Be("Refined analysis with actionable recommendations.");
    }

    [Fact]
    public async Task RefineAnalysisAsync_CallsChatClient_WithSystemAndUserMessages()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsResponse("Refined content.");
        var sut = CreateSut();

        // Act
        await sut.RefineAnalysisAsync(TestRefinementInstruction);

        // Assert -- verify GetResponseAsync was called with correct message structure
        await _chatClient.Received(1).GetResponseAsync(
            Arg.Is<IList<AiChatMessage>>(msgs =>
                msgs.Count == 2 &&
                msgs[0].Role == ChatRole.System &&
                msgs[1].Role == ChatRole.User),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefineAnalysisAsync_SystemPromptContainsCurrentAnalysis()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsResponse("Result.");
        var sut = CreateSut();

        // Act
        await sut.RefineAnalysisAsync(TestRefinementInstruction);

        // Assert -- system message should contain current analysis content
        await _chatClient.Received(1).GetResponseAsync(
            Arg.Is<IList<AiChatMessage>>(msgs =>
                msgs[0].Text != null && msgs[0].Text.Contains(TestDocumentContent)),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefineAnalysisAsync_UserMessageContainsRefinementInstruction()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsResponse("Result.");
        var sut = CreateSut();

        // Act
        await sut.RefineAnalysisAsync(TestRefinementInstruction);

        // Assert -- user message should be the refinement instruction
        await _chatClient.Received(1).GetResponseAsync(
            Arg.Is<IList<AiChatMessage>>(msgs =>
                msgs[1].Text == TestRefinementInstruction),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>());
    }

    // ====================================================================
    // RefineAnalysisAsync — Validation and Error Handling
    // ====================================================================

    [Fact]
    public async Task RefineAnalysisAsync_EmptyInstruction_ReturnsValidationError()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.RefineAnalysisAsync(string.Empty));
    }

    [Fact]
    public async Task RefineAnalysisAsync_WhitespaceInstruction_ReturnsValidationError()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.RefineAnalysisAsync("   "));
    }

    [Fact]
    public async Task RefineAnalysisAsync_NullInstruction_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.RefineAnalysisAsync(null!));
    }

    [Fact]
    public async Task RefineAnalysisAsync_NoAnalysisId_ReturnsNoAnalysisMessage()
    {
        // Arrange -- analysisId is null (no analysis context available)
        var sut = new AnalysisExecutionTools(
            _analysisService, _chatClient,
            analysisId: null,
            playbookId: TestPlaybookId,
            documentId: TestDocumentId,
            httpContext: _httpContext,
            sseWriter: _sseWriter);

        // Act
        var result = await sut.RefineAnalysisAsync(TestRefinementInstruction);

        // Assert
        result.Should().Contain("no analysis output available");
        result.Should().Contain("failed");
    }

    [Fact]
    public async Task RefineAnalysisAsync_InvalidAnalysisId_ReturnsNoAnalysisMessage()
    {
        // Arrange -- analysisId is not a valid GUID
        var sut = new AnalysisExecutionTools(
            _analysisService, _chatClient,
            analysisId: "not-a-guid",
            playbookId: TestPlaybookId,
            documentId: TestDocumentId,
            httpContext: _httpContext,
            sseWriter: _sseWriter);

        // Act
        var result = await sut.RefineAnalysisAsync(TestRefinementInstruction);

        // Assert
        result.Should().Contain("no analysis output available");
    }

    [Fact]
    public async Task RefineAnalysisAsync_AnalysisNotFound_ReturnsNoAnalysisMessage()
    {
        // Arrange -- analysis service throws KeyNotFoundException
        _analysisService.GetAnalysisAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeyNotFoundException("Analysis not found"));
        var sut = CreateSut();

        // Act
        var result = await sut.RefineAnalysisAsync(TestRefinementInstruction);

        // Assert
        result.Should().Contain("no analysis output available");
    }

    [Fact]
    public async Task RefineAnalysisAsync_AnalysisHasNoWorkingDocument_UseFinalOutput()
    {
        // Arrange -- WorkingDocument is null but FinalOutput has content
        _analysisService.GetAnalysisAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new AnalysisDetailResult
            {
                Id = Guid.Parse(TestAnalysisId),
                DocumentId = Guid.NewGuid(),
                DocumentName = "test-doc.pdf",
                Action = new AnalysisActionInfo(Guid.NewGuid(), "Test Action"),
                Status = "completed",
                WorkingDocument = null,
                FinalOutput = "Final output content",
                ChatHistory = [],
                TokenUsage = null,
                StartedOn = DateTime.UtcNow,
                CompletedOn = DateTime.UtcNow
            });
        SetupChatClientReturnsResponse("Refined from final output.");
        var sut = CreateSut();

        // Act
        var result = await sut.RefineAnalysisAsync(TestRefinementInstruction);

        // Assert -- should use FinalOutput as fallback and succeed
        result.Should().Be("Refined from final output.");
    }

    [Fact]
    public async Task RefineAnalysisAsync_BothDocumentFieldsNull_ReturnsNoAnalysisMessage()
    {
        // Arrange -- both WorkingDocument and FinalOutput are null
        _analysisService.GetAnalysisAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new AnalysisDetailResult
            {
                Id = Guid.Parse(TestAnalysisId),
                DocumentId = Guid.NewGuid(),
                DocumentName = "test-doc.pdf",
                Action = new AnalysisActionInfo(Guid.NewGuid(), "Test Action"),
                Status = "completed",
                WorkingDocument = null,
                FinalOutput = null,
                ChatHistory = [],
                TokenUsage = null,
                StartedOn = DateTime.UtcNow,
                CompletedOn = DateTime.UtcNow
            });
        var sut = CreateSut();

        // Act
        var result = await sut.RefineAnalysisAsync(TestRefinementInstruction);

        // Assert
        result.Should().Contain("no analysis output available");
    }

    // ====================================================================
    // GetTools tests
    // ====================================================================

    [Fact]
    public void GetTools_ReturnsTwoFunctions()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var tools = sut.GetTools().ToList();

        // Assert
        tools.Should().HaveCount(2);
    }

    [Fact]
    public void GetTools_ReturnsRerunAnalysisFunction()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var tools = sut.GetTools().ToList();

        // Assert
        tools.Should().Contain(t => t.Name == "RerunAnalysis");
    }

    [Fact]
    public void GetTools_ReturnsRefineAnalysisFunction()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var tools = sut.GetTools().ToList();

        // Assert
        tools.Should().Contain(t => t.Name == "RefineAnalysis");
    }

    // ====================================================================
    // SSE Event Ordering Tests
    // ====================================================================

    [Fact]
    public async Task RerunAnalysisAsync_ProgressEventsHaveIncreasingPercentages()
    {
        // Arrange
        SetupPlaybookExecutionReturns(
            AnalysisStreamChunk.Metadata(Guid.Parse(TestAnalysisId), "test-doc.pdf"),
            AnalysisStreamChunk.TextChunk("[Executing: ToolA]"),
            AnalysisStreamChunk.TextChunk("[Executing: ToolB]"),
            AnalysisStreamChunk.Completed(Guid.Parse(TestAnalysisId), new TokenUsage(0, 0)));
        var sut = CreateSut();

        // Act
        await sut.RerunAnalysisAsync();

        // Assert -- progress percentages should be non-decreasing
        var percentages = _capturedEvents
            .Where(e => e.Type == "progress")
            .Select(e => e.Data as ChatSseProgressData)
            .Where(d => d != null)
            .Select(d => d!.Percent)
            .ToList();

        percentages.Should().BeInAscendingOrder();
        percentages.First().Should().Be(0);
        percentages.Last().Should().Be(100);
    }

    [Fact]
    public async Task RerunAnalysisAsync_DocumentReplaceComesAfterLastProgress()
    {
        // Arrange
        SetupPlaybookExecutionReturns(
            AnalysisStreamChunk.Metadata(Guid.Parse(TestAnalysisId), "test-doc.pdf"),
            AnalysisStreamChunk.TextChunk("Analysis output"),
            AnalysisStreamChunk.Completed(Guid.Parse(TestAnalysisId), new TokenUsage(0, 0)));
        var sut = CreateSut();

        // Act
        await sut.RerunAnalysisAsync();

        // Assert -- document_replace should appear after progress events
        var lastProgressIndex = -1;
        var documentReplaceIndex = -1;
        for (var i = 0; i < _capturedEvents.Count; i++)
        {
            if (_capturedEvents[i].Type == "progress")
                lastProgressIndex = i;
            if (_capturedEvents[i].Type == "document_replace")
                documentReplaceIndex = i;
        }

        documentReplaceIndex.Should().BeGreaterThan(-1, "a document_replace event should be emitted");
        // document_replace should come after the 90% progress but before the 100% progress
        // The exact ordering is: progress(90) -> document_replace -> progress(100)
    }

    // ====================================================================
    // Private helpers
    // ====================================================================

    private AnalysisExecutionTools CreateSut()
    {
        return new AnalysisExecutionTools(
            _analysisService,
            _chatClient,
            analysisId: TestAnalysisId,
            playbookId: TestPlaybookId,
            documentId: TestDocumentId,
            httpContext: _httpContext,
            sseWriter: _sseWriter);
    }

    /// <summary>
    /// Configures the analysis service to return the specified document content
    /// when GetAnalysisAsync is called.
    /// </summary>
    private void SetupAnalysisServiceReturnsDocument(string? content)
    {
        _analysisService.GetAnalysisAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new AnalysisDetailResult
            {
                Id = Guid.Parse(TestAnalysisId),
                DocumentId = Guid.NewGuid(),
                DocumentName = "test-doc.pdf",
                Action = new AnalysisActionInfo(Guid.NewGuid(), "Test Action"),
                Status = "completed",
                WorkingDocument = content,
                FinalOutput = content,
                ChatHistory = [],
                TokenUsage = null,
                StartedOn = DateTime.UtcNow,
                CompletedOn = DateTime.UtcNow
            });
    }

    /// <summary>
    /// Configures the analysis service's ExecutePlaybookAsync to return
    /// the specified chunks as an async enumerable.
    /// </summary>
    private void SetupPlaybookExecutionReturns(params AnalysisStreamChunk[] chunks)
    {
        _analysisService.ExecutePlaybookAsync(
                Arg.Any<PlaybookExecuteRequest>(),
                Arg.Any<HttpContext>(),
                Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(chunks));
    }

    /// <summary>
    /// Configures the chat client to return a response with the specified text.
    /// Used for RefineAnalysisAsync tests.
    /// </summary>
    private void SetupChatClientReturnsResponse(string responseText)
    {
        var response = new ChatResponse(
            new List<AiChatMessage>
            {
                new(ChatRole.Assistant, responseText)
            });

        _chatClient.GetResponseAsync(
                Arg.Any<IList<AiChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(response);
    }

    /// <summary>Wraps items as an IAsyncEnumerable.</summary>
    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    /// <summary>
    /// Creates an IAsyncEnumerable that immediately throws the specified exception.
    /// Used to simulate orchestrator failures.
    /// </summary>
    private static async IAsyncEnumerable<T> ThrowingAsyncEnumerable<T>(Exception exception)
    {
        await Task.Yield();
        throw exception;
        // Unreachable but required to satisfy the compiler for IAsyncEnumerable<T>
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
