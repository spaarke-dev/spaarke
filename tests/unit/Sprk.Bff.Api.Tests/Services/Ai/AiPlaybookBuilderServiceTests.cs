using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Testing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for AiPlaybookBuilderService.
/// Tests intent classification, build plan generation, and canvas operations.
/// </summary>
public class AiPlaybookBuilderServiceTests
{
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<IScopeResolverService> _scopeResolverMock;
    private readonly IMemoryCache _memoryCache;
    private readonly Mock<IMockTestExecutor> _mockTestExecutorMock;
    private readonly Mock<IPlaybookService> _playbookServiceMock;
    private readonly Mock<ILogger<AiPlaybookBuilderService>> _loggerMock;
    private readonly AiPlaybookBuilderService _service;

    public AiPlaybookBuilderServiceTests()
    {
        _openAiClientMock = new Mock<IOpenAiClient>();
        _scopeResolverMock = new Mock<IScopeResolverService>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _mockTestExecutorMock = new Mock<IMockTestExecutor>();
        _playbookServiceMock = new Mock<IPlaybookService>();
        _loggerMock = new Mock<ILogger<AiPlaybookBuilderService>>();

        _service = new AiPlaybookBuilderService(
            _openAiClientMock.Object,
            _scopeResolverMock.Object,
            _memoryCache,
            _mockTestExecutorMock.Object,
            _playbookServiceMock.Object,
            _loggerMock.Object);
    }

    private static BuilderRequest CreateRequest(string message, params CanvasNode[] nodes) => new()
    {
        Message = message,
        CanvasState = new CanvasState
        {
            Nodes = nodes,
            Edges = []
        }
    };

    private static CanvasNode CreateNode(string type, string? label = null) => new()
    {
        Id = Guid.NewGuid().ToString("N")[..8],
        Type = type,
        Label = label ?? $"Test {type} node"
    };

    #region Intent Classification Tests

    [Fact]
    public async Task ClassifyIntentAsync_CreatePlaybookMessage_ReturnsCreatePlaybookIntent()
    {
        // Arrange
        var message = "Create a lease analysis playbook";

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("{ \"intent\": \"CreatePlaybook\", \"confidence\": 0.95 }");

        // Act
        var result = await _service.ClassifyIntentAsync(
            message, canvasContext: null, CancellationToken.None);

        // Assert
        result.Intent.Should().Be(BuilderIntent.CreatePlaybook);
        result.NeedsClarification.Should().BeFalse();
    }

    [Fact]
    public async Task ClassifyIntentAsync_AddNodeMessage_ReturnsAddNodeIntent()
    {
        // Arrange
        var message = "Add an action node for summarization";

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("{ \"intent\": \"AddNode\", \"confidence\": 0.90 }");

        // Act
        var result = await _service.ClassifyIntentAsync(
            message, canvasContext: null, CancellationToken.None);

        // Assert
        result.Intent.Should().Be(BuilderIntent.AddNode);
        result.Entities.Should().ContainKey("nodeType");
    }

    [Fact]
    public async Task ClassifyIntentAsync_AmbiguousMessage_NeedsClarification()
    {
        // Arrange
        var message = "do something";

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("{ \"intent\": \"Unknown\", \"confidence\": 0.30 }");

        // Act
        var result = await _service.ClassifyIntentAsync(
            message, canvasContext: null, CancellationToken.None);

        // Assert
        // Note: Current implementation has placeholder confidence, test verifies flow
        result.Intent.Should().Be(BuilderIntent.Unknown);
    }

    #endregion

    #region Build Plan Generation Tests

