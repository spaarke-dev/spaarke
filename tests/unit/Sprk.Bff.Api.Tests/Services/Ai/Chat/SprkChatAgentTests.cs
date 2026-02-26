using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

// Explicit alias to resolve ChatMessage ambiguity between the Dataverse domain model
// (Sprk.Bff.Api.Models.Ai.Chat.ChatMessage) and the Agent Framework type
// (Microsoft.Extensions.AI.ChatMessage). Both namespaces are required:
// - Models.Ai.Chat provides ChatContext (domain model)
// - Microsoft.Extensions.AI provides IChatClient, ChatMessage, ChatRole for LLM calls
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for SprkChatAgent.
/// Verifies streaming, system prompt injection, history handling, and tool configuration.
/// </summary>
public class SprkChatAgentTests
{
    private readonly Mock<IChatClient> _chatClientMock;
    private readonly Mock<ILogger<SprkChatAgent>> _loggerMock;

    private static readonly Guid TestPlaybookId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public SprkChatAgentTests()
    {
        _chatClientMock = new Mock<IChatClient>();
        _loggerMock = new Mock<ILogger<SprkChatAgent>>();
    }

    #region Context property tests

    [Fact]
    public void Context_ReturnsInjectedChatContext()
    {
        // Arrange
        var context = CreateContext("You are a legal analyst.");
        var agent = CreateAgent(context);

        // Act & Assert
        agent.Context.Should().BeSameAs(context);
    }

    [Fact]
    public void Context_PlaybookId_MatchesInjectedContext()
    {
        // Arrange
        var context = CreateContext("System prompt.", playbookId: TestPlaybookId);
        var agent = CreateAgent(context);

        // Act & Assert
        agent.Context.PlaybookId.Should().Be(TestPlaybookId);
    }

    #endregion

    #region SendMessageAsync tests

    [Fact]
    public async Task SendMessageAsync_ThrowsArgumentException_WhenMessageIsEmpty()
    {
        // Arrange
        var agent = CreateAgent(CreateContext("System prompt."));

        // Act
        var action = async () =>
        {
            await foreach (var _ in agent.SendMessageAsync("", [], CancellationToken.None)) { }
        };

        // Assert
        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*message*");
    }

