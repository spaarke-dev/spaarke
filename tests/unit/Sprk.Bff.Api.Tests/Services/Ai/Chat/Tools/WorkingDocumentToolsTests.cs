using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat.Tools;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat.Tools;

/// <summary>
/// Unit tests for <see cref="WorkingDocumentTools"/>.
///
/// Verifies:
/// - Happy path: EditWorkingDocumentAsync emits correct SSE sequence (start -> tokens -> end)
/// - Happy path: AppendSectionAsync emits correct SSE sequence with heading token
/// - Cancellation: OperationCanceledException mid-stream -> cancelled=true end event
/// - LLM failure: Exception mid-stream -> error end event with ADR-019 details
/// - Empty document: graceful handling with NO_DOCUMENT error code
/// - Event ordering invariants: start first, end last, monotonic indices
/// - Constructor validation: null argument checks
///
/// ADR-015: Tests use generic content (no real document strings in CI logs).
/// </summary>
public class WorkingDocumentToolsTests
{
    // === Test constants (generic content per ADR-015) ===

    private const string TestAnalysisId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string TestInstruction = "Fix the formatting in paragraph two.";
    private const string TestSectionTitle = "Risk Assessment";
    private const string TestSectionInstruction = "Summarize the identified risks.";
    private const string TestDocumentContent = "Test document content for unit testing.";

    // === Shared test infrastructure ===

    private readonly IChatClient _chatClient;
    private readonly IAnalysisOrchestrationService _analysisService;
    private readonly ILogger _logger;
    private readonly List<DocumentStreamEvent> _capturedEvents;
    private readonly Func<DocumentStreamEvent, CancellationToken, Task> _writeSSE;

    public WorkingDocumentToolsTests()
    {
        _chatClient = Substitute.For<IChatClient>();
        _analysisService = Substitute.For<IAnalysisOrchestrationService>();
        _logger = Substitute.For<ILogger>();
        _capturedEvents = new List<DocumentStreamEvent>();
        _writeSSE = (evt, ct) =>
        {
            _capturedEvents.Add(evt);
            return Task.CompletedTask;
        };
    }