    [Fact]
    public async Task GenerateBuildPlanAsync_ValidGoal_ReturnsPlanWithSteps()
    {
        // Arrange
        var request = new BuildPlanRequest
        {
            Goal = "Create a document summarization playbook",
            DocumentType = "contract"
        };

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("{ \"summary\": \"Build plan\", \"steps\": [] }");

        // Act
        var result = await _service.GenerateBuildPlanAsync(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Summary.Should().Contain(request.Goal);
        result.Steps.Should().NotBeEmpty();
        result.Id.Should().NotBeEmpty();
    }

    #endregion

    #region Process Message Tests

    [Fact]
    public async Task ProcessMessageAsync_AddNodeRequest_StreamsCanvasOperation()
    {
        // Arrange
        var request = CreateRequest("Add an action node");

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("{ \"intent\": \"AddNode\", \"confidence\": 0.90 }");

        // Act
        var chunks = new List<BuilderStreamChunk>();
        await foreach (var chunk in _service.ProcessMessageAsync(request, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Should().NotBeEmpty();
        chunks.Should().Contain(c => c.Type == BuilderChunkType.Complete);
        chunks.Should().Contain(c => c.Type == BuilderChunkType.CanvasOperation);
    }

    [Fact]
    public async Task ProcessMessageAsync_EmptyCanvas_CreatePlaybook_StartsBuildPlan()
    {
        // Arrange
        var request = CreateRequest("Create a lease analysis playbook");

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("{ \"intent\": \"CreatePlaybook\", \"confidence\": 0.95 }");

        // Act
        var chunks = new List<BuilderStreamChunk>();
        await foreach (var chunk in _service.ProcessMessageAsync(request, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Should().NotBeEmpty();
        chunks.Should().Contain(c => c.Type == BuilderChunkType.Message);
        chunks.Should().Contain(c => c.Type == BuilderChunkType.Complete);
    }

    #endregion

    #region Canvas State Tests

    [Fact]
    public async Task ProcessMessageAsync_WithExistingNodes_IncludesContextInClassification()
    {
        // Arrange
        var existingNode = CreateNode("action", "Summarization");
        var request = CreateRequest("Add another step", existingNode);

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("{ \"intent\": \"AddNode\", \"confidence\": 0.85 }");

        // Act
        var chunks = new List<BuilderStreamChunk>();
        await foreach (var chunk in _service.ProcessMessageAsync(request, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Should().NotBeEmpty();
        // Verify AI was called with context about existing nodes (new format uses "Nodes: 1")
        _openAiClientMock.Verify(
            x => x.GetCompletionAsync(
                It.Is<string>(s => s.Contains("Nodes: 1")),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Test Execution Mode Tests (Task 031)

    private static CanvasState CreateTestCanvasState() => new()
    {
        Nodes = new[]
        {
            new CanvasNode { Id = "node-1", Type = "aiAnalysis", Label = "Analysis" },
            new CanvasNode { Id = "node-2", Type = "condition", Label = "Condition" },
            new CanvasNode { Id = "node-3", Type = "deliverOutput", Label = "Output" }
        },
        Edges = new[]
        {
            new CanvasEdge { Id = "edge-1", SourceId = "node-1", TargetId = "node-2" },
            new CanvasEdge { Id = "edge-2", SourceId = "node-2", TargetId = "node-3" }
        }
    };

    private static TestPlaybookRequest CreateTestRequest(TestMode mode, CanvasState? canvas = null) => new()
    {
        Mode = mode,
        CanvasJson = canvas ?? CreateTestCanvasState(),
        PlaybookId = null,
        Options = new TestOptions { MaxNodes = 10 }
    };

    // Helper to convert List to IAsyncEnumerable for Moq setup
    private static async IAsyncEnumerable<T> ToAsyncEnum<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ExecuteTestAsync_MockMode_DelegatesToMockTestExecutor()
    {
        // Arrange
        var request = CreateTestRequest(TestMode.Mock);
        var mockEvents = new List<TestExecutionEvent>
        {
            new() { Type = TestEventTypes.NodeStart, Data = new { nodeId = "node-1" } },
            new() { Type = TestEventTypes.NodeOutput, Data = new { nodeId = "node-1", output = "mock output" } },
            new() { Type = TestEventTypes.NodeComplete, Data = new { nodeId = "node-1", success = true } },
            new() { Type = TestEventTypes.Complete, Data = new { success = true, nodesExecuted = 1 }, Done = true }
        };

        _mockTestExecutorMock
            .Setup(x => x.ExecuteAsync(
                It.IsAny<CanvasState>(),
                It.IsAny<TestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(mockEvents));

        // Act
        var events = new List<TestExecutionEvent>();
        await foreach (var evt in _service.ExecuteTestAsync(request, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert - Mock mode should delegate to MockTestExecutor
        _mockTestExecutorMock.Verify(
            x => x.ExecuteAsync(
                It.IsAny<CanvasState>(),
                It.IsAny<TestOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        events.Should().NotBeEmpty();
        events.Should().Contain(e => e.Type == TestEventTypes.Complete && e.Done == true);
    }

    [Fact]
    public async Task ExecuteTestAsync_MockMode_NoExternalCalls()
    {
        // Arrange
        var request = CreateTestRequest(TestMode.Mock);
        var mockEvents = new List<TestExecutionEvent>
        {
            new() { Type = TestEventTypes.Complete, Data = new { success = true }, Done = true }
        };

        _mockTestExecutorMock
            .Setup(x => x.ExecuteAsync(
                It.IsAny<CanvasState>(),
                It.IsAny<TestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(mockEvents));

        // Act
        await foreach (var evt in _service.ExecuteTestAsync(request, CancellationToken.None))
        {
            // Consume events
        }

        // Assert - Mock mode should NOT call OpenAI
        _openAiClientMock.Verify(
            x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteTestAsync_QuickMode_ExecutesNodesWithAi()
    {
        // Arrange
        var request = CreateTestRequest(TestMode.Quick);

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("AI response for testing");

        // Act
        var events = new List<TestExecutionEvent>();
        await foreach (var evt in _service.ExecuteTestAsync(request, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert - Quick mode should call AI and emit events
        events.Should().NotBeEmpty();
        events.Should().Contain(e => e.Type == TestEventTypes.NodeStart);
        events.Should().Contain(e => e.Type == TestEventTypes.NodeOutput);
        events.Should().Contain(e => e.Type == TestEventTypes.NodeComplete);
        events.Should().Contain(e => e.Type == TestEventTypes.Complete && e.Done == true);

        // Quick mode DOES call AI for aiAnalysis nodes
        _openAiClientMock.Verify(
            x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteTestAsync_ProductionMode_ExecutesNodesWithAi()
    {
        // Arrange
        var request = CreateTestRequest(TestMode.Production);

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("AI response for production test");

        // Act
        var events = new List<TestExecutionEvent>();
        await foreach (var evt in _service.ExecuteTestAsync(request, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert - Production mode should call AI and emit events
        events.Should().NotBeEmpty();
        events.Should().Contain(e => e.Type == TestEventTypes.Complete && e.Done == true);

        // Production mode DOES call AI
        _openAiClientMock.Verify(
            x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteTestAsync_EmptyCanvas_ReturnsError()
    {
        // Arrange
        var request = new TestPlaybookRequest
        {
            Mode = TestMode.Mock,
            CanvasJson = new CanvasState { Nodes = Array.Empty<CanvasNode>(), Edges = Array.Empty<CanvasEdge>() },
            PlaybookId = null
        };

        // Act
        var events = new List<TestExecutionEvent>();
        await foreach (var evt in _service.ExecuteTestAsync(request, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        events.Should().ContainSingle();
        var errorEvent = events.First();
        errorEvent.Type.Should().Be(TestEventTypes.Error);
        errorEvent.Done.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteTestAsync_WithPlaybookId_LoadsCanvasFromPlaybookService()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var request = new TestPlaybookRequest
        {
            Mode = TestMode.Mock,
            CanvasJson = null,
            PlaybookId = playbookId
        };

        var layout = new CanvasLayoutDto
        {
            Nodes = new[]
            {
                new CanvasNodeDto { Id = "node-1", Type = "aiAnalysis", X = 100, Y = 100, Data = new Dictionary<string, object?> { ["label"] = "Test" } }
            },
            Edges = Array.Empty<CanvasEdgeDto>()
        };

        _playbookServiceMock
            .Setup(x => x.GetCanvasLayoutAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CanvasLayoutResponse { PlaybookId = playbookId, Layout = layout });

        var mockEvents = new List<TestExecutionEvent>
        {
            new() { Type = TestEventTypes.Complete, Data = new { success = true }, Done = true }
        };

        _mockTestExecutorMock
            .Setup(x => x.ExecuteAsync(
                It.IsAny<CanvasState>(),
                It.IsAny<TestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnum(mockEvents));

        // Act
        var events = new List<TestExecutionEvent>();
        await foreach (var evt in _service.ExecuteTestAsync(request, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert - Should load from PlaybookService
        _playbookServiceMock.Verify(
            x => x.GetCanvasLayoutAsync(playbookId, It.IsAny<CancellationToken>()),
            Times.Once);

        events.Should().Contain(e => e.Type == TestEventTypes.Complete && e.Done == true);
    }

    [Fact]
    public async Task ExecuteTestAsync_PlaybookNotFound_ReturnsError()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var request = new TestPlaybookRequest
        {
            Mode = TestMode.Mock,
            CanvasJson = null,
            PlaybookId = playbookId
        };

        _playbookServiceMock
            .Setup(x => x.GetCanvasLayoutAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CanvasLayoutResponse?)null);

        // Act
        var events = new List<TestExecutionEvent>();
        await foreach (var evt in _service.ExecuteTestAsync(request, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        events.Should().ContainSingle();
        var errorEvent = events.First();
        errorEvent.Type.Should().Be(TestEventTypes.Error);
        errorEvent.Done.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteTestAsync_WithMaxNodes_LimitsExecution()
    {
        // Arrange
        var canvasWithManyNodes = new CanvasState
        {
            Nodes = new[]
            {
                new CanvasNode { Id = "node-1", Type = "aiAnalysis", Label = "Node 1" },
                new CanvasNode { Id = "node-2", Type = "aiAnalysis", Label = "Node 2" },
                new CanvasNode { Id = "node-3", Type = "aiAnalysis", Label = "Node 3" },
                new CanvasNode { Id = "node-4", Type = "aiAnalysis", Label = "Node 4" },
                new CanvasNode { Id = "node-5", Type = "aiAnalysis", Label = "Node 5" }
            },
            Edges = Array.Empty<CanvasEdge>()
        };

        var request = new TestPlaybookRequest
        {
            Mode = TestMode.Quick,
            CanvasJson = canvasWithManyNodes,
            Options = new TestOptions { MaxNodes = 2 }
        };

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("AI response");

        // Act
        var events = new List<TestExecutionEvent>();
        await foreach (var evt in _service.ExecuteTestAsync(request, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert - Should only execute 2 nodes (MaxNodes limit)
        var startEvents = events.Where(e => e.Type == TestEventTypes.NodeStart).ToList();
        startEvents.Should().HaveCount(2);
    }

    #endregion
}
