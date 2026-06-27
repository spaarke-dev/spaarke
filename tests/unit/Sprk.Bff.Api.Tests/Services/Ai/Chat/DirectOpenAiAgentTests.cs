using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

// Explicit alias: resolves ChatMessage ambiguity between domain model and AI SDK types.
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="DirectOpenAiAgent"/>.
///
/// Tests cover:
///   - Provider identity (ProviderId, SupportsStreaming)
///   - Streaming tokens emitted in order
///   - "done" always emitted as last event
///   - Empty tool list handled gracefully (no crashes)
///   - Cancellation token respected (no tokens after cancellation)
///   - Exception during startup → "error" + "done" emitted cleanly (ADR-019)
///   - Exception during streaming → "error" + "done" emitted cleanly (ADR-019)
///   - Content filter finish reason → "error" + "done" emitted cleanly
/// </summary>
[Trait("status", "repaired")]
public class DirectOpenAiAgentTests
{
    private readonly Mock<IChatClient> _chatClientMock;
    private readonly Mock<IOrchestratorPromptBuilder> _promptBuilderMock;
    private readonly Mock<ILogger<DirectOpenAiAgent>> _loggerMock;

    public DirectOpenAiAgentTests()
    {
        _chatClientMock = new Mock<IChatClient>();
        _promptBuilderMock = new Mock<IOrchestratorPromptBuilder>();
        _loggerMock = new Mock<ILogger<DirectOpenAiAgent>>();

        // Default prompt builder setup: return a minimal OrchestratorPrompt.
        _promptBuilderMock
            .Setup(b => b.BuildSystemPrompt(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<OrchestratorPromptContext>()))
            .Returns(new OrchestratorPrompt(
                SystemPromptPrefix: "You are Spaarke AI.",
                PerTurnSuffix: string.Empty,
                ToolSchemaNames: [],
                EstimatedTokens: 6,
                PrefixCacheHit: false));
    }

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void ProviderId_Returns_AzureOpenAiDirect()
    {
        var agent = CreateAgent();
        agent.ProviderId.Should().Be("azure-openai-direct");
    }

    [Fact]
    public void SupportsStreaming_Returns_True()
    {
        var agent = CreateAgent();
        agent.SupportsStreaming.Should().BeTrue();
    }

    // ── Streaming — happy path ────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_EmitsTokenEventsInOrder_ThenDone()
    {
        // Arrange
        var tokens = new[] { "Hello", " world", "!" };
        SetupStreamingResponse(tokens);

        var agent = CreateAgent();
        var request = CreateRequest("What is the contract value?");

        // Act
        var events = await CollectEventsAsync(agent, request);

        // Assert
        var tokenEvents = events.Where(e => e.Type == "token").ToList();
        tokenEvents.Should().HaveCount(3);
        tokenEvents[0].Data.GetString().Should().Be("Hello");
        tokenEvents[1].Data.GetString().Should().Be(" world");
        tokenEvents[2].Data.GetString().Should().Be("!");

        events.Last().Type.Should().Be("done");
    }

    [Fact]
    public async Task ProcessAsync_DoneIsAlwaysLastEvent_WhenStreamSucceeds()
    {
        // Arrange
        SetupStreamingResponse(["Hello"]);

        var agent = CreateAgent();
        var request = CreateRequest("Test message");

        // Act
        var events = await CollectEventsAsync(agent, request);

        // Assert
        events.Last().Type.Should().Be("done");
    }

    [Fact]
    public async Task ProcessAsync_EmitsOnlyDoneEvent_WhenStreamHasNoTextContent()
    {
        // Arrange — stream with no text content (e.g., empty update)
        SetupStreamingResponseWithUpdates([CreateUpdateWithNoText()]);

        var agent = CreateAgent();
        var request = CreateRequest("Test");

        // Act
        var events = await CollectEventsAsync(agent, request);

        // Assert
        events.Should().ContainSingle();
        events.Single().Type.Should().Be("done");
    }

    // ── Empty tool list ───────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_HandlesEmptyToolList_WithoutError()
    {
        // Arrange — request with no requested capabilities (null)
        SetupStreamingResponse(["Response text"]);

        var agent = CreateAgent();
        var request = CreateRequest("Tell me about this matter.", requestedCapabilities: null);

        // Act
        var events = await CollectEventsAsync(agent, request);

        // Assert: should get one token + done, no error
        events.Should().Contain(e => e.Type == "token");
        events.Should().Contain(e => e.Type == "done");
        events.Should().NotContain(e => e.Type == "error");
    }

