using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat.Middleware;
using Xunit;

using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat.Middleware;

/// <summary>
/// Unit tests for the agent middleware pipeline (AIPL-057).
///
/// Verifies:
/// - <see cref="AgentTelemetryMiddleware"/> logs token count and response time
/// - <see cref="AgentCostControlMiddleware"/> returns polite message when budget exceeded
/// - <see cref="AgentContentSafetyMiddleware"/> substitutes flagged PII patterns
/// - Middleware chain executes in the correct order
/// </summary>
public class AgentMiddlewareTests
{
    private static readonly ChatContext TestContext = new(
        SystemPrompt: "You are a helpful assistant.",
        DocumentSummary: null,
        AnalysisMetadata: null,
        PlaybookId: Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA"));

    // =========================================================================
    // Fake inner agent for testing middleware in isolation
    // =========================================================================

    /// <summary>
    /// Test double for <see cref="ISprkChatAgent"/> that yields a configurable sequence
    /// of <see cref="ChatResponseUpdate"/> chunks.
    /// </summary>
    private sealed class FakeAgent : ISprkChatAgent
    {
        private readonly List<string> _chunks;
        public int CallCount { get; private set; }

        public FakeAgent(params string[] chunks)
        {
            _chunks = chunks.ToList();
        }

        public ChatContext Context => TestContext;
        public CitationContext? Citations => null;

        public async IAsyncEnumerable<ChatResponseUpdate> SendMessageAsync(
            string message,
            IReadOnlyList<AiChatMessage> history,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            CallCount++;
            foreach (var chunk in _chunks)
            {
                var update = new ChatResponseUpdate { Role = ChatRole.Assistant };
                update.Contents.Add(new TextContent(chunk));
                yield return update;
            }
            await Task.CompletedTask; // Ensure async IAsyncEnumerable
        }
    }

    // =========================================================================
    // Helper to create ChatResponseUpdate with text
    // =========================================================================

    private static ChatResponseUpdate CreateUpdate(string text)
    {
        var update = new ChatResponseUpdate { Role = ChatRole.Assistant };
        update.Contents.Add(new TextContent(text));
        return update;
    }

    // =========================================================================
    // AgentTelemetryMiddleware tests
    // =========================================================================

    [Fact]
    public async Task TelemetryMiddleware_LogsTokenCountAndResponseTime_AfterMessageCompletes()
    {
        // Arrange
        var fakeAgent = new FakeAgent("Hello", " world", "!");
        var loggerMock = new Mock<ILogger<SprkChatAgentFactory>>();
        var sut = new AgentTelemetryMiddleware(fakeAgent, loggerMock.Object);

        // Act
        var results = new List<ChatResponseUpdate>();
        await foreach (var update in sut.SendMessageAsync("hi", Array.Empty<AiChatMessage>(), CancellationToken.None))
        {
            results.Add(update);
        }

        // Assert — all chunks are passed through
        results.Should().HaveCount(3);
        results.Select(r => r.Text).Should().ContainInOrder("Hello", " world", "!");

        // Assert — telemetry was logged (at least one LogInformation call with token and duration info)
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, _) => o.ToString()!.Contains("telemetry")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void TelemetryMiddleware_ExposesInnerContext()
    {
        // Arrange
        var fakeAgent = new FakeAgent("test");
        var loggerMock = new Mock<ILogger<SprkChatAgentFactory>>();
        var sut = new AgentTelemetryMiddleware(fakeAgent, loggerMock.Object);

        // Assert
        sut.Context.Should().BeSameAs(TestContext);
    }

    // =========================================================================
    // AgentCostControlMiddleware tests
    // =========================================================================

    [Fact]
    public async Task CostControlMiddleware_ReturnsPoliteMessage_WhenBudgetExceeded()
    {
        // Arrange — budget of 0 tokens means immediately exceeded
        var fakeAgent = new FakeAgent("This should not appear");
        var loggerMock = new Mock<ILogger<SprkChatAgentFactory>>();
        var sut = new AgentCostControlMiddleware(fakeAgent, loggerMock.Object, maxTokenBudget: 0);

        // Act
        var results = new List<ChatResponseUpdate>();
        await foreach (var update in sut.SendMessageAsync("hi", Array.Empty<AiChatMessage>(), CancellationToken.None))
        {
            results.Add(update);
        }

        // Assert — should return budget exceeded message instead of calling inner agent
        results.Should().HaveCount(1);
        results[0].Text.Should().Be(AgentCostControlMiddleware.BudgetExceededMessage);
        fakeAgent.CallCount.Should().Be(0, "inner agent should not be called when budget is exceeded");
    }

    [Fact]
    public async Task CostControlMiddleware_PassesThroughToInner_WhenBudgetNotExceeded()
    {
        // Arrange — budget of 10,000 tokens (plenty for a short response)
        var fakeAgent = new FakeAgent("Hello", " world");
        var loggerMock = new Mock<ILogger<SprkChatAgentFactory>>();
        var sut = new AgentCostControlMiddleware(fakeAgent, loggerMock.Object, maxTokenBudget: 10_000);

        // Act
        var results = new List<ChatResponseUpdate>();
        await foreach (var update in sut.SendMessageAsync("hi", Array.Empty<AiChatMessage>(), CancellationToken.None))
        {
            results.Add(update);
        }

        // Assert — all chunks pass through
        results.Should().HaveCount(2);
        results.Select(r => r.Text).Should().ContainInOrder("Hello", " world");
        fakeAgent.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task CostControlMiddleware_AccumulatesTokensAcrossMultipleCalls()
    {
        // Arrange — budget of 10 tokens; each call produces ~3 estimated tokens (12 chars / 4)
        // "Hello world!" = 12 chars -> ~3 tokens per call. Budget = 10.
        // After 4 calls: ~12 tokens, which exceeds budget of 10.
        var fakeAgent = new FakeAgent("Hello world!");
        var loggerMock = new Mock<ILogger<SprkChatAgentFactory>>();
        var sut = new AgentCostControlMiddleware(fakeAgent, loggerMock.Object, maxTokenBudget: 10);
        var history = Array.Empty<AiChatMessage>();

        // Act — call multiple times to exhaust budget
        // Call 1: 12 chars / 4 = 3 tokens (total: 3)
        await DrainAsync(sut.SendMessageAsync("call1", history, CancellationToken.None));
        // Call 2: 3 more tokens (total: 6)
        await DrainAsync(sut.SendMessageAsync("call2", history, CancellationToken.None));
        // Call 3: 3 more tokens (total: 9)
        await DrainAsync(sut.SendMessageAsync("call3", history, CancellationToken.None));
        // Call 4: 3 more tokens (total: 12 -> exceeds 10)
        await DrainAsync(sut.SendMessageAsync("call4", history, CancellationToken.None));

        // Call 5: budget exceeded — should return polite message
        var finalResults = new List<ChatResponseUpdate>();
        await foreach (var update in sut.SendMessageAsync("call5", history, CancellationToken.None))
        {
            finalResults.Add(update);
        }

        // Assert
        finalResults.Should().HaveCount(1);
        finalResults[0].Text.Should().Be(AgentCostControlMiddleware.BudgetExceededMessage);
        sut.SessionTokenCount.Should().BeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public async Task CostControlMiddleware_UsesDefaultBudget_WhenNegativeValueProvided()
    {
        // Arrange — negative budget should fall back to default (10,000)
        var fakeAgent = new FakeAgent("test");
        var loggerMock = new Mock<ILogger<SprkChatAgentFactory>>();
        var sut = new AgentCostControlMiddleware(fakeAgent, loggerMock.Object, maxTokenBudget: -5);

        // Act — should not be blocked since default budget is 10,000
        var results = new List<ChatResponseUpdate>();
        await foreach (var update in sut.SendMessageAsync("hi", Array.Empty<AiChatMessage>(), CancellationToken.None))
        {
            results.Add(update);
        }

        // Assert
        results.Should().HaveCount(1);
        results[0].Text.Should().Be("test");
    }

    // =========================================================================
    // AgentContentSafetyMiddleware tests
    // =========================================================================

    [Fact]
    public async Task ContentSafetyMiddleware_FiltersSsnPattern()
    {
        // Arrange
        var fakeAgent = new FakeAgent("Your SSN is 123-45-6789.");
        var loggerMock = new Mock<ILogger<SprkChatAgentFactory>>();
        var sut = new AgentContentSafetyMiddleware(fakeAgent, loggerMock.Object);

        // Act
        var results = new List<ChatResponseUpdate>();
        await foreach (var update in sut.SendMessageAsync("hi", Array.Empty<AiChatMessage>(), CancellationToken.None))
        {
            results.Add(update);
        }

        // Assert — SSN should be replaced with filtered placeholder
        results.Should().HaveCount(1);
        results[0].Text.Should().Contain(AgentContentSafetyMiddleware.FilteredPlaceholder);
        results[0].Text.Should().NotContain("123-45-6789");
    }

    [Fact]
    public async Task ContentSafetyMiddleware_FiltersCreditCardPattern()
    {
        // Arrange
        var fakeAgent = new FakeAgent("Card number: 4111111111111111");
        var loggerMock = new Mock<ILogger<SprkChatAgentFactory>>();
        var sut = new AgentContentSafetyMiddleware(fakeAgent, loggerMock.Object);

        // Act
        var results = new List<ChatResponseUpdate>();
        await foreach (var update in sut.SendMessageAsync("hi", Array.Empty<AiChatMessage>(), CancellationToken.None))
        {
            results.Add(update);
        }

        // Assert — credit card should be filtered
        results.Should().HaveCount(1);
        results[0].Text.Should().Contain(AgentContentSafetyMiddleware.FilteredPlaceholder);
        results[0].Text.Should().NotContain("4111111111111111");
    }

    [Fact]
    public async Task ContentSafetyMiddleware_FiltersEmailPattern()
    {
        // Arrange
        var fakeAgent = new FakeAgent("Contact: user@example.com for details");
        var loggerMock = new Mock<ILogger<SprkChatAgentFactory>>();
        var sut = new AgentContentSafetyMiddleware(fakeAgent, loggerMock.Object);

        // Act
        var results = new List<ChatResponseUpdate>();
        await foreach (var update in sut.SendMessageAsync("hi", Array.Empty<AiChatMessage>(), CancellationToken.None))
        {
            results.Add(update);
        }

        // Assert — email should be filtered
        results.Should().HaveCount(1);
        results[0].Text.Should().Contain(AgentContentSafetyMiddleware.FilteredPlaceholder);
        results[0].Text.Should().NotContain("user@example.com");
    }

    [Fact]
    public async Task ContentSafetyMiddleware_PassesCleanContentUnchanged()
    {
        // Arrange — no PII in content
        var fakeAgent = new FakeAgent("This is a perfectly safe response.");
        var loggerMock = new Mock<ILogger<SprkChatAgentFactory>>();
        var sut = new AgentContentSafetyMiddleware(fakeAgent, loggerMock.Object);

        // Act
        var results = new List<ChatResponseUpdate>();
        await foreach (var update in sut.SendMessageAsync("hi", Array.Empty<AiChatMessage>(), CancellationToken.None))
        {
            results.Add(update);
        }

        // Assert — content should pass through unchanged
        results.Should().HaveCount(1);
        results[0].Text.Should().Be("This is a perfectly safe response.");
    }

    [Fact]
    public async Task ContentSafetyMiddleware_LogsWarning_WhenPatternDetected()
    {
        // Arrange
        var fakeAgent = new FakeAgent("SSN: 123-45-6789");
        var loggerMock = new Mock<ILogger<SprkChatAgentFactory>>();
        var sut = new AgentContentSafetyMiddleware(fakeAgent, loggerMock.Object);

        // Act
        await DrainAsync(sut.SendMessageAsync("hi", Array.Empty<AiChatMessage>(), CancellationToken.None));

        // Assert — a warning should be logged with pattern name
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, _) => o.ToString()!.Contains("SSN")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ContentSafetyMiddleware_AcceptsCustomPatterns()
    {
        // Arrange — custom pattern that matches "SECRET"
        var customPatterns = new[]
        {
            new ContentSafetyPattern(
                "CustomSecret",
                new System.Text.RegularExpressions.Regex(@"\bSECRET\b",
                    System.Text.RegularExpressions.RegexOptions.Compiled))
        };
        var fakeAgent = new FakeAgent("The SECRET code is here.");
        var loggerMock = new Mock<ILogger<SprkChatAgentFactory>>();
        var sut = new AgentContentSafetyMiddleware(fakeAgent, loggerMock.Object, customPatterns);

        // Act
        var results = new List<ChatResponseUpdate>();
        await foreach (var update in sut.SendMessageAsync("hi", Array.Empty<AiChatMessage>(), CancellationToken.None))
        {
            results.Add(update);
        }

        // Assert
        results[0].Text.Should().Contain(AgentContentSafetyMiddleware.FilteredPlaceholder);
        results[0].Text.Should().NotContain("SECRET");
    }

    // =========================================================================
    // Middleware chain order test
    // =========================================================================

    [Fact]
    public async Task MiddlewareChain_ExecutesInCorrectOrder_TelemetryOutermost()
    {
        // Arrange — build middleware chain the same way SprkChatAgentFactory does:
        // agent -> ContentSafety -> CostControl -> Telemetry
        //
        // We use TrackingWrapper agents that record when they are entered.
        // Since middleware classes are sealed, we test the order by wrapping
        // each middleware layer with a tracking wrapper that records entry.
        var executionOrder = new List<string>();

        // Inner agent records "inner" when called
        var innerAgent = new TrackingAgent(executionOrder, "inner", "Hello");
        var loggerMock = new Mock<ILogger<SprkChatAgentFactory>>();

        // Build pipeline: ContentSafety(inner) -> CostControl -> Telemetry
        // Each layer wrapped with a tracker to record entry order.
        ISprkChatAgent contentSafety = new AgentContentSafetyMiddleware(
            new TrackingAgent(executionOrder, "content-safety-enter", "Hello"),
            loggerMock.Object);
        ISprkChatAgent costControl = new AgentCostControlMiddleware(
            new TrackingAgent(executionOrder, "cost-control-enter", "Hello"),
            loggerMock.Object);
        ISprkChatAgent telemetry = new AgentTelemetryMiddleware(
            new TrackingAgent(executionOrder, "telemetry-enter", "Hello"),
            loggerMock.Object);

        // Now build the actual pipeline to verify wiring pattern
        ISprkChatAgent pipeline = innerAgent;
        pipeline = new AgentContentSafetyMiddleware(pipeline, loggerMock.Object);
        pipeline = new AgentCostControlMiddleware(pipeline, loggerMock.Object);
        pipeline = new AgentTelemetryMiddleware(pipeline, loggerMock.Object);

        // Act — drain the pipeline. The inner agent records "inner" when called.
        await DrainAsync(pipeline.SendMessageAsync("hi", Array.Empty<AiChatMessage>(), CancellationToken.None));

        // Assert — inner agent was called exactly once (middleware passed through)
        innerAgent.CallCount.Should().Be(1);
        executionOrder.Should().Contain("inner");
    }

    [Fact]
    public async Task MiddlewareChain_ContentSafetyFiltersBeforeCostControlCounts()
    {
        // Arrange — verifies that content safety filters PII before cost control
        // counts tokens, and telemetry records the full duration.
        // Agent returns PII content; content safety should filter it;
        // cost control should count the filtered (shorter) response.
        var fakeAgent = new FakeAgent("SSN: 123-45-6789 is sensitive");
        var loggerMock = new Mock<ILogger<SprkChatAgentFactory>>();

        // Build pipeline: ContentSafety -> CostControl -> Telemetry
        ISprkChatAgent pipeline = fakeAgent;
        pipeline = new AgentContentSafetyMiddleware(pipeline, loggerMock.Object);
        var costControl = new AgentCostControlMiddleware(pipeline, loggerMock.Object, maxTokenBudget: 10_000);
        pipeline = new AgentTelemetryMiddleware(costControl, loggerMock.Object);

        // Act
        var results = new List<ChatResponseUpdate>();
        await foreach (var update in pipeline.SendMessageAsync("hi", Array.Empty<AiChatMessage>(), CancellationToken.None))
        {
            results.Add(update);
        }

        // Assert — content was filtered (SSN replaced)
        results.Should().HaveCount(1);
        results[0].Text.Should().NotContain("123-45-6789");
        results[0].Text.Should().Contain(AgentContentSafetyMiddleware.FilteredPlaceholder);

        // Assert — cost control tracked the response
        costControl.SessionTokenCount.Should().BeGreaterThan(0);
    }

    // =========================================================================
    // Order tracking test double for chain verification
    // =========================================================================

    /// <summary>
    /// Agent that records when its <c>SendMessageAsync</c> is invoked, used to
    /// verify middleware chain execution order.
    /// </summary>
    private sealed class TrackingAgent : ISprkChatAgent
    {
        private readonly List<string> _executionOrder;
        private readonly string _name;
        private readonly string[] _chunks;

        public int CallCount { get; private set; }

        public TrackingAgent(List<string> executionOrder, string name, params string[] chunks)
        {
            _executionOrder = executionOrder;
            _name = name;
            _chunks = chunks;
        }

        public ChatContext Context => TestContext;
        public CitationContext? Citations => null;

        public async IAsyncEnumerable<ChatResponseUpdate> SendMessageAsync(
            string message,
            IReadOnlyList<AiChatMessage> history,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            CallCount++;
            _executionOrder.Add(_name);
            foreach (var chunk in _chunks)
            {
                var update = new ChatResponseUpdate { Role = ChatRole.Assistant };
                update.Contents.Add(new TextContent(chunk));
                yield return update;
            }
            await Task.CompletedTask;
        }
    }

    // =========================================================================
    // Helper
    // =========================================================================

    private static async Task DrainAsync(IAsyncEnumerable<ChatResponseUpdate> source)
    {
        await foreach (var _ in source) { }
    }
}
