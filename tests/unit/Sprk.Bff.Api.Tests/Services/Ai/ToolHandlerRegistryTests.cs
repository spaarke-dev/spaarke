using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

public class ToolHandlerRegistryTests
{
    private readonly Mock<ILogger<ToolHandlerRegistry>> _loggerMock;

    public ToolHandlerRegistryTests()
    {
        _loggerMock = new Mock<ILogger<ToolHandlerRegistry>>();
    }

    private ToolHandlerRegistry CreateRegistry(
        IEnumerable<IAnalysisToolHandler>? handlers = null,
        ToolFrameworkOptions? options = null)
    {
        return new ToolHandlerRegistry(
            handlers ?? Array.Empty<IAnalysisToolHandler>(),
            Options.Create(options ?? new ToolFrameworkOptions()),
            _loggerMock.Object);
    }

    [Fact]
    public void Constructor_WithNoHandlers_CreatesEmptyRegistry()
    {
        // Arrange & Act
        var registry = CreateRegistry();

        // Assert
        Assert.Equal(0, registry.HandlerCount);
        Assert.Empty(registry.GetRegisteredHandlerIds());
    }

    [Fact]
    public void Constructor_WithHandlers_RegistersAllHandlers()
    {
        // Arrange
        var handler1 = new TestToolHandler("Handler1", ToolType.EntityExtractor);
        var handler2 = new TestToolHandler("Handler2", ToolType.ClauseAnalyzer);
        var handlers = new[] { handler1, handler2 };

        // Act
        var registry = CreateRegistry(handlers);

        // Assert
        Assert.Equal(2, registry.HandlerCount);
        Assert.Contains("Handler1", registry.GetRegisteredHandlerIds());
        Assert.Contains("Handler2", registry.GetRegisteredHandlerIds());
    }

    [Fact]
    public void GetHandler_WithValidHandlerId_ReturnsHandler()
    {
        // Arrange
        var handler = new TestToolHandler("TestHandler", ToolType.EntityExtractor);
        var registry = CreateRegistry(new[] { handler });

        // Act
        var result = registry.GetHandler("TestHandler");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TestHandler", result.HandlerId);
    }

