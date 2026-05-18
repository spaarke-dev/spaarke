using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Sessions;

/// <summary>
/// Unit tests for <see cref="SessionSummarizationService"/> (AIPU2-032).
///
/// Verifies:
/// (a) ShouldSummarize returns false below both thresholds.
/// (b) ShouldSummarize returns true at exactly 25 messages (message count threshold).
/// (c) ShouldSummarize returns true when estimated token count reaches 8,000.
/// (d) SummarizeAsync uses GPT-4o as the model name in the outgoing ChatOptions.
/// (e) SummarizeAsync parses narrative summary and key conclusions from model response.
/// (f) SummarizeAsync falls back gracefully when model response lacks section markers.
/// (g) EstimateTokens correctly approximates via character count / 4.
/// </summary>
public class SessionSummarizationServiceTests
{
    private readonly Mock<IChatClient> _chatClientMock;
    private readonly Mock<ILogger<SessionSummarizationService>> _loggerMock;
    private readonly SessionSummarizationService _sut;

    public SessionSummarizationServiceTests()
    {
        _chatClientMock = new Mock<IChatClient>();
        _loggerMock = new Mock<ILogger<SessionSummarizationService>>();

        _sut = new SessionSummarizationService(
            _chatClientMock.Object,
            _loggerMock.Object);
    }

    // =========================================================================
    // ShouldSummarize — threshold detection
    // =========================================================================

