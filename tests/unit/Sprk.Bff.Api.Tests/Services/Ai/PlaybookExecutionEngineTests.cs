using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for PlaybookExecutionEngine.
/// Tests both conversational and batch execution modes.
/// </summary>
public class PlaybookExecutionEngineTests
{
    private readonly Mock<IAiPlaybookBuilderService> _builderServiceMock;
    private readonly Mock<IPlaybookOrchestrationService> _orchestrationServiceMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly Mock<ILogger<PlaybookExecutionEngine>> _loggerMock;
    private readonly PlaybookExecutionEngine _engine;

    public PlaybookExecutionEngineTests()
    {
        _builderServiceMock = new Mock<IAiPlaybookBuilderService>();
        _orchestrationServiceMock = new Mock<IPlaybookOrchestrationService>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _loggerMock = new Mock<ILogger<PlaybookExecutionEngine>>();

        _engine = new PlaybookExecutionEngine(
            _builderServiceMock.Object,
            _orchestrationServiceMock.Object,
            _httpContextAccessorMock.Object,
            _loggerMock.Object);
    }

    private static ConversationContext CreateConversationContext(
        string message,
        ConversationMessage[]? history = null,
        CanvasNode[]? nodes = null) => new()
    {
        CurrentMessage = message,
        History = history ?? [],
        SessionState = new SessionState
        {
            SessionId = Guid.NewGuid().ToString("N"),
            CanvasState = new CanvasState
            {
                Nodes = nodes ?? [],
                Edges = []
            }
        }
    };

    private static ConversationMessage CreateHistoryMessage(ConversationRole role, string content) => new()
    {
        Role = role,
        Content = content,
        Timestamp = DateTimeOffset.UtcNow
    };

    private static CanvasNode CreateNode(string type, string? label = null) => new()
    {
        Id = Guid.NewGuid().ToString("N")[..8],
        Type = type,
        Label = label ?? $"Test {type} node"
    };

    #region ExecuteConversationalAsync Tests

    [Fact]
    public async Task ExecuteConversationalAsync_ValidContext_ReturnsResults()
    {
        // Arrange
        var context = CreateConversationContext("Add a node");

        _builderServiceMock
            .Setup(x => x.ProcessMessageAsync(
                It.IsAny<BuilderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateMockBuilderStream());

        // Act
        var results = new List<BuilderResult>();
        await foreach (var result in _engine.ExecuteConversationalAsync(context, CancellationToken.None))
        {
            results.Add(result);
        }

        // Assert
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.Type == BuilderResultType.Thinking);
        results.Should().Contain(r => r.Type == BuilderResultType.StateUpdate);
    }