    [Fact]
    public async Task SendMessageAsync_ThrowsArgumentException_WhenMessageIsWhitespace()
    {
        // Arrange
        var agent = CreateAgent(CreateContext("System prompt."));

        // Act
        var action = async () =>
        {
            await foreach (var _ in agent.SendMessageAsync("   ", [], CancellationToken.None)) { }
        };

        // Assert
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendMessageAsync_CallsCompleteStreamingAsync_WithSystemMessageFirst()
    {
        // Arrange
        const string systemPrompt = "You are a legal document analyst.";
        var context = CreateContext(systemPrompt);
        var agent = CreateAgent(context);

        IReadOnlyList<AiChatMessage>? capturedMessages = null;

        _chatClientMock
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AiChatMessage>, ChatOptions?, CancellationToken>(
                (msgs, _, _) => capturedMessages = msgs.ToList())
            .Returns(AsyncEnumerableEmpty<ChatResponseUpdate>());

        // Act
        await foreach (var _ in agent.SendMessageAsync("What is in this contract?", [], CancellationToken.None)) { }

        // Assert
        capturedMessages.Should().NotBeNull();
        capturedMessages!.First().Role.Should().Be(ChatRole.System);
        capturedMessages!.First().Text.Should().Contain(systemPrompt);
    }

    [Fact]
    public async Task SendMessageAsync_AppendsUserMessageLast()
    {
        // Arrange
        const string userMessage = "Summarize the key clauses.";
        var context = CreateContext("System prompt.");
        var agent = CreateAgent(context);

        IReadOnlyList<AiChatMessage>? capturedMessages = null;

        _chatClientMock
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AiChatMessage>, ChatOptions?, CancellationToken>(
                (msgs, _, _) => capturedMessages = msgs.ToList())
            .Returns(AsyncEnumerableEmpty<ChatResponseUpdate>());

        // Act
        await foreach (var _ in agent.SendMessageAsync(userMessage, [], CancellationToken.None)) { }

        // Assert
        capturedMessages.Should().NotBeNull();
        capturedMessages!.Last().Role.Should().Be(ChatRole.User);
        capturedMessages!.Last().Text.Should().Be(userMessage);
    }

    [Fact]
    public async Task SendMessageAsync_IncludesHistoryBetweenSystemAndUser()
    {
        // Arrange
        var context = CreateContext("System prompt.");
        var agent = CreateAgent(context);

        var history = new List<AiChatMessage>
        {
            new AiChatMessage(ChatRole.User, "First question"),
            new AiChatMessage(ChatRole.Assistant, "First answer")
        };

        IReadOnlyList<AiChatMessage>? capturedMessages = null;

        _chatClientMock
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AiChatMessage>, ChatOptions?, CancellationToken>(
                (msgs, _, _) => capturedMessages = msgs.ToList())
            .Returns(AsyncEnumerableEmpty<ChatResponseUpdate>());

        // Act
        await foreach (var _ in agent.SendMessageAsync("Second question", history, CancellationToken.None)) { }

        // Assert
        // Layout: [system] + [2 history] + [user] = 4 total
        capturedMessages.Should().HaveCount(4);
        capturedMessages![0].Role.Should().Be(ChatRole.System);
        capturedMessages[1].Text.Should().Be("First question");
        capturedMessages[2].Text.Should().Be("First answer");
        capturedMessages[3].Role.Should().Be(ChatRole.User);
        capturedMessages[3].Text.Should().Be("Second question");
    }

    [Fact]
    public async Task SendMessageAsync_InjectsDocumentSummary_IntoSystemMessage_WhenPresent()
    {
        // Arrange
        const string systemPrompt = "You are a legal analyst.";
        const string documentSummary = "This is a software license agreement dated 2025.";

        var context = new ChatContext(
            SystemPrompt: systemPrompt,
            DocumentSummary: documentSummary,
            AnalysisMetadata: null,
            PlaybookId: TestPlaybookId);

        var agent = CreateAgent(context);

        IReadOnlyList<AiChatMessage>? capturedMessages = null;

        _chatClientMock
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AiChatMessage>, ChatOptions?, CancellationToken>(
                (msgs, _, _) => capturedMessages = msgs.ToList())
            .Returns(AsyncEnumerableEmpty<ChatResponseUpdate>());

        // Act
        await foreach (var _ in agent.SendMessageAsync("What are the key terms?", [], CancellationToken.None)) { }

        // Assert
        capturedMessages.Should().NotBeNull();
        var systemText = capturedMessages!.First().Text;
        systemText.Should().Contain(systemPrompt);
        systemText.Should().Contain(documentSummary);
        systemText.Should().Contain("Current Document Context");
    }

    [Fact]
    public async Task SendMessageAsync_DoesNotIncludeDocumentContextBlock_WhenSummaryIsNull()
    {
        // Arrange
        var context = new ChatContext(
            SystemPrompt: "Analyze documents.",
            DocumentSummary: null,
            AnalysisMetadata: null,
            PlaybookId: TestPlaybookId);

        var agent = CreateAgent(context);

        IReadOnlyList<AiChatMessage>? capturedMessages = null;

        _chatClientMock
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AiChatMessage>, ChatOptions?, CancellationToken>(
                (msgs, _, _) => capturedMessages = msgs.ToList())
            .Returns(AsyncEnumerableEmpty<ChatResponseUpdate>());

        // Act
        await foreach (var _ in agent.SendMessageAsync("Hello", [], CancellationToken.None)) { }

        // Assert
        capturedMessages.Should().NotBeNull();
        var systemText = capturedMessages!.First().Text;
        systemText.Should().NotContain("Current Document Context");
    }

    [Fact]
    public async Task SendMessageAsync_PassesNullOptions_WhenNoToolsRegistered()
    {
        // Arrange
        var context = CreateContext("System prompt.");
        // No tools — empty list
        var agent = new SprkChatAgent(_chatClientMock.Object, context, [], citationContext: null, _loggerMock.Object);

        ChatOptions? capturedOptions = null;

        _chatClientMock
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AiChatMessage>, ChatOptions?, CancellationToken>(
                (_, opts, _) => capturedOptions = opts)
            .Returns(AsyncEnumerableEmpty<ChatResponseUpdate>());

        // Act
        await foreach (var _ in agent.SendMessageAsync("Test", [], CancellationToken.None)) { }

        // Assert — null options avoids overhead when no tools are registered
        capturedOptions.Should().BeNull();
    }

    [Fact]
    public async Task SendMessageAsync_PassesToolsInOptions_WhenToolsRegistered()
    {
        // Arrange
        var context = CreateContext("System prompt.");
        var mockTool = AIFunctionFactory.Create(() => "tool result", "TestTool", "A test tool");
        var agent = new SprkChatAgent(_chatClientMock.Object, context, [mockTool], citationContext: null, _loggerMock.Object);

        ChatOptions? capturedOptions = null;

        _chatClientMock
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AiChatMessage>, ChatOptions?, CancellationToken>(
                (_, opts, _) => capturedOptions = opts)
            .Returns(AsyncEnumerableEmpty<ChatResponseUpdate>());

        // Act
        await foreach (var _ in agent.SendMessageAsync("Test", [], CancellationToken.None)) { }

        // Assert
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Tools.Should().HaveCount(1);
        capturedOptions.ToolMode.Should().Be(ChatToolMode.Auto);
    }

    [Fact]
    public async Task SendMessageAsync_StreamsAllUpdates_FromChatClient()
    {
        // Arrange
        var context = CreateContext("System prompt.");
        var agent = CreateAgent(context);

        var expectedUpdates = new[]
        {
            CreateStreamingUpdate("Hello "),
            CreateStreamingUpdate("world!"),
        };

        _chatClientMock
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(expectedUpdates));

        // Act
        var receivedUpdates = new List<ChatResponseUpdate>();
        await foreach (var update in agent.SendMessageAsync("Hi", [], CancellationToken.None))
        {
            receivedUpdates.Add(update);
        }

        // Assert
        receivedUpdates.Should().HaveCount(2);
    }

    #endregion

    #region Private helpers

    private SprkChatAgent CreateAgent(ChatContext context, IReadOnlyList<AIFunction>? tools = null, CitationContext? citationContext = null)
        => new SprkChatAgent(_chatClientMock.Object, context, tools ?? [], citationContext, _loggerMock.Object);

    private static ChatContext CreateContext(
        string systemPrompt,
        string? documentSummary = null,
        Guid? playbookId = null)
        => new ChatContext(
            SystemPrompt: systemPrompt,
            DocumentSummary: documentSummary,
            AnalysisMetadata: null,
            PlaybookId: playbookId ?? TestPlaybookId);

    /// <summary>Returns an empty async enumerable of T.</summary>
    private static async IAsyncEnumerable<T> AsyncEnumerableEmpty<T>()
    {
        await Task.CompletedTask;
        yield break;
    }

    /// <summary>Wraps an array of T as an IAsyncEnumerable.</summary>
    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    private static ChatResponseUpdate CreateStreamingUpdate(string text)
    {
        return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(text)]
        };
    }

    #endregion
}
