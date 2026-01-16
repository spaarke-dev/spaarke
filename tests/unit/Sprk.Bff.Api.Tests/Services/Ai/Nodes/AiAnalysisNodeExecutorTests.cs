using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Unit tests for AiAnalysisNodeExecutor.
/// </summary>
public class AiAnalysisNodeExecutorTests
{
    private readonly Mock<IToolHandlerRegistry> _toolHandlerRegistryMock;
    private readonly Mock<ILogger<AiAnalysisNodeExecutor>> _loggerMock;
    private readonly AiAnalysisNodeExecutor _executor;

    public AiAnalysisNodeExecutorTests()
    {
        _toolHandlerRegistryMock = new Mock<IToolHandlerRegistry>();
        _loggerMock = new Mock<ILogger<AiAnalysisNodeExecutor>>();
        _executor = new AiAnalysisNodeExecutor(_toolHandlerRegistryMock.Object, _loggerMock.Object);
    }

    #region SupportedActionTypes Tests

    [Fact]
    public void SupportedActionTypes_ContainsAiAnalysis()
    {
        // Assert
        _executor.SupportedActionTypes.Should().Contain(ActionType.AiAnalysis);
        _executor.SupportedActionTypes.Should().HaveCount(1);
    }

    #endregion

    #region Validate Tests