    [Fact]
    public void GetHandler_WithInvalidHandlerId_ReturnsNull()
    {
        // Arrange
        var handler = new TestToolHandler("TestHandler", ToolType.EntityExtractor);
        var registry = CreateRegistry(new[] { handler });

        // Act
        var result = registry.GetHandler("NonExistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetHandler_IsCaseInsensitive()
    {
        // Arrange
        var handler = new TestToolHandler("TestHandler", ToolType.EntityExtractor);
        var registry = CreateRegistry(new[] { handler });

        // Act
        var result = registry.GetHandler("TESTHANDLER");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TestHandler", result.HandlerId);
    }

    [Fact]
    public void GetHandler_WithNullHandlerId_ReturnsNull()
    {
        // Arrange
        var handler = new TestToolHandler("TestHandler", ToolType.EntityExtractor);
        var registry = CreateRegistry(new[] { handler });

        // Act
        var result = registry.GetHandler(null!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetHandler_WithDisabledHandler_ReturnsNull()
    {
        // Arrange
        var handler = new TestToolHandler("TestHandler", ToolType.EntityExtractor);
        var options = new ToolFrameworkOptions
        {
            DisabledHandlers = new[] { "TestHandler" }
        };
        var registry = CreateRegistry(new[] { handler }, options);

        // Act
        var result = registry.GetHandler("TestHandler");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetHandlersByType_WithMatchingType_ReturnsHandlers()
    {
        // Arrange
        var handler1 = new TestToolHandler("Handler1", ToolType.EntityExtractor);
        var handler2 = new TestToolHandler("Handler2", ToolType.EntityExtractor);
        var handler3 = new TestToolHandler("Handler3", ToolType.ClauseAnalyzer);
        var handlers = new[] { handler1, handler2, handler3 };
        var registry = CreateRegistry(handlers);

        // Act
        var result = registry.GetHandlersByType(ToolType.EntityExtractor);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, h => Assert.Equal(ToolType.EntityExtractor, h.SupportedToolTypes[0]));
    }

    [Fact]
    public void GetHandlersByType_WithNoMatchingType_ReturnsEmptyList()
    {
        // Arrange
        var handler = new TestToolHandler("Handler1", ToolType.EntityExtractor);
        var registry = CreateRegistry(new[] { handler });

        // Act
        var result = registry.GetHandlersByType(ToolType.Custom);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetHandlersByType_ExcludesDisabledHandlers()
    {
        // Arrange
        var handler1 = new TestToolHandler("Handler1", ToolType.EntityExtractor);
        var handler2 = new TestToolHandler("Handler2", ToolType.EntityExtractor);
        var options = new ToolFrameworkOptions
        {
            DisabledHandlers = new[] { "Handler1" }
        };
        var registry = CreateRegistry(new[] { handler1, handler2 }, options);

        // Act
        var result = registry.GetHandlersByType(ToolType.EntityExtractor);

        // Assert
        Assert.Single(result);
        Assert.Equal("Handler2", result[0].HandlerId);
    }

    [Fact]
    public void GetAllHandlerInfo_ReturnsInfoForAllHandlers()
    {
        // Arrange
        var handler1 = new TestToolHandler("Handler1", ToolType.EntityExtractor);
        var handler2 = new TestToolHandler("Handler2", ToolType.ClauseAnalyzer);
        var registry = CreateRegistry(new[] { handler1, handler2 });

        // Act
        var result = registry.GetAllHandlerInfo();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, h => h.HandlerId == "Handler1" && h.IsEnabled);
        Assert.Contains(result, h => h.HandlerId == "Handler2" && h.IsEnabled);
    }

    [Fact]
    public void GetAllHandlerInfo_ShowsDisabledStatus()
    {
        // Arrange
        var handler = new TestToolHandler("Handler1", ToolType.EntityExtractor);
        var options = new ToolFrameworkOptions
        {
            DisabledHandlers = new[] { "Handler1" }
        };
        var registry = CreateRegistry(new[] { handler }, options);

        // Act
        var result = registry.GetAllHandlerInfo();

        // Assert
        Assert.Single(result);
        Assert.False(result[0].IsEnabled);
    }

    [Fact]
    public void IsHandlerAvailable_WithEnabledHandler_ReturnsTrue()
    {
        // Arrange
        var handler = new TestToolHandler("TestHandler", ToolType.EntityExtractor);
        var registry = CreateRegistry(new[] { handler });

        // Act
        var result = registry.IsHandlerAvailable("TestHandler");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsHandlerAvailable_WithDisabledHandler_ReturnsFalse()
    {
        // Arrange
        var handler = new TestToolHandler("TestHandler", ToolType.EntityExtractor);
        var options = new ToolFrameworkOptions
        {
            DisabledHandlers = new[] { "TestHandler" }
        };
        var registry = CreateRegistry(new[] { handler }, options);

        // Act
        var result = registry.IsHandlerAvailable("TestHandler");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsHandlerAvailable_WithNonExistentHandler_ReturnsFalse()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var result = registry.IsHandlerAvailable("NonExistent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HandlerCount_ExcludesDisabledHandlers()
    {
        // Arrange
        var handler1 = new TestToolHandler("Handler1", ToolType.EntityExtractor);
        var handler2 = new TestToolHandler("Handler2", ToolType.ClauseAnalyzer);
        var options = new ToolFrameworkOptions
        {
            DisabledHandlers = new[] { "Handler1" }
        };
        var registry = CreateRegistry(new[] { handler1, handler2 }, options);

        // Act & Assert
        Assert.Equal(1, registry.HandlerCount);
    }

    [Fact]
    public void Constructor_WithDuplicateHandlerIds_SkipsDuplicates()
    {
        // Arrange
        var handler1 = new TestToolHandler("DuplicateId", ToolType.EntityExtractor);
        var handler2 = new TestToolHandler("DuplicateId", ToolType.ClauseAnalyzer);
        var handlers = new[] { handler1, handler2 };

        // Act
        var registry = CreateRegistry(handlers);

        // Assert
        Assert.Equal(1, registry.HandlerCount);
        var handler = registry.GetHandler("DuplicateId");
        Assert.NotNull(handler);
        Assert.Equal(ToolType.EntityExtractor, handler.SupportedToolTypes[0]); // First wins
    }

    [Fact]
    public void Constructor_WithEmptyHandlerId_SkipsHandler()
    {
        // Arrange
        var validHandler = new TestToolHandler("ValidHandler", ToolType.EntityExtractor);
        var emptyIdHandler = new TestToolHandler("", ToolType.ClauseAnalyzer);
        var handlers = new[] { validHandler, emptyIdHandler };

        // Act
        var registry = CreateRegistry(handlers);

        // Assert
        Assert.Equal(1, registry.HandlerCount);
        Assert.Contains("ValidHandler", registry.GetRegisteredHandlerIds());
    }

    [Fact]
    public void GetRegisteredHandlerIds_ExcludesDisabledHandlers()
    {
        // Arrange
        var handler1 = new TestToolHandler("Handler1", ToolType.EntityExtractor);
        var handler2 = new TestToolHandler("Handler2", ToolType.ClauseAnalyzer);
        var options = new ToolFrameworkOptions
        {
            DisabledHandlers = new[] { "Handler1" }
        };
        var registry = CreateRegistry(new[] { handler1, handler2 }, options);

        // Act
        var result = registry.GetRegisteredHandlerIds();

        // Assert
        Assert.Single(result);
        Assert.Equal("Handler2", result[0]);
    }

    [Fact]
    public void Handler_WithMultipleToolTypes_IndexedByAllTypes()
    {
        // Arrange
        var handler = new MultiTypeTestHandler("MultiHandler",
            new[] { ToolType.EntityExtractor, ToolType.ClauseAnalyzer });
        var registry = CreateRegistry(new[] { handler });

        // Act
        var extractorHandlers = registry.GetHandlersByType(ToolType.EntityExtractor);
        var analyzerHandlers = registry.GetHandlersByType(ToolType.ClauseAnalyzer);

        // Assert
        Assert.Single(extractorHandlers);
        Assert.Single(analyzerHandlers);
        Assert.Same(extractorHandlers[0], analyzerHandlers[0]);
    }
}

/// <summary>
/// Test implementation of IAnalysisToolHandler for unit testing.
/// </summary>
internal class TestToolHandler : IAnalysisToolHandler
{
    public string HandlerId { get; }
    public ToolHandlerMetadata Metadata { get; }
    public IReadOnlyList<ToolType> SupportedToolTypes { get; }

    public TestToolHandler(string handlerId, ToolType toolType)
    {
        HandlerId = handlerId;
        SupportedToolTypes = new[] { toolType };
        Metadata = new ToolHandlerMetadata(
            $"Test {handlerId}",
            $"Test handler for {handlerId}",
            "1.0.0",
            new[] { "text/plain" },
            Array.Empty<ToolParameterDefinition>());
    }

    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
        => ToolValidationResult.Success();

    public Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ToolResult.Ok(
            HandlerId,
            tool.Id,
            tool.Name,
            new { message = "test" },
            "Test result",
            1.0,
            ToolExecutionMetadata.Empty));
    }
}

/// <summary>
/// Test handler that supports multiple tool types.
/// </summary>
internal class MultiTypeTestHandler : IAnalysisToolHandler
{
    public string HandlerId { get; }
    public ToolHandlerMetadata Metadata { get; }
    public IReadOnlyList<ToolType> SupportedToolTypes { get; }

    public MultiTypeTestHandler(string handlerId, ToolType[] toolTypes)
    {
        HandlerId = handlerId;
        SupportedToolTypes = toolTypes;
        Metadata = new ToolHandlerMetadata(
            $"Test {handlerId}",
            $"Multi-type test handler",
            "1.0.0",
            new[] { "text/plain" },
            Array.Empty<ToolParameterDefinition>());
    }

    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
        => ToolValidationResult.Success();

    public Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ToolResult.Ok(
            HandlerId,
            tool.Id,
            tool.Name,
            new { message = "test" },
            "Test result",
            1.0,
            ToolExecutionMetadata.Empty));
    }
}