    [Fact]
    public async Task ProcessAsync_HandlesEmptyConversationHistory_WithoutError()
    {
        // Arrange — first turn with no history
        SetupStreamingResponse(["First response"]);

        var agent = CreateAgent();
        var request = new AgentRequest(
            SessionId: "session-001",
            UserId: "user-001",
            TenantId: "tenant-001",
            UserMessage: "Hello!",
            ConversationHistory: []);

        // Act
        var events = await CollectEventsAsync(agent, request);

        // Assert
        events.Should().Contain(e => e.Type == "token");
        events.Last().Type.Should().Be("done");
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_StopsEmitting_WhenCancellationTriggered()
    {
        // Arrange — stream that yields many tokens but we cancel after 1.
        // Per DirectOpenAiAgent.cs:176, the producer calls cancellationToken.ThrowIfCancellationRequested()
        // inside the streaming loop, so a triggered cancellation surfaces as
        // OperationCanceledException to the caller (standard .NET cooperative-cancellation
        // contract). The behavioral guarantee is: streaming halts EARLY (fewer than the full
        // 10 tokens) once the token is cancelled, not that the producer silently completes.
        using var cts = new CancellationTokenSource();

        var updates = CreateManyTokenUpdates(10);
        SetupStreamingResponseWithUpdates(updates, cts.Token);

        var agent = CreateAgent();
        var request = CreateRequest("Long question");

        // Act — cancel after collecting first token event
        var events = new List<SseEvent>();
        try
        {
            await foreach (var evt in agent.ProcessAsync(request, cts.Token))
            {
                events.Add(evt);
                if (evt.Type == "token")
                    cts.Cancel();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected: cooperative cancellation propagates from the producer.
        }

        // Assert: cancelled early — fewer than 10 tokens
        events.Count(e => e.Type == "token").Should().BeLessThan(10);
    }

    // ── Error handling (ADR-019) ──────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_EmitsErrorThenDone_WhenChatClientThrows()
    {
        // Arrange — chat client throws on streaming call
        _chatClientMock
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("Azure OpenAI service unavailable"));

        var agent = CreateAgent();
        var request = CreateRequest("Test");

        // Act
        var events = await CollectEventsAsync(agent, request);

        // Assert
        events.Should().Contain(e => e.Type == "error");
        events.Last().Type.Should().Be("done");
    }

    [Fact]
    public async Task ProcessAsync_EmitsErrorThenDone_WhenPromptBuilderThrows()
    {
        // Arrange — prompt builder throws
        _promptBuilderMock
            .Setup(b => b.BuildSystemPrompt(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<OrchestratorPromptContext>()))
            .Throws(new ArgumentNullException("activeToolNames"));

        var agent = CreateAgent();
        var request = CreateRequest("Test");

        // Act
        var events = await CollectEventsAsync(agent, request);

        // Assert: error and done emitted; no exception propagated
        events.Should().Contain(e => e.Type == "error");
        events.Last().Type.Should().Be("done");
    }

    [Fact]
    public async Task ProcessAsync_NeverThrowsException_WhenChatClientFails()
    {
        // Arrange
        _chatClientMock
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new HttpRequestException("Network error"));

        var agent = CreateAgent();
        var request = CreateRequest("Test");

        // Act — must not throw; all errors communicated via SSE events (ADR-019)
        Func<Task> act = async () => await CollectEventsAsync(agent, request);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ProcessAsync_EmitsErrorThenDone_WhenContentFilterTriggered()
    {
        // Arrange — stream ends with ContentFilter finish reason
        SetupStreamingResponseWithUpdates([CreateContentFilterUpdate()]);

        var agent = CreateAgent();
        var request = CreateRequest("Sensitive question");

        // Act
        var events = await CollectEventsAsync(agent, request);

        // Assert
        events.Should().Contain(e => e.Type == "error");
        events.Last().Type.Should().Be("done");
    }

    // ── Conversation history mapping ──────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_IncludesConversationHistory_InChatClientCall()
    {
        // Arrange — request with 2-turn history
        SetupStreamingResponse(["Answer"]);

        IEnumerable<AiChatMessage>? capturedMessages = null;
        _chatClientMock
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AiChatMessage>, ChatOptions?, CancellationToken>(
                (msgs, _, _) => capturedMessages = msgs)
            .Returns(CreateStreamEnumerable(["Answer"]));

        var agent = CreateAgent();
        var request = CreateRequest(
            "Follow-up question",
            history:
            [
                new ConversationTurn(AgentRole.User,      "First question",   DateTimeOffset.UtcNow.AddMinutes(-2)),
                new ConversationTurn(AgentRole.Assistant, "First answer",     DateTimeOffset.UtcNow.AddMinutes(-1))
            ]);

        // Act
        await CollectEventsAsync(agent, request);

        // Assert: system + 2 history + 1 user = 4 messages total
        capturedMessages.Should().NotBeNull();
        var msgList = capturedMessages!.ToList();
        msgList.Should().HaveCount(4);
        msgList[0].Role.Should().Be(ChatRole.System);
        msgList[1].Role.Should().Be(ChatRole.User);
        msgList[2].Role.Should().Be(ChatRole.Assistant);
        msgList[3].Role.Should().Be(ChatRole.User);
        msgList[3].Text.Should().Be("Follow-up question");
    }

    [Fact]
    public async Task ProcessAsync_SystemMessageIsFirst_InChatClientCall()
    {
        // Arrange
        SetupStreamingResponse(["Response"]);

        IEnumerable<AiChatMessage>? capturedMessages = null;
        _chatClientMock
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AiChatMessage>, ChatOptions?, CancellationToken>(
                (msgs, _, _) => capturedMessages = msgs)
            .Returns(CreateStreamEnumerable(["Response"]));

        var agent = CreateAgent();
        var request = CreateRequest("User message");

        // Act
        await CollectEventsAsync(agent, request);

        // Assert
        capturedMessages!.First().Role.Should().Be(ChatRole.System);
    }

    // ── Prompt builder integration ────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_CallsPromptBuilder_WithEmptyToolNames()
    {
        // Arrange
        SetupStreamingResponse(["ok"]);

        IReadOnlyList<string>? capturedToolNames = null;
        _promptBuilderMock
            .Setup(b => b.BuildSystemPrompt(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<OrchestratorPromptContext>()))
            .Callback<IReadOnlyList<string>, OrchestratorPromptContext>(
                (toolNames, _) => capturedToolNames = toolNames)
            .Returns(new OrchestratorPrompt("sys", "", [], 2, false));

        var agent = CreateAgent();
        var request = CreateRequest("Test");

        // Act
        await CollectEventsAsync(agent, request);

        // Assert: DirectOpenAiAgent does not surface tools — passes an empty list.
        capturedToolNames.Should().NotBeNull();
        capturedToolNames!.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_PassesTenantId_InPromptContext()
    {
        // Arrange
        SetupStreamingResponse(["ok"]);

        OrchestratorPromptContext? capturedContext = null;
        _promptBuilderMock
            .Setup(b => b.BuildSystemPrompt(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<OrchestratorPromptContext>()))
            .Callback<IReadOnlyList<string>, OrchestratorPromptContext>(
                (_, ctx) => capturedContext = ctx)
            .Returns(new OrchestratorPrompt("sys", "", [], 2, false));

        var agent = CreateAgent();
        var request = CreateRequest("Test", tenantId: "tenant-xyz");

        // Act
        await CollectEventsAsync(agent, request);

        // Assert
        capturedContext.Should().NotBeNull();
        capturedContext!.TenantId.Should().Be("tenant-xyz");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private DirectOpenAiAgent CreateAgent() =>
        new(_chatClientMock.Object, _promptBuilderMock.Object, _loggerMock.Object);

    private static AgentRequest CreateRequest(
        string userMessage,
        string sessionId = "session-001",
        string userId = "user-001",
        string tenantId = "tenant-001",
        IReadOnlyList<ConversationTurn>? history = null,
        IReadOnlyList<string>? requestedCapabilities = null) =>
        new(
            SessionId: sessionId,
            UserId: userId,
            TenantId: tenantId,
            UserMessage: userMessage,
            ConversationHistory: history ?? [],
            ContextDocuments: null,
            RequestedCapabilities: requestedCapabilities);

    private void SetupStreamingResponse(IEnumerable<string> tokens)
    {
        _chatClientMock
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateStreamEnumerable(tokens));
    }

    private void SetupStreamingResponseWithUpdates(
        IEnumerable<ChatResponseUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        _chatClientMock
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateStreamEnumerableFromUpdates(updates, cancellationToken));
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateStreamEnumerable(
        IEnumerable<string> tokens,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new TextContent(token)]
            };
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateStreamEnumerableFromUpdates(
        IEnumerable<ChatResponseUpdate> updates,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
            await Task.Yield();
        }
    }

    private static ChatResponseUpdate CreateUpdateWithNoText() =>
        new()
        {
            Role = ChatRole.Assistant,
            Contents = []
        };

    private static ChatResponseUpdate CreateContentFilterUpdate() =>
        new()
        {
            Role = ChatRole.Assistant,
            Contents = [],
            FinishReason = ChatFinishReason.ContentFilter
        };

    private static IEnumerable<ChatResponseUpdate> CreateManyTokenUpdates(int count) =>
        Enumerable.Range(0, count).Select(i => new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent($"token-{i}")]
        });

    private static async Task<List<SseEvent>> CollectEventsAsync(
        ISprkAgent agent,
        AgentRequest request,
        CancellationToken cancellationToken = default)
    {
        var events = new List<SseEvent>();
        await foreach (var evt in agent.ProcessAsync(request, cancellationToken))
            events.Add(evt);
        return events;
    }
}
