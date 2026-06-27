using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Memory;

/// <summary>
/// Unit tests for <see cref="SummarizationCompressionService"/> (R6 Pillar 7 / task 064).
/// </summary>
/// <remarks>
/// Covers: kill-switch short-circuit, insufficient-input short-circuit, happy-path
/// compression, LLM circuit-broken graceful failure, generic LLM exception graceful
/// failure, and output-budget defensive truncation.
/// </remarks>
public sealed class SummarizationCompressionServiceTests
{
    private readonly Mock<IOpenAiClient> _openAiClient = new();
    private readonly Mock<ILogger<SummarizationCompressionService>> _logger = new();

    private static SummarizationCompressionService CreateSut(
        Mock<IOpenAiClient> openAi,
        Mock<ILogger<SummarizationCompressionService>> logger,
        SummarizationCompressionOptions? options = null)
    {
        options ??= new SummarizationCompressionOptions { Enabled = true };
        return new SummarizationCompressionService(
            openAi.Object,
            Options.Create(options),
            logger.Object);
    }

    private static IReadOnlyList<ChatMessage> BuildMessages(int count, string contentPrefix = "msg")
    {
        var list = new List<ChatMessage>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(new ChatMessage(
                MessageId: $"m-{i}",
                SessionId: "session-test",
                Role: (i % 2 == 0) ? ChatMessageRole.User : ChatMessageRole.Assistant,
                Content: $"{contentPrefix}-{i}",
                TokenCount: 10,
                CreatedAt: DateTimeOffset.UtcNow,
                SequenceNumber: i));
        }
        return list;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Kill switch
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompressAsync_ReturnsNull_WhenKillSwitchOff()
    {
        var sut = CreateSut(_openAiClient, _logger, new SummarizationCompressionOptions { Enabled = false });

        var result = await sut.CompressAsync(BuildMessages(5), maxSummaryTokens: 256);

        result.Should().BeNull();
        _openAiClient.Verify(
            c => c.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // Rationale: kill switch off must short-circuit before the LLM call.
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Insufficient input
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompressAsync_ReturnsNull_WhenInputEmpty()
    {
        var sut = CreateSut(_openAiClient, _logger);

        var result = await sut.CompressAsync(Array.Empty<ChatMessage>(), maxSummaryTokens: 256);

        result.Should().BeNull();
        _openAiClient.Verify(
            c => c.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CompressAsync_ReturnsNull_WhenInputBelowMinimum()
    {
        var sut = CreateSut(_openAiClient, _logger);

        var result = await sut.CompressAsync(BuildMessages(1), maxSummaryTokens: 256);

        result.Should().BeNull(because: "single-message input is below MinMessagesToCompress (2)");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Happy path
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompressAsync_ReturnsSummaryMessage_OnHappyPath()
    {
        _openAiClient
            .Setup(c => c.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("The user asked about X; the agent confirmed Y.");

        var sut = CreateSut(_openAiClient, _logger);

        var result = await sut.CompressAsync(BuildMessages(6), maxSummaryTokens: 256);

        result.Should().NotBeNull();
        result!.Role.Should().Be(ChatMessageRole.System);
        result.Content.Should().StartWith("Summary of earlier conversation: ",
            because: "the service wraps output in a canonical prefix for downstream pattern matching");
        result.SessionId.Should().Be("session-test");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // LLM failures
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompressAsync_ReturnsNull_WhenLlmThrowsGenericException()
    {
        _openAiClient
            .Setup(c => c.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient LLM failure"));

        var sut = CreateSut(_openAiClient, _logger);

        var result = await sut.CompressAsync(BuildMessages(4), maxSummaryTokens: 256);

        result.Should().BeNull(
            because: "P2 Quiet posture: LLM exceptions degrade silently and the caller short-circuits to the raw window");
    }

    [Fact]
    public async Task CompressAsync_ReturnsNull_WhenCircuitBroken()
    {
        _openAiClient
            .Setup(c => c.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OpenAiCircuitBrokenException(TimeSpan.FromSeconds(30)));

        var sut = CreateSut(_openAiClient, _logger);

        var result = await sut.CompressAsync(BuildMessages(4), maxSummaryTokens: 256);

        result.Should().BeNull();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Token budget defensive truncation
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompressAsync_TruncatesResponse_WhenExceedingTokenBudget()
    {
        // Token budget: 128 tokens × 4 chars/token (default CharsPerToken) = 512 chars ceiling.
        // Generate a response well over that ceiling.
        var oversizedResponse = new string('a', 2_000);
        _openAiClient
            .Setup(c => c.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(oversizedResponse);

        var sut = CreateSut(_openAiClient, _logger);

        var result = await sut.CompressAsync(BuildMessages(4), maxSummaryTokens: 128);

        result.Should().NotBeNull();
        // Allow some headroom for the "Summary of earlier conversation: " prefix.
        result!.Content.Length.Should().BeLessThanOrEqualTo(
            (int)(128 * 4.0) + "Summary of earlier conversation: ".Length + 16,
            because: "service must enforce the NFR-10 reserved-slot budget on output");
    }
}