    [Fact]
    public async Task ExecuteConversationalAsync_WithHistory_PassesHistoryToBuilder()
    {
        // Arrange
        var history = new[]
        {
            CreateHistoryMessage(ConversationRole.User, "Create a playbook"),
            CreateHistoryMessage(ConversationRole.Assistant, "I'll create that for you.")
        };
        var context = CreateConversationContext("Add an action node", history);

        _builderServiceMock
            .Setup(x => x.ProcessMessageAsync(
                It.IsAny<BuilderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateMockBuilderStream());

        // Act
        var results = new List<BuilderResult>();
        await foreach (var result in _engine.ExecuteConversationalAsync(context, CancellationToken.None))
        {
            results.Add(result);
        }

        // Assert
        _builderServiceMock.Verify(
            x => x.ProcessMessageAsync(
                It.Is<BuilderRequest>(r =>
                    r.ChatHistory != null &&
                    r.ChatHistory.Length == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteConversationalAsync_WithCanvasState_PassesStateToBuilder()
    {
        // Arrange
        var nodes = new[] { CreateNode("action", "Test Action") };
        var context = CreateConversationContext("Modify the action", nodes: nodes);

        _builderServiceMock
            .Setup(x => x.ProcessMessageAsync(
                It.IsAny<BuilderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateMockBuilderStream());

        // Act
        var results = new List<BuilderResult>();
        await foreach (var result in _engine.ExecuteConversationalAsync(context, CancellationToken.None))
        {
            results.Add(result);
        }

        // Assert
        _builderServiceMock.Verify(
            x => x.ProcessMessageAsync(
                It.Is<BuilderRequest>(r =>
                    r.CanvasState.Nodes.Length == 1 &&
                    r.CanvasState.Nodes[0].Type == "action"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteConversationalAsync_NullContext_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in _engine.ExecuteConversationalAsync(null!, CancellationToken.None))
            {
                // Should not reach here
            }
        });
    }

    [Fact]
    public async Task ExecuteConversationalAsync_EmptyMessage_ThrowsArgumentException()
    {
        // Arrange
        var context = new ConversationContext
        {
            CurrentMessage = "",
            SessionState = new SessionState
            {
                SessionId = "test",
                CanvasState = new CanvasState { Nodes = [], Edges = [] }
            }
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in _engine.ExecuteConversationalAsync(context, CancellationToken.None))
            {
                // Should not reach here
            }
        });
    }

    [Fact]
    public async Task ExecuteConversationalAsync_BuilderReturnsCanvasOperation_ConvertsToBuilderResult()
    {
        // Arrange
        var context = CreateConversationContext("Add a node");
        var patch = new CanvasPatch
        {
            Operation = CanvasPatchOperation.AddNode,
            Node = CreateNode("action")
        };

        _builderServiceMock
            .Setup(x => x.ProcessMessageAsync(
                It.IsAny<BuilderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateMockBuilderStreamWithOperation(patch));

        // Act
        var results = new List<BuilderResult>();
        await foreach (var result in _engine.ExecuteConversationalAsync(context, CancellationToken.None))
        {
            results.Add(result);
        }

        // Assert
        results.Should().Contain(r => r.Type == BuilderResultType.CanvasOperation);
        var operationResult = results.First(r => r.Type == BuilderResultType.CanvasOperation);
        operationResult.Patch.Should().NotBeNull();
        operationResult.Patch!.Operation.Should().Be(CanvasPatchOperation.AddNode);
    }

    [Fact]
    public async Task ExecuteConversationalAsync_BuilderReturnsError_ConvertsToErrorResult()
    {
        // Arrange
        var context = CreateConversationContext("Do something invalid");

        _builderServiceMock
            .Setup(x => x.ProcessMessageAsync(
                It.IsAny<BuilderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateMockBuilderStreamWithError("Test error"));

        // Act
        var results = new List<BuilderResult>();
        await foreach (var result in _engine.ExecuteConversationalAsync(context, CancellationToken.None))
        {
            results.Add(result);
        }

        // Assert
        results.Should().Contain(r => r.Type == BuilderResultType.Error);
        var errorResult = results.First(r => r.Type == BuilderResultType.Error);
        errorResult.Error.Should().NotBeNull();
        errorResult.Error!.Message.Should().Be("Test error");
    }

    [Fact]
    public async Task ExecuteConversationalAsync_UpdatesSessionState()
    {
        // Arrange
        var context = CreateConversationContext("Add a node");
        var originalLastActive = context.SessionState.LastActiveAt;

        _builderServiceMock
            .Setup(x => x.ProcessMessageAsync(
                It.IsAny<BuilderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateMockBuilderStream());

        // Act
        var results = new List<BuilderResult>();
        await foreach (var result in _engine.ExecuteConversationalAsync(context, CancellationToken.None))
        {
            results.Add(result);
        }

        // Assert
        var stateUpdate = results.FirstOrDefault(r => r.Type == BuilderResultType.StateUpdate);
        stateUpdate.Should().NotBeNull();
        stateUpdate!.UpdatedState.Should().NotBeNull();
        stateUpdate.UpdatedState!.LastActiveAt.Should().BeOnOrAfter(originalLastActive);
    }

    #endregion

    #region ExecuteBatchAsync Tests

    [Fact]
    public async Task ExecuteBatchAsync_ValidRequest_DelegatesToOrchestrationService()
    {
        // Arrange
        var request = new PlaybookRunRequest
        {
            PlaybookId = Guid.NewGuid(),
            DocumentIds = [Guid.NewGuid()]
        };

        var httpContext = new DefaultHttpContext();
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        _orchestrationServiceMock
            .Setup(x => x.ExecuteAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateMockPlaybookStream(request.PlaybookId));

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _engine.ExecuteBatchAsync(request, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        events.Should().NotBeEmpty();
        _orchestrationServiceMock.Verify(
            x => x.ExecuteAsync(
                It.Is<PlaybookRunRequest>(r => r.PlaybookId == request.PlaybookId),
                httpContext,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteBatchAsync_NoHttpContext_ThrowsInvalidOperationException()
    {
        // Arrange
        var request = new PlaybookRunRequest
        {
            PlaybookId = Guid.NewGuid(),
            DocumentIds = [Guid.NewGuid()]
        };

        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns((HttpContext?)null);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in _engine.ExecuteBatchAsync(request, CancellationToken.None))
            {
                // Should not reach here
            }
        });
    }

    #endregion

    #region DetermineExecutionMode Tests

    [Fact]
    public void DetermineExecutionMode_CanvasStateOnly_ReturnsConversational()
    {
        // Arrange
        var playbookId = Guid.NewGuid();

        // Act
        var mode = _engine.DetermineExecutionMode(
            playbookId,
            hasCanvasState: true,
            hasDocuments: false);

        // Assert
        mode.Should().Be(ExecutionMode.Conversational);
    }

    [Fact]
    public void DetermineExecutionMode_DocumentsOnly_ReturnsBatch()
    {
        // Arrange
        var playbookId = Guid.NewGuid();

        // Act
        var mode = _engine.DetermineExecutionMode(
            playbookId,
            hasCanvasState: false,
            hasDocuments: true);

        // Assert
        mode.Should().Be(ExecutionMode.Batch);
    }

    [Fact]
    public void DetermineExecutionMode_BothCanvasAndDocuments_ReturnsBatch()
    {
        // Arrange
        var playbookId = Guid.NewGuid();

        // Act
        var mode = _engine.DetermineExecutionMode(
            playbookId,
            hasCanvasState: true,
            hasDocuments: true);

        // Assert
        mode.Should().Be(ExecutionMode.Batch);
    }

    [Fact]
    public void DetermineExecutionMode_Neither_ReturnsConversationalDefault()
    {
        // Arrange
        var playbookId = Guid.NewGuid();

        // Act
        var mode = _engine.DetermineExecutionMode(
            playbookId,
            hasCanvasState: false,
            hasDocuments: false);

        // Assert
        mode.Should().Be(ExecutionMode.Conversational);
    }

    #endregion

    #region ConversationContext Tests

    [Fact]
    public void ConversationContext_DefaultHistory_IsEmptyArray()
    {
        // Act
        var context = new ConversationContext
        {
            CurrentMessage = "test",
            SessionState = new SessionState
            {
                SessionId = "test",
                CanvasState = new CanvasState { Nodes = [], Edges = [] }
            }
        };

        // Assert
        context.History.Should().NotBeNull();
        context.History.Should().BeEmpty();
    }

    [Fact]
    public void SessionState_DefaultTimestamps_AreSet()
    {
        // Act
        var state = new SessionState
        {
            SessionId = "test",
            CanvasState = new CanvasState { Nodes = [], Edges = [] }
        };

        // Assert
        state.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        state.LastActiveAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    #endregion

    #region BuilderResult Factory Tests

    [Fact]
    public void BuilderResult_ThinkingFactory_CreatesCorrectType()
    {
        // Act
        var result = BuilderResult.Thinking("Processing...");

        // Assert
        result.Type.Should().Be(BuilderResultType.Thinking);
        result.Text.Should().Be("Processing...");
    }

    [Fact]
    public void BuilderResult_MessageFactory_CreatesCorrectType()
    {
        // Act
        var result = BuilderResult.Message("Hello");

        // Assert
        result.Type.Should().Be(BuilderResultType.Message);
        result.Text.Should().Be("Hello");
    }

    [Fact]
    public void BuilderResult_OperationFactory_CreatesCorrectType()
    {
        // Arrange
        var patch = new CanvasPatch { Operation = CanvasPatchOperation.AddNode };

        // Act
        var result = BuilderResult.Operation(patch);

        // Assert
        result.Type.Should().Be(BuilderResultType.CanvasOperation);
        result.Patch.Should().Be(patch);
    }

    [Fact]
    public void BuilderResult_ClarificationFactory_CreatesCorrectType()
    {
        // Act
        var result = BuilderResult.Clarification("Which node?", ["Node A", "Node B"]);

        // Assert
        result.Type.Should().Be(BuilderResultType.Clarification);
        result.ClarificationQuestion.Should().Be("Which node?");
        result.ClarificationOptions.Should().HaveCount(2);
    }

    [Fact]
    public void BuilderResult_ErrorFactory_CreatesCorrectType()
    {
        // Act
        var result = BuilderResult.ErrorResult("Something went wrong", "ERR_001");

        // Assert
        result.Type.Should().Be(BuilderResultType.Error);
        result.Error.Should().NotBeNull();
        result.Error!.Message.Should().Be("Something went wrong");
        result.Error.Code.Should().Be("ERR_001");
    }

    [Fact]
    public void BuilderResult_CompleteFactory_CreatesCorrectType()
    {
        // Act
        var result = BuilderResult.Complete();

        // Assert
        result.Type.Should().Be(BuilderResultType.Complete);
    }

    #endregion

    #region Helper Methods

    private static async IAsyncEnumerable<BuilderStreamChunk> CreateMockBuilderStream()
    {
        yield return BuilderStreamChunk.Message("Processing your request...");
        yield return BuilderStreamChunk.Complete();
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<BuilderStreamChunk> CreateMockBuilderStreamWithOperation(CanvasPatch patch)
    {
        yield return BuilderStreamChunk.Message("Adding node...");
        yield return BuilderStreamChunk.Operation(patch);
        yield return BuilderStreamChunk.Complete();
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<BuilderStreamChunk> CreateMockBuilderStreamWithError(string error)
    {
        yield return BuilderStreamChunk.ErrorChunk(error);
        yield return BuilderStreamChunk.Complete();
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<PlaybookStreamEvent> CreateMockPlaybookStream(Guid playbookId)
    {
        var runId = Guid.NewGuid();
        yield return PlaybookStreamEvent.RunStarted(runId, playbookId, 1);
        yield return PlaybookStreamEvent.RunCompleted(runId, playbookId, new PlaybookRunMetrics
        {
            TotalNodes = 1,
            CompletedNodes = 1
        });
        await Task.CompletedTask;
    }

    #endregion
}