    // ====================================================================
    // Constructor validation tests
    // ====================================================================

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenChatClientIsNull()
    {
        // Act
        var action = () => new WorkingDocumentTools(
            null!, _writeSSE, _analysisService, _logger, TestAnalysisId);

        // Assert
        action.Should().Throw<ArgumentNullException>().WithParameterName("chatClient");
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenWriteSSEIsNull()
    {
        // Act
        var action = () => new WorkingDocumentTools(
            _chatClient, null!, _analysisService, _logger, TestAnalysisId);

        // Assert
        action.Should().Throw<ArgumentNullException>().WithParameterName("writeSSE");
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenAnalysisServiceIsNull()
    {
        // Act
        var action = () => new WorkingDocumentTools(
            _chatClient, _writeSSE, null!, _logger, TestAnalysisId);

        // Assert
        action.Should().Throw<ArgumentNullException>().WithParameterName("analysisService");
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
    {
        // Act
        var action = () => new WorkingDocumentTools(
            _chatClient, _writeSSE, _analysisService, null!, TestAnalysisId);

        // Assert
        action.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_AcceptsNullAnalysisId()
    {
        // Act — analysisId is optional, null should not throw
        var action = () => new WorkingDocumentTools(
            _chatClient, _writeSSE, _analysisService, _logger, analysisId: null);

        // Assert
        action.Should().NotThrow();
    }

    // ====================================================================
    // EditWorkingDocumentAsync — Happy Path
    // ====================================================================

    [Fact]
    public async Task EditWorkingDocumentAsync_HappyPath_EmitsStartTokensEnd()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsTokens("Alpha", "Beta", "Gamma");
        var sut = CreateSut();

        // Act
        var result = await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert — 5 events: 1 start + 3 tokens + 1 end
        _capturedEvents.Should().HaveCount(5);

        _capturedEvents[0].Should().BeOfType<DocumentStreamStartEvent>();
        _capturedEvents[1].Should().BeOfType<DocumentStreamTokenEvent>();
        _capturedEvents[2].Should().BeOfType<DocumentStreamTokenEvent>();
        _capturedEvents[3].Should().BeOfType<DocumentStreamTokenEvent>();
        _capturedEvents[4].Should().BeOfType<DocumentStreamEndEvent>();
    }

    [Fact]
    public async Task EditWorkingDocumentAsync_HappyPath_StartEventHasCorrectFields()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsTokens("Token1");
        var sut = CreateSut();

        // Act
        await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert
        var startEvent = _capturedEvents[0] as DocumentStreamStartEvent;
        startEvent.Should().NotBeNull();
        startEvent!.Type.Should().Be("document_stream_start");
        startEvent.TargetPosition.Should().Be("document");
        startEvent.OperationType.Should().Be("replace");
        startEvent.OperationId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task EditWorkingDocumentAsync_HappyPath_TokenEventsHaveCorrectContent()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsTokens("Alpha", "Beta", "Gamma");
        var sut = CreateSut();

        // Act
        await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert
        var tokenEvents = _capturedEvents.OfType<DocumentStreamTokenEvent>().ToList();
        tokenEvents.Should().HaveCount(3);
        tokenEvents[0].Token.Should().Be("Alpha");
        tokenEvents[0].Index.Should().Be(0);
        tokenEvents[1].Token.Should().Be("Beta");
        tokenEvents[1].Index.Should().Be(1);
        tokenEvents[2].Token.Should().Be("Gamma");
        tokenEvents[2].Index.Should().Be(2);
    }

    [Fact]
    public async Task EditWorkingDocumentAsync_HappyPath_EndEventHasCorrectFields()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsTokens("A", "B", "C");
        var sut = CreateSut();

        // Act
        await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert
        var endEvent = _capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent.Should().NotBeNull();
        endEvent!.Type.Should().Be("document_stream_end");
        endEvent.Cancelled.Should().BeFalse();
        endEvent.TotalTokens.Should().Be(3);
        endEvent.ErrorCode.Should().BeNull();
        endEvent.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task EditWorkingDocumentAsync_HappyPath_OperationIdConsistentAcrossAllEvents()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsTokens("X", "Y");
        var sut = CreateSut();

        // Act
        await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert — all events share the same operationId
        var startId = ((DocumentStreamStartEvent)_capturedEvents[0]).OperationId;
        startId.Should().NotBe(Guid.Empty);

        foreach (var evt in _capturedEvents)
        {
            switch (evt)
            {
                case DocumentStreamStartEvent s:
                    s.OperationId.Should().Be(startId);
                    break;
                case DocumentStreamTokenEvent t:
                    t.OperationId.Should().Be(startId);
                    break;
                case DocumentStreamEndEvent e:
                    e.OperationId.Should().Be(startId);
                    break;
            }
        }
    }

    [Fact]
    public async Task EditWorkingDocumentAsync_HappyPath_ReturnsSummaryString()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsTokens("A");
        var sut = CreateSut();

        // Act
        var result = await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert
        result.Should().Contain("edited successfully");
        result.Should().Contain("Streamed 1 tokens");
    }