    [Fact]
    public void ShouldSummarize_ReturnsFalse_WhenBelowBothThresholds()
    {
        // Arrange: 10 messages with short content (well below both thresholds)
        var messages = BuildMessages(count: 10, contentLength: 50);

        // Act
        var result = _sut.ShouldSummarize(messages);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldSummarize_ReturnsTrue_WhenMessageCountReaches25()
    {
        // Arrange: exactly 25 messages (message count threshold)
        var messages = BuildMessages(count: 25, contentLength: 10);

        // Act
        var result = _sut.ShouldSummarize(messages);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldSummarize_ReturnsTrue_WhenMessageCountExceeds25()
    {
        // Arrange: 30 messages
        var messages = BuildMessages(count: 30, contentLength: 10);

        // Act
        var result = _sut.ShouldSummarize(messages);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldSummarize_ReturnsFalse_WhenAt24Messages()
    {
        // Arrange: 24 messages — one below the threshold, small content
        var messages = BuildMessages(count: 24, contentLength: 10);

        // Act
        var result = _sut.ShouldSummarize(messages);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldSummarize_ReturnsTrue_WhenEstimatedTokensReach8000()
    {
        // Arrange: 5 messages, each with 6400 chars = 32000 total chars / 4 = 8000 tokens
        // (meets the token threshold even though message count is well below 25)
        var messages = BuildMessages(count: 5, contentLength: 6400);

        // Act
        var result = _sut.ShouldSummarize(messages);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldSummarize_ReturnsFalse_WhenEstimatedTokensJustBelowThreshold()
    {
        // Arrange: 4 messages, each 7900 chars = 31600 total chars / 4 = 7900 tokens < 8000
        var messages = BuildMessages(count: 4, contentLength: 7900);

        // Act
        var result = _sut.ShouldSummarize(messages);

        // Assert
        result.Should().BeFalse();
    }

    // =========================================================================
    // EstimateTokens — static helper
    // =========================================================================

    [Fact]
    public void EstimateTokens_ReturnsTotalCharsDiv4()
    {
        // Arrange: 3 messages, each 400 chars = 1200 / 4 = 300
        var messages = BuildMessages(count: 3, contentLength: 400);

        // Act
        var estimate = SessionSummarizationService.EstimateTokens(messages);

        // Assert
        estimate.Should().Be(300);
    }

    [Fact]
    public void EstimateTokens_ReturnsZero_WhenMessagesAreEmpty()
    {
        // Arrange
        var messages = new List<SessionMessage>();

        // Act
        var estimate = SessionSummarizationService.EstimateTokens(messages);

        // Assert
        estimate.Should().Be(0);
    }

    // =========================================================================
    // SummarizeAsync — model call and response parsing
    // =========================================================================

    [Fact]
    public async Task SummarizeAsync_UsesGpt4oModelName_InOutgoingRequest()
    {
        // Arrange
        ChatOptions? capturedOptions = null;
        var modelResponse = BuildWellFormedModelResponse(
            narrative: "Summary text.",
            conclusionsJson: "[]");

        _chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, opts, _) =>
            {
                capturedOptions = opts;
            })
            .ReturnsAsync(modelResponse);

        var messages = BuildMessages(count: 3, contentLength: 100);

        // Act
        await _sut.SummarizeAsync(messages);

        // Assert — GPT-4o must be specified (AIPU2-032 acceptance criterion)
        capturedOptions.Should().NotBeNull();
        capturedOptions!.ModelId.Should().Be("gpt-4o");
    }

    [Fact]
    public async Task SummarizeAsync_ReturnsNarrativeSummary_FromModelResponse()
    {
        // Arrange
        const string expectedNarrative = "The parties discussed the indemnification clause " +
            "and agreed it applies except in cases of gross negligence.";

        var modelResponse = BuildWellFormedModelResponse(
            narrative: expectedNarrative,
            conclusionsJson: "[]");

        _chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(modelResponse);

        var messages = BuildMessages(count: 3, contentLength: 100);

        // Act
        var summary = await _sut.SummarizeAsync(messages);

        // Assert
        summary.NarrativeSummary.Should().Be(expectedNarrative);
    }

    [Fact]
    public async Task SummarizeAsync_ParsesKeyConclusions_FromModelResponse()
    {
        // Arrange
        const string conclusionsJson = """
            [
              {
                "topic": "Indemnification",
                "conclusion": "Applies except in cases of gross negligence",
                "confidence": "high",
                "sourceReference": "Section 4.2 of the MSA"
              },
              {
                "topic": "Governing Law",
                "conclusion": "Delaware law governs",
                "confidence": "high",
                "sourceReference": null
              }
            ]
            """;

        var modelResponse = BuildWellFormedModelResponse(
            narrative: "Summary.",
            conclusionsJson: conclusionsJson);

        _chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(modelResponse);

        var messages = BuildMessages(count: 3, contentLength: 100);

        // Act
        var summary = await _sut.SummarizeAsync(messages);

        // Assert
        summary.KeyConclusions.Should().HaveCount(2);

        var first = summary.KeyConclusions[0];
        first.Topic.Should().Be("Indemnification");
        first.Conclusion.Should().Be("Applies except in cases of gross negligence");
        first.Confidence.Should().Be("high");
        first.SourceReference.Should().Be("Section 4.2 of the MSA");

        var second = summary.KeyConclusions[1];
        second.Topic.Should().Be("Governing Law");
        second.Confidence.Should().Be("high");
        second.SourceReference.Should().BeNull();
    }

    [Fact]
    public async Task SummarizeAsync_SetsOriginalMessageCountAndModelUsed()
    {
        // Arrange
        var modelResponse = BuildWellFormedModelResponse("Summary.", "[]");

        _chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(modelResponse);

        var messages = BuildMessages(count: 7, contentLength: 200);

        // Act
        var summary = await _sut.SummarizeAsync(messages);

        // Assert
        summary.OriginalMessageCount.Should().Be(7);
        summary.ModelUsed.Should().Be("gpt-4o");
        summary.SummarizedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, precision: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SummarizeAsync_FallsBackGracefully_WhenModelResponseLacksSectionMarkers()
    {
        // Arrange: model returns unstructured text (no section markers)
        const string rawResponse = "This is an unstructured summary without section markers.";

        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, rawResponse));

        _chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);

        var messages = BuildMessages(count: 3, contentLength: 100);

        // Act
        var summary = await _sut.SummarizeAsync(messages);

        // Assert — full response used as narrative, no conclusions
        summary.NarrativeSummary.Should().Contain("unstructured summary");
        summary.KeyConclusions.Should().BeEmpty();
        summary.ModelUsed.Should().Be("gpt-4o");
    }

    [Fact]
    public async Task SummarizeAsync_HandlesMarkdownCodeFenceWrappedJson()
    {
        // Arrange: model wraps JSON in ```json fences (common model behavior)
        const string conclusionsJson = """
            ```json
            [
              {
                "topic": "Liability",
                "conclusion": "Capped at annual fees",
                "confidence": "medium",
                "sourceReference": null
              }
            ]
            ```
            """;

        var modelResponse = BuildWellFormedModelResponse("Summary.", conclusionsJson);

        _chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(modelResponse);

        var messages = BuildMessages(count: 3, contentLength: 100);

        // Act
        var summary = await _sut.SummarizeAsync(messages);

        // Assert — fences are stripped correctly
        summary.KeyConclusions.Should().HaveCount(1);
        summary.KeyConclusions[0].Topic.Should().Be("Liability");
        summary.KeyConclusions[0].Confidence.Should().Be("medium");
    }

    [Fact]
    public async Task SummarizeAsync_ThrowsArgumentException_WhenMessagesIsEmpty()
    {
        // Arrange
        var messages = new List<SessionMessage>();

        // Act
        Func<Task> act = () => _sut.SummarizeAsync(messages);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*empty*");
    }

    // =========================================================================
    // Constants
    // =========================================================================

    [Fact]
    public void Constants_MessageThresholdIs25()
    {
        ISessionSummarizationService.MessageThreshold.Should().Be(25);
    }

    [Fact]
    public void Constants_TokenThresholdIs8000()
    {
        ISessionSummarizationService.TokenThreshold.Should().Be(8000);
    }

    [Fact]
    public void Constants_TailMessageCountIs10()
    {
        SessionSummarizationService.TailMessageCount.Should().Be(10);
    }

    [Fact]
    public void Constants_ModelNameIsGpt4o()
    {
        SessionSummarizationService.SummarizationModel.Should().Be("gpt-4o");
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private static List<SessionMessage> BuildMessages(int count, int contentLength)
    {
        var content = new string('x', contentLength);
        return Enumerable.Range(1, count)
            .Select(i => new SessionMessage
            {
                MessageId = $"msg-{i}",
                Role = i % 2 == 0 ? "assistant" : "user",
                Content = content,
                Timestamp = DateTimeOffset.UtcNow
            })
            .ToList();
    }

    /// <summary>
    /// Builds a well-formed model response with NARRATIVE_SUMMARY and KEY_CONCLUSIONS_JSON sections.
    /// </summary>
    private static ChatResponse BuildWellFormedModelResponse(string narrative, string conclusionsJson)
    {
        var responseText = $"""
            NARRATIVE_SUMMARY:
            {narrative}

            KEY_CONCLUSIONS_JSON:
            {conclusionsJson}
            """;

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText));
    }
}
