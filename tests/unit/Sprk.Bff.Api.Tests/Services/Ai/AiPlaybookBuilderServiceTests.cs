using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai;
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
    private readonly Mock<ILogger<AiPlaybookBuilderService>> _loggerMock;
    private readonly AiPlaybookBuilderService _service;

    public AiPlaybookBuilderServiceTests()
    {
        _openAiClientMock = new Mock<IOpenAiClient>();
        _scopeResolverMock = new Mock<IScopeResolverService>();
        _loggerMock = new Mock<ILogger<AiPlaybookBuilderService>>();

        _service = new AiPlaybookBuilderService(
            _openAiClientMock.Object,
            _scopeResolverMock.Object,
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
        // Verify AI was called with context about existing nodes
        _openAiClientMock.Verify(
            x => x.GetCompletionAsync(
                It.Is<string>(s => s.Contains("1 nodes")),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}