    [Fact]
    public void Validate_WithValidContext_ReturnsSuccess()
    {
        // Arrange
        var handler = CreateMockHandler("TestHandler");
        _toolHandlerRegistryMock
            .Setup(r => r.GetHandler("TestHandler"))
            .Returns(handler);

        var context = CreateValidContext();

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WithNoTool_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext();
        context = context with
        {
            Scopes = new ResolvedScopes([], [], []) // No tools
        };

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("requires a tool"));
    }

    [Fact]
    public void Validate_WithNoHandlerClass_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext();
        context = context with
        {
            Scopes = new ResolvedScopes([], [],
            [
                new AnalysisTool { Id = Guid.NewGuid(), Name = "NoHandler", HandlerClass = null }
            ])
        };

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("handler class"));
    }

    [Fact]
    public void Validate_WithUnregisteredHandler_ReturnsFailure()
    {
        // Arrange
        _toolHandlerRegistryMock
            .Setup(r => r.GetHandler(It.IsAny<string>()))
            .Returns((IAnalysisToolHandler?)null);

        var context = CreateValidContext();

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not registered"));
    }

    [Fact]
    public void Validate_WithNoDocument_ReturnsFailure()
    {
        // Arrange
        var handler = CreateMockHandler("TestHandler");
        _toolHandlerRegistryMock
            .Setup(r => r.GetHandler("TestHandler"))
            .Returns(handler);

        var context = CreateValidContext();
        context = context with { Document = null };

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("document context"));
    }

    [Fact]
    public void Validate_WithEmptyDocumentText_ReturnsFailure()
    {
        // Arrange
        var handler = CreateMockHandler("TestHandler");
        _toolHandlerRegistryMock
            .Setup(r => r.GetHandler("TestHandler"))
            .Returns(handler);

        var context = CreateValidContext();
        context = context with
        {
            Document = new DocumentContext
            {
                DocumentId = Guid.NewGuid(),
                Name = "Test",
                ExtractedText = ""
            }
        };

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("extracted text"));
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WithValidContext_ReturnsSuccessfulOutput()
    {
        // Arrange
        var handler = CreateMockHandler("TestHandler");
        _toolHandlerRegistryMock
            .Setup(r => r.GetHandler("TestHandler"))
            .Returns(handler);

        var toolResult = ToolResult.Ok(
            "TestHandler",
            Guid.NewGuid(),
            "Test Tool",
            new { entities = new[] { "Entity1", "Entity2" } },
            summary: "Found 2 entities",
            confidence: 0.95,
            execution: new ToolExecutionMetadata
            {
                StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
                CompletedAt = DateTimeOffset.UtcNow,
                InputTokens = 500,
                OutputTokens = 100,
                ModelCalls = 1,
                ModelName = "gpt-4"
            });

        var mockHandler = Mock.Get(handler);
        mockHandler
            .Setup(h => h.Validate(It.IsAny<ToolExecutionContext>(), It.IsAny<AnalysisTool>()))
            .Returns(ToolValidationResult.Success());
        mockHandler
            .Setup(h => h.ExecuteAsync(It.IsAny<ToolExecutionContext>(), It.IsAny<AnalysisTool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(toolResult);

        var context = CreateValidContext();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.NodeId.Should().Be(context.Node.Id);
        result.OutputVariable.Should().Be(context.Node.OutputVariable);
        result.TextContent.Should().Be("Found 2 entities");
        result.Confidence.Should().Be(0.95);
        result.Metrics.TokensIn.Should().Be(500);
        result.Metrics.TokensOut.Should().Be(100);
        result.Metrics.ModelName.Should().Be("gpt-4");
        result.ToolResults.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenToolFails_ReturnsErrorOutput()
    {
        // Arrange
        var handler = CreateMockHandler("TestHandler");
        _toolHandlerRegistryMock
            .Setup(r => r.GetHandler("TestHandler"))
            .Returns(handler);

        var toolResult = ToolResult.Error(
            "TestHandler",
            Guid.NewGuid(),
            "Test Tool",
            "Model rate limit exceeded",
            ToolErrorCodes.RateLimitExceeded);

        var mockHandler = Mock.Get(handler);
        mockHandler
            .Setup(h => h.Validate(It.IsAny<ToolExecutionContext>(), It.IsAny<AnalysisTool>()))
            .Returns(ToolValidationResult.Success());
        mockHandler
            .Setup(h => h.ExecuteAsync(It.IsAny<ToolExecutionContext>(), It.IsAny<AnalysisTool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(toolResult);

        var context = CreateValidContext();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("rate limit");
        result.ErrorCode.Should().Be(ToolErrorCodes.RateLimitExceeded);
    }

    [Fact]
    public async Task ExecuteAsync_WhenValidationFails_ReturnsErrorOutput()
    {
        // Arrange - no handler registered
        _toolHandlerRegistryMock
            .Setup(r => r.GetHandler(It.IsAny<string>()))
            .Returns((IAnalysisToolHandler?)null);

        var context = CreateValidContext();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ReturnsCancelledOutput()
    {
        // Arrange
        var handler = CreateMockHandler("TestHandler");
        _toolHandlerRegistryMock
            .Setup(r => r.GetHandler("TestHandler"))
            .Returns(handler);

        var mockHandler = Mock.Get(handler);
        mockHandler
            .Setup(h => h.Validate(It.IsAny<ToolExecutionContext>(), It.IsAny<AnalysisTool>()))
            .Returns(ToolValidationResult.Success());
        mockHandler
            .Setup(h => h.ExecuteAsync(It.IsAny<ToolExecutionContext>(), It.IsAny<AnalysisTool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var context = CreateValidContext();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.Cancelled);
    }

    [Fact]
    public async Task ExecuteAsync_WhenExceptionThrown_ReturnsInternalErrorOutput()
    {
        // Arrange
        var handler = CreateMockHandler("TestHandler");
        _toolHandlerRegistryMock
            .Setup(r => r.GetHandler("TestHandler"))
            .Returns(handler);

        var mockHandler = Mock.Get(handler);
        mockHandler
            .Setup(h => h.Validate(It.IsAny<ToolExecutionContext>(), It.IsAny<AnalysisTool>()))
            .Returns(ToolValidationResult.Success());
        mockHandler
            .Setup(h => h.ExecuteAsync(It.IsAny<ToolExecutionContext>(), It.IsAny<AnalysisTool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected error"));

        var context = CreateValidContext();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.InternalError);
        result.ErrorMessage.Should().Contain("Unexpected error");
    }

    [Fact]
    public async Task ExecuteAsync_WhenToolValidationFails_ReturnsValidationError()
    {
        // Arrange
        var handler = CreateMockHandler("TestHandler");
        _toolHandlerRegistryMock
            .Setup(r => r.GetHandler("TestHandler"))
            .Returns(handler);

        var mockHandler = Mock.Get(handler);
        mockHandler
            .Setup(h => h.Validate(It.IsAny<ToolExecutionContext>(), It.IsAny<AnalysisTool>()))
            .Returns(ToolValidationResult.Failure("Invalid configuration"));

        var context = CreateValidContext();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("Invalid configuration");
    }

    #endregion

    #region Helper Methods

    private static IAnalysisToolHandler CreateMockHandler(string handlerId)
    {
        var mock = new Mock<IAnalysisToolHandler>();
        mock.Setup(h => h.HandlerId).Returns(handlerId);
        mock.Setup(h => h.SupportedToolTypes).Returns(new List<ToolType> { ToolType.EntityExtractor });
        mock.Setup(h => h.Metadata).Returns(new ToolHandlerMetadata(
            "Test Handler", "Test description", "1.0",
            new[] { "text/plain" }, Array.Empty<ToolParameterDefinition>()));
        return mock.Object;
    }

    private static NodeExecutionContext CreateValidContext()
    {
        var nodeId = Guid.NewGuid();
        var toolId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        return new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto
            {
                Id = nodeId,
                PlaybookId = Guid.NewGuid(),
                ActionId = actionId,
                ToolId = toolId,
                Name = "Test Node",
                ExecutionOrder = 1,
                OutputVariable = "testOutput",
                IsActive = true
            },
            Action = new AnalysisAction
            {
                Id = actionId,
                Name = "Test Action",
                SystemPrompt = "You are a test assistant."
            },
            ActionType = ActionType.AiAnalysis,
            Scopes = new ResolvedScopes([], [],
            [
                new AnalysisTool
                {
                    Id = toolId,
                    Name = "Test Tool",
                    Type = ToolType.EntityExtractor,
                    HandlerClass = "TestHandler"
                }
            ]),
            Document = new DocumentContext
            {
                DocumentId = Guid.NewGuid(),
                Name = "Test Document",
                ExtractedText = "This is test document content for analysis."
            },
            TenantId = "test-tenant"
        };
    }

    #endregion
}

/// <summary>
/// Unit tests for NodeExecutorRegistry.
/// </summary>
public class NodeExecutorRegistryTests
{
    private readonly Mock<ILogger<NodeExecutorRegistry>> _loggerMock;

    public NodeExecutorRegistryTests()
    {
        _loggerMock = new Mock<ILogger<NodeExecutorRegistry>>();
    }

    [Fact]
    public void Constructor_RegistersExecutors()
    {
        // Arrange
        var executor = CreateMockExecutor(ActionType.AiAnalysis);
        var executors = new[] { executor };

        // Act
        var registry = new NodeExecutorRegistry(executors, _loggerMock.Object);

        // Assert
        registry.ExecutorCount.Should().Be(1);
        registry.HasExecutor(ActionType.AiAnalysis).Should().BeTrue();
    }

    [Fact]
    public void GetExecutor_WithRegisteredType_ReturnsExecutor()
    {
        // Arrange
        var executor = CreateMockExecutor(ActionType.AiAnalysis);
        var registry = new NodeExecutorRegistry(new[] { executor }, _loggerMock.Object);

        // Act
        var result = registry.GetExecutor(ActionType.AiAnalysis);

        // Assert
        result.Should().BeSameAs(executor);
    }

    [Fact]
    public void GetExecutor_WithUnregisteredType_ReturnsNull()
    {
        // Arrange
        var executor = CreateMockExecutor(ActionType.AiAnalysis);
        var registry = new NodeExecutorRegistry(new[] { executor }, _loggerMock.Object);

        // Act
        var result = registry.GetExecutor(ActionType.SendEmail);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetAllExecutors_ReturnsAllRegistered()
    {
        // Arrange
        var executor1 = CreateMockExecutor(ActionType.AiAnalysis);
        var executor2 = CreateMockExecutor(ActionType.CreateTask);
        var registry = new NodeExecutorRegistry(new[] { executor1, executor2 }, _loggerMock.Object);

        // Act
        var result = registry.GetAllExecutors();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(executor1);
        result.Should().Contain(executor2);
    }

    [Fact]
    public void GetSupportedActionTypes_ReturnsAllTypes()
    {
        // Arrange
        var executor1 = CreateMockExecutor(ActionType.AiAnalysis);
        var executor2 = CreateMockExecutor(ActionType.CreateTask);
        var registry = new NodeExecutorRegistry(new[] { executor1, executor2 }, _loggerMock.Object);

        // Act
        var result = registry.GetSupportedActionTypes();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(ActionType.AiAnalysis);
        result.Should().Contain(ActionType.CreateTask);
    }

    [Fact]
    public void Constructor_WithDuplicateActionType_KeepsFirst()
    {
        // Arrange
        var executor1 = CreateMockExecutor(ActionType.AiAnalysis, "Executor1");
        var executor2 = CreateMockExecutor(ActionType.AiAnalysis, "Executor2");

        // Act
        var registry = new NodeExecutorRegistry(new[] { executor1, executor2 }, _loggerMock.Object);

        // Assert
        registry.ExecutorCount.Should().Be(2); // Both registered as executors
        var result = registry.GetExecutor(ActionType.AiAnalysis);
        result.Should().BeSameAs(executor1); // First one wins for lookup
    }

    [Fact]
    public void Constructor_WithMultipleActionTypes_RegistersAll()
    {
        // Arrange
        var executor = CreateMockExecutor(new[] { ActionType.AiAnalysis, ActionType.AiCompletion });

        // Act
        var registry = new NodeExecutorRegistry(new[] { executor }, _loggerMock.Object);

        // Assert
        registry.ExecutorCount.Should().Be(1);
        registry.HasExecutor(ActionType.AiAnalysis).Should().BeTrue();
        registry.HasExecutor(ActionType.AiCompletion).Should().BeTrue();
        registry.GetExecutor(ActionType.AiAnalysis).Should().BeSameAs(executor);
        registry.GetExecutor(ActionType.AiCompletion).Should().BeSameAs(executor);
    }

    private static INodeExecutor CreateMockExecutor(ActionType actionType, string? name = null)
    {
        return CreateMockExecutor(new[] { actionType }, name);
    }

    private static INodeExecutor CreateMockExecutor(ActionType[] actionTypes, string? name = null)
    {
        var mock = new Mock<INodeExecutor>();
        mock.Setup(e => e.SupportedActionTypes).Returns(actionTypes);
        return mock.Object;
    }
}

/// <summary>
/// Unit tests for NodeOutput.
/// </summary>
public class NodeOutputTests
{
    [Fact]
    public void Ok_CreatesSuccessfulOutput()
    {
        // Arrange
        var nodeId = Guid.NewGuid();
        var data = new { name = "Test", value = 42 };

        // Act
        var output = NodeOutput.Ok(nodeId, "result", data, "Summary text", 0.9);

        // Assert
        output.Success.Should().BeTrue();
        output.NodeId.Should().Be(nodeId);
        output.OutputVariable.Should().Be("result");
        output.TextContent.Should().Be("Summary text");
        output.Confidence.Should().Be(0.9);
        output.StructuredData.Should().NotBeNull();
    }

    [Fact]
    public void Error_CreatesFailedOutput()
    {
        // Arrange
        var nodeId = Guid.NewGuid();

        // Act
        var output = NodeOutput.Error(nodeId, "result", "Something went wrong", "ERROR_CODE");

        // Assert
        output.Success.Should().BeFalse();
        output.NodeId.Should().Be(nodeId);
        output.OutputVariable.Should().Be("result");
        output.ErrorMessage.Should().Be("Something went wrong");
        output.ErrorCode.Should().Be("ERROR_CODE");
    }

    [Fact]
    public void GetData_DeserializesStructuredData()
    {
        // Arrange
        var data = new TestData { Name = "Test", Value = 123 };
        var output = NodeOutput.Ok(Guid.NewGuid(), "result", data);

        // Act
        var result = output.GetData<TestData>();

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Test");
        result.Value.Should().Be(123);
    }

    [Fact]
    public void GetData_WithNullData_ReturnsDefault()
    {
        // Arrange
        var output = NodeOutput.Ok(Guid.NewGuid(), "result", null);

        // Act
        var result = output.GetData<TestData>();

        // Assert
        result.Should().BeNull();
    }

    private class TestData
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }
}

/// <summary>
/// Unit tests for NodeExecutionMetrics.
/// </summary>
public class NodeExecutionMetricsTests
{
    [Fact]
    public void Duration_CalculatesCorrectly()
    {
        // Arrange
        var start = DateTimeOffset.UtcNow;
        var end = start.AddSeconds(5);
        var metrics = new NodeExecutionMetrics { StartedAt = start, CompletedAt = end };

        // Assert
        metrics.Duration.TotalSeconds.Should().Be(5);
        metrics.DurationMs.Should().Be(5000);
    }

    [Fact]
    public void TotalTokens_SumsInputAndOutput()
    {
        // Arrange
        var metrics = new NodeExecutionMetrics
        {
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            TokensIn = 500,
            TokensOut = 200
        };

        // Assert
        metrics.TotalTokens.Should().Be(700);
    }

    [Fact]
    public void TotalTokens_WithMissingValues_ReturnsNull()
    {
        // Arrange
        var metrics = new NodeExecutionMetrics
        {
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            TokensIn = 500
        };

        // Assert
        metrics.TotalTokens.Should().BeNull();
    }

    [Fact]
    public void FromToolMetadata_CopiesValues()
    {
        // Arrange
        var toolMetadata = new ToolExecutionMetadata
        {
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-2),
            CompletedAt = DateTimeOffset.UtcNow,
            InputTokens = 400,
            OutputTokens = 150,
            CacheHit = true,
            ModelCalls = 2,
            ModelName = "gpt-4-turbo"
        };

        // Act
        var metrics = NodeExecutionMetrics.FromToolMetadata(toolMetadata);

        // Assert
        metrics.TokensIn.Should().Be(400);
        metrics.TokensOut.Should().Be(150);
        metrics.CacheHit.Should().BeTrue();
        metrics.ModelCalls.Should().Be(2);
        metrics.ModelName.Should().Be("gpt-4-turbo");
    }
}