    [Fact]
    public async Task EditWorkingDocumentAsync_ThrowsArgumentException_WhenInstructionIsEmpty()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.EditWorkingDocumentAsync(string.Empty));
    }

    [Fact]
    public async Task EditWorkingDocumentAsync_ThrowsArgumentException_WhenInstructionIsWhitespace()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.EditWorkingDocumentAsync("   "));
    }

    // ====================================================================
    // AppendSectionAsync — Happy Path
    // ====================================================================

    [Fact]
    public async Task AppendSectionAsync_HappyPath_EmitsStartHeadingTokensEnd()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsTokens("Content A", "Content B");
        var sut = CreateSut();

        // Act
        var result = await sut.AppendSectionAsync(TestSectionTitle, TestSectionInstruction);

        // Assert — 5 events: 1 start + 1 heading token + 2 LLM tokens + 1 end
        _capturedEvents.Should().HaveCount(5);
        _capturedEvents[0].Should().BeOfType<DocumentStreamStartEvent>();
        _capturedEvents[1].Should().BeOfType<DocumentStreamTokenEvent>(); // heading
        _capturedEvents[2].Should().BeOfType<DocumentStreamTokenEvent>(); // LLM token 1
        _capturedEvents[3].Should().BeOfType<DocumentStreamTokenEvent>(); // LLM token 2
        _capturedEvents[4].Should().BeOfType<DocumentStreamEndEvent>();
    }

    [Fact]
    public async Task AppendSectionAsync_HappyPath_StartEventHasInsertOperation()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsTokens("Content");
        var sut = CreateSut();

        // Act
        await sut.AppendSectionAsync(TestSectionTitle, TestSectionInstruction);

        // Assert
        var startEvent = _capturedEvents[0] as DocumentStreamStartEvent;
        startEvent.Should().NotBeNull();
        startEvent!.TargetPosition.Should().Be("end");
        startEvent.OperationType.Should().Be("insert");
    }

    [Fact]
    public async Task AppendSectionAsync_HappyPath_FirstTokenIsSectionHeading()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsTokens("Body content");
        var sut = CreateSut();

        // Act
        await sut.AppendSectionAsync(TestSectionTitle, TestSectionInstruction);

        // Assert
        var firstToken = _capturedEvents[1] as DocumentStreamTokenEvent;
        firstToken.Should().NotBeNull();
        firstToken!.Token.Should().Contain(TestSectionTitle);
        firstToken.Token.Should().StartWith("## ");
        firstToken.Index.Should().Be(0);
    }

    [Fact]
    public async Task AppendSectionAsync_HappyPath_EndEventShowsCorrectTotalTokens()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsTokens("A", "B");
        var sut = CreateSut();

        // Act
        await sut.AppendSectionAsync(TestSectionTitle, TestSectionInstruction);

        // Assert — 1 heading + 2 LLM = 3 total
        var endEvent = _capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent.Should().NotBeNull();
        endEvent!.TotalTokens.Should().Be(3);
        endEvent.Cancelled.Should().BeFalse();
    }

    [Fact]
    public async Task AppendSectionAsync_HappyPath_ReturnsSummaryWithSectionTitle()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsTokens("Content");
        var sut = CreateSut();

        // Act
        var result = await sut.AppendSectionAsync(TestSectionTitle, TestSectionInstruction);

        // Assert
        result.Should().Contain(TestSectionTitle);
        result.Should().Contain("appended");
    }

    [Fact]
    public async Task AppendSectionAsync_ThrowsArgumentException_WhenSectionTitleIsEmpty()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.AppendSectionAsync(string.Empty, TestSectionInstruction));
    }

    [Fact]
    public async Task AppendSectionAsync_ThrowsArgumentException_WhenInstructionIsEmpty()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.AppendSectionAsync(TestSectionTitle, string.Empty));
    }

    [Fact]
    public async Task AppendSectionAsync_WithoutDocument_StillEmitsHeadingAndLLMContent()
    {
        // Arrange — no document available, append should still work
        SetupAnalysisServiceReturnsDocument(null);
        SetupChatClientReturnsTokens("Generated content");
        var sut = CreateSut();

        // Act
        await sut.AppendSectionAsync(TestSectionTitle, TestSectionInstruction);

        // Assert — should still emit start, heading, LLM content, end
        _capturedEvents.Should().HaveCount(4); // start + heading + 1 LLM token + end
        _capturedEvents[0].Should().BeOfType<DocumentStreamStartEvent>();
        _capturedEvents[1].Should().BeOfType<DocumentStreamTokenEvent>();
        _capturedEvents[2].Should().BeOfType<DocumentStreamTokenEvent>();
        _capturedEvents[3].Should().BeOfType<DocumentStreamEndEvent>();

        var endEvent = _capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent!.Cancelled.Should().BeFalse();
        endEvent.ErrorCode.Should().BeNull();
    }

    // ====================================================================
    // Cancellation tests
    // ====================================================================

    [Fact]
    public async Task EditWorkingDocumentAsync_Cancellation_EmitsPartialTokensThenCancelledEnd()
    {
        // Arrange — yield 2 tokens then throw OperationCanceledException
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsThenCancels("Token1", "Token2");
        var sut = CreateSut();

        // Act
        var result = await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert — start + 2 tokens + cancelled end = 4 events
        _capturedEvents.Should().HaveCount(4);

        var tokenEvents = _capturedEvents.OfType<DocumentStreamTokenEvent>().ToList();
        tokenEvents.Should().HaveCount(2);
        tokenEvents[0].Token.Should().Be("Token1");
        tokenEvents[1].Token.Should().Be("Token2");

        var endEvent = _capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent.Should().NotBeNull();
        endEvent!.Cancelled.Should().BeTrue();
        endEvent.TotalTokens.Should().Be(2);
        endEvent.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task EditWorkingDocumentAsync_Cancellation_ReturnsSummaryWithPartialInfo()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsThenCancels("A", "B");
        var sut = CreateSut();

        // Act
        var result = await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert
        result.Should().Contain("cancelled");
        result.Should().Contain("2 tokens");
    }

    [Fact]
    public async Task AppendSectionAsync_Cancellation_EmitsHeadingThenCancelledEnd()
    {
        // Arrange — yield 1 LLM token then cancel (heading token also emitted = 2 total tokens)
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsThenCancels("Partial");
        var sut = CreateSut();

        // Act
        var result = await sut.AppendSectionAsync(TestSectionTitle, TestSectionInstruction);

        // Assert — start + heading + 1 LLM token + cancelled end = 4 events
        _capturedEvents.Should().HaveCount(4);

        var endEvent = _capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent.Should().NotBeNull();
        endEvent!.Cancelled.Should().BeTrue();
        endEvent.TotalTokens.Should().Be(2); // 1 heading + 1 LLM token
    }

    // ====================================================================
    // LLM failure / error handling tests
    // ====================================================================

    [Fact]
    public async Task EditWorkingDocumentAsync_LLMFailure_EmitsErrorEndEvent()
    {
        // Arrange — yield 1 token then throw an exception
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsThenFails("Partial", new InvalidOperationException("LLM service unavailable"));
        var sut = CreateSut();

        // Act
        var result = await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert — start + 1 token + error end = 3 events
        _capturedEvents.Should().HaveCount(3);

        var endEvent = _capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent.Should().NotBeNull();
        endEvent!.Cancelled.Should().BeFalse();
        endEvent.TotalTokens.Should().Be(1);
        endEvent.ErrorCode.Should().Be("LLM_STREAM_FAILED");
        endEvent.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EditWorkingDocumentAsync_LLMFailure_DoesNotPropagateException()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsThenFails("X", new HttpRequestException("Connection refused"));
        var sut = CreateSut();

        // Act — should NOT throw; exception is handled internally
        var action = async () => await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EditWorkingDocumentAsync_LLMFailure_ReturnsSummaryWithErrorType()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsThenFails("X", new TimeoutException("Timed out"));
        var sut = CreateSut();

        // Act
        var result = await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert
        result.Should().Contain("failed");
        result.Should().Contain("TimeoutException");
    }

    [Fact]
    public async Task AppendSectionAsync_LLMFailure_EmitsErrorEndEventAfterHeading()
    {
        // Arrange — heading token emitted, then LLM fails before producing tokens
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientFails(new InvalidOperationException("Model overloaded"));
        var sut = CreateSut();

        // Act
        var result = await sut.AppendSectionAsync(TestSectionTitle, TestSectionInstruction);

        // Assert — start + heading + error end = 3 events
        _capturedEvents.Should().HaveCount(3);

        // Heading was emitted (index 0)
        var headingToken = _capturedEvents[1] as DocumentStreamTokenEvent;
        headingToken.Should().NotBeNull();
        headingToken!.Token.Should().Contain(TestSectionTitle);

        var endEvent = _capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent.Should().NotBeNull();
        endEvent!.ErrorCode.Should().Be("LLM_STREAM_FAILED");
        endEvent.TotalTokens.Should().Be(1); // heading token only
    }

    // ====================================================================
    // Empty / no document tests
    // ====================================================================

    [Fact]
    public async Task EditWorkingDocumentAsync_NoDocument_EmitsStartThenErrorEnd()
    {
        // Arrange — analysis service returns null document
        SetupAnalysisServiceReturnsDocument(null);
        var sut = CreateSut();

        // Act
        var result = await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert — start + end with NO_DOCUMENT error
        _capturedEvents.Should().HaveCount(2);
        _capturedEvents[0].Should().BeOfType<DocumentStreamStartEvent>();

        var endEvent = _capturedEvents[1] as DocumentStreamEndEvent;
        endEvent.Should().NotBeNull();
        endEvent!.Cancelled.Should().BeFalse();
        endEvent.TotalTokens.Should().Be(0);
        endEvent.ErrorCode.Should().Be("NO_DOCUMENT");
        endEvent.ErrorMessage.Should().Contain("No working document");
    }

    [Fact]
    public async Task EditWorkingDocumentAsync_NoDocument_ReturnsInformativeMessage()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(null);
        var sut = CreateSut();

        // Act
        var result = await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert
        result.Should().Contain("no working document");
    }

    [Fact]
    public async Task EditWorkingDocumentAsync_NullAnalysisId_EmitsNoDocumentError()
    {
        // Arrange — no analysis context at all
        var sut = CreateSut(analysisId: null);

        // Act
        var result = await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert
        _capturedEvents.Should().HaveCount(2);

        var endEvent = _capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent.Should().NotBeNull();
        endEvent!.ErrorCode.Should().Be("NO_DOCUMENT");
    }

    [Fact]
    public async Task EditWorkingDocumentAsync_InvalidAnalysisId_EmitsNoDocumentError()
    {
        // Arrange — analysis ID is not a valid GUID
        var sut = CreateSut(analysisId: "not-a-guid");

        // Act
        var result = await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert
        var endEvent = _capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent.Should().NotBeNull();
        endEvent!.ErrorCode.Should().Be("NO_DOCUMENT");
    }

    [Fact]
    public async Task EditWorkingDocumentAsync_AnalysisNotFound_EmitsNoDocumentError()
    {
        // Arrange — analysis service throws KeyNotFoundException
        _analysisService.GetAnalysisAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeyNotFoundException("Analysis not found"));
        var sut = CreateSut();

        // Act
        var result = await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert
        _capturedEvents.Should().HaveCount(2);

        var endEvent = _capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent.Should().NotBeNull();
        endEvent!.ErrorCode.Should().Be("NO_DOCUMENT");
    }

    // ====================================================================
    // SSE event ordering invariant tests
    // ====================================================================

    [Fact]
    public async Task EditWorkingDocumentAsync_EventOrdering_StartIsAlwaysFirst()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsTokens("A", "B", "C", "D", "E");
        var sut = CreateSut();

        // Act
        await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert
        _capturedEvents.Should().NotBeEmpty();
        _capturedEvents.First().Should().BeOfType<DocumentStreamStartEvent>();
    }

    [Fact]
    public async Task EditWorkingDocumentAsync_EventOrdering_EndIsAlwaysLast()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsTokens("A", "B", "C");
        var sut = CreateSut();

        // Act
        await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert
        _capturedEvents.Should().NotBeEmpty();
        _capturedEvents.Last().Should().BeOfType<DocumentStreamEndEvent>();
    }

    [Fact]
    public async Task EditWorkingDocumentAsync_EventOrdering_TokenIndicesAreMonotonicallyIncreasing()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsTokens("A", "B", "C", "D");
        var sut = CreateSut();

        // Act
        await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert
        var tokenEvents = _capturedEvents.OfType<DocumentStreamTokenEvent>().ToList();
        tokenEvents.Should().HaveCount(4);

        for (var i = 0; i < tokenEvents.Count; i++)
        {
            tokenEvents[i].Index.Should().Be(i, $"Token at position {i} should have index {i}");
        }
    }

    [Fact]
    public async Task AppendSectionAsync_EventOrdering_StartFirstEndLast()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsTokens("Section content");
        var sut = CreateSut();

        // Act
        await sut.AppendSectionAsync(TestSectionTitle, TestSectionInstruction);

        // Assert
        _capturedEvents.First().Should().BeOfType<DocumentStreamStartEvent>();
        _capturedEvents.Last().Should().BeOfType<DocumentStreamEndEvent>();
    }

    [Fact]
    public async Task AppendSectionAsync_EventOrdering_TokenIndicesMonotonic()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsTokens("P1", "P2", "P3");
        var sut = CreateSut();

        // Act
        await sut.AppendSectionAsync(TestSectionTitle, TestSectionInstruction);

        // Assert — heading at index 0, then LLM tokens at 1, 2, 3
        var tokenEvents = _capturedEvents.OfType<DocumentStreamTokenEvent>().ToList();
        tokenEvents.Should().HaveCount(4); // 1 heading + 3 LLM tokens

        for (var i = 0; i < tokenEvents.Count; i++)
        {
            tokenEvents[i].Index.Should().Be(i, $"Token at position {i} should have index {i}");
        }
    }

    [Fact]
    public async Task EditWorkingDocumentAsync_Cancellation_EndIsAlwaysLast()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsThenCancels("A");
        var sut = CreateSut();

        // Act
        await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert
        _capturedEvents.Last().Should().BeOfType<DocumentStreamEndEvent>();
    }

    [Fact]
    public async Task EditWorkingDocumentAsync_LLMFailure_EndIsAlwaysLast()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsThenFails("A", new Exception("Unexpected"));
        var sut = CreateSut();

        // Act
        await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert
        _capturedEvents.Last().Should().BeOfType<DocumentStreamEndEvent>();
    }

    [Fact]
    public async Task EditWorkingDocumentAsync_NoDocument_EndIsAlwaysLast()
    {
        // Arrange
        SetupAnalysisServiceReturnsDocument(null);
        var sut = CreateSut();

        // Act
        await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert
        _capturedEvents.Last().Should().BeOfType<DocumentStreamEndEvent>();
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
    public void GetTools_ReturnsEditWorkingDocumentFunction()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var tools = sut.GetTools().ToList();

        // Assert
        tools.Should().Contain(t => t.Name == "EditWorkingDocument");
    }

    [Fact]
    public void GetTools_ReturnsAppendSectionFunction()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var tools = sut.GetTools().ToList();

        // Assert
        tools.Should().Contain(t => t.Name == "AppendSection");
    }

    // ====================================================================
    // LLM produces empty tokens (skipped by implementation)
    // ====================================================================

    [Fact]
    public async Task EditWorkingDocumentAsync_SkipsEmptyTokens_DoesNotEmitTokenEvent()
    {
        // Arrange — LLM returns some empty/null text updates mixed with real tokens
        SetupAnalysisServiceReturnsDocument(TestDocumentContent);
        SetupChatClientReturnsUpdates(
            CreateStreamingUpdate("Real"),
            CreateStreamingUpdate(""),    // empty — should be skipped
            CreateStreamingUpdate("Token"));
        var sut = CreateSut();

        // Act
        await sut.EditWorkingDocumentAsync(TestInstruction);

        // Assert — only 2 non-empty tokens emitted
        var tokenEvents = _capturedEvents.OfType<DocumentStreamTokenEvent>().ToList();
        tokenEvents.Should().HaveCount(2);
        tokenEvents[0].Token.Should().Be("Real");
        tokenEvents[1].Token.Should().Be("Token");

        var endEvent = _capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent!.TotalTokens.Should().Be(2);
    }

    // ====================================================================
    // Private helpers
    // ====================================================================

    private WorkingDocumentTools CreateSut(string? analysisId = TestAnalysisId)
    {
        return new WorkingDocumentTools(
            _chatClient,
            _writeSSE,
            _analysisService,
            _logger,
            analysisId);
    }

    /// <summary>
    /// Configures the analysis service to return a document with the specified content.
    /// If content is null, the WorkingDocument and FinalOutput fields are both null.
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
    /// Configures IChatClient.GetStreamingResponseAsync to return the specified tokens
    /// as ChatResponseUpdate objects via an async enumerable.
    /// </summary>
    private void SetupChatClientReturnsTokens(params string[] tokens)
    {
        var updates = tokens.Select(CreateStreamingUpdate).ToArray();
        SetupChatClientReturnsUpdates(updates);
    }

    /// <summary>
    /// Configures IChatClient.GetStreamingResponseAsync to return the specified updates
    /// via an async enumerable.
    /// </summary>
    private void SetupChatClientReturnsUpdates(params ChatResponseUpdate[] updates)
    {
        _chatClient.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<Microsoft.Extensions.AI.ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(updates));
    }

    /// <summary>
    /// Configures IChatClient to yield specified tokens then throw OperationCanceledException.
    /// </summary>
    private void SetupChatClientReturnsThenCancels(params string[] tokensBeforeCancel)
    {
        _chatClient.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<Microsoft.Extensions.AI.ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerableThenThrow(
                tokensBeforeCancel.Select(CreateStreamingUpdate),
                new OperationCanceledException()));
    }

    /// <summary>
    /// Configures IChatClient to yield specified tokens then throw the given exception.
    /// </summary>
    private void SetupChatClientReturnsThenFails(string tokenBeforeFail, Exception exception)
    {
        _chatClient.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<Microsoft.Extensions.AI.ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerableThenThrow(
                new[] { CreateStreamingUpdate(tokenBeforeFail) },
                exception));
    }

    /// <summary>
    /// Configures IChatClient to immediately throw the given exception (no tokens yielded).
    /// </summary>
    private void SetupChatClientFails(Exception exception)
    {
        _chatClient.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<Microsoft.Extensions.AI.ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerableThenThrow(
                Enumerable.Empty<ChatResponseUpdate>(),
                exception));
    }

    private static ChatResponseUpdate CreateStreamingUpdate(string text)
    {
        return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(text)]
        };
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
    /// Yields items from the sequence then throws the specified exception.
    /// Used to simulate mid-stream failures and cancellations.
    /// </summary>
    private static async IAsyncEnumerable<ChatResponseUpdate> ToAsyncEnumerableThenThrow(
        IEnumerable<ChatResponseUpdate> items,
        Exception exceptionToThrow)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }

        throw exceptionToThrow;
    }
}
